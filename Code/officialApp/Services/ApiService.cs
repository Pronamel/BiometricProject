
// This service handles communication with the server for device management.
// It provides methods to get device information, update it, and manage connected devices.


using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using officialApp.Models;

namespace officialApp.Services;

public class ApiService : IApiService
{
    public static ApiService Instance { get; private set; }
    
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    
    // JWT Authentication fields
    private string? _jwtToken;
    private DateTime _tokenExpiry;
    private string? _currentOfficialId;
    private string? _currentStationId;
    
    // Authentication properties
    public bool IsAuthenticated => 
        !string.IsNullOrEmpty(_jwtToken) && DateTime.UtcNow < _tokenExpiry;
    
    public string? CurrentOfficialId => _currentOfficialId;

    static ApiService()
    {
        Instance = new ApiService(new HttpClient());
    }

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        
        // TODO: Move this to configuration or environment variable
        _baseUrl = "https://34-238-14-248.nip.io";
        
        _httpClient.Timeout = TimeSpan.FromSeconds(10); // Increased from 3 to 10 seconds for HTTPS handshake
        
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

    public async Task<OfficialLoginResponse?> LoginAsync(string officialId, string stationId, string county, string constituency, string systemCode, string? password = null)
    {
        try
        {
            var loginRequest = new OfficialLoginRequest
            {
                OfficialId = officialId,
                StationId = stationId,
                County = county,
                Constituency = constituency,
                SystemCode = systemCode,
                Password = password
            };

            var jsonContent = JsonSerializer.Serialize(loginRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending login request:");
            Console.WriteLine($"  Official ID: '{officialId}'");
            Console.WriteLine($"  Station ID: '{stationId}'");
            Console.WriteLine($"  JSON: {jsonContent}");

            var response = await _httpClient.PostAsync($"{_baseUrl}/auth/official-login", content);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Response Status: {response.StatusCode}");
            
            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Response Body: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                var loginResponse = JsonSerializer.Deserialize<OfficialLoginResponse>(responseContent, _jsonOptions);

                if (loginResponse?.Success == true && !string.IsNullOrEmpty(loginResponse.Token))
                {
                    // Store authentication state
                    _jwtToken = loginResponse.Token;
                    _tokenExpiry = loginResponse.ExpiresAt;
                    _currentOfficialId = loginResponse.OfficialId;
                    _currentStationId = loginResponse.StationId;

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} logged in successfully");
                    return loginResponse;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Login error: {ex.Message}");
            return null;
        }
    }
    public async Task<VoteNotificationResponse?> CheckForVotesAsync()
    {
        try
        {
            if (!IsAuthenticated)
            {
                Console.WriteLine("Not authenticated for vote checking");
                return null;
            }

            var response = await SendAuthenticatedGetAsync("/api/official/wait-for-votes");
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var voteResponse = JsonSerializer.Deserialize<VoteNotificationResponse>(responseContent, _jsonOptions);
                return voteResponse;
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Vote checking error: {ex.Message}");
            return null;
        }
    }
    public void Logout()
    {
        _jwtToken = null;
        _tokenExpiry = DateTime.MinValue;
        _currentOfficialId = null;
        _currentStationId = null;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official logged out");
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
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    //--------------------------------------------
    // Device Management API Methods (DISABLED - removed from server)
    //--------------------------------------------

    // NOTE: Device management functionality has been removed from the server
    // These methods are kept for reference but will return false/null
    public async Task<bool> SendDeviceManagementInfoAsync(DeviceManagementInfo deviceInfo)
    {
        // Device management removed from server - always return false
        return false;
    }

    public async Task<DeviceManagementInfo?> GetDeviceManagementInfoAsync()
    {
        // Device management removed from server - always return null
        return null;
    }

    //--------------------------------------------
    // Long Polling Methods  
    //--------------------------------------------

    public async Task<OfficialRequestsResponse?> WaitForVoterRequestsAsync()
    {
        try
        {
            var response = await SendAuthenticatedGetAsync("/api/official/wait-for-requests");
            
            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<OfficialRequestsResponse>(jsonString, _jsonOptions);
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> GenerateAccessCodeAsync(string voterId)
    {
        try
        {
            var request = new GenerateCodeRequest { VoterId = voterId };
            var jsonContent = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await SendAuthenticatedPostAsync("/api/official/generate-code", content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}