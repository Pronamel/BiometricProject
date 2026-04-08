// This service handles communication with the server for voter authentication and real-time communication.
// It provides methods for voter session creation, access code verification, and device heartbeat updates.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Runtime.Versioning;
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
    private Guid? _authenticatedVoterDatabaseId;
    private Guid? _representedVoterDatabaseId;
    private Guid? _proxyVoterDatabaseId;
    private bool _isProxyVotingSession;
    private string? _assignedStationId;
    private Guid? _assignedStationGuid;
    
    // Voter linking fields
    private int _assignedVoterId = 0;
    private string _selectedCounty = string.Empty;
    private string _pollingStationCode = string.Empty;
    private string _selectedConstituency = string.Empty;
    private string _deviceId = string.Empty;

    // Cached server public key for hybrid request encryption.
    private string? _voterEncryptionPublicKeyPem;
    private string? _voterEncryptionKeyId;
    private string? _voterEncryptionKeyVersion;

    // Device heartbeat loop for continuous status updates
    private CancellationTokenSource? _deviceHeartbeatCancellation;
    
    // Current device status - can be updated by any part of the app
    public string CurrentDeviceStatus { get; set; } = "Device initializing";
    
    // Authentication properties
    public bool IsAuthenticated => 
        !string.IsNullOrEmpty(_jwtToken) && DateTime.UtcNow < _tokenExpiry;
    
    public string? CurrentVoterId => _currentVoterId;
    public Guid? AuthenticatedVoterDatabaseId => _authenticatedVoterDatabaseId;
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
    [SupportedOSPlatform("windows")]
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
    /// Retrieves the macOS Hardware UUID (IOPlatformUUID) using system_profiler.
    /// This is a unique identifier for the Mac's hardware and is safe to transmit.
    /// The UUID is specifically designed by Apple to be read by applications.
    /// </summary>
    [SupportedOSPlatform("macos")]
    private string GetMacDeviceUuid()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/system_profiler",
                Arguments = "SPHardwareDataType",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(processInfo))
            {
                if (process == null)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ Failed to start system_profiler process");
                    return "Unknown";
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000); // 5 second timeout

                // Parse the UUID from output: "Hardware UUID: XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX"
                var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("Hardware UUID", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(':');
                        if (parts.Length == 2)
                        {
                            var uuid = parts[1].Trim();
                            if (!string.IsNullOrEmpty(uuid))
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Mac Hardware UUID retrieved: {uuid}");
                                return uuid;
                            }
                        }
                    }
                }

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ Hardware UUID not found in system_profiler output");
                return "Unknown";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ Error retrieving Mac Hardware UUID: {ex.Message}");
            return "Unknown";
        }
    }

    /// <summary>
    /// Hashes the device identifier (Machine GUID on Windows, Hardware UUID on macOS) 
    /// to a 32-character hex string using SHA256.
    /// This provides a clean, consistent device identifier for transmission.
    /// </summary>
    private string GetHashedDeviceId()
    {
        try
        {
            string deviceId;

            if (OperatingSystem.IsWindows())
            {
                deviceId = GetMachineGuid();
            }
            else if (OperatingSystem.IsMacOS())
            {
                deviceId = GetMacDeviceUuid();
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ Device ID lookup is not supported on this platform");
                return "Unknown";
            }
            
            if (deviceId == "Unknown")
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ Device identifier is unknown, cannot hash");
                return "Unknown";
            }

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(deviceId));
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

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Voter encryption public key loaded from server");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   KeyId: {_voterEncryptionKeyId}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   KeyVersion: {_voterEncryptionKeyVersion}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Failed to load voter encryption public key: {ex.Message}");
            return false;
        }
    }

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
                    _assignedStationId = linkResponse.ConnectedStationId;
                    _assignedStationGuid = Guid.TryParse(linkResponse.ConnectedStationId, out var parsedStationId)
                        ? parsedStationId
                        : null;
                    
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
        string? firstName, string? lastName, string? dateOfBirth, string? postCode, string? townOfBirth,
        string county, string constituency)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(firstName) ||
                string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(dateOfBirth) ||
                string.IsNullOrWhiteSpace(postCode) ||
                string.IsNullOrWhiteSpace(townOfBirth))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Missing required voter identity fields for lookup");
                return new VoterAuthLookupResponse
                {
                    Success = false,
                    Message = "FirstName, LastName, DateOfBirth, PostCode, and TownOfBirth are required."
                };
            }

            var lookupRequest = new VoterAuthLookupRequest
            {
                FirstName = firstName,
                LastName = lastName,
                DateOfBirth = dateOfBirth,
                PostCode = postCode,
                TownOfBirth = townOfBirth,
                County = county,
                Constituency = constituency
            };

            var envelope = await BuildEncryptedEnvelopeAsync(lookupRequest);
            if (!envelope.Success)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Voter lookup encryption key unavailable");
                return null;
            }

            var encryptedRequest = new
            {
                wrappedDek = envelope.WrappedDek,
                encryptedPayload = envelope.EncryptedPayload
            };

            var jsonContent = JsonSerializer.Serialize(encryptedRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Looking up voter for authentication (SDI):");
            Console.WriteLine($"  Name: encrypted");
            Console.WriteLine($"  PostCode: encrypted");
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
                            _authenticatedVoterDatabaseId = lookupResponse.VoterId.Value;
                            _currentVoterId = lookupResponse.VoterId.Value.ToString();
                        }

                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter found: {lookupResponse.FullName} (Matched by: {lookupResponse.MatchedBy})");
                    }
                    else if (lookupResponse.RequiresDisambiguation && lookupResponse.CandidateVoterIds?.Count > 0)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Multiple voters matched identity. Candidate count: {lookupResponse.CandidateVoterIds.Count}");
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

    public async Task<List<Candidate>> FetchCandidatesAsync()
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📥 Fetching candidates...");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] IsAuthenticated: {IsAuthenticated}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Token: {(_jwtToken != null ? _jwtToken.Substring(0, Math.Min(50, _jwtToken.Length)) + "..." : "null")}");
            
            var response = await SendAuthenticatedGetAsync("/api/candidates");
            
            if (response == null || !response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to fetch candidates - Status: {response?.StatusCode}");
                if (response != null)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error response: {errorContent}");
                }
                return new List<Candidate>();
            }

            var candidateJson = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Raw candidates JSON: {candidateJson}");
            
            var candidates = JsonSerializer.Deserialize<List<Candidate>>(candidateJson, _jsonOptions);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Successfully fetched {candidates?.Count ?? 0} candidates");
            
            foreach (var candidate in candidates ?? new List<Candidate>())
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   - {candidate.FirstName} {candidate.LastName} ({candidate.Party})");
            }
            
            return candidates ?? new List<Candidate>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error fetching candidates: {ex.Message}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Stack trace: {ex.StackTrace}");
            return new List<Candidate>();
        }
    }

    public async Task<CastVoteResponse> CastVoteAsync(Guid candidateId, string candidateName, string partyName)
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

            if (!_assignedStationGuid.HasValue || _assignedStationGuid.Value == Guid.Empty)
            {
                return new CastVoteResponse
                {
                    Success = false,
                    Message = "No linked polling station ID found. Please relink to an official before voting.",
                    Timestamp = DateTime.UtcNow
                };
            }

            var targetVoterDatabaseId = _isProxyVotingSession
                ? _representedVoterDatabaseId
                : _authenticatedVoterDatabaseId;

            if (!targetVoterDatabaseId.HasValue || targetVoterDatabaseId.Value == Guid.Empty)
            {
                return new CastVoteResponse
                {
                    Success = false,
                    Message = "Voter authentication context missing. Please authenticate again.",
                    Timestamp = DateTime.UtcNow
                };
            }

            if (_isProxyVotingSession && (!_proxyVoterDatabaseId.HasValue || _proxyVoterDatabaseId == Guid.Empty))
            {
                return new CastVoteResponse
                {
                    Success = false,
                    Message = "Proxy voter context missing. Please restart proxy authentication.",
                    Timestamp = DateTime.UtcNow
                };
            }

            var castVoteRequest = new CastVoteRequest
            {
                VoterId = _assignedVoterId,
                VoterDatabaseId = targetVoterDatabaseId,
                ProxyVoterDatabaseId = _isProxyVotingSession ? _proxyVoterDatabaseId : null,
                County = _selectedCounty,
                PollingStationId = _assignedStationGuid.Value,
                CandidateId = candidateId,
                CandidateName = candidateName,
                PartyName = partyName,
                Constituency = _selectedConstituency
            };

            var endpoint = _isProxyVotingSession ? "/api/voter/cast-proxy-vote" : "/api/voter/cast-vote";
            StringContent content;

            if (_isProxyVotingSession)
            {
                var envelope = await BuildEncryptedEnvelopeAsync(castVoteRequest);
                if (!envelope.Success)
                {
                    return new CastVoteResponse
                    {
                        Success = false,
                        Message = "Proxy vote encryption key unavailable. Please try again.",
                        Timestamp = DateTime.UtcNow
                    };
                }

                var encryptedRequest = new
                {
                    wrappedDek = envelope.WrappedDek,
                    encryptedPayload = envelope.EncryptedPayload
                };

                var jsonContent = JsonSerializer.Serialize(encryptedRequest, _jsonOptions);
                content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Proxy vote payload protected with encrypted envelope");
            }
            else
            {
                var jsonContent = JsonSerializer.Serialize(castVoteRequest, _jsonOptions);
                content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Casting vote:");
            Console.WriteLine($"  Voter ID: {_assignedVoterId}");
            Console.WriteLine($"  Voter Database ID: {targetVoterDatabaseId}");
            Console.WriteLine($"  Proxy Session: {_isProxyVotingSession}");
            Console.WriteLine($"  Proxy Voter Database ID: {_proxyVoterDatabaseId}");
            Console.WriteLine($"  County: {_selectedCounty}");
            Console.WriteLine($"  Polling Station ID: {_assignedStationGuid}");
            Console.WriteLine($"  Candidate ID: {candidateId}");
            Console.WriteLine($"  Candidate: {candidateName} - {partyName}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Using JWT Token: {(!string.IsNullOrEmpty(_jwtToken) ? "Yes" : "No")}");

            var response = await SendAuthenticatedPostAsync(endpoint, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cast Vote Response Status: {response.StatusCode}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cast Vote Response: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                var voteResponse = JsonSerializer.Deserialize<CastVoteResponse>(responseContent, _jsonOptions);
                if (voteResponse != null)
                {
                    if (voteResponse.Success && _isProxyVotingSession)
                    {
                        ClearProxyVotingSession();
                    }

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

    public async Task<ProxyAuthorizationResponse?> ValidateProxyAuthorizationAsync(Guid representedVoterId, Guid proxyVoterId)
    {
        try
        {
            var request = new ProxyAuthorizationRequest
            {
                RepresentedVoterId = representedVoterId,
                ProxyVoterId = proxyVoterId
            };

            var envelope = await BuildEncryptedEnvelopeAsync(request);
            if (!envelope.Success)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Proxy validation encryption key unavailable");
                return null;
            }

            var encryptedRequest = new
            {
                wrappedDek = envelope.WrappedDek,
                encryptedPayload = envelope.EncryptedPayload
            };

            var jsonContent = JsonSerializer.Serialize(encryptedRequest, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            var response = await SendAuthenticatedPostAsync("/api/voter/validate-proxy-authorization", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                return JsonSerializer.Deserialize<ProxyAuthorizationResponse>(responseContent, _jsonOptions);
            }

            return new ProxyAuthorizationResponse
            {
                Success = false,
                Message = "Unexpected proxy validation response from server."
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Proxy authorization validation error: {ex.Message}");
            return new ProxyAuthorizationResponse
            {
                Success = false,
                Message = $"Proxy validation failed: {ex.Message}"
            };
        }
    }

    public void ConfigureProxyVotingSession(Guid representedVoterId, Guid proxyVoterId)
    {
        _representedVoterDatabaseId = representedVoterId;
        _proxyVoterDatabaseId = proxyVoterId;
        _isProxyVotingSession = true;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Proxy voting session configured. Represented={representedVoterId}, Proxy={proxyVoterId}");
    }

    public void ClearProxyVotingSession()
    {
        _representedVoterDatabaseId = null;
        _proxyVoterDatabaseId = null;
        _isProxyVotingSession = false;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Proxy voting session cleared.");
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

    public async Task<List<VoterCommandResponse>> GetPendingDeviceCommandsAsync()
    {
        var commands = new List<VoterCommandResponse>();

        try
        {
            if (!IsAuthenticated || _assignedVoterId == 0 || string.IsNullOrWhiteSpace(_deviceId))
            {
                return commands;
            }

            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_baseUrl}/api/voter/pending-device-commands?deviceId={Uri.EscapeDataString(_deviceId)}");
            AddAuthorizationHeader(request);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return commands;
            }

            var body = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<PendingDeviceCommandsResponse>(body, _jsonOptions);
            if (parsed?.Success == true && parsed.Commands != null)
            {
                commands.AddRange(parsed.Commands);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Failed to poll pending commands: {ex.Message}");
        }

        return commands;
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

            var envelope = await BuildEncryptedEnvelopeAsync(verifyRequest);
            if (!envelope.Success)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Access-code verification encryption key unavailable");
                return false;
            }

            var encryptedRequest = new
            {
                wrappedDek = envelope.WrappedDek,
                encryptedPayload = envelope.EncryptedPayload
            };

            var jsonContent = JsonSerializer.Serialize(encryptedRequest, _jsonOptions);
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
            _authenticatedVoterDatabaseId = null;
            _representedVoterDatabaseId = null;
            _proxyVoterDatabaseId = null;
            _isProxyVotingSession = false;
            _assignedStationId = null;
            _assignedStationGuid = null;
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

    public async Task<FingerprintVerificationResponse?> VerifyFingerprintAsync(string? voterId, byte[] scannedFingerprint, List<string>? candidateVoterIds = null)
    {
        try
        {
            if (candidateVoterIds != null && candidateVoterIds.Count > 0)
            {
                // In collision mode, server expects candidate IDs only.
                voterId = null;
            }

            var hasSingleVoter = !string.IsNullOrWhiteSpace(voterId);
            var hasCandidates = candidateVoterIds != null && candidateVoterIds.Count > 0;

            if (hasSingleVoter == hasCandidates)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error: Provide either voterId or candidateVoterIds");
                return null;
            }

            if (scannedFingerprint == null || scannedFingerprint.Length == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error: Scanned fingerprint is empty");
                return null;
            }

            var verifyRequest = new 
            { 
                userType = "voter",  // Identifier indicating this is a voter
                username = (string?)null,  // Not applicable for voters
                password = (string?)null,  // Not applicable for voters
                voterId = voterId,
                candidateVoterIds = candidateVoterIds,
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   UserType: voter");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   VoterId: {(string.IsNullOrWhiteSpace(voterId) ? "<collision-mode>" : voterId)}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   CandidateVoterIds: {candidateVoterIds?.Count ?? 0}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Scanned fingerprint size: {scannedFingerprint.Length} bytes");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/verify-prints", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📥 Fingerprint verification response status: {response.StatusCode}");

            var verifyResponse = JsonSerializer.Deserialize<FingerprintVerificationResponse>(responseContent, _jsonOptions);

            if (verifyResponse != null)
            {
                if (verifyResponse.IsMatch && verifyResponse.MatchedVoterId.HasValue)
                {
                    _authenticatedVoterDatabaseId = verifyResponse.MatchedVoterId.Value;
                    _currentVoterId = verifyResponse.MatchedVoterId.Value.ToString();
                }

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {(response.IsSuccessStatusCode ? "✅" : "⚠️")} Fingerprint verification result:");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Match: {verifyResponse.IsMatch}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Score: {verifyResponse.Score}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Threshold: {verifyResponse.Threshold}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   MatchedVoterId: {verifyResponse.MatchedVoterId}");
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