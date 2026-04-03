// This service handles communication with the server for voter authentication and real-time communication.
// It provides methods for voter session creation, access code verification, and device heartbeat updates.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SecureVoteApp.Models;

namespace SecureVoteApp.Services;

public class ApiService : IApiService
{
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
    private string _deviceId = string.Empty;

    // Device heartbeat loop for continuous status updates
    private CancellationTokenSource? _deviceHeartbeatCancellation;
    
    // Current device status - can be updated by any part of the app
    public string CurrentDeviceStatus { get; set; } = "Device initializing";
    
    // Authentication properties
    public bool IsAuthenticated => 
        !string.IsNullOrEmpty(_jwtToken) && DateTime.UtcNow < _tokenExpiry;
    
    public string? CurrentVoterId => _currentVoterId;
    public string? AssignedStationId => _assignedStationId;
    public int AssignedVoterId => _assignedVoterId;
    public string SelectedCounty => _selectedCounty;
    public string PollingStationCode => _pollingStationCode;
    public string SelectedConstituency => _selectedConstituency;
    public string DeviceId => _deviceId;

    public string? GetAuthToken()
    {
        if (!IsAuthenticated)
        {
            return null;
        }

        return _jwtToken;
    }

    public string GetRealtimeHubUrl() => $"{_baseUrl}/hubs/voting";

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
    // Device ID Methods
    //--------------------------------------------

    /// <summary>
    /// Retrieves the Windows Machine GUID from the registry.
    /// This is a unique identifier for the Windows installation on this computer.
    /// </summary>
    private string GetMachineGuid()
    {
        try
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Cryptography"))
            {
                var guid = key?.GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrEmpty(guid))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Machine GUID retrieved: {guid}");
                    return guid;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ Error retrieving Machine GUID: {ex.Message}");
        }
        
        return "Unknown";
    }

    /// <summary>
    /// Hashes the Machine GUID to a 32-character hex string using SHA256.
    /// This provides a clean, consistent device identifier for transmission.
    /// </summary>
    private string GetHashedDeviceId()
    {
        try
        {
            string machineGuid = GetMachineGuid();
            
            if (machineGuid == "Unknown")
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ Machine GUID is unknown, cannot hash");
                return "Unknown";
            }

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(machineGuid));
                string hashedDeviceId = Convert.ToHexString(hash).Substring(0, 32);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Device ID hashed: {hashedDeviceId}");
                return hashedDeviceId;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ Error hashing device ID: {ex.Message}");
            return "Unknown";
        }
    }

    //--------------------------------------------
    // JWT Authentication Methods
    //--------------------------------------------

    private string HashPollingStationCode(string code)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(code));
            return Convert.ToBase64String(hashedBytes);
        }
    }

    public async Task<VoterLinkResponse> LinkToOfficialAsync(string pollingStationCode, string county, string constituency)
    {
        try
        {
            // Hash the polling station code before sending for security
            var hashedCode = HashPollingStationCode(pollingStationCode);
            
            var linkRequest = new VoterLinkRequest
            {
                PollingStationCode = hashedCode,
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
                {                    
                    // Store linking information for vote casting
                    _assignedVoterId = linkResponse.AssignedVoterId;
                    _currentVoterId = linkResponse.AssignedVoterId.ToString();
                    _selectedCounty = county;
                    _pollingStationCode = pollingStationCode;
                    _selectedConstituency = constituency;
                    
                    // Retrieve and store device ID (Machine GUID hashed to 32 characters)
                    _deviceId = GetHashedDeviceId();
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device ID set: {_deviceId}");
                    
                    // Store JWT token from link response
                    if (!string.IsNullOrEmpty(linkResponse.Token))
                    {
                        _jwtToken = linkResponse.Token;
                        _tokenExpiry = DateTime.UtcNow.AddHours(8);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter JWT token stored, expires in 8 hours");
                        
                        // Start device heartbeat loop to send continuous status updates
                        StartDeviceHeartbeatAsync();
                    }
                    
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

    public async Task<VoterAuthLookupResponse?> LookupVoterForAuthAsync(
        string? firstName, string? lastName, string? dateOfBirth, string? postCode,
        string county, string constituency)
    {
        try
        {
            var lookupRequest = new VoterAuthLookupRequest
            {
                FirstName = firstName,
                LastName = lastName,
                DateOfBirth = dateOfBirth,
                PostCode = postCode,
                County = county,
                Constituency = constituency
            };

            var jsonContent = JsonSerializer.Serialize(lookupRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Looking up voter for authentication (SDI):");
            Console.WriteLine($"  Name: {(string.IsNullOrEmpty(firstName) ? "Not provided" : $"{firstName} {lastName}")}");
            Console.WriteLine($"  PostCode: {(string.IsNullOrEmpty(postCode) ? "Not provided" : postCode)}");
            Console.WriteLine($"  County: {county}");
            Console.WriteLine($"  Constituency: {constituency}");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/voter/lookup-for-auth", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Lookup Response Status: {response.StatusCode}");

            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var lookupResponse = JsonSerializer.Deserialize<VoterAuthLookupResponse>(responseContent, _jsonOptions);
                if (lookupResponse != null)
                {
                    if (lookupResponse.Success)
                    {
                        if (lookupResponse.VoterId.HasValue)
                        {
                            _currentVoterId = lookupResponse.VoterId.Value.ToString();
                        }

                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter found: {lookupResponse.FullName} (Matched by: {lookupResponse.MatchedBy})");
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Voter not found: {lookupResponse.Message}");
                    }
                    return lookupResponse;
                }
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Unexpected response: {responseContent}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Voter lookup error: {ex.Message}");
            return null;
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Using JWT Token: {(!string.IsNullOrEmpty(_jwtToken) ? "Yes" : "No")}");

            var response = await SendAuthenticatedPostAsync("/api/voter/cast-vote", content);
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

    //--------------------------------------------
    // Device Status Reporting
    //--------------------------------------------

    public async Task<bool> SendDeviceStatusAsync(string status)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📤 SendDeviceStatusAsync called with status: '{status}'");
            
            if (_assignedVoterId == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ Cannot send device status - not linked to any official system");
                return false;
            }

            if (string.IsNullOrEmpty(_deviceId))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ Cannot send device status - device ID not available");
                return false;
            }

            var sendStatusRequest = new
            {
                voterId = _assignedVoterId,
                deviceId = _deviceId,
                status = status
            };

            var jsonContent = JsonSerializer.Serialize(sendStatusRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending device status:");
            Console.WriteLine($"  Voter ID: {_assignedVoterId}");
            Console.WriteLine($"  Device ID: {_deviceId}");
            Console.WriteLine($"  Status: {status}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Using JWT Token: {(!string.IsNullOrEmpty(_jwtToken) ? "Yes" : "No")}");

            var response = await SendAuthenticatedPostAsync("/api/voter/send-device-status", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device Status Response Status: {response.StatusCode}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device Status Response: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                var statusResponse = JsonSerializer.Deserialize<dynamic>(responseContent, _jsonOptions);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Device status sent successfully");
                return true;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to send device status");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device status error: {ex.Message}");
            return false;
        }
    }

    //--------------------------------------------
    // Access Code Management Methods
    //--------------------------------------------

    private string HashAccessCode(string plaintext)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(plaintext));
            return Convert.ToBase64String(hashedBytes);
        }
    }

    public async Task<bool> VerifyAccessCodeAsync(string accessCode, string county, string constituency)
    {
        try
        {
            if (string.IsNullOrEmpty(accessCode))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Access code is empty");
                return false;
            }

            // Hash the plaintext code before sending
            var hashedCode = HashAccessCode(accessCode);

            var verifyRequest = new
            {
                accessCode = hashedCode,
                county = county,
                constituency = constituency
            };

            var jsonContent = JsonSerializer.Serialize(verifyRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Verifying access code for {county}/{constituency}");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/voter/verify-access-code", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var verifyResponse = JsonSerializer.Deserialize<dynamic>(responseContent, _jsonOptions);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Access code verified successfully");
                return true;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Access code verification failed: {responseContent}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error verifying access code: {ex.Message}");
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            if (IsAuthenticated)
            {
                var response = await SendAuthenticatedPostAsync("/auth/voter-logout", new StringContent("{}", Encoding.UTF8, "application/json"));
                var body = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter logout response: {response.StatusCode} {body}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error calling voter logout endpoint: {ex.Message}");
        }
        finally
        {
            _jwtToken = null;
            _tokenExpiry = DateTime.MinValue;
            _currentVoterId = null;
            _currentSessionId = null;
            _assignedStationId = null;
            _assignedVoterId = 0;
            _selectedCounty = string.Empty;
            _pollingStationCode = string.Empty;
            _selectedConstituency = string.Empty;
            _deviceId = string.Empty;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter logged out");
        }
    }

    //--------------------------------------------
    // Device Heartbeat Loop
    //--------------------------------------------
    
    /// <summary>
    /// Starts a background loop that sends device status updates every 10 seconds.
    /// This allows the official to continuously monitor the voter device.
    /// Only activates after JWT token has been acquired.
    /// </summary>
    public async void StartDeviceHeartbeatAsync()
    {
        if (_deviceHeartbeatCancellation != null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ Device heartbeat already running");
            return;
        }
        
        _deviceHeartbeatCancellation = new CancellationTokenSource();
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔄 Starting device heartbeat loop (10 second interval)");
        
        try
        {
            while (!_deviceHeartbeatCancellation.Token.IsCancellationRequested)
            {
                await Task.Delay(10000, _deviceHeartbeatCancellation.Token); // 10 seconds
                
                if (!_deviceHeartbeatCancellation.Token.IsCancellationRequested && IsAuthenticated)
                {
                    await SendDeviceStatusAsync(CurrentDeviceStatus);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🛑 Device heartbeat stopped");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Device heartbeat error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Stops the device heartbeat loop.
    /// </summary>
    private void StopDeviceHeartbeat()
    {
        if (_deviceHeartbeatCancellation != null)
        {
            _deviceHeartbeatCancellation.Cancel();
            _deviceHeartbeatCancellation.Dispose();
            _deviceHeartbeatCancellation = null;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device heartbeat cancelled");
        }
    }
    public void Logout()
    {
        LogoutAsync().GetAwaiter().GetResult();
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

    //--------------------------------------------
    // Fingerprint Verification Methods
    //--------------------------------------------

    public async Task<FingerprintVerificationResponse?> VerifyFingerprintAsync(string voterId, byte[] scannedFingerprint)
    {
        try
        {
            if (string.IsNullOrEmpty(voterId))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error: VoterId is empty");
                return null;
            }

            if (scannedFingerprint == null || scannedFingerprint.Length == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error: Scanned fingerprint is empty");
                return null;
            }

            // Convert scanned fingerprint to base64
            string scannedFingerprintBase64 = Convert.ToBase64String(scannedFingerprint);

            var verifyRequest = new 
            { 
                userType = "voter",  // Identifier indicating this is a voter
                username = (string?)null,  // Not applicable for voters
                password = (string?)null,  // Not applicable for voters
                voterId = voterId,
                scannedFingerprint = scannedFingerprintBase64
            };

            var jsonContent = JsonSerializer.Serialize(verifyRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📸 Sending fingerprint verification request to /api/verify-prints");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   UserType: voter");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   VoterId: {voterId}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Scanned fingerprint size: {scannedFingerprint.Length} bytes");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/verify-prints", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📥 Fingerprint verification response status: {response.StatusCode}");

            var verifyResponse = JsonSerializer.Deserialize<FingerprintVerificationResponse>(responseContent, _jsonOptions);

            if (verifyResponse != null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {(response.IsSuccessStatusCode ? "✅" : "⚠️")} Fingerprint verification result:");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Match: {verifyResponse.IsMatch}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Score: {verifyResponse.Score}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Threshold: {verifyResponse.Threshold}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Message: {verifyResponse.Message}");
                return verifyResponse;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Fingerprint verification response parse failed: {responseContent}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error verifying fingerprint: {ex.Message}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Stack: {ex.StackTrace}");
            return null;
        }
    }
}