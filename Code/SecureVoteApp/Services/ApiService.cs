// This service handles communication with the server for voter authentication and real-time communication.
// It provides methods for voter session creation, access code verification, and long polling for official commands.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SecureVoteApp.Models;

namespace SecureVoteApp.Services;

public class ApiService : IApiService
{
    public static ApiService Instance { get; private set; }
    
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    
    // JWT Authentication fields
    private string? _jwtToken;
    private DateTime _tokenExpiry;
    private string? _currentVoterId;
    private string? _currentSessionId;
    private string? _assignedStationId;
    
    // Voter linking fields
    private int _assignedVoterId = 0;
    private string _selectedCounty = string.Empty;
    private string _pollingStationCode = string.Empty;
    private string _selectedConstituency = string.Empty;
    
    // Authentication properties
    public bool IsAuthenticated => 
        !string.IsNullOrEmpty(_jwtToken) && DateTime.UtcNow < _tokenExpiry;
    
    public string? CurrentVoterId => _currentVoterId;
    public string? AssignedStationId => _assignedStationId;
    public int AssignedVoterId => _assignedVoterId;
    public string SelectedCounty => _selectedCounty;
    public string PollingStationCode => _pollingStationCode;
    public string SelectedConstituency => _selectedConstituency;

    static ApiService()
    {
        Instance = new ApiService(new HttpClient());
    }

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        
        // TODO: Move this to configuration or environment variable
        _baseUrl = "http://localhost:5000"; // HTTP for local testing (will use HTTPS in production)
        
        _httpClient.Timeout = TimeSpan.FromSeconds(3); // Timeout for local server
        
        // Configure JSON serialization options
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }

    //--------------------------------------------
    // JWT Authentication Methods
    //--------------------------------------------

    public async Task<VoterSessionResponse?> CreateSessionAsync(string voterId, string county, string constituency, string? stationId = null)
    {
        try
        {
            var sessionRequest = new VoterSessionRequest
            {
                VoterId = voterId,
                County = county,
                Constituency = constituency,
                StationId = stationId
            };

            var jsonContent = JsonSerializer.Serialize(sessionRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Creating voter session:");
            Console.WriteLine($"  Voter ID: '{voterId}'");
            Console.WriteLine($"  County: '{county}'");
            Console.WriteLine($"  Station ID: '{stationId ?? "Not assigned"}'");

            var response = await _httpClient.PostAsync($"{_baseUrl}/auth/voter-session", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Response Status: {response.StatusCode}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Response Body: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                var sessionResponse = JsonSerializer.Deserialize<VoterSessionResponse>(responseContent, _jsonOptions);

                if (sessionResponse?.Success == true && !string.IsNullOrEmpty(sessionResponse.Token))
                {
                    // Store authentication state
                    _jwtToken = sessionResponse.Token;
                    _tokenExpiry = sessionResponse.ExpiresAt;
                    _currentVoterId = sessionResponse.VoterId;
                    _currentSessionId = sessionResponse.SessionId;
                    _assignedStationId = stationId;

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter {voterId} session created successfully");
                    return sessionResponse;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Session creation error: {ex.Message}");
            return null;
        }
    }

    public async Task<VoterLinkResponse> LinkToOfficialAsync(string pollingStationCode, string county, string constituency)
    {
        try
        {
            var linkRequest = new VoterLinkRequest
            {
                PollingStationCode = pollingStationCode,
                County = county,
                Constituency = constituency
            };

            var jsonContent = JsonSerializer.Serialize(linkRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Linking voter to official:");
            Console.WriteLine($"  Polling Station Code: '{pollingStationCode}'");
            Console.WriteLine($"  County: '{county}'");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/voter/link-to-official", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Link Response Status: {response.StatusCode}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Link Response Body: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                var linkResponse = JsonSerializer.Deserialize<VoterLinkResponse>(responseContent, _jsonOptions);
                if (linkResponse != null)
                {                    // Store linking information for vote casting
                    _assignedVoterId = linkResponse.AssignedVoterId;
                    _selectedCounty = county;
                    _pollingStationCode = pollingStationCode;
                    _selectedConstituency = constituency;
                    return linkResponse;
                }
            }

            // Parse error response if available
            try
            {
                var errorResponse = JsonSerializer.Deserialize<VoterLinkResponse>(responseContent, _jsonOptions);
                if (errorResponse != null)
                {
                    return errorResponse;
                }
            }
            catch
            {
                // Ignore JSON parsing errors
            }

            // Return generic failure response
            return new VoterLinkResponse
            {
                Success = false,
                Message = "Failed to connect to polling station. Please check your codes and try again.",
                AssignedVoterId = 0,
                ConnectedOfficialId = "",
                ConnectedStationId = ""
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Voter linking error: {ex.Message}");
            return new VoterLinkResponse
            {
                Success = false,
                Message = $"Connection error: {ex.Message}",
                AssignedVoterId = 0,
                ConnectedOfficialId = "",
                ConnectedStationId = ""
            };
        }
    }

    public async Task<CastVoteResponse> CastVoteAsync(string candidateName, string partyName)
    {
        try
        {
            if (_assignedVoterId == 0)
            {
                return new CastVoteResponse
                {
                    Success = false,
                    Message = "Not linked to any official system. Please restart and link to an official first.",
                    Timestamp = DateTime.UtcNow
                };
            }

            var castVoteRequest = new CastVoteRequest
            {
                VoterId = _assignedVoterId,
                County = _selectedCounty,
                PollingStationCode = _pollingStationCode,
                CandidateName = candidateName,
                PartyName = partyName,
                Constituency = _selectedConstituency
            };

            var jsonContent = JsonSerializer.Serialize(castVoteRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Casting vote:");
            Console.WriteLine($"  Voter ID: {_assignedVoterId}");
            Console.WriteLine($"  County: {_selectedCounty}");
            Console.WriteLine($"  Polling Station: {_pollingStationCode}");
            Console.WriteLine($"  Candidate: {candidateName} - {partyName}");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/voter/cast-vote", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cast Vote Response Status: {response.StatusCode}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cast Vote Response: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                var voteResponse = JsonSerializer.Deserialize<CastVoteResponse>(responseContent, _jsonOptions);
                if (voteResponse != null)
                {
                    return voteResponse;
                }
            }

            // Parse error response
            try
            {
                var errorResponse = JsonSerializer.Deserialize<CastVoteResponse>(responseContent, _jsonOptions);
                if (errorResponse != null)
                {
                    return errorResponse;
                }
            }
            catch
            {
                // Ignore JSON parsing errors
            }

            return new CastVoteResponse
            {
                Success = false,
                Message = "Failed to cast vote. Please try again.",
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Vote casting error: {ex.Message}");
            return new CastVoteResponse
            {
                Success = false,
                Message = $"Error casting vote: {ex.Message}",
                Timestamp = DateTime.UtcNow
            };
        }
    }

    public void Logout()
    {
        _jwtToken = null;
        _tokenExpiry = DateTime.MinValue;
        _currentVoterId = null;
        _currentSessionId = null;
        _assignedStationId = null;
        _assignedVoterId = 0;
        _selectedCounty = string.Empty;
        _pollingStationCode = string.Empty;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter logged out");
    }

    private void AddAuthorizationHeader(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_jwtToken) && DateTime.UtcNow < _tokenExpiry)
        {
            request.Headers.Add("Authorization", $"Bearer {_jwtToken}");
        }
    }

    private async Task<HttpResponseMessage> SendAuthenticatedGetAsync(string endpoint)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{endpoint}");
        AddAuthorizationHeader(request);
        return await _httpClient.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAuthenticatedPostAsync(string endpoint, HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{endpoint}");
        request.Content = content;
        AddAuthorizationHeader(request);
        return await _httpClient.SendAsync(request);
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/securevote");
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter app connected successfully: {responseContent}");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter app connection failed: {ex.Message}");
            return false;
        }
    }

    //--------------------------------------------
    // Voter Access Management
    //--------------------------------------------

    public async Task<bool> RequestAccessAsync(string? deviceName = null)
    {
        try
        {
            if (string.IsNullOrEmpty(_currentVoterId))
        {
                Console.WriteLine("No voter ID available for access request");
                return false;
            }

            var request = new VoterAccessRequest 
            { 
                VoterId = _currentVoterId,
                DeviceName = deviceName ?? "SecureVoteApp"
            };

            var jsonContent = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await SendAuthenticatedPostAsync("/api/voter/request-access", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Access request error: {ex.Message}");
            return false;
        }
    }

    public async Task<CodeWaitResponse?> WaitForAccessCodeAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_currentVoterId))
            {
                return null;
            }

            var response = await SendAuthenticatedGetAsync($"/api/voter/wait-for-code/{_currentVoterId}");
            
            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<CodeWaitResponse>(jsonString, _jsonOptions);
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Code wait error: {ex.Message}");
            return null;
        }
    }

    //--------------------------------------------
    // Real-time Communication (Distributed Validation)
    //--------------------------------------------

    public async Task<bool> SubmitCodeForVerificationAsync(string accessCode)
    {
        try
        {
            var request = new CodeVerificationRequest
            {
                VoterId = _currentVoterId ?? "",
                AccessCode = accessCode,
                StationId = _assignedStationId
            };

            var jsonContent = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await SendAuthenticatedPostAsync("/api/voter/verify-code", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Code verification submission error: {ex.Message}");
            return false;
        }
    }

    public async Task<VoterCommandResponse?> ListenForCommandsAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_currentVoterId))
            {
                return null;
            }

            var response = await SendAuthenticatedGetAsync($"/api/voter/listen/{_currentVoterId}");
            
            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<VoterCommandResponse>(jsonString, _jsonOptions);
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Command listening error: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> SendStatusUpdateAsync(string status, string? additionalData = null)
    {
        try
        {
            var request = new VoterStatusUpdate
            {
                VoterId = _currentVoterId ?? "",
                Status = status,
                AdditionalData = additionalData,
                Timestamp = DateTime.UtcNow
            };

            var jsonContent = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await SendAuthenticatedPostAsync("/api/voter/status-update", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Status update error: {ex.Message}");
            return false;
        }
    }
}