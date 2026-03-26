
// This service handles communication with the server for device management.
// It provides methods to get device information, update it, and manage connected devices.


using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using officialApp.Models;

namespace officialApp.Services;

public class ApiService : IApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    
    // JWT Authentication fields
    private string? _jwtToken;
    private DateTime _tokenExpiry;
    private string? _currentOfficialId;
    private string? _currentStationId;
    private string? _currentCounty;
    private string? _currentConstituency;
    private string? _currentSystemCode;
    private long _currentTokenId;
    
    // Authentication properties
    public bool IsAuthenticated => 
        !string.IsNullOrEmpty(_jwtToken) && DateTime.UtcNow < _tokenExpiry;
    
    public string? CurrentOfficialId => _currentOfficialId;
    public string? CurrentStationId => _currentStationId;
    public string? CurrentCounty => _currentCounty;
    public string? CurrentConstituency => _currentConstituency;
    public string? CurrentSystemCode => _currentSystemCode;
    public long CurrentTokenId => _currentTokenId;

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

    public async Task<OfficialLoginResponse?> LoginAsync(string username, string password)
    {
        try
        {
            var loginRequest = new { Username = username, Password = password };

            var jsonContent = JsonSerializer.Serialize(loginRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending login request:");
            Console.WriteLine($"  Username: '{username}'");
            Console.WriteLine($"  Password: '{password}'");
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
                    _currentCounty = loginResponse.County;
                    _currentConstituency = loginResponse.Constituency;
                    _currentSystemCode = loginResponse.SystemCode;
                    _currentTokenId = loginResponse.TokenId;

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {username} logged in successfully");
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

    public async Task<bool> SetAccessCodeAsync(string accessCode)
    {
        try
        {
            if (!IsAuthenticated)
            {
                Console.WriteLine("Not authenticated for setting access code");
                return false;
            }

            if (string.IsNullOrEmpty(accessCode))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Access code is empty");
                return false;
            }

            // Hash the plaintext code before sending
            var hashedCode = HashAccessCode(accessCode);

            var setCodeRequest = new { accessCode = hashedCode };
            var jsonContent = JsonSerializer.Serialize(setCodeRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Setting access code for station {_currentStationId}");

            var response = await SendAuthenticatedPostAsync("/api/official/set-access-code", content);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Access code set successfully");
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to set access code: {errorContent}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error setting access code: {ex.Message}");
            return false;
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
    //--------------------------------------------
    // Database Query Methods
    //--------------------------------------------

    public async Task<List<dynamic>?> GetAllVotersAsync()
    {
        try
        {
            var response = await SendAuthenticatedGetAsync("/api/official/database");
            
            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();
                
                // Parse as list of objects
                var jsonDoc = JsonSerializer.Deserialize<System.Collections.Generic.List<dynamic>>(jsonString, _jsonOptions);
                return jsonDoc;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error fetching data: {ex.Message}");
            return null;
        }
    }

    //--------------------------------------------
    // Fingerprint Verification Methods
    //--------------------------------------------

    public async Task<FingerprintComparisonResponse?> CompareFingerpringsAsync(byte[] fingerprint1, byte[] fingerprint2)
    {
        try
        {
            if (fingerprint1 == null || fingerprint1.Length == 0 || fingerprint2 == null || fingerprint2.Length == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error: One or both fingerprints are empty");
                return null;
            }

            // Convert fingerprints to base64
            string fp1Base64 = Convert.ToBase64String(fingerprint1);
            string fp2Base64 = Convert.ToBase64String(fingerprint2);

            var comparisonRequest = new 
            { 
                fingerprint1 = fp1Base64,
                fingerprint2 = fp2Base64
            };

            var jsonContent = JsonSerializer.Serialize(comparisonRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending fingerprint comparison request");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fingerprint 1 size: {fingerprint1.Length} bytes");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fingerprint 2 size: {fingerprint2.Length} bytes");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/verify-prints", content);

            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fingerprint comparison response status: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var comparisonResponse = JsonSerializer.Deserialize<FingerprintComparisonResponse>(responseContent, _jsonOptions);
                
                if (comparisonResponse != null)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fingerprint comparison result:");
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Match: {comparisonResponse.IsMatch}");
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Score: {comparisonResponse.Score}");
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Threshold: {comparisonResponse.Threshold}");
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Margin: {comparisonResponse.Margin}");
                }
                
                return comparisonResponse;
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fingerprint comparison failed: {responseContent}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error comparing fingerprints: {ex.Message}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Stack: {ex.StackTrace}");
            return null;
        }
    }

    public async Task<FingerprintComparisonResponse?> VerifyFingerprintAsync(string username, string password, byte[] scannedFingerprint)
    {
        try
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error: Username or password is empty");
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
                userType = "official",  // Identifier indicating this is an official
                username = username,
                password = password,
                voterId = (string?)null,  // Not applicable for officials
                scannedFingerprint = scannedFingerprintBase64
            };

            var jsonContent = JsonSerializer.Serialize(verifyRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📸 Sending fingerprint verification request to /api/verify-prints");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   UserType: official");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Username: {username}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Scanned fingerprint size: {scannedFingerprint.Length} bytes");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/verify-prints", content);

            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📥 Fingerprint verification response status: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var verifyResponse = JsonSerializer.Deserialize<FingerprintComparisonResponse>(responseContent, _jsonOptions);
                
                if (verifyResponse != null)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Fingerprint verification result:");
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Match: {verifyResponse.IsMatch}");
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Score: {verifyResponse.Score}");
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Threshold: {verifyResponse.Threshold}");
                }
                
                return verifyResponse;
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Fingerprint verification failed: {responseContent}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error verifying fingerprint: {ex.Message}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Stack: {ex.StackTrace}");
            return null;
        }
    }

    public async Task<bool> UploadOfficialFingerprintAsync(string username, string password, byte[] fingerprintData)
    {
        try
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || fingerprintData == null || fingerprintData.Length == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error: Username, password, or fingerprint data is empty");
                return false;
            }

            // Convert fingerprint (PNG) to base64
            string fingerprintBase64 = Convert.ToBase64String(fingerprintData);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Base64 encoded (PNG): {fingerprintBase64.Length} characters");

            var uploadRequest = new
            {
                username = username,
                password = password,
                fingerPrintScan = fingerprintBase64
            };

            var jsonContent = JsonSerializer.Serialize(uploadRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📸 Uploading fingerprint (PNG format) for {username}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Size: {fingerprintData.Length} bytes");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/official/upload-fingerprint", content);

            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📥 Response: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Fingerprint uploaded successfully");
                return true;
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Upload failed: {responseContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error uploading fingerprint: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> CreateOfficialWithFingerprintAsync(string username, string password, byte[] fingerprintData)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || fingerprintData == null || fingerprintData.Length == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ CreateOfficial failed: missing required fields");
                return false;
            }

            string fingerprintBase64 = Convert.ToBase64String(fingerprintData);
            var request = new
            {
                username,
                password,
                fingerPrintScan = fingerprintBase64
            };

            var jsonContent = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/official/create-official", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Official created successfully: {username}");
                return true;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Official creation failed: {response.StatusCode} - {responseBody}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error creating official: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> CreateVoterWithFingerprintAsync(
        string firstName,
        string lastName,
        string dateOfBirth,
        string addressLine1,
        string addressLine2,
        string postCode,
        string county,
        string constituency,
        byte[] fingerprintData)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(firstName) ||
                string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(dateOfBirth) ||
                string.IsNullOrWhiteSpace(addressLine1) ||
                string.IsNullOrWhiteSpace(postCode) ||
                string.IsNullOrWhiteSpace(county) ||
                string.IsNullOrWhiteSpace(constituency) ||
                fingerprintData == null ||
                fingerprintData.Length == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ CreateVoter failed: missing required fields");
                return false;
            }

            string fingerprintBase64 = Convert.ToBase64String(fingerprintData);
            var request = new
            {
                firstName,
                lastName,
                dateOfBirth,
                addressLine1,
                addressLine2,
                postCode,
                county,
                constituency,
                fingerPrintScan = fingerprintBase64
            };

            var jsonContent = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/official/create-voter", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter created successfully: {firstName} {lastName}");
                return true;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Voter creation failed: {response.StatusCode} - {responseBody}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error creating voter: {ex.Message}");
            return false;
        }
    }
}