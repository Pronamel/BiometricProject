
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
using Isopoh.Cryptography.Argon2;
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
    private string? _voterEncryptionPublicKeyPem;
    private string? _voterEncryptionKeyId;
    private string? _voterEncryptionKeyVersion;
    private string? _voterEncryptionKeyFingerprint;
    
    // Authentication properties
    public bool IsAuthenticated => 
        !string.IsNullOrEmpty(_jwtToken) && DateTime.UtcNow < _tokenExpiry;
    
    public string? CurrentOfficialId => _currentOfficialId;
    public string? CurrentStationId => _currentStationId;
    public string? CurrentCounty => _currentCounty;
    public string? CurrentConstituency => _currentConstituency;
    public string? CurrentSystemCode => _currentSystemCode;
    public long CurrentTokenId => _currentTokenId;

    private async Task<(bool Success, string WrappedDek, string EncryptedPayload)> BuildEncryptedEnvelopeAsync(object payload)
    {
        var publicKeyLoaded = await LoadVoterEncryptionPublicKeyAsync();
        if (!publicKeyLoaded || string.IsNullOrWhiteSpace(_voterEncryptionPublicKeyPem))
        {
            return (false, string.Empty, string.Empty);
        }

        var payloadJson = JsonSerializer.Serialize(payload, _jsonOptions);
        byte[] dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            var wrappedDek = WrapDekWithRsaPublicKey(dek, _voterEncryptionPublicKeyPem);
            var encryptedPayload = EncryptStringToBase64(payloadJson, dek);
            return (true, wrappedDek, encryptedPayload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

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
    // JWT Authentication Methods
    //--------------------------------------------

    public async Task<OfficialLoginResponse?> LoginAsync(string username, string password)
    {
        try
        {
            var loginRequest = new { Username = username, Password = password };

            var envelope = await BuildEncryptedEnvelopeAsync(loginRequest);
            if (!envelope.Success)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Login encryption key unavailable");
                return null;
            }

            var encryptedRequest = new
            {
                wrappedDek = envelope.WrappedDek,
                encryptedPayload = envelope.EncryptedPayload
            };

            var jsonContent = JsonSerializer.Serialize(encryptedRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending login request:");
            Console.WriteLine($"  Username: '{username}'");
            Console.WriteLine("  Payload: [encrypted envelope]");

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

                    await LoadVoterEncryptionPublicKeyAsync();

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
    public async Task<bool> SendDeviceCommandAsync(SendDeviceCommandRequest request)
    {
        try
        {
            if (!IsAuthenticated)
            {
                Console.WriteLine("Not authenticated for sending device command");
                return false;
            }

            var jsonContent = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await SendAuthenticatedPostAsync("/api/official/send-device-command", content);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Sent command '{request.CommandType}' for voter {request.VoterId}, device {request.DeviceId}");
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to send device command: {response.StatusCode} {errorBody}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device command error: {ex.Message}");
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

    private static string ComputeSha256Hex(string value)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static byte[] EncryptWithAesGcm(byte[] plaintext, byte[] dek)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];

        using var aes = new AesGcm(dek, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);
        return payload;
    }

    private static string EncryptStringToBase64(string plaintext, byte[] dek)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        return Convert.ToBase64String(EncryptWithAesGcm(bytes, dek));
    }

    private static string EncryptBytesToBase64(byte[] plaintext, byte[] dek)
    {
        return Convert.ToBase64String(EncryptWithAesGcm(plaintext, dek));
    }

    private static string WrapDekWithRsaPublicKey(byte[] dek, string publicKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var wrappedDek = rsa.Encrypt(dek, RSAEncryptionPadding.OaepSHA256);
        return Convert.ToBase64String(wrappedDek);
    }

    private sealed class VoterPublicKeyResponse
    {
        public bool Success { get; set; }
        public string? KeyId { get; set; }
        public string? KeyVersion { get; set; }
        public string? PublicKeyPem { get; set; }
        public string? Fingerprint { get; set; }
    }

    private async Task<bool> LoadVoterEncryptionPublicKeyAsync()
    {
        if (!string.IsNullOrWhiteSpace(_voterEncryptionPublicKeyPem))
        {
            return true;
        }

        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/crypto/voter-public-key");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Failed to fetch voter encryption public key: {response.StatusCode}");
                return false;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            var keyResponse = JsonSerializer.Deserialize<VoterPublicKeyResponse>(responseBody, _jsonOptions);

            if (keyResponse == null || string.IsNullOrWhiteSpace(keyResponse.PublicKeyPem))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Voter encryption public key response missing PEM");
                return false;
            }

            _voterEncryptionPublicKeyPem = keyResponse.PublicKeyPem;
            _voterEncryptionKeyId = keyResponse.KeyId;
            _voterEncryptionKeyVersion = keyResponse.KeyVersion;
            _voterEncryptionKeyFingerprint = keyResponse.Fingerprint;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Voter encryption public key loaded from server");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   KeyId: {_voterEncryptionKeyId}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   KeyVersion: {_voterEncryptionKeyVersion}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Fingerprint: {_voterEncryptionKeyFingerprint}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Failed to load voter encryption public key: {ex.Message}");
            return false;
        }
    }

    private string HashOfficialPasswordForCreate(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            throw new ArgumentException("Password cannot be empty", nameof(plaintext));
        }

        return Argon2.Hash(plaintext);
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
            var envelope = await BuildEncryptedEnvelopeAsync(setCodeRequest);
            if (!envelope.Success)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Access code encryption key unavailable");
                return false;
            }

            var encryptedRequest = new
            {
                wrappedDek = envelope.WrappedDek,
                encryptedPayload = envelope.EncryptedPayload
            };

            var jsonContent = JsonSerializer.Serialize(encryptedRequest, _jsonOptions);
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
        _voterEncryptionPublicKeyPem = null;
        _voterEncryptionKeyId = null;
        _voterEncryptionKeyVersion = null;
        _voterEncryptionKeyFingerprint = null;
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

    public async Task<PollingStationVoteCountResponse?> GetPollingStationVoteCountAsync()
    {
        try
        {
            if (!IsAuthenticated)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Not authenticated for polling station vote count");
                return null;
            }

            var response = await SendAuthenticatedGetAsync("/api/official/polling-station-vote-count");
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to fetch polling station vote count: {response.StatusCode} {errorBody}");
                return null;
            }

            var body = await response.Content.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<PollingStationVoteCountResponse>(body, _jsonOptions);

            if (payload?.Success != true)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Polling station vote count response was not successful");
                return null;
            }

            return payload;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error fetching polling station vote count: {ex.Message}");
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

            var verifyRequest = new 
            { 
                userType = "official",  // Identifier indicating this is an official
                username = username,
                password = password,
                voterId = (string?)null,  // Not applicable for officials
                scannedFingerprint = Convert.ToBase64String(scannedFingerprint)
            };

            var envelope = await BuildEncryptedEnvelopeAsync(verifyRequest);
            if (!envelope.Success)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Fingerprint verification encryption key unavailable");
                return null;
            }

            var encryptedRequest = new
            {
                wrappedDek = envelope.WrappedDek,
                encryptedPayload = envelope.EncryptedPayload
            };

            var jsonContent = JsonSerializer.Serialize(encryptedRequest, _jsonOptions);
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

            var publicKeyLoaded = await LoadVoterEncryptionPublicKeyAsync();
            if (!publicKeyLoaded || string.IsNullOrWhiteSpace(_voterEncryptionPublicKeyPem))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Fingerprint upload encryption cannot continue: public key was not loaded");
                return false;
            }

            string encryptionMode = "CLIENT_DEK_RSA";
            string keyVersion = _voterEncryptionKeyVersion ?? "v1";
            string keyId = _voterEncryptionKeyId ?? "officialapp-rsa-v1";
            string wrappedDek;
            string encryptedFingerPrintScan;

            try
            {
                var dek = RandomNumberGenerator.GetBytes(32);
                wrappedDek = WrapDekWithRsaPublicKey(dek, _voterEncryptionPublicKeyPem);
                encryptedFingerPrintScan = EncryptBytesToBase64(fingerprintData, dek);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Fingerprint upload client-side encryption failed: {ex.Message}");
                return false;
            }

            var uploadRequest = new
            {
                username,
                password,
                encryptionMode,
                keyVersion,
                keyId,
                wrappedDek,
                encryptedFingerPrintScan
            };

            var envelope = await BuildEncryptedEnvelopeAsync(uploadRequest);
            if (!envelope.Success)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Fingerprint upload encryption key unavailable");
                return false;
            }

            var encryptedRequest = new
            {
                wrappedDek = envelope.WrappedDek,
                encryptedPayload = envelope.EncryptedPayload
            };

            var jsonContent = JsonSerializer.Serialize(encryptedRequest, _jsonOptions);
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

            string passwordHash = HashOfficialPasswordForCreate(password);
            var publicKeyLoaded = await LoadVoterEncryptionPublicKeyAsync();
            if (!publicKeyLoaded || string.IsNullOrWhiteSpace(_voterEncryptionPublicKeyPem))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Official creation encryption cannot continue: public key was not loaded");
                return false;
            }

            string encryptionMode = "CLIENT_DEK_RSA";
            string keyVersion = _voterEncryptionKeyVersion ?? "v1";
            string keyId = _voterEncryptionKeyId ?? "officialapp-rsa-v1";
            string wrappedDek;
            string encryptedFingerPrintScan;

            try
            {
                var dek = RandomNumberGenerator.GetBytes(32);
                wrappedDek = WrapDekWithRsaPublicKey(dek, _voterEncryptionPublicKeyPem);
                encryptedFingerPrintScan = EncryptBytesToBase64(fingerprintData, dek);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Official creation client-side encryption failed: {ex.Message}");
                return false;
            }

            var request = new
            {
                username,
                password = passwordHash,
                assignedPollingStationId,
                encryptionMode,
                keyVersion,
                keyId,
                wrappedDek,
                encryptedFingerPrintScan
            };

            var envelope = await BuildEncryptedEnvelopeAsync(request);
            if (!envelope.Success)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Official creation encryption key unavailable");
                return false;
            }

            var encryptedRequest = new
            {
                wrappedDek = envelope.WrappedDek,
                encryptedPayload = envelope.EncryptedPayload
            };

            var jsonContent = JsonSerializer.Serialize(encryptedRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📤 Creating official: {username}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Polling Station ID: {assignedPollingStationId}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Password: [Argon2 hashed on client]");

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
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ VALIDATION ERROR: Missing required voter fields");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   NI: {(string.IsNullOrWhiteSpace(nationalInsuranceNumber) ? "NOT PROVIDED (optional)" : "OK")}");
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
            string countyHash = ComputeSha256Hex(county);
            string constituencyHash = ComputeSha256Hex(constituency);
            string? encryptionMode = null;
            string? keyVersion = null;
            string? keyId = null;
            string? wrappedDek = null;
            string? encryptedNationalInsuranceNumber = null;
            string? encryptedFirstName = null;
            string? encryptedLastName = null;
            string? encryptedDateOfBirth = null;
            string? encryptedAddressLine1 = null;
            string? encryptedAddressLine2 = null;
            string? encryptedPostCode = null;
            string? encryptedCounty = null;
            string? encryptedConstituency = null;
            string? encryptedFingerPrintScan = null;
            
            // Convert date from UK format (DD/MM/yyyy) to ISO format (yyyy-MM-dd) for server
            string isoDateOfBirth = ConvertDateToIso8601(dateOfBirth);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Date conversion: '{dateOfBirth}' -> '{isoDateOfBirth}'");

            var publicKeyLoaded = await LoadVoterEncryptionPublicKeyAsync();
            if (!publicKeyLoaded || string.IsNullOrWhiteSpace(_voterEncryptionPublicKeyPem))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Client-side voter encryption cannot continue: public key was not loaded");
                return false;
            }

            try
            {
                var dek = RandomNumberGenerator.GetBytes(32);
                encryptionMode = "CLIENT_DEK_RSA";
                keyVersion = _voterEncryptionKeyVersion ?? "v1";
                keyId = _voterEncryptionKeyId ?? "officialapp-rsa-v1";
                wrappedDek = WrapDekWithRsaPublicKey(dek, _voterEncryptionPublicKeyPem);

                encryptedNationalInsuranceNumber = EncryptStringToBase64(nationalInsuranceNumber ?? string.Empty, dek);
                encryptedFirstName = EncryptStringToBase64(firstName, dek);
                encryptedLastName = EncryptStringToBase64(lastName, dek);
                encryptedDateOfBirth = EncryptStringToBase64(isoDateOfBirth, dek);
                encryptedAddressLine1 = EncryptStringToBase64(addressLine1, dek);
                encryptedAddressLine2 = EncryptStringToBase64(addressLine2 ?? string.Empty, dek);
                encryptedPostCode = EncryptStringToBase64(postCode, dek);
                encryptedCounty = EncryptStringToBase64(county, dek);
                encryptedConstituency = EncryptStringToBase64(constituency, dek);
                encryptedFingerPrintScan = EncryptBytesToBase64(fingerprintData, dek);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Client-side voter encryption failed: {ex.Message}");
                return false;
            }
            
            // Create voter request with all required fields
            // NOTE: Official context (officialId, stationId, county, constituency) is extracted from JWT token claims server-side
            var request = new
            {
                nationalInsuranceNumber = string.Empty,
                firstName,
                lastName,
                dateOfBirth = isoDateOfBirth,
                addressLine1 = string.Empty,
                addressLine2 = string.Empty,
                postCode,
                county,
                constituency,
                fingerPrintScan = string.Empty,
                countyHash,
                constituencyHash,
                encryptionMode,
                keyVersion,
                keyId,
                wrappedDek,
                encryptedNationalInsuranceNumber,
                encryptedFirstName,
                encryptedLastName,
                encryptedDateOfBirth,
                encryptedAddressLine1,
                encryptedAddressLine2,
                encryptedPostCode,
                encryptedCounty,
                encryptedConstituency,
                encryptedFingerPrintScan
            };

            var envelope = await BuildEncryptedEnvelopeAsync(request);
            if (!envelope.Success)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Create voter envelope encryption unavailable");
                return false;
            }

            var encryptedRequest = new
            {
                wrappedDek = envelope.WrappedDek,
                encryptedPayload = envelope.EncryptedPayload
            };

            var jsonContent = JsonSerializer.Serialize(encryptedRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📤 Sending request payload:");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   === VOTER DATA ===");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Sensitive fields encrypted: yes");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Non-sensitive routing fields included: county, constituency");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   CountyHash (SHA-256): {countyHash}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   ConstituencyHash (SHA-256): {constituencyHash}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   EncryptionMode: {encryptionMode ?? "none"}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   KeyVersion: {keyVersion ?? "none"}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   EncryptedPayloadAttached: {!string.IsNullOrWhiteSpace(wrappedDek)}");
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