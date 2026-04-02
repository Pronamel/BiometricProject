
// This service handles communication with the server for device management.
// It provides methods to get device information, update it, and manage connected devices.


using System;
using System.Collections.Generic;
using System.Linq;
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
            var hashedPassword = HashPassword(password);
            var loginRequest = new { Username = username, Password = hashedPassword };

            var jsonContent = JsonSerializer.Serialize(loginRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending login request:");
            Console.WriteLine($"  Username: '{username}'");
            Console.WriteLine("  Password: '[SHA256 hashed]'");
            Console.WriteLine($"  JSON: {jsonContent}");

            var response = await _httpClient.PostAsync($"{_baseUrl}/auth/official-login", content);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Response Status: {response.StatusCode}");
            
            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Response Body: {responseContent}");

            // Try to deserialize response regardless of status code to capture error details
            var loginResponse = JsonSerializer.Deserialize<OfficialLoginResponse>(responseContent, _jsonOptions);

            if (response.IsSuccessStatusCode)
            {
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
            else
            {
                // Handle error responses (409 Conflict, 401 Unauthorized, etc.)
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Login failed with status {response.StatusCode}");
                if (loginResponse != null)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error code: {loginResponse.Code}, Message: {loginResponse.Message}");
                    return loginResponse;  // Return response with error details
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

    public async Task<DeviceStatusResponse?> GetDeviceStatusesAsync()
    {
        try
        {
            if (!IsAuthenticated)
            {
                Console.WriteLine("Not authenticated for device status checking");
                return null;
            }

            var response = await SendAuthenticatedGetAsync("/api/official/wait-for-device-statuses");
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var deviceStatusResponse = JsonSerializer.Deserialize<DeviceStatusResponse>(responseContent, _jsonOptions);
                return deviceStatusResponse;
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Device status checking error: {ex.Message}");
            return null;
        }
    }

    //--------------------------------------------
    // Access Code Management Methods
    //--------------------------------------------

    private string HashPassword(string plaintext)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(plaintext));
            return Convert.ToBase64String(hashedBytes);
        }
    }

    private string HashAccessCode(string plaintext)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(plaintext));
            return Convert.ToBase64String(hashedBytes);
        }
    }

    private string ConvertDateToIso8601(string dateString)
    {
        // Convert UK date format (DD/MM/yyyy) to ISO 8601 (yyyy-MM-dd)
        // Supports both DD/MM/yyyy and MM/DD/yyyy formats via TryParse
        try
        {
            // Try parsing with UK format first (DD/MM/yyyy)
            if (DateTime.TryParseExact(dateString, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
            {
                return parsedDate.ToString("yyyy-MM-dd");
            }
            
            // Fallback: try US format (MM/DD/yyyy)
            if (DateTime.TryParseExact(dateString, "MM/dd/yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out parsedDate))
            {
                return parsedDate.ToString("yyyy-MM-dd");
            }
            
            // Last resort: try ISO format already
            if (DateTime.TryParse(dateString, out parsedDate))
            {
                return parsedDate.ToString("yyyy-MM-dd");
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  Could not parse date '{dateString}' in any format");
            return dateString; // Return as-is if parsing fails
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  Error converting date: {ex.Message}");
            return dateString;
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

    public async Task<bool> LogoutAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_jwtToken))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No active session - local logout only");
                ClearLocalSession();
                return true;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending logout request to server...");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/auth/official-logout");
            AddAuthorizationHeader(request);

            var response = await _httpClient.SendAsync(request);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Logout response status: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Server logout successful: {responseContent}");
                ClearLocalSession();
                return true;
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Server logout failed with status {response.StatusCode}");
                ClearLocalSession(); // Clear local session even if server logout fails
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error during logout: {ex.Message}");
            ClearLocalSession(); // Clear local session even on exception
            return false;
        }
    }

    private void ClearLocalSession()
    {
        _jwtToken = null;
        _tokenExpiry = DateTime.MinValue;
        _currentOfficialId = null;
        _currentStationId = null;
        _currentCounty = null;
        _currentConstituency = null;
        _currentSystemCode = null;
        _currentTokenId = 0;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Local session cleared");
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

    public async Task<List<PollingStationOption>?> GetAllPollingStationsAsync()
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 Fetching polling stations from server");
            
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/polling-stations");
            
            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();
                var pollingStations = JsonSerializer.Deserialize<List<PollingStationOption>>(jsonString, _jsonOptions);
                
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Retrieved {pollingStations?.Count ?? 0} polling stations");
                if (pollingStations != null)
                {
                    foreach (var ps in pollingStations.Take(3))
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   - {ps.DisplayName}");
                    }
                }
                
                return pollingStations;
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to fetch polling stations: {response.StatusCode}");
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error: {errorContent}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error fetching polling stations: {ex.Message}");
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
                password = HashPassword(password),
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
                password = HashPassword(password),
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

    public async Task<bool> CreateOfficialWithFingerprintAsync(
        string username,
        string password,
        string assignedPollingStationId,
        string assignedCountyId,
        byte[] fingerprintData)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(assignedPollingStationId) ||
                fingerprintData == null ||
                fingerprintData.Length == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ CreateOfficial failed: missing required fields");
                return false;
            }

            string fingerprintBase64 = Convert.ToBase64String(fingerprintData);
            var request = new
            {
                username,
                password,
                assignedPollingStationId,
                fingerPrintScan = fingerprintBase64
            };

            var jsonContent = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📤 Creating official: {username}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Polling Station ID: {assignedPollingStationId}");

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
        string nationalInsuranceNumber,
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
            Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] ========== CREATE VOTER REQUEST STARTED ==========");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Target URL: {_baseUrl}/api/official/create-voter");
            
            // CHECK AUTHENTICATION STATUS FIRST
            if (!IsAuthenticated)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ AUTHENTICATION ERROR: Official is not authenticated");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Please log in before creating a voter");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Current Token Valid: {!string.IsNullOrEmpty(_jwtToken) && DateTime.UtcNow < _tokenExpiry}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ========== CREATE VOTER REQUEST FAILED - NOT AUTHENTICATED ==========\n");
                return false;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Authentication verified - Official is logged in");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Official ID: {_currentOfficialId}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Station ID: {_currentStationId}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   County: {_currentCounty}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Constituency: {_currentConstituency}");
            
            if (string.IsNullOrWhiteSpace(nationalInsuranceNumber) ||
                string.IsNullOrWhiteSpace(firstName) ||
                string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(dateOfBirth) ||
                string.IsNullOrWhiteSpace(addressLine1) ||
                string.IsNullOrWhiteSpace(postCode) ||
                string.IsNullOrWhiteSpace(county) ||
                string.IsNullOrWhiteSpace(constituency) ||
                fingerprintData == null ||
                fingerprintData.Length == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ VALIDATION ERROR: Missing required voter fields");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   NI: {(string.IsNullOrWhiteSpace(nationalInsuranceNumber) ? "MISSING" : "OK")}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   FirstName: {(string.IsNullOrWhiteSpace(firstName) ? "MISSING" : "OK")}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   LastName: {(string.IsNullOrWhiteSpace(lastName) ? "MISSING" : "OK")}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   DateOfBirth: {(string.IsNullOrWhiteSpace(dateOfBirth) ? "MISSING" : "OK")}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   AddressLine1: {(string.IsNullOrWhiteSpace(addressLine1) ? "MISSING" : "OK")}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   PostCode: {(string.IsNullOrWhiteSpace(postCode) ? "MISSING" : "OK")}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   County: {(string.IsNullOrWhiteSpace(county) ? "MISSING" : "OK")}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Constituency: {(string.IsNullOrWhiteSpace(constituency) ? "MISSING" : "OK")}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Fingerprint Data: {(fingerprintData == null || fingerprintData.Length == 0 ? "MISSING" : $"{fingerprintData.Length} bytes")}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ========== CREATE VOTER REQUEST FAILED - VALIDATION ERROR ==========\n");
                return false;
            }

            string fingerprintBase64 = Convert.ToBase64String(fingerprintData);
            
            // Convert date from UK format (DD/MM/yyyy) to ISO format (yyyy-MM-dd) for server
            string isoDateOfBirth = ConvertDateToIso8601(dateOfBirth);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Date conversion: '{dateOfBirth}' -> '{isoDateOfBirth}'");
            
            // Create voter request with all required fields
            // NOTE: Official context (officialId, stationId, county, constituency) is extracted from JWT token claims server-side
            var request = new
            {
                nationalInsuranceNumber,
                firstName,
                lastName,
                dateOfBirth = isoDateOfBirth,
                addressLine1,
                addressLine2,
                postCode,
                county,
                constituency,
                fingerPrintScan = fingerprintBase64
            };

            var jsonContent = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📤 Sending request payload:");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   === VOTER DATA ===");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   FirstName: {firstName}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   LastName: {lastName}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   NI Number: {nationalInsuranceNumber}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   DOB (original): {dateOfBirth}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   DOB (converted to ISO): {isoDateOfBirth}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Address: {addressLine1}, {addressLine2}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   PostCode: {postCode}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   County: {county}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Constituency: {constituency}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Fingerprint Bytes: {fingerprintData.Length}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   === OFFICIAL CONTEXT ===");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Official ID: {_currentOfficialId}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Station ID: {_currentStationId}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Token ID: {_currentTokenId}");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔗 Making authenticated HTTP POST request with Bearer token...");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Full URL: {_baseUrl}/api/official/create-voter");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Content-Type: application/json");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Authorization: Bearer [token present: {!string.IsNullOrEmpty(_jwtToken)}]");
            
            var response = await SendAuthenticatedPostAsync("/api/official/create-voter", content);
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📥 Response received - Status Code: {response.StatusCode}");
            var responseBody = await response.Content.ReadAsStringAsync();
            
            if (responseBody.Length > 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Response Body: {responseBody}");
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  Response body is empty");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ 404 NOT FOUND: The server cannot find the endpoint");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Possible causes:");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   1. Server is not running or not accessible");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   2. Endpoint /api/official/create-voter is not registered on the server");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   3. Network/proxy issue preventing request from reaching server");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   4. Server returned 404 due to invalid routing configuration");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ========== CREATE VOTER REQUEST FAILED - 404 NOT FOUND ==========\n");
                return false;
            }

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter created successfully: {firstName} {lastName}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ========== CREATE VOTER REQUEST COMPLETED SUCCESSFULLY ==========\n");
                return true;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ SERVER ERROR: Voter creation failed");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Status Code: {response.StatusCode}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Response: {responseBody}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ========== CREATE VOTER REQUEST FAILED ==========\n");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ EXCEPTION during CreateVoter: {ex.GetType().Name}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error Message: {ex.Message}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Stack Trace: {ex.StackTrace}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ========== CREATE VOTER REQUEST FAILED WITH EXCEPTION ==========\n");
            return false;
        }
    }
}

public class PollingStationOption
{
    public Guid PollingStationId { get; set; }
    public string? Code { get; set; }
    public string? County { get; set; }
    public string? Constituency { get; set; }
    public string? DisplayName { get; set; }
}