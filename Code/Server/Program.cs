//===========================================
// USING STATEMENTS
//===========================================
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Server.Services;
using Server.Data;
using Server.Models;
using Server.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Hubs;
using SourceAFIS;
using System.Globalization;

//===========================================
// BUILDER CONFIGURATION
//===========================================
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

// Configure Kestrel for Nginx reverse proxy
// App listens ONLY on localhost:5000 (HTTP)
// Nginx handles HTTPS externally
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5000, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });
});

Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Configured to listen on http://localhost:5000 (Nginx reverse proxy handles HTTPS)");

// Add basic services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR(options =>
{
    // Faster liveness detection so official UI presence updates do not lag for long after drops.
    options.KeepAliveInterval = TimeSpan.FromSeconds(4);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(12);
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
});

//===========================================
// JSON SERIALIZATION CONFIGURATION
//===========================================
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

//===========================================
// JWT CONFIGURATION
//===========================================
var jwtSecret = SecretsHelper.GetJWTSecret().GetAwaiter().GetResult();
var jwtKey = Encoding.ASCII.GetBytes(jwtSecret);
var sdiHmacSecret = SecretsHelper.GetSdiHmacSecret().GetAwaiter().GetResult();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
            ValidateIssuer = true,
            ValidIssuer = "SecureVoteServer",
            ValidateAudience = true,
            ValidAudience = "VotingClients",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        // Allow JWT in query string for SignalR websocket connections.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrWhiteSpace(accessToken) && path.StartsWithSegments("/hubs/voting"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

//===========================================
// DATABASE CONFIGURATION
//===========================================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Missing ConnectionStrings:DefaultConnection in configuration.");
}

try
{
    var csb = new NpgsqlConnectionStringBuilder(connectionString);
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Database config loaded: Host={csb.Host}; Port={csb.Port}; Database={csb.Database}; Username={csb.Username}; SslMode={csb.SslMode}");
}
catch (Exception ex)
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Warning: Could not parse database connection string details: {ex.Message}");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

//===========================================
// IN-MEMORY STORAGE SETUP - COUNTY-BASED CHANNELS
//===========================================

// County+Constituency-based access codes: County -> Constituency -> (VoterId -> Code)
var countyVoterCodes = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>>();

// County+Constituency-based vote notifications: County -> Constituency -> List of vote notifications for officials
var countyVoteChannels = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<VoteNotification>>>();

// County+Constituency-based device status notifications: County -> Constituency -> (OfficialId -> List of device statuses)
var countyDeviceStatuses = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<DeviceStatusNotification>>>>();

// County+Constituency-based device command notifications: County -> Constituency -> ("{voterId}:{deviceId}" -> List of commands)
var countyDeviceCommands = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<DeviceCommandNotification>>>>();

// Global storage (still shared across all counties)
var activeVotingSessions = new ConcurrentDictionary<string, DateTime>(); // SessionId -> Expiry
var tokenCounter = new TokenCounter(); // Global unique token counter

// Official system tracking: (County + SystemCode) -> OfficialInfo (now includes Constituency)
var activeOfficials = new ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)>();

// Official polling station hashes: OfficialId -> (County, Constituency, HashedCode)
var officialPollingStationHashes = new ConcurrentDictionary<string, (string County, string Constituency, string HashedCode)>();

//===========================================
// SERVICE REGISTRATION
//===========================================
builder.Services.AddSingleton(countyVoterCodes);
builder.Services.AddSingleton(countyVoteChannels);
builder.Services.AddSingleton(countyDeviceStatuses);
builder.Services.AddSingleton(countyDeviceCommands);
builder.Services.AddSingleton(activeVotingSessions);
builder.Services.AddSingleton(activeOfficials);
builder.Services.AddSingleton(officialPollingStationHashes);
builder.Services.AddSingleton(tokenCounter);
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddScoped<VoterService>();
builder.Services.AddScoped<OfficialService>();
builder.Services.AddScoped<DatabaseService>();

//===========================================
// CORS CONFIGURATION
//===========================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("ProductionCors", policy =>
    {
        // Allow production domain via Nginx
        policy.WithOrigins(
                "https://34-238-14-248.nip.io",
                "http://localhost:5000",
                "https://localhost:5001"
              )
              .AllowAnyMethod() 
              .AllowAnyHeader()
              .AllowCredentials();
    });
    
    // Keep development policy for local testing
    options.AddPolicy("DevelopmentCors", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod() 
              .AllowAnyHeader();
    });
});

//===========================================
// APP BUILD & MIDDLEWARE PIPELINE
//===========================================
var app = builder.Build();
var currentElectionId = Guid.Parse("e5f6a7b8-c9d1-4e5f-3a4b-5c6d7e8f9a1b");

// Test database connection on startup
try
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Testing database connection...");
    Console.Out.Flush();
    
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (await dbContext.Database.CanConnectAsync())
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Database connection successful!");
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Database connection failed!");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Database connection error: {ex.Message}");
}

try
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Preloading voter encryption public key from AWS Secrets Manager...");
    await SecretsHelper.GetVoterEncryptionPublicKeyPem();
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Voter encryption public key preloaded successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Voter encryption public key preload failed: {ex.Message}");
    throw;
}

try
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Preloading voter encryption private key from AWS Secrets Manager...");
    var privateKeyPem = NormalizePem(await SecretsHelper.GetVoterEncryptionPrivateKeyPem());
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Voter encryption private key preloaded successfully");

    try
    {
        var publicKeyPem = NormalizePem(await SecretsHelper.GetVoterEncryptionPublicKeyPem());
        var derivedPublicKeyPem = ExportPublicKeyPemFromPrivateKey(privateKeyPem);
        var configuredPublicFingerprint = ComputeSha256Hex(publicKeyPem);
        var derivedPublicFingerprint = ComputeSha256Hex(derivedPublicKeyPem);

        if (string.Equals(configuredPublicFingerprint, derivedPublicFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Voter encryption key pair validated (public/private match)");
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Voter encryption key mismatch: configured public key does not match configured private key");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Public fingerprint:  {configuredPublicFingerprint}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Private->Public fingerprint: {derivedPublicFingerprint}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Unable to validate voter encryption key pair: {ex.Message}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Voter encryption private key preload failed: {ex.Message}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Voter fingerprint verification will return 503 until private key is configured");
}
Console.Out.Flush();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("DevelopmentCors");
}
else
{
    app.UseExceptionHandler("/Error");
    // app.UseHsts(); // Disabled for HTTP-only configuration
    app.UseCors("ProductionCors");
}

// Trust forwarded headers from Nginx reverse proxy
// This ensures the app knows requests are HTTPS and gets the real client IP
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Authentication and Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Request body logging middleware - specifically for fingerprint uploads
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    var path = context.Request.Path;
    
    if (method == "POST" && path.StartsWithSegments("/api/official/upload-fingerprint"))
    {
        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] 🔍 [MIDDLEWARE] Fingerprint upload request received");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [MIDDLEWARE] Content-Type: {context.Request.ContentType}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [MIDDLEWARE] Content-Length: {context.Request.ContentLength}");
        
        // Read the body
        context.Request.EnableBuffering();
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
        {
            string requestBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [MIDDLEWARE] Request body length: {requestBody.Length} bytes");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [MIDDLEWARE] Request body preview (first 500 chars):");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {requestBody.Substring(0, Math.Min(500, requestBody.Length))}...");
            
            // Try to parse JSON to show structure
            try
            {
                var json = JsonDocument.Parse(requestBody);
                var root = json.RootElement;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [MIDDLEWARE] JSON properties found:");
                foreach (var prop in root.EnumerateObject())
                {
                    string value = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString()?.Substring(0, Math.Min(50, prop.Value.GetString()?.Length ?? 0)) + "...",
                        _ => prop.Value.ToString().Substring(0, Math.Min(50, prop.Value.ToString().Length))
                    };
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   - {prop.Name}: {value}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [MIDDLEWARE] Failed to parse JSON: {ex.Message}");
            }
        }
    }
    
    await next();
});

// Request logging middleware
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    var path = context.Request.Path;
    
    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    var clientIP = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

    Console.WriteLine($"[{timestamp}] {method} {path} from {clientIP}");
    
    await next();
});

//===========================================
// HELPER FUNCTIONS
//===========================================
string GenerateJwtToken(string userId, string role, Dictionary<string, object>? additionalClaims = null)
{
    var tokenHandler = new JwtSecurityTokenHandler();
    var key = Encoding.ASCII.GetBytes(jwtSecret);
    
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, userId),
        new Claim(ClaimTypes.Role, role),
        new Claim("sub", userId),
        new Claim("role", role)
    };
    
    // Add any additional claims
    if (additionalClaims != null)
    {
        foreach (var claim in additionalClaims)
        {
            claims.Add(new Claim(claim.Key, claim.Value.ToString()!));
        }
        // Ensure constituency is always included if present
        if (additionalClaims.ContainsKey("constituency"))
        {
            claims.Add(new Claim("constituency", additionalClaims["constituency"].ToString()!));
        }
    }
    
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(claims),
        Expires = DateTime.UtcNow.AddHours(8),
        Issuer = "SecureVoteServer",
        Audience = "VotingClients",
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };
    
    var token = tokenHandler.CreateToken(tokenDescriptor);
    return tokenHandler.WriteToken(token);
}

static string NormalizeNameForSdi(string value)
{
    return value.Trim().ToUpperInvariant();
}

static string NormalizePostcodeForSdi(string value)
{
    return value.Trim().ToUpperInvariant().Replace(" ", string.Empty);
}

static string NormalizeTownOfBirthForSdi(string value)
{
    return string.Join(" ", value.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

static string BuildIdentityCanonicalString(string firstName, string lastName, DateTime dateOfBirth, string postCode, string townOfBirth)
{
    // Demo-only: first/last name matching is used for the presentation and will be removed after the presentation.
    return string.Join("|",
        NormalizeNameForSdi(firstName),
        NormalizeNameForSdi(lastName),
        dateOfBirth.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        NormalizePostcodeForSdi(postCode),
        NormalizeTownOfBirthForSdi(townOfBirth));
}

static string ComputeSdiHmacSha256(string canonicalIdentity, string secret)
{
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalIdentity));
    return Convert.ToHexString(hashBytes).ToLowerInvariant();
}

static string ComputeSha256Hex(string value)
{
    using var sha256 = SHA256.Create();
    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value.Trim()));
    return Convert.ToHexString(hashBytes).ToLowerInvariant();
}

static string ToPem(string label, byte[] derBytes)
{
    var base64 = Convert.ToBase64String(derBytes);
    var sb = new StringBuilder();
    sb.AppendLine($"-----BEGIN {label}-----");

    for (int i = 0; i < base64.Length; i += 64)
    {
        int len = Math.Min(64, base64.Length - i);
        sb.AppendLine(base64.Substring(i, len));
    }

    sb.Append($"-----END {label}-----");
    return sb.ToString();
}

static string ExportPublicKeyPemFromPrivateKey(string privateKeyPem)
{
    using var rsa = RSA.Create();
    rsa.ImportFromPem(privateKeyPem);
    var publicDer = rsa.ExportSubjectPublicKeyInfo();
    return ToPem("PUBLIC KEY", publicDer);
}

static byte[] DecryptAesGcmPayload(byte[] payload, byte[] dek)
{
    if (payload == null || payload.Length < 28)
    {
        throw new CryptographicException("Encrypted payload is invalid or too short");
    }

    const int nonceLength = 12;
    const int tagLength = 16;
    int cipherLength = payload.Length - nonceLength - tagLength;

    if (cipherLength <= 0)
    {
        throw new CryptographicException("Encrypted payload cipher text is missing");
    }

    byte[] nonce = new byte[nonceLength];
    byte[] tag = new byte[tagLength];
    byte[] ciphertext = new byte[cipherLength];

    Buffer.BlockCopy(payload, 0, nonce, 0, nonceLength);
    Buffer.BlockCopy(payload, nonceLength, tag, 0, tagLength);
    Buffer.BlockCopy(payload, nonceLength + tagLength, ciphertext, 0, cipherLength);

    byte[] plaintext = new byte[cipherLength];
    using var aes = new AesGcm(dek, tagSizeInBytes: tagLength);
    aes.Decrypt(nonce, ciphertext, tag, plaintext);

    return plaintext;
}

static string NormalizePem(string pem)
{
    return pem.Replace("\\r", string.Empty).Replace("\\n", "\n").Trim();
}

async Task<byte[]> DecryptVoterFingerprintAsync(Voter voter)
{
    if (voter.FingerprintScan == null || voter.FingerprintScan.Length == 0)
    {
        throw new InvalidOperationException("Voter fingerprint is missing");
    }

    if (voter.WrappedDek == null || voter.WrappedDek.Length == 0)
    {
        throw new InvalidOperationException("Voter wrapped DEK is missing");
    }

    var privateKeyPem = NormalizePem(await SecretsHelper.GetVoterEncryptionPrivateKeyPem());

    using var rsa = RSA.Create();
    rsa.ImportFromPem(privateKeyPem);

    byte[] dek = rsa.Decrypt(voter.WrappedDek, RSAEncryptionPadding.OaepSHA256);
    try
    {
        return DecryptAesGcmPayload(voter.FingerprintScan, dek);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(dek);
    }
}

async Task<(string FirstName, string LastName, string NationalInsuranceNumber)> DecryptVoterIdentityAsync(Voter voter)
{
    if (voter.WrappedDek == null || voter.WrappedDek.Length == 0)
    {
        throw new InvalidOperationException("Voter wrapped DEK is missing");
    }

    if (voter.FirstName == null || voter.FirstName.Length == 0)
    {
        throw new InvalidOperationException("Voter encrypted first name is missing");
    }

    if (voter.LastName == null || voter.LastName.Length == 0)
    {
        throw new InvalidOperationException("Voter encrypted last name is missing");
    }

    if (voter.NationalId == null || voter.NationalId.Length == 0)
    {
        throw new InvalidOperationException("Voter encrypted national insurance number is missing");
    }

    var privateKeyPem = NormalizePem(await SecretsHelper.GetVoterEncryptionPrivateKeyPem());

    using var rsa = RSA.Create();
    rsa.ImportFromPem(privateKeyPem);

    byte[] dek = rsa.Decrypt(voter.WrappedDek, RSAEncryptionPadding.OaepSHA256);
    try
    {
        var firstNameBytes = DecryptAesGcmPayload(voter.FirstName, dek);
        var lastNameBytes = DecryptAesGcmPayload(voter.LastName, dek);
        var nationalIdBytes = DecryptAesGcmPayload(voter.NationalId, dek);

        var firstName = Encoding.UTF8.GetString(firstNameBytes).Trim();
        var lastName = Encoding.UTF8.GetString(lastNameBytes).Trim();
        var nationalInsuranceNumber = Encoding.UTF8.GetString(nationalIdBytes).Trim();

        return (firstName, lastName, nationalInsuranceNumber);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(dek);
    }
}

async Task<byte[]> DecryptOfficialFingerprintAsync(Official official)
{
    if (official.FingerPrintScan == null || official.FingerPrintScan.Length == 0)
    {
        throw new InvalidOperationException("Official fingerprint is missing");
    }

    if (official.WrappedDek == null || official.WrappedDek.Length == 0)
    {
        throw new InvalidOperationException("Official fingerprint must be encrypted. Wrapped DEK is missing.");
    }

    var privateKeyPem = NormalizePem(await SecretsHelper.GetVoterEncryptionPrivateKeyPem());

    using var rsa = RSA.Create();
    rsa.ImportFromPem(privateKeyPem);

    byte[] dek = rsa.Decrypt(official.WrappedDek, RSAEncryptionPadding.OaepSHA256);
    try
    {
        return DecryptAesGcmPayload(official.FingerPrintScan, dek);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(dek);
    }
}

async Task<byte[]> UnwrapRequestDekAsync(string wrappedDekBase64)
{
    if (string.IsNullOrWhiteSpace(wrappedDekBase64))
    {
        throw new InvalidOperationException("Wrapped DEK is required");
    }

    var wrappedDek = Convert.FromBase64String(wrappedDekBase64);
    var privateKeyPem = NormalizePem(await SecretsHelper.GetVoterEncryptionPrivateKeyPem());

    using var rsa = RSA.Create();
    rsa.ImportFromPem(privateKeyPem);
    return rsa.Decrypt(wrappedDek, RSAEncryptionPadding.OaepSHA256);
}

static byte[] DecryptRequestBytesField(string encryptedBase64, byte[] dek)
{
    var encryptedPayload = Convert.FromBase64String(encryptedBase64);
    return DecryptAesGcmPayload(encryptedPayload, dek);
}

static bool TryReadEncryptedEnvelope(JsonElement root, out string wrappedDek, out string encryptedPayload)
{
    wrappedDek = root.TryGetProperty("wrappedDek", out var wrappedElement)
        ? wrappedElement.GetString() ?? string.Empty
        : string.Empty;

    encryptedPayload = root.TryGetProperty("encryptedPayload", out var payloadElement)
        ? payloadElement.GetString() ?? string.Empty
        : string.Empty;

    return !string.IsNullOrWhiteSpace(wrappedDek) && !string.IsNullOrWhiteSpace(encryptedPayload);
}

async Task<T> DecryptEnvelopePayloadAsync<T>(string wrappedDekBase64, string encryptedPayloadBase64)
{
    var dek = await UnwrapRequestDekAsync(wrappedDekBase64);
    try
    {
        var plaintextBytes = DecryptRequestBytesField(encryptedPayloadBase64, dek);
        var plaintextJson = Encoding.UTF8.GetString(plaintextBytes);
        var payload = JsonSerializer.Deserialize<T>(plaintextJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (payload == null)
        {
            throw new InvalidOperationException("Decrypted payload could not be deserialized");
        }

        return payload;
    }
    finally
    {
        CryptographicOperations.ZeroMemory(dek);
    }
}

//===========================================
// REQUEST/RESPONSE ENCRYPTION ARCHITECTURE
//===========================================
// Multi-Layer Encryption Strategy:
//
// Layer 1: Transport Security (HTTPS/TLS) ✓
// - All traffic over HTTPS via Nginx reverse proxy
// - TLS 1.2+ provides encryption for all requests and responses
//
// Layer 2: Application-Level Encryption (For Sensitive Requests) ✓
// - Login, access codes, fingerprints use AES-GCM with RSA-wrapped DEK
// - Client encrypts using server's public key
// - Server decrypts using private key via UnwrapRequestDekAsync
//
// Response encryption NOT implemented because:
// 1. HTTPS/TLS already encrypts responses (sufficient protection)
// 2. Would require asymmetric key distribution to clients
// 3. Adds complexity and CPU overhead without security benefit
//
// Request decryption flow:
// 1. Client creates 32-byte DEK
// 2. Client wraps DEK with server RSA public key
// 3. Client encrypts request JSON with DEK using AES-GCM
// 4. Client sends { wrappedDek, encryptedPayload } to server
// 5. Server receives encrypted request
// 6. Server (this file) calls UnwrapRequestDekAsync to get DEK
// 7. Server calls DecryptAesGcmPayload to decrypt request
// 8. Server processes decrypted request normally



//===========================================
// DATA INITIALIZATION
//===========================================






//===========================================
// API ENDPOINTS - AUTHENTICATION
//===========================================
// gets login from database and checks if official exists with those credentials, then generates JWT token with station and official info
app.MapPost("/auth/official-login", async (HttpContext httpContext, DatabaseService dbService, TokenCounter counter, OfficialService officialService,
    ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
    ConcurrentDictionary<string, (string County, string Constituency, string HashedCode)> officialPollingStationHashes) =>
{
    OfficialLoginRequest request;
    try
    {
        httpContext.Request.EnableBuffering();
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;

        if (!TryReadEncryptedEnvelope(root, out var wrappedDek, out var encryptedPayload))
        {
            return Results.BadRequest(new { success = false, message = "Encrypted payload is required" });
        }

        request = await DecryptEnvelopePayloadAsync<OfficialLoginRequest>(wrappedDek, encryptedPayload);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to decrypt official login payload: {ex.Message}");
        return Results.BadRequest(new { success = false, message = "Invalid encrypted payload" });
    }

    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received login request for user: {request.Username}");
    
    // Check if official with username and password exists
    var official = await dbService.GetOfficialByCredentialsAsync(request.Username, request.Password);
    
    if (official == null)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Authentication REJECTED - no matching official found for {request.Username}");
        return Results.Unauthorized();
    }
    
    // Check if official is already logged in
    var officialId = official.OfficialId.ToString();
    if (officialService.IsOfficialAlreadyLoggedIn(officialId))
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Login REJECTED - Official {officialId} is already logged in elsewhere");
        return Results.Conflict(new { 
            success = false, 
            message = "This account is currently active on another device or location. Only one device can be logged in per account at a time. Please have the other user logout first, or contact your administrator if you believe this is an error.",
            code = "ALREADY_LOGGED_IN",
            details = "Account session conflict: concurrent login not allowed"
        });
    }
    
    if (official.AssignedPollingStation == null)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ No polling station assigned for {request.Username}");
        return Results.BadRequest(new { success = false, message = "No polling station assigned" });
    }

    var pollingStation = official.AssignedPollingStation;
    var county = pollingStation.County ?? "Unknown";
    var constituency = pollingStation.Constituency?.Name ?? "Unknown";
    var stationId = pollingStation.PollingStationId.ToString();
    var pollingStationCode = pollingStation.PollingStationCode ?? "Unknown";
    var systemCode = $"OFF-{pollingStationCode}";
    var uniqueTokenId = counter.GetNextId();
    
    // Store the hashed polling station code with county/constituency (direct from DB - NO re-hashing)
    officialPollingStationHashes[officialId] = (county, constituency, pollingStationCode);
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Stored polling station hash for official {officialId}:");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   County: {county}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Constituency: {constituency}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Hash (length {pollingStationCode.Length}): {pollingStationCode}");
    
    // Register this official system with their code (already hashed from DB)
    var systemKey = $"{county}_{systemCode}_{officialId}";
    activeOfficials[systemKey] = (officialId, stationId, constituency, DateTime.UtcNow, new List<int>());
    
    var additionalClaims = new Dictionary<string, object>
    {
        ["station"] = stationId,
        ["officialId"] = officialId,
        ["county"] = county,
        ["systemCode"] = systemCode,
        ["constituency"] = constituency,
        ["tokenId"] = uniqueTokenId
    };
    
    var token = GenerateJwtToken($"official_{officialId}_{uniqueTokenId}", "official", additionalClaims);
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Official login successful: {officialId} at {stationId} in {county}/{constituency} (Token ID: {uniqueTokenId})");
    
    return Results.Ok(new { 
        success = true, 
        token = token,
        role = "official",
        stationId = stationId,
        officialId = officialId,
        county = county,
        systemCode = systemCode,
        constituency = constituency,
        tokenId = uniqueTokenId,
        expiresAt = DateTime.UtcNow.AddHours(8)
    });
})
.WithName("OfficialLogin");

// ============================================
// OFFICIAL LOGOUT ENDPOINT
// ============================================
// Removes official from all channels and clears session data
app.MapPost("/auth/official-logout", (ClaimsPrincipal user,
    ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
    ConcurrentDictionary<string, (string County, string Constituency, string HashedCode)> officialPollingStationHashes,
    ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<VoteNotification>>> countyVoteChannels) =>
{
    var officialId = user.FindFirst("officialId")?.Value ?? "Unknown";
    var county = user.FindFirst("county")?.Value;
    var constituency = user.FindFirst("constituency")?.Value;
    var systemCode = user.FindFirst("systemCode")?.Value;
    
    if (string.IsNullOrEmpty(officialId))
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Logout failed: Official ID not found in token");
        return Results.BadRequest(new { success = false, message = "Official ID not found in token" });
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} logging out from {county}/{constituency}");
    
    var removalSummary = new Dictionary<string, bool>();
    
    // 1. Remove from activeOfficials dictionary
    if (!string.IsNullOrEmpty(county) && !string.IsNullOrEmpty(systemCode))
    {
        var systemKey = $"{county}_{systemCode}_{officialId}";
        var removed = activeOfficials.TryRemove(systemKey, out _);
        removalSummary["activeOfficials"] = removed;
        
        if (removed)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Removed official {officialId} from activeOfficials");
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  Official {officialId} not found in activeOfficials");
        }
    }
    
    // 2. Remove from officialPollingStationHashes dictionary
    var hashRemoved = officialPollingStationHashes.TryRemove(officialId, out _);
    removalSummary["pollingStationHashes"] = hashRemoved;
    
    if (hashRemoved)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Removed polling station hash for official {officialId}");
    }
    else
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  Polling station hash not found for official {officialId}");
    }
    
    // 3. Clear their personal vote queue
    var queueRemoved = false;
    if (!string.IsNullOrEmpty(county))
    {
        if (countyVoteChannels.TryGetValue(county, out var officialQueues))
        {
            queueRemoved = officialQueues.TryRemove(officialId, out _);
        }
    }
    removalSummary["voteQueues"] = queueRemoved;
    
    if (queueRemoved)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Cleared vote queue for official {officialId} in {county}");
    }
    else
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  Vote queue not found for official {officialId}");
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Official {officialId} fully logged out and removed from all channels");
    
    return Results.Ok(new { 
        success = true, 
        message = $"Official {officialId} successfully logged out",
        removedFrom = removalSummary
    });
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialLogout");

// Voter logout endpoint - revokes active voter session created during authentication
app.MapPost("/auth/voter-logout", (ClaimsPrincipal user, VoterService voterService) =>
{
    var voterId = user.FindFirst("voterId")?.Value;

    if (string.IsNullOrWhiteSpace(voterId))
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Voter logout failed: voter ID not found in token");
        return Results.BadRequest(new { success = false, message = "Voter ID not found in token" });
    }

    var removed = voterService.RevokeVoterSession(voterId);

    if (removed)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter {voterId} logged out and session removed");
    }
    else
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  Voter {voterId} logout requested, but no active session was found");
    }

    return Results.Ok(new
    {
        success = true,
        message = "Voter logout processed",
        sessionRemoved = removed
    });
})
.RequireAuthorization(policy => policy.RequireRole("voter"))
.WithName("VoterLogout");

app.MapGet("/api/crypto/voter-public-key", async () =>
{
    try
    {
        var publicKeyPem = await SecretsHelper.GetVoterEncryptionPublicKeyPem();

        return Results.Ok(new
        {
            success = true,
            keyId = "officialapp-rsa-v1",
            keyVersion = "v1",
            publicKeyPem,
            fingerprint = ComputeSha256Hex(publicKeyPem)
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to serve voter public key: {ex.Message}");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
})
.WithName("GetVoterPublicKey");

// Create a voter record in the database from official app input.
app.MapPost("/api/official/create-voter", async (HttpContext httpContext, DatabaseService dbService) =>
{
    CreateVoterRequest request;
    try
    {
        httpContext.Request.EnableBuffering();
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;
        if (!TryReadEncryptedEnvelope(root, out var wrappedDek, out var encryptedPayload))
        {
            return Results.BadRequest(new { success = false, message = "Encrypted payload is required" });
        }

        request = await DecryptEnvelopePayloadAsync<CreateVoterRequest>(wrappedDek, encryptedPayload);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to decrypt create-voter payload: {ex.Message}");
        return Results.BadRequest(new { success = false, message = "Invalid encrypted payload" });
    }

    if (!string.Equals(request.EncryptionMode, "CLIENT_DEK_RSA", StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(request.KeyId) ||
        string.IsNullOrWhiteSpace(request.KeyVersion) ||
        string.IsNullOrWhiteSpace(request.WrappedDek) ||
        string.IsNullOrWhiteSpace(request.EncryptedNationalInsuranceNumber) ||
        string.IsNullOrWhiteSpace(request.EncryptedFirstName) ||
        string.IsNullOrWhiteSpace(request.EncryptedLastName) ||
        string.IsNullOrWhiteSpace(request.EncryptedDateOfBirth) ||
        string.IsNullOrWhiteSpace(request.EncryptedTownOfBirth) ||
        string.IsNullOrWhiteSpace(request.EncryptedPostCode) ||
        string.IsNullOrWhiteSpace(request.EncryptedFingerPrintScan))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Encrypted voter payload is required"
        });
    }

    if (string.IsNullOrWhiteSpace(request.Constituency) || string.IsNullOrWhiteSpace(request.County))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "County and Constituency are required"
        });
    }

    if (string.IsNullOrWhiteSpace(request.CountyHash))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "CountyHash is required"
        });
    }

    var expectedCountyHash = ComputeSha256Hex(request.County);
    if (!string.Equals(request.CountyHash, expectedCountyHash, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "CountyHash does not match County"
        });
    }

    if (string.IsNullOrWhiteSpace(request.ConstituencyHash))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "ConstituencyHash is required"
        });
    }

    var expectedConstituencyHash = ComputeSha256Hex(request.Constituency);
    if (!string.Equals(request.ConstituencyHash, expectedConstituencyHash, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "ConstituencyHash does not match Constituency"
        });
    }

    if (string.IsNullOrWhiteSpace(request.FirstName) ||
        string.IsNullOrWhiteSpace(request.LastName) ||
        string.IsNullOrWhiteSpace(request.DateOfBirth) ||
        string.IsNullOrWhiteSpace(request.PostCode) ||
        string.IsNullOrWhiteSpace(request.TownOfBirth))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "FirstName, LastName, DateOfBirth, PostCode, and TownOfBirth are required for identity indexing"
        });
    }

    if (!DateTime.TryParse(request.DateOfBirth, out var parsedDateOfBirth))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "DateOfBirth is invalid"
        });
    }

    // Demo-only: first/last name matching is used for the presentation and will be removed after the presentation.
    var canonicalIdentity = BuildIdentityCanonicalString(
        request.FirstName,
        request.LastName,
        parsedDateOfBirth,
        request.PostCode,
        request.TownOfBirth);
    var sdi = ComputeSdiHmacSha256(canonicalIdentity, sdiHmacSecret!);

    var result = await dbService.CreateVoterAsync(
        request.CountyHash!,
        request.Constituency,
        sdi,
        request.ConstituencyHash!,
        request.KeyId!,
        request.WrappedDek!,
        request.EncryptedNationalInsuranceNumber!,
        request.EncryptedFirstName!,
        request.EncryptedLastName!,
        request.EncryptedDateOfBirth!,
        request.EncryptedTownOfBirth!,
        request.EncryptedPostCode!,
        request.EncryptedFingerPrintScan!);

    if (!result.Success)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = result.Message
        });
    }

    return Results.Ok(new
    {
        success = true,
        message = result.Message,
        voterId = result.VoterId,
        constituency = request.Constituency,
        county = request.County,
        registeredDate = DateTime.Now.Date
    });
})
.WithName("CreateVoter");

// Create an official record in the database from official app input.
app.MapPost("/api/official/create-official", async (HttpContext httpContext, DatabaseService dbService) =>
{
    CreateOfficialRequest request;
    try
    {
        httpContext.Request.EnableBuffering();
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;
        if (!TryReadEncryptedEnvelope(root, out var wrappedDek, out var encryptedPayload))
        {
            return Results.BadRequest(new { success = false, message = "Encrypted payload is required" });
        }

        request = await DecryptEnvelopePayloadAsync<CreateOfficialRequest>(wrappedDek, encryptedPayload);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to decrypt create-official payload: {ex.Message}");
        return Results.BadRequest(new { success = false, message = "Invalid encrypted payload" });
    }

    if (string.IsNullOrWhiteSpace(request.Username) ||
        string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Username and Password are required"
        });
    }

    if (string.IsNullOrWhiteSpace(request.AssignedPollingStationId))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "AssignedPollingStationId is required"
        });
    }

    if (!string.Equals(request.EncryptionMode, "CLIENT_DEK_RSA", StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(request.KeyVersion) ||
        string.IsNullOrWhiteSpace(request.KeyId) ||
        string.IsNullOrWhiteSpace(request.WrappedDek) ||
        string.IsNullOrWhiteSpace(request.EncryptedFingerPrintScan))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Encrypted fingerprint payload is required"
        });
    }

    // Parse polling station ID as GUID
    if (!Guid.TryParse(request.AssignedPollingStationId, out var pollingStationId))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "AssignedPollingStationId must be a valid GUID"
        });
    }

    var result = await dbService.CreateOfficialAsync(
        request.Username,
        request.Password,
        pollingStationId,
        request.KeyId!,
        request.WrappedDek!,
        request.EncryptedFingerPrintScan!);

    if (!result.Success)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = result.Message
        });
    }

    return Results.Ok(new
    {
        success = true,
        message = result.Message,
        officialId = result.OfficialId,
        username = request.Username,
        pollingStationId = pollingStationId,
        createdDate = DateTime.Now.Date
    });
})
.WithName("CreateOfficial");

//===========================================
// API ENDPOINTS - ACCESS CODE MANAGEMENT
//===========================================
// Official sets the access code for their polling station (code is pre-hashed from the app and stored directly in DB)
app.MapPost("/api/official/set-access-code", async (HttpContext httpContext, ClaimsPrincipal user, 
    [FromServices] ApplicationDbContext dbContext,
    ConcurrentDictionary<string, (string County, string Constituency, string HashedCode)> officialPollingStationHashes) =>
{
    SetAccessCodeRequest request;
    try
    {
        httpContext.Request.EnableBuffering();
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;
        if (!TryReadEncryptedEnvelope(root, out var wrappedDek, out var encryptedPayload))
        {
            return Results.BadRequest(new { success = false, message = "Encrypted payload is required" });
        }

        request = await DecryptEnvelopePayloadAsync<SetAccessCodeRequest>(wrappedDek, encryptedPayload);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to decrypt set-access-code payload: {ex.Message}");
        return Results.BadRequest(new { success = false, message = "Invalid encrypted payload" });
    }

    var stationId = user.FindFirst("station")?.Value;
    var officialId = user.FindFirst("officialId")?.Value ?? "Unknown";
    var county = user.FindFirst("county")?.Value;
    var constituency = user.FindFirst("constituency")?.Value;
    
    if (string.IsNullOrEmpty(stationId))
    {
        return Results.BadRequest(new { success = false, message = "Station ID not found in authentication token" });
    }
    
    var requestedAccessCode = request.AccessCode?.Trim();

    if (string.IsNullOrEmpty(requestedAccessCode))
    {
        return Results.BadRequest(new { success = false, message = "Access code hash is required" });
    }
    
    try
    {
        // Find polling station by ID
        var station = await dbContext.PollingStations.FirstOrDefaultAsync(s => s.PollingStationId == Guid.Parse(stationId));
        
        if (station == null)
        {
            return Results.NotFound(new { success = false, message = "Polling station not found" });
        }

        // SELECT existing polling stations and check PollingStationCode collision.
        var existingStationWithSameCode = await dbContext.PollingStations
            .Where(s => s.PollingStationId != station.PollingStationId)
            .Select(s => new
            {
                s.PollingStationId,
                s.PollingStationCode,
                s.ConstituencyId,
                s.County
            })
            .FirstOrDefaultAsync(s => s.PollingStationCode == requestedAccessCode);

        if (existingStationWithSameCode != null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Official {officialId} attempted duplicate access code for station {stationId}. Existing station: {existingStationWithSameCode.PollingStationId} ({existingStationWithSameCode.County}/{existingStationWithSameCode.ConstituencyId})");
            return Results.Conflict(new { success = false, message = "Code already exists" });
        }
        
        // Store the pre-hashed code directly from the app
        station.PollingStationCode = requestedAccessCode;
        
        await dbContext.SaveChangesAsync();

        // Keep in-memory official hash map in sync so voter linking uses the latest code immediately.
        if (!string.IsNullOrEmpty(officialId) &&
            !string.IsNullOrEmpty(county) &&
            !string.IsNullOrEmpty(constituency))
        {
            officialPollingStationHashes[officialId] = (county, constituency, requestedAccessCode);
        }
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Official {officialId} set access code for station {stationId}");
        
        return Results.Ok(new { success = true, message = "Access code set successfully" });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error setting access code: {ex.Message}");
        return Results.BadRequest(new { success = false, message = "Failed to set access code" });
    }
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialSetAccessCode");






app.MapPost("/api/voter/verify-access-code", async (HttpContext httpContext, 
    [FromServices] ApplicationDbContext dbContext) =>
{
    VerifyAccessCodeRequest request;
    try
    {
        httpContext.Request.EnableBuffering();
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;
        if (!TryReadEncryptedEnvelope(root, out var wrappedDek, out var encryptedPayload))
        {
            return Results.BadRequest(new VerifyAccessCodeResponse(false, "Encrypted payload is required"));
        }

        request = await DecryptEnvelopePayloadAsync<VerifyAccessCodeRequest>(wrappedDek, encryptedPayload);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to decrypt verify-access-code payload: {ex.Message}");
        return Results.BadRequest(new VerifyAccessCodeResponse(false, "Invalid encrypted payload"));
    }

    if (string.IsNullOrEmpty(request.AccessCode))
    {
        return Results.BadRequest(new VerifyAccessCodeResponse(false, "Access code hash is required"));
    }
    
    try
    {
        // Find the polling station for this county and constituency
        var constituency = await dbContext.Constituencies
            .FirstOrDefaultAsync(c => c.Name == request.Constituency);
        
        if (constituency == null)
        {
            return Results.BadRequest(new VerifyAccessCodeResponse(false, "Constituency not found"));
        }
        
        var station = await dbContext.PollingStations
            .FirstOrDefaultAsync(s => s.ConstituencyId == constituency.ConstituencyId && s.County == request.County);
        
        if (station == null)
        {
            return Results.BadRequest(new VerifyAccessCodeResponse(false, "Polling station not found for this county/constituency"));
        }
        
        // Compare the pre-hashed codes directly (both are already hashed from their respective apps)
        if (station.PollingStationCode == request.AccessCode)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter verified access code for {request.County}/{request.Constituency}");
            return Results.Ok(new VerifyAccessCodeResponse(true, "Access code verified successfully"));
        }
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Voter entered incorrect access code for {request.County}/{request.Constituency}");
        return Results.BadRequest(new VerifyAccessCodeResponse(false, "Invalid access code"));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error verifying access code: {ex.Message}");
        return Results.BadRequest(new VerifyAccessCodeResponse(false, "Error verifying access code"));
    }
})
.WithName("VoterVerifyAccessCode");

//===========================================
// API ENDPOINTS - VOTER AUTHENTICATION LOOKUP
//===========================================
// SDI-based voter lookup using FirstName + LastName + DateOfBirth + PostCode + TownOfBirth.
app.MapPost("/api/voter/lookup-for-auth", async (HttpContext httpContext, DatabaseService dbService) =>
{
    Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] ===== VOTER AUTH LOOKUP ATTEMPT =====");

    VoterAuthLookupRequest request;
    try
    {
        httpContext.Request.EnableBuffering();
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;

        if (!TryReadEncryptedEnvelope(root, out var wrappedDek, out var encryptedPayload))
        {
            return Results.BadRequest(new VoterAuthLookupResponse(
                false,
                "Encrypted payload is required.",
                null,
                null,
                null,
                null,
                false,
                null
            ));
        }

        request = await DecryptEnvelopePayloadAsync<VoterAuthLookupRequest>(wrappedDek, encryptedPayload);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to decrypt voter lookup payload: {ex.Message}");
        return Results.BadRequest(new VoterAuthLookupResponse(
            false,
            "Invalid encrypted payload.",
            null,
            null,
            null,
            null,
            false,
            null
        ));
    }

    Voter? voter = null;
    List<Voter> candidateVoters = new();
    string? matchedBy = null;

    string? firstName = request.FirstName;
    string? lastName = request.LastName;
    string? dateOfBirth = request.DateOfBirth;
    string? postCode = request.PostCode;
    string? townOfBirth = request.TownOfBirth;

    if (string.IsNullOrWhiteSpace(firstName) ||
        string.IsNullOrWhiteSpace(lastName) ||
        string.IsNullOrWhiteSpace(dateOfBirth) ||
        string.IsNullOrWhiteSpace(postCode) ||
        string.IsNullOrWhiteSpace(townOfBirth))
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  Missing required identity fields for SDI lookup");
        return Results.BadRequest(new VoterAuthLookupResponse(
            false,
            "FirstName, LastName, DateOfBirth, PostCode, and TownOfBirth are required.",
            null,
            null,
            null,
            null,
            false,
            null
        ));
    }

    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 Attempting SDI lookup for {firstName} {lastName} ({dateOfBirth}) {postCode}");

    var dobInput = dateOfBirth.Trim();
    DateTime parsedDob;

    if (DateTime.TryParseExact(
            dobInput,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var dateOnlyDob))
    {
        parsedDob = DateTime.SpecifyKind(dateOnlyDob.Date, DateTimeKind.Unspecified);
    }
    else if (DateTime.TryParse(
                 dobInput,
                 CultureInfo.InvariantCulture,
                 DateTimeStyles.AllowWhiteSpaces,
                 out var parsedDobWithTime))
    {
        parsedDob = DateTime.SpecifyKind(parsedDobWithTime.Date, DateTimeKind.Unspecified);
    }
    else
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  Invalid DateOfBirth format: {dateOfBirth}");
        return Results.BadRequest(new VoterAuthLookupResponse(
            false,
            "DateOfBirth is invalid. Use yyyy-MM-dd.",
            null,
            null,
            null,
            null,
            false,
            null
        ));
    }

    // Demo-only: first/last name matching is used for the presentation and will be removed after the presentation.
    var canonicalIdentity = BuildIdentityCanonicalString(
        firstName,
        lastName,
        parsedDob,
        postCode,
        townOfBirth);
    var sdi = ComputeSdiHmacSha256(canonicalIdentity, sdiHmacSecret);

    candidateVoters = await dbService.GetVotersBySdiAsync(sdi, limit: 10);
    if (candidateVoters.Count > 0)
    {
        matchedBy = "SDI";
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter candidate(s) found by SDI: {candidateVoters.Count}");
    }

    if (candidateVoters.Count == 1)
    {
        voter = candidateVoters[0];
    }

    if (candidateVoters.Count > 1)
    {
        var collisionCandidateIds = candidateVoters
            .Select(v => v.VoterId)
            .ToList();

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ SDI collision detected; requiring fingerprint disambiguation across {collisionCandidateIds.Count} candidates");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ===== END VOTER AUTH LOOKUP =====\n");

        return Results.BadRequest(new VoterAuthLookupResponse(
            false,
            "Multiple voter records matched your details. Please continue with fingerprint scan.",
            null,
            "Name protected",
            null,
            matchedBy,
            true,
            collisionCandidateIds
        ));
    }

    // Return result
    if (voter is not null)
    {
        if (voter.HasVoted)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Voter {voter.VoterId} already voted - redirecting to official assistance");
            return Results.BadRequest(new VoterAuthLookupResponse(
                false,
                "You have already voted. Please speak to an official.",
                null,
                null,
                null,
                matchedBy,
                false,
                null
            ));
        }

        var fullName = "Name protected";
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ VOTER AUTH LOOKUP SUCCESSFUL - VoterId: {voter.VoterId}, Matched By: {matchedBy}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ===== END VOTER AUTH LOOKUP =====\n");
        
        return Results.Ok(new VoterAuthLookupResponse(
            true,
            "Voter found successfully",
            voter.VoterId,
            fullName,
            null,
            matchedBy,
            false,
            null
        ));
    }

    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ VOTER NOT FOUND - No matching voter found");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ===== END VOTER AUTH LOOKUP =====\n");
    
    return Results.BadRequest(new VoterAuthLookupResponse(
        false,
        "Voter not found. Please check your details and try again.",
        null,
        null,
        null,
        null,
        false,
        null
    ));
})
.WithName("VoterLookupForAuth");

//===========================================
// API ENDPOINTS - PROXY AUTHORIZATION VALIDATION
//===========================================
app.MapPost("/api/voter/validate-proxy-authorization", async (HttpContext httpContext, DatabaseService dbService) =>
{
    ProxyAuthorizationRequest request;
    try
    {
        httpContext.Request.EnableBuffering();
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;

        if (!TryReadEncryptedEnvelope(root, out var wrappedDek, out var encryptedPayload))
        {
            return Results.BadRequest(new ProxyAuthorizationResponse(false, "Encrypted payload is required."));
        }

        request = await DecryptEnvelopePayloadAsync<ProxyAuthorizationRequest>(wrappedDek, encryptedPayload);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to decrypt proxy authorization payload: {ex.Message}");
        return Results.BadRequest(new ProxyAuthorizationResponse(false, "Invalid encrypted payload."));
    }

    if (request.RepresentedVoterId == Guid.Empty || request.ProxyVoterId == Guid.Empty)
    {
        return Results.BadRequest(new ProxyAuthorizationResponse(false, "Represented voter and proxy voter IDs are required."));
    }

    if (request.RepresentedVoterId == request.ProxyVoterId)
    {
        return Results.BadRequest(new ProxyAuthorizationResponse(false, "Proxy voter cannot be the same as represented voter."));
    }

    var representedVoter = await dbService.GetVoterByIdAsync(request.RepresentedVoterId);
    if (representedVoter == null)
    {
        return Results.BadRequest(new ProxyAuthorizationResponse(false, "Represented voter not found."));
    }

    if (representedVoter.HasVoted)
    {
        return Results.BadRequest(new ProxyAuthorizationResponse(false, "Represented voter has already voted."));
    }

    var proxyVoter = await dbService.GetVoterByIdAsync(request.ProxyVoterId);
    if (proxyVoter == null)
    {
        return Results.BadRequest(new ProxyAuthorizationResponse(false, "Proxy voter not found."));
    }

    if (string.IsNullOrWhiteSpace(representedVoter.ProxySdi))
    {
        return Results.BadRequest(new ProxyAuthorizationResponse(false, "Represented voter has no proxy authorization configured."));
    }

    var representedProxySdi = representedVoter.ProxySdi.Trim().ToLowerInvariant();
    var actualProxySdi = (proxyVoter.Sdi ?? string.Empty).Trim().ToLowerInvariant();
    if (!string.Equals(representedProxySdi, actualProxySdi, StringComparison.Ordinal))
    {
        return Results.BadRequest(new ProxyAuthorizationResponse(false, "Proxy voter is not authorized for represented voter."));
    }

    return Results.Ok(new ProxyAuthorizationResponse(true, "Proxy authorization validated."));
})
.RequireAuthorization(policy => policy.RequireRole("voter"))
.WithName("ValidateProxyAuthorization");

//===========================================
// API ENDPOINTS - OFFICIAL PROXY ASSIGNMENT
//===========================================
app.MapPost("/api/official/assign-proxy-voter", async (HttpContext httpContext, DatabaseService dbService) =>
{
    AssignProxyVoterRequest request;
    try
    {
        httpContext.Request.EnableBuffering();
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;

        if (!TryReadEncryptedEnvelope(root, out var wrappedDek, out var encryptedPayload))
        {
            return Results.BadRequest(new AssignProxyVoterResponse(false, "Encrypted payload is required.", null, null));
        }

        request = await DecryptEnvelopePayloadAsync<AssignProxyVoterRequest>(wrappedDek, encryptedPayload);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to decrypt assign-proxy payload: {ex.Message}");
        return Results.BadRequest(new AssignProxyVoterResponse(false, "Invalid encrypted payload.", null, null));
    }

    if (string.IsNullOrWhiteSpace(request.RepresentedFirstName) ||
        string.IsNullOrWhiteSpace(request.RepresentedLastName) ||
        string.IsNullOrWhiteSpace(request.RepresentedDateOfBirth) ||
        string.IsNullOrWhiteSpace(request.RepresentedPostCode) ||
        string.IsNullOrWhiteSpace(request.RepresentedTownOfBirth) ||
        string.IsNullOrWhiteSpace(request.ProxyFirstName) ||
        string.IsNullOrWhiteSpace(request.ProxyLastName) ||
        string.IsNullOrWhiteSpace(request.ProxyDateOfBirth) ||
        string.IsNullOrWhiteSpace(request.ProxyPostCode) ||
        string.IsNullOrWhiteSpace(request.ProxyTownOfBirth) ||
        string.IsNullOrWhiteSpace(request.ScannedFingerprint))
    {
        return Results.BadRequest(new AssignProxyVoterResponse(false, "All voter, proxy voter, and fingerprint fields are required.", null, null));
    }

    if (!DateTime.TryParse(request.RepresentedDateOfBirth, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var representedDob))
    {
        return Results.BadRequest(new AssignProxyVoterResponse(false, "Represented voter date of birth is invalid.", null, null));
    }

    if (!DateTime.TryParse(request.ProxyDateOfBirth, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var proxyDob))
    {
        return Results.BadRequest(new AssignProxyVoterResponse(false, "Proxy voter date of birth is invalid.", null, null));
    }

    // Demo-only: first/last name matching is used for the presentation and will be removed after the presentation.
    var representedCanonicalIdentity = BuildIdentityCanonicalString(
        request.RepresentedFirstName,
        request.RepresentedLastName,
        representedDob,
        request.RepresentedPostCode,
        request.RepresentedTownOfBirth);
    var representedSdi = ComputeSdiHmacSha256(representedCanonicalIdentity, sdiHmacSecret!);

    // Demo-only: first/last name matching is used for the presentation and will be removed after the presentation.
    var proxyCanonicalIdentity = BuildIdentityCanonicalString(
        request.ProxyFirstName,
        request.ProxyLastName,
        proxyDob,
        request.ProxyPostCode,
        request.ProxyTownOfBirth);
    var proxySdi = ComputeSdiHmacSha256(proxyCanonicalIdentity, sdiHmacSecret!);

    if (string.Equals(representedSdi, proxySdi, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new AssignProxyVoterResponse(false, "Proxy voter cannot be the same as the represented voter.", null, null));
    }

    var representedVoter = await dbService.GetVoterBySdiAsync(representedSdi);
    if (representedVoter == null)
    {
        return Results.BadRequest(new AssignProxyVoterResponse(false, "Represented voter not found.", null, null));
    }

    var proxyVoter = await dbService.GetVoterBySdiAsync(proxySdi);
    if (proxyVoter == null)
    {
        return Results.BadRequest(new AssignProxyVoterResponse(false, "Proxy voter not found.", null, null));
    }

    if (representedVoter.HasVoted)
    {
        return Results.BadRequest(new AssignProxyVoterResponse(false, "Represented voter has already voted.", null, null));
    }

    if (representedVoter.FingerprintScan == null || representedVoter.FingerprintScan.Length == 0)
    {
        return Results.BadRequest(new AssignProxyVoterResponse(false, "Represented voter fingerprint is missing.", null, null));
    }

    byte[] storedFingerprintBytes;
    try
    {
        storedFingerprintBytes = await DecryptVoterFingerprintAsync(representedVoter);
    }
    catch (Exception ex)
    {
        var wrappedDekLength = representedVoter.WrappedDek?.Length ?? 0;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Failed to decrypt represented voter fingerprint for voter ID {representedVoter.VoterId}: {ex.Message}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   KeyId={representedVoter.KeyId ?? "<null>"}, WrappedDekBytes={wrappedDekLength}");
        return Results.Json(new
        {
            success = false,
            message = "Represented voter fingerprint decryption unavailable. Configure voter encryption private key.",
            code = "VOTER_DECRYPTION_UNAVAILABLE"
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    byte[] scannedFingerprintBytes;
    try
    {
        scannedFingerprintBytes = Convert.FromBase64String(request.ScannedFingerprint);
    }
    catch (FormatException)
    {
        return Results.BadRequest(new AssignProxyVoterResponse(false, "Scanned fingerprint must be valid base64.", null, null));
    }

    const double MATCH_THRESHOLD = 40.0;
    var scannedImage = new FingerprintImage(scannedFingerprintBytes);
    var storedImage = new FingerprintImage(storedFingerprintBytes);
    var scannedTemplate = new FingerprintTemplate(scannedImage);
    var storedTemplate = new FingerprintTemplate(storedImage);
    var matcher = new FingerprintMatcher(scannedTemplate);
    var score = matcher.Match(storedTemplate);

    if (score < MATCH_THRESHOLD)
    {
        return Results.BadRequest(new AssignProxyVoterResponse(false, "Fingerprint scan is not a match for the represented voter.", null, null));
    }

    var assignResult = await dbService.AssignProxyToVoterAsync(representedVoter.VoterId, proxySdi);
    if (!assignResult.Success)
    {
        return Results.BadRequest(new AssignProxyVoterResponse(false, assignResult.Message, null, null));
    }

    return Results.Ok(new AssignProxyVoterResponse(
        true,
        "Proxy voter assigned successfully.",
        representedVoter.VoterId,
        proxyVoter.VoterId));
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("AssignProxyVoter");

//===========================================
// API ENDPOINTS - VOTER-OFFICIAL LINKING
//===========================================
app.MapPost("/api/voter/link-to-official", (VoterLinkRequest request, 
    ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
    ConcurrentDictionary<string, (string County, string Constituency, string HashedCode)> officialPollingStationHashes,
    TokenCounter voterIdCounter) =>
{
    Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] ===== VOTER LINK ATTEMPT =====");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter requesting access to: County={request.County}, Constituency={request.Constituency}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter sent hashed code (length {request.PollingStationCode.Length}): {request.PollingStationCode}");
    
    Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Searching through {officialPollingStationHashes.Count} officials for county/constituency match:");
    
    // Find matching official by iterating through all officials
    var matchingOfficialId = "";
    foreach (var kvp in officialPollingStationHashes)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Checking Official {kvp.Key}:");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   County: {kvp.Value.County} (requested: {request.County}, match: {kvp.Value.County == request.County})");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Constituency: {kvp.Value.Constituency} (requested: {request.Constituency}, match: {kvp.Value.Constituency == request.Constituency})");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Code in DB: {kvp.Value.HashedCode}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Code from voter: {request.PollingStationCode}");
        
        var countyMatch = kvp.Value.County == request.County;
        var constituencyMatch = kvp.Value.Constituency == request.Constituency;
        var codeMatch = kvp.Value.HashedCode == request.PollingStationCode;
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Code match: {codeMatch}");
        
        if (countyMatch && constituencyMatch && codeMatch)
        {
            matchingOfficialId = kvp.Key;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ MATCH FOUND with official {kvp.Key}!");
            break;  // Found a match, stop searching
        }
    }
    
    if (string.IsNullOrEmpty(matchingOfficialId))
    {
        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] ❌ No official found with matching county/constituency/code");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Available polling stations:");
        foreach (var kvp in officialPollingStationHashes)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   - Official {kvp.Key}: {kvp.Value.County}/{kvp.Value.Constituency} Code={kvp.Value.HashedCode.Substring(0, Math.Min(10, kvp.Value.HashedCode.Length))}...");
        }
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ===== END VOTER LINK ATTEMPT =====\n");
        return Results.BadRequest(new VoterLinkResponse(
            false,
            0,
            "",
            "",
            $"Polling station code does not match. Please verify the code with election staff.",
            null
        ));
    }
    
    // Find the official's info to get station ID
    var officialKey = activeOfficials.Keys
        .FirstOrDefault(k => k.EndsWith($"_{matchingOfficialId}"));
    
    if (officialKey == null || !activeOfficials.TryGetValue(officialKey, out var officialInfo))
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Official {matchingOfficialId} not currently online");
        return Results.BadRequest(new VoterLinkResponse(
            false,
            0,
            "",
            "",
            $"Official is not currently available. Please try again later.",
            null
        ));
    }
    
    var assignedVoterId = (int)voterIdCounter.GetNextId();
    
    Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] ✅ Assigned voter ID: {assignedVoterId}");
    
    // Add voter to official's connected voters list
    var updatedOfficialInfo = officialInfo with { ConnectedVoters = new List<int>(officialInfo.ConnectedVoters) { assignedVoterId } };
    activeOfficials[officialKey] = updatedOfficialInfo;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Added voter {assignedVoterId} to official's connected voters");
    
    // Create JWT token for voter
    var voterTokenId = voterIdCounter.GetNextId();
    var voterClaims = new Dictionary<string, object>
    {
        ["voterId"] = assignedVoterId.ToString(),
        ["county"] = request.County,
        ["constituency"] = request.Constituency,
        ["stationId"] = officialInfo.StationId,
        ["officialId"] = officialInfo.OfficialId,
        ["tokenId"] = voterTokenId
    };
    var voterToken = GenerateJwtToken($"voter_{assignedVoterId}_{voterTokenId}", "voter", voterClaims);
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Generated JWT token for voter {assignedVoterId}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ===== VOTER LINK SUCCESSFUL =====\n");
    
    return Results.Ok(new VoterLinkResponse(
        true,
        assignedVoterId,
        matchingOfficialId,
        officialInfo.StationId,
        "Successfully linked to official",
        voterToken
    ));
})
.WithName("VoterLinkToOfficial");

app.MapPost("/api/voter/cast-vote", async (CastVoteRequest request,
    ClaimsPrincipal user,
    ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
    ApplicationDbContext dbContext,
    IHubContext<VotingHub> hubContext,
    [FromServices] ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<VoteNotification>>> countyVoteChannels) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast attempt - Voter ID: {request.VoterId}, County: {request.County}, Constituency: {request.Constituency}, StationId: {request.PollingStationId}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote for: {request.CandidateName} - {request.PartyName} (CandidateId: {request.CandidateId})");

    if (request.CandidateId == Guid.Empty || request.PollingStationId == Guid.Empty)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast rejected - candidate or station ID missing");
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: candidate and station IDs are required",
            DateTime.UtcNow
        ));
    }

    // Validate cast request against voter token claims.
    var tokenVoterId = user.FindFirst("voterId")?.Value;
    var tokenCounty = user.FindFirst("county")?.Value;
    var tokenConstituency = user.FindFirst("constituency")?.Value;
    var tokenStationId = user.FindFirst("stationId")?.Value;
    var tokenOfficialId = user.FindFirst("officialId")?.Value;

    if (!int.TryParse(tokenVoterId, out var parsedTokenVoterId) || parsedTokenVoterId != request.VoterId)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast rejected - request voter ID {request.VoterId} does not match token voter ID {tokenVoterId}");
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: invalid voter token context",
            DateTime.UtcNow
        ));
    }

    if (!string.Equals(tokenCounty, request.County, StringComparison.Ordinal) ||
        !string.Equals(tokenConstituency, request.Constituency, StringComparison.Ordinal))
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast rejected - request county/constituency does not match token claims");
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: invalid location context",
            DateTime.UtcNow
        ));
    }

    if (!string.IsNullOrWhiteSpace(tokenStationId) &&
        !string.Equals(tokenStationId, request.PollingStationId.ToString(), StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast rejected - request station ID does not match token claim");
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: invalid polling station context",
            DateTime.UtcNow
        ));
    }

    if (!request.VoterDatabaseId.HasValue || request.VoterDatabaseId == Guid.Empty)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast rejected - voter database ID missing");
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: voter identity missing",
            DateTime.UtcNow
        ));
    }

    if (request.ProxyVoterDatabaseId.HasValue)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast rejected - proxy voter context sent to personal vote endpoint");
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: proxy context must use proxy vote endpoint",
            DateTime.UtcNow
        ));
    }

    var voter = await dbContext.Voters.FirstOrDefaultAsync(v => v.VoterId == request.VoterDatabaseId.Value);
    if (voter == null)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast rejected - voter not found: {request.VoterDatabaseId}");
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: voter not found",
            DateTime.UtcNow
        ));
    }

    if (voter.HasVoted)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast rejected - voter {request.VoterDatabaseId} has already voted");
        return Results.BadRequest(new CastVoteResponse(
            false,
            "You have already voted. Please speak to an official.",
            DateTime.UtcNow
        ));
    }

    var constituencyId = await dbContext.Constituencies
        .Where(c => EF.Functions.ILike(c.Name, request.Constituency))
        .Select(c => (Guid?)c.ConstituencyId)
        .FirstOrDefaultAsync();

    if (!constituencyId.HasValue)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast rejected - constituency not found: {request.Constituency}");
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: constituency not found",
            DateTime.UtcNow
        ));
    }

    var candidateExists = await dbContext.Candidates.AnyAsync(c =>
        c.CandidateId == request.CandidateId && c.ElectionId == currentElectionId);

    if (!candidateExists)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast rejected - candidate {request.CandidateId} not in current election {currentElectionId}");
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: invalid candidate for current election",
            DateTime.UtcNow
        ));
    }

    var pollingStationExists = await dbContext.PollingStations.AnyAsync(ps =>
        ps.PollingStationId == request.PollingStationId && ps.ConstituencyId == constituencyId.Value);

    if (!pollingStationExists)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast rejected - polling station {request.PollingStationId} not in constituency {constituencyId.Value}");
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: invalid polling station for constituency",
            DateTime.UtcNow
        ));
    }
    
    // Find ALL active officials
    var allOfficials = activeOfficials.ToList();
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Total active officials: {allOfficials.Count}");
    
    // Find officials that have THIS VOTER in their ConnectedVoters list (hash-based linking)
    var officialsWithVoter = allOfficials
        .Where(o => o.Value.ConnectedVoters.Contains(request.VoterId))
        .ToList();
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Officials with this voter connected: {officialsWithVoter.Count}");
    foreach (var kvp in officialsWithVoter)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   - Official: {kvp.Value.OfficialId}, County: {request.County}, Constituency: {kvp.Value.Constituency}");
    }
    
    // Filter to officials in the same constituency
    var officialsInConstituency = officialsWithVoter
        .Where(o => o.Value.Constituency == request.Constituency)
        .ToList();
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Officials in same constituency: {officialsInConstituency.Count}");
    
    // Fallback: recover official association from token claims when in-memory list was reset.
    if (officialsInConstituency.Count == 0)
    {
        var fallbackOfficials = allOfficials.Where(o =>
            o.Value.Constituency == request.Constituency &&
            (
                (!string.IsNullOrEmpty(tokenOfficialId) && o.Value.OfficialId == tokenOfficialId) ||
                (!string.IsNullOrEmpty(tokenStationId) && o.Value.StationId == tokenStationId)
            ))
            .ToList();

        if (fallbackOfficials.Count > 0)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Recovered voter-official link from token claims for voter {request.VoterId}");

            // Self-heal the in-memory link for subsequent requests.
            foreach (var kvp in fallbackOfficials)
            {
                if (!kvp.Value.ConnectedVoters.Contains(request.VoterId))
                {
                    var healed = kvp.Value with { ConnectedVoters = new List<int>(kvp.Value.ConnectedVoters) { request.VoterId } };
                    activeOfficials[kvp.Key] = healed;
                }
            }

            officialsInConstituency = fallbackOfficials;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Officials in same constituency after token fallback: {officialsInConstituency.Count}");
        }
    }

    if (officialsInConstituency.Count > 0)
    {
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            voter.HasVoted = true;
            await dbContext.SaveChangesAsync();

            var voteRecord = new VoteRecord
            {
                RecordId = Guid.NewGuid(),
                ElectionId = currentElectionId,
                CandidateId = request.CandidateId,
                ConstituencyId = constituencyId.Value,
                PollingStationId = request.PollingStationId,
                VotedAt = DateTime.UtcNow
            };

            dbContext.VoteRecords.Add(voteRecord);
            await dbContext.SaveChangesAsync();

            await transaction.CommitAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Vote record inserted: {voteRecord.RecordId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast failed - database insert error: {ex.Message}");
            return Results.Problem(
                detail: "Vote failed: could not persist vote record",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }

        // Create vote notification with county and constituency
        var voteNotification = new VoteNotification(
            request.VoterId,
            request.CandidateName,
            request.PartyName,
            DateTime.UtcNow,
            "", // Will be filled per official
            "",
            request.County,
            request.Constituency
        );
        
        // Add vote to all linked officials in the same constituency
        var officialQueues = countyVoteChannels.GetOrAdd(request.County, _ => new ConcurrentDictionary<string, ConcurrentBag<VoteNotification>>());
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Broadcasting vote to {officialsInConstituency.Count} officials in constituency {request.Constituency}");
        
        foreach (var kvp in officialsInConstituency)
        {
            var thisOfficialId = kvp.Value.OfficialId;
            var stationId = kvp.Value.StationId;
            
            // Create individual notification for this official
            var individualNotification = voteNotification with 
            { 
                OfficialId = thisOfficialId, 
                StationId = stationId 
            };
            
            var officialQueue = officialQueues.GetOrAdd(thisOfficialId, _ => new ConcurrentBag<VoteNotification>());
            officialQueue.Add(individualNotification);
            _ = hubContext.Clients.Group(RealtimeGroups.Official(thisOfficialId)).SendAsync("official.v1.voteReceived", new
            {
                voterId = individualNotification.VoterId,
                candidateName = individualNotification.CandidateName,
                partyName = individualNotification.PartyName,
                timestamp = individualNotification.Timestamp,
                officialId = individualNotification.OfficialId,
                stationId = individualNotification.StationId
            });
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Added vote to official {thisOfficialId} in constituency {request.Constituency}");
        }
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote successfully cast! Voter {request.VoterId} voted for {request.CandidateName}");
        
        return Results.Ok(new CastVoteResponse(
            true,
            "Vote successfully cast",
            DateTime.UtcNow
        ));
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast failed - Voter {request.VoterId} not linked to any official in constituency {request.Constituency}");
    return Results.BadRequest(new CastVoteResponse(
        false,
        "Vote failed: Voter not properly linked to official system",
        DateTime.UtcNow
    ));
})
.RequireAuthorization(policy => policy.RequireRole("voter"))
.WithName("CastVote");

app.MapPost("/api/voter/cast-proxy-vote", async (HttpContext httpContext,
    ClaimsPrincipal user,
    ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
    ApplicationDbContext dbContext,
    IHubContext<VotingHub> hubContext,
    [FromServices] ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<VoteNotification>>> countyVoteChannels) =>
{
    CastVoteRequest request;
    try
    {
        httpContext.Request.EnableBuffering();
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;

        if (!TryReadEncryptedEnvelope(root, out var wrappedDek, out var encryptedPayload))
        {
            return Results.BadRequest(new CastVoteResponse(
                false,
                "Encrypted payload is required",
                DateTime.UtcNow
            ));
        }

        request = await DecryptEnvelopePayloadAsync<CastVoteRequest>(wrappedDek, encryptedPayload);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to decrypt proxy vote cast payload: {ex.Message}");
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Invalid encrypted payload",
            DateTime.UtcNow
        ));
    }

    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Proxy vote cast attempt - Voter ID: {request.VoterId}, County: {request.County}, Constituency: {request.Constituency}, StationId: {request.PollingStationId}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Proxy vote for: {request.CandidateName} - {request.PartyName} (CandidateId: {request.CandidateId})");

    if (request.CandidateId == Guid.Empty || request.PollingStationId == Guid.Empty)
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: candidate and station IDs are required",
            DateTime.UtcNow
        ));
    }

    var tokenVoterId = user.FindFirst("voterId")?.Value;
    var tokenCounty = user.FindFirst("county")?.Value;
    var tokenConstituency = user.FindFirst("constituency")?.Value;
    var tokenStationId = user.FindFirst("stationId")?.Value;
    var tokenOfficialId = user.FindFirst("officialId")?.Value;

    if (!int.TryParse(tokenVoterId, out var parsedTokenVoterId) || parsedTokenVoterId != request.VoterId)
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: invalid voter token context",
            DateTime.UtcNow
        ));
    }

    if (!string.Equals(tokenCounty, request.County, StringComparison.Ordinal) ||
        !string.Equals(tokenConstituency, request.Constituency, StringComparison.Ordinal))
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: invalid location context",
            DateTime.UtcNow
        ));
    }

    if (!string.IsNullOrWhiteSpace(tokenStationId) &&
        !string.Equals(tokenStationId, request.PollingStationId.ToString(), StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: invalid polling station context",
            DateTime.UtcNow
        ));
    }

    if (!request.VoterDatabaseId.HasValue || request.VoterDatabaseId == Guid.Empty)
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: represented voter identity missing",
            DateTime.UtcNow
        ));
    }

    if (!request.ProxyVoterDatabaseId.HasValue || request.ProxyVoterDatabaseId == Guid.Empty)
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: proxy voter identity missing",
            DateTime.UtcNow
        ));
    }

    if (request.VoterDatabaseId == request.ProxyVoterDatabaseId)
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: proxy voter cannot be the same as represented voter",
            DateTime.UtcNow
        ));
    }

    var representedVoter = await dbContext.Voters.FirstOrDefaultAsync(v => v.VoterId == request.VoterDatabaseId.Value);
    if (representedVoter == null)
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: represented voter not found",
            DateTime.UtcNow
        ));
    }

    if (representedVoter.HasVoted)
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Represented voter has already voted. Please speak to an official.",
            DateTime.UtcNow
        ));
    }

    var proxyVoter = await dbContext.Voters.FirstOrDefaultAsync(v => v.VoterId == request.ProxyVoterDatabaseId.Value);
    if (proxyVoter == null)
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: proxy voter not found",
            DateTime.UtcNow
        ));
    }

    if (string.IsNullOrWhiteSpace(representedVoter.ProxySdi))
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: represented voter has no proxy authorization configured",
            DateTime.UtcNow
        ));
    }

    var representedProxySdi = representedVoter.ProxySdi.Trim().ToLowerInvariant();
    var actualProxySdi = (proxyVoter.Sdi ?? string.Empty).Trim().ToLowerInvariant();
    if (!string.Equals(representedProxySdi, actualProxySdi, StringComparison.Ordinal))
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: proxy voter is not authorized for represented voter",
            DateTime.UtcNow
        ));
    }

    var constituencyId = await dbContext.Constituencies
        .Where(c => EF.Functions.ILike(c.Name, request.Constituency))
        .Select(c => (Guid?)c.ConstituencyId)
        .FirstOrDefaultAsync();

    if (!constituencyId.HasValue)
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: constituency not found",
            DateTime.UtcNow
        ));
    }

    var candidateExists = await dbContext.Candidates.AnyAsync(c =>
        c.CandidateId == request.CandidateId && c.ElectionId == currentElectionId);

    if (!candidateExists)
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: invalid candidate for current election",
            DateTime.UtcNow
        ));
    }

    var pollingStationExists = await dbContext.PollingStations.AnyAsync(ps =>
        ps.PollingStationId == request.PollingStationId && ps.ConstituencyId == constituencyId.Value);

    if (!pollingStationExists)
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: invalid polling station for constituency",
            DateTime.UtcNow
        ));
    }

    var allOfficials = activeOfficials.ToList();
    var officialsWithVoter = allOfficials
        .Where(o => o.Value.ConnectedVoters.Contains(request.VoterId))
        .ToList();

    var officialsInConstituency = officialsWithVoter
        .Where(o => o.Value.Constituency == request.Constituency)
        .ToList();

    if (officialsInConstituency.Count == 0)
    {
        var fallbackOfficials = allOfficials.Where(o =>
            o.Value.Constituency == request.Constituency &&
            (
                (!string.IsNullOrEmpty(tokenOfficialId) && o.Value.OfficialId == tokenOfficialId) ||
                (!string.IsNullOrEmpty(tokenStationId) && o.Value.StationId == tokenStationId)
            ))
            .ToList();

        if (fallbackOfficials.Count > 0)
        {
            foreach (var kvp in fallbackOfficials)
            {
                if (!kvp.Value.ConnectedVoters.Contains(request.VoterId))
                {
                    var healed = kvp.Value with { ConnectedVoters = new List<int>(kvp.Value.ConnectedVoters) { request.VoterId } };
                    activeOfficials[kvp.Key] = healed;
                }
            }

            officialsInConstituency = fallbackOfficials;
        }
    }

    if (officialsInConstituency.Count == 0)
    {
        return Results.BadRequest(new CastVoteResponse(
            false,
            "Vote failed: Voter not properly linked to official system",
            DateTime.UtcNow
        ));
    }

    try
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        representedVoter.HasVoted = true;
        await dbContext.SaveChangesAsync();

        var voteRecord = new VoteRecord
        {
            RecordId = Guid.NewGuid(),
            ElectionId = currentElectionId,
            CandidateId = request.CandidateId,
            ConstituencyId = constituencyId.Value,
            PollingStationId = request.PollingStationId,
            VotedAt = DateTime.UtcNow
        };

        dbContext.VoteRecords.Add(voteRecord);
        await dbContext.SaveChangesAsync();

        await transaction.CommitAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Proxy vote cast failed - database insert error: {ex.Message}");
        return Results.Problem(
            detail: "Vote failed: could not persist vote record",
            statusCode: StatusCodes.Status500InternalServerError
        );
    }

    var voteNotification = new VoteNotification(
        request.VoterId,
        request.CandidateName,
        request.PartyName,
        DateTime.UtcNow,
        string.Empty,
        string.Empty,
        request.County,
        request.Constituency
    );

    var officialQueues = countyVoteChannels.GetOrAdd(request.County, _ => new ConcurrentDictionary<string, ConcurrentBag<VoteNotification>>());

    foreach (var kvp in officialsInConstituency)
    {
        var thisOfficialId = kvp.Value.OfficialId;
        var stationId = kvp.Value.StationId;

        var individualNotification = voteNotification with
        {
            OfficialId = thisOfficialId,
            StationId = stationId
        };

        var officialQueue = officialQueues.GetOrAdd(thisOfficialId, _ => new ConcurrentBag<VoteNotification>());
        officialQueue.Add(individualNotification);
        _ = hubContext.Clients.Group(RealtimeGroups.Official(thisOfficialId)).SendAsync("official.v1.voteReceived", new
        {
            voterId = individualNotification.VoterId,
            candidateName = individualNotification.CandidateName,
            partyName = individualNotification.PartyName,
            timestamp = individualNotification.Timestamp,
            officialId = individualNotification.OfficialId,
            stationId = individualNotification.StationId
        });
    }

    return Results.Ok(new CastVoteResponse(
        true,
        "Proxy vote successfully cast",
        DateTime.UtcNow
    ));
})
.RequireAuthorization(policy => policy.RequireRole("voter"))
.WithName("CastProxyVote");

//===========================================
// DEVICE STATUS ENDPOINT
//===========================================
app.MapPost("/api/voter/send-device-status", (SendDeviceStatusRequest request,
    ClaimsPrincipal user,
    ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
    IHubContext<VotingHub> hubContext,
    [FromServices] ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<DeviceStatusNotification>>>> countyDeviceStatuses) =>
{
    // Validate device status against voter token claims
    var tokenVoterId = user.FindFirst("voterId")?.Value;
    var tokenCounty = user.FindFirst("county")?.Value;
    var tokenConstituency = user.FindFirst("constituency")?.Value;
    var tokenStationId = user.FindFirst("stationId")?.Value;
    var tokenOfficialId = user.FindFirst("officialId")?.Value;

    if (!int.TryParse(tokenVoterId, out var parsedTokenVoterId) || parsedTokenVoterId != request.VoterId)
    {
        return Results.BadRequest(new { 
            success = false, 
            message = "Device status failed: invalid voter token context" 
        });
    }

    if (string.IsNullOrEmpty(tokenCounty) || string.IsNullOrEmpty(tokenConstituency))
    {
        return Results.BadRequest(new { 
            success = false, 
            message = "County or constituency not found in authentication token" 
        });
    }

    // Find officials with this voter connected (same logic as cast-vote)
    var allOfficials = activeOfficials.ToList();
    var officialsWithVoter = allOfficials
        .Where(o => o.Value.ConnectedVoters.Contains(request.VoterId))
        .ToList();
    
    var officialsInConstituency = officialsWithVoter
        .Where(o => o.Value.Constituency == tokenConstituency)
        .ToList();
    
    // Fallback: recover from token claims
    if (officialsInConstituency.Count == 0)
    {
        var fallbackOfficials = allOfficials.Where(o =>
            o.Value.Constituency == tokenConstituency &&
            (
                (!string.IsNullOrEmpty(tokenOfficialId) && o.Value.OfficialId == tokenOfficialId) ||
                (!string.IsNullOrEmpty(tokenStationId) && o.Value.StationId == tokenStationId)
            ))
            .ToList();

        if (fallbackOfficials.Count > 0)
        {
            foreach (var kvp in fallbackOfficials)
            {
                if (!kvp.Value.ConnectedVoters.Contains(request.VoterId))
                {
                    var healed = kvp.Value with { ConnectedVoters = new List<int>(kvp.Value.ConnectedVoters) { request.VoterId } };
                    activeOfficials[kvp.Key] = healed;
                }
            }
            officialsInConstituency = fallbackOfficials;
        }
    }

    if (officialsInConstituency.Count > 0)
    {
        var deviceNotification = new DeviceStatusNotification(
            request.VoterId,
            request.DeviceId,
            request.Status,
            DateTime.UtcNow,
            "",
            "",
            tokenCounty,
            tokenConstituency
        );
        
        var constituencyStatuses = countyDeviceStatuses
            .GetOrAdd(tokenCounty, _ => new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<DeviceStatusNotification>>>())
            .GetOrAdd(tokenConstituency, _ => new ConcurrentDictionary<string, ConcurrentBag<DeviceStatusNotification>>());
        
        foreach (var kvp in officialsInConstituency)
        {
            var officialId = kvp.Value.OfficialId;
            var stationId = kvp.Value.StationId;
            
            var individualNotification = deviceNotification with 
            { 
                OfficialId = officialId, 
                StationId = stationId 
            };
            
            var officialQueue = constituencyStatuses.GetOrAdd(officialId, _ => new ConcurrentBag<DeviceStatusNotification>());
            officialQueue.Add(individualNotification);
            _ = hubContext.Clients.Group(RealtimeGroups.Official(officialId)).SendAsync("official.v1.deviceStatusReceived", new
            {
                voterId = individualNotification.VoterId,
                deviceId = individualNotification.DeviceId,
                status = individualNotification.Status,
                timestamp = individualNotification.Timestamp,
                county = individualNotification.County,
                constituency = individualNotification.Constituency
            });
        }
        
        return Results.Ok(new { 
            success = true, 
            message = "Device status sent successfully",
            timestamp = DateTime.UtcNow 
        });
    }
    
    return Results.BadRequest(new { 
        success = false, 
        message = "Device status failed: Voter not properly linked to official system" 
    });
})
.RequireAuthorization(policy => policy.RequireRole("voter"))
.WithName("VoterSendDeviceStatus");

app.MapGet("/api/voter/pending-device-commands", (ClaimsPrincipal user,
    string deviceId,
    [FromServices] ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<DeviceCommandNotification>>>> countyDeviceCommands) =>
{
    var tokenVoterId = user.FindFirst("voterId")?.Value;
    var tokenCounty = user.FindFirst("county")?.Value;
    var tokenConstituency = user.FindFirst("constituency")?.Value;

    if (!int.TryParse(tokenVoterId, out var parsedTokenVoterId) ||
        string.IsNullOrWhiteSpace(tokenCounty) ||
        string.IsNullOrWhiteSpace(tokenConstituency) ||
        string.IsNullOrWhiteSpace(deviceId))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Invalid voter context or device ID"
        });
    }

    if (!countyDeviceCommands.TryGetValue(tokenCounty, out var byConstituency) ||
        !byConstituency.TryGetValue(tokenConstituency, out var byTarget))
    {
        return Results.Ok(new { success = true, commands = Array.Empty<object>() });
    }

    var targetKey = $"{parsedTokenVoterId}:{deviceId}";
    if (!byTarget.TryGetValue(targetKey, out var queue))
    {
        return Results.Ok(new { success = true, commands = Array.Empty<object>() });
    }

    var drained = new List<DeviceCommandNotification>();
    while (queue.TryTake(out var command))
    {
        drained.Add(command);
    }

    var payload = drained
        .OrderBy(c => c.Timestamp)
        .Select(c => new
        {
            success = true,
            commandType = c.CommandType,
            officialId = c.OfficialId,
            message = c.Message,
            data = new
            {
                c.Timestamp,
                c.DeviceId,
                c.VoterId
            }
        })
        .ToList();

    return Results.Ok(new
    {
        success = true,
        commands = payload
    });
})
.RequireAuthorization(policy => policy.RequireRole("voter"))
.WithName("VoterGetPendingDeviceCommands");






app.MapPost("/api/official/send-device-command", (SendDeviceCommandRequest request,
    ClaimsPrincipal user,
    ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
    IHubContext<VotingHub> hubContext,
    [FromServices] ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<DeviceCommandNotification>>>> countyDeviceCommands) =>
{
    var county = user.FindFirst("county")?.Value;
    var constituency = user.FindFirst("constituency")?.Value;
    var officialId = user.FindFirst("officialId")?.Value ?? "Unknown";
    var stationId = user.FindFirst("station")?.Value;

    if (string.IsNullOrWhiteSpace(county) || string.IsNullOrWhiteSpace(constituency))
    {
        return Results.BadRequest(new { success = false, message = "County or constituency not found in authentication token" });
    }

    if (request.VoterId <= 0 || string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.CommandType))
    {
        return Results.BadRequest(new { success = false, message = "VoterId, DeviceId, and CommandType are required" });
    }

    var normalizedCommand = request.CommandType.Trim().ToLowerInvariant();
    if (normalizedCommand != "lock_device" && normalizedCommand != "unlock_device")
    {
        return Results.BadRequest(new { success = false, message = "Unsupported command type" });
    }

    var linkedOfficial = activeOfficials
        .Where(kvp =>
            kvp.Key.StartsWith($"{county}_", StringComparison.Ordinal) &&
            kvp.Value.OfficialId == officialId &&
            kvp.Value.Constituency == constituency)
        .Select(kvp => kvp.Value)
        .FirstOrDefault();

    if (string.IsNullOrWhiteSpace(linkedOfficial.OfficialId) || !linkedOfficial.ConnectedVoters.Contains(request.VoterId))
    {
        // Fallback recovery if in-memory link was reset while token/session is still valid.
        var fallbackOfficials = activeOfficials
            .Where(kvp =>
                kvp.Key.StartsWith($"{county}_", StringComparison.Ordinal) &&
                kvp.Value.OfficialId == officialId &&
                kvp.Value.Constituency == constituency &&
                (string.IsNullOrEmpty(stationId) || kvp.Value.StationId == stationId))
            .ToList();

        if (fallbackOfficials.Count > 0)
        {
            foreach (var kvp in fallbackOfficials)
            {
                if (!kvp.Value.ConnectedVoters.Contains(request.VoterId))
                {
                    var healed = kvp.Value with { ConnectedVoters = new List<int>(kvp.Value.ConnectedVoters) { request.VoterId } };
                    activeOfficials[kvp.Key] = healed;
                }
            }

            linkedOfficial = fallbackOfficials
                .Select(kvp => activeOfficials[kvp.Key])
                .FirstOrDefault(v => v.ConnectedVoters.Contains(request.VoterId));
        }

        if (string.IsNullOrWhiteSpace(linkedOfficial.OfficialId) || !linkedOfficial.ConnectedVoters.Contains(request.VoterId))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Rejecting device command from official {officialId}: voter {request.VoterId} not linked");
            return Results.BadRequest(new { success = false, message = "Target voter is not linked to this official" });
        }
    }

    var targetKey = $"{request.VoterId}:{request.DeviceId}";
    var queueByCounty = countyDeviceCommands.GetOrAdd(county, _ => new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<DeviceCommandNotification>>>());
    var queueByConstituency = queueByCounty.GetOrAdd(constituency, _ => new ConcurrentDictionary<string, ConcurrentBag<DeviceCommandNotification>>());
    var targetQueue = queueByConstituency.GetOrAdd(targetKey, _ => new ConcurrentBag<DeviceCommandNotification>());

    var command = new DeviceCommandNotification(
        request.VoterId,
        request.DeviceId,
        normalizedCommand,
        DateTime.UtcNow,
        county,
        constituency,
        officialId,
        normalizedCommand == "lock_device" ? "Device locked by official" : "Device unlocked by official"
    );

    targetQueue.Add(command);

    _ = hubContext.Clients.Groups(
        RealtimeGroups.Voter(request.VoterId.ToString()),
        RealtimeGroups.VoterDevice(request.VoterId.ToString(), request.DeviceId)
    ).SendAsync("voter.v1.deviceCommandReceived", new
    {
        success = true,
        commandType = command.CommandType,
        officialId = command.OfficialId,
        message = command.Message,
        data = new
        {
            command.Timestamp,
            command.DeviceId
        }
    });

    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Queued command '{normalizedCommand}' for voter {request.VoterId}, device {request.DeviceId} from official {officialId}");

    return Results.Ok(new
    {
        success = true,
        message = "Command queued",
        commandType = normalizedCommand,
        voterId = request.VoterId,
        deviceId = request.DeviceId,
        timestamp = command.Timestamp
    });
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialSendDeviceCommand");

app.MapGet("/api/official/polling-station-vote-count", async (
    ClaimsPrincipal user,
    DatabaseService dbService) =>
{
    var stationId = user.FindFirst("station")?.Value;

    if (string.IsNullOrWhiteSpace(stationId) || !Guid.TryParse(stationId, out var pollingStationId))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Polling station claim missing or invalid"
        });
    }

    var totalVotes = await dbService.GetVoteRecordsCountByPollingStationAsync(pollingStationId);
    var expectedVotes = await dbService.GetExpectedVotesByPollingStationAsync(pollingStationId);

    return Results.Ok(new
    {
        success = true,
        pollingStationId,
        totalVotes,
        expectedVotes
    });
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialGetPollingStationVoteCount");


app.MapGet("/api/official/election-statistics", async (
    ClaimsPrincipal user,
    Guid? electionId,
    DatabaseService dbService,
    ConnectionRegistry connectionRegistry) =>
{
    var stationId = user.FindFirst("station")?.Value;
    var constituencyName = user.FindFirst("constituency")?.Value;

    if (string.IsNullOrWhiteSpace(stationId) || !Guid.TryParse(stationId, out var pollingStationId))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Polling station claim missing or invalid"
        });
    }

    try
    {
        // Get basic election statistics
        var (statsSuccess, resolvedElectionId, totalVotes, _, _) = 
            await dbService.GetElectionStatisticsAsync(pollingStationId, electionId);

        if (!statsSuccess || !resolvedElectionId.HasValue)
        {
            return Results.NotFound(new
            {
                success = false,
                message = electionId.HasValue
                    ? $"Election {electionId.Value} not found"
                    : "No active election found"
            });
        }

        // Election-wide leaderboard and constituency performance.
        var candidateVotes = await dbService.GetVotesByCandidate(resolvedElectionId.Value);
        var constituencyStats = await dbService.GetConstituencyStats(resolvedElectionId.Value);

        totalVotes = constituencyStats.Sum(c => c.TotalVotes);
        var registeredVoters = await dbService.GetRegisteredVotersCountAsync();
        var totalPollingStations = await dbService.GetTotalPollingStationsCountAsync();
        
        // Get connected stations and ensure the requesting station is included
        var connectedStations = new HashSet<string>(connectionRegistry.GetConnectedStationIds(), StringComparer.OrdinalIgnoreCase);
        connectedStations.Add(stationId); // Always include the requesting station
        var pollingStationsConnected = connectedStations.Count;

        var topConstituencies = constituencyStats
            .Where(c => c.ExpectedVotes > 0)
            .Select(c => new
            {
                constituencyId = c.ConstituencyId,
                constituencyName = c.ConstituencyName,
                totalVotes = c.TotalVotes,
                expectedVotes = c.ExpectedVotes,
                turnoutRate = Math.Round((decimal)c.TotalVotes / c.ExpectedVotes * 100, 2)
            })
            .OrderByDescending(c => c.turnoutRate)
            .ThenByDescending(c => c.totalVotes)
            .Take(12)
            .ToList();

        var countryPopulation = await dbService.GetCountryPopulationAsync();

        // Turnout is measured against country population for the dashboard.
        decimal turnoutRate = countryPopulation > 0 ? (decimal)totalVotes / countryPopulation * 100 : 0;
        decimal pollingStationConnectionRate = totalPollingStations > 0 ? (decimal)pollingStationsConnected / totalPollingStations * 100 : 0;
        decimal populationParticipationRate = countryPopulation > 0 ? (decimal)totalVotes / countryPopulation * 100 : 0;

        var bestConstituency = topConstituencies.FirstOrDefault();

        return Results.Ok(new
        {
            success = true,
            electionId = resolvedElectionId.Value,
            pollingStationId,
            totalVotes,
            registeredVoters,
            turnoutRate = Math.Round(turnoutRate, 6),
            pollingStationsConnected,
            totalPollingStations,
            pollingStationConnectionRate = Math.Round(pollingStationConnectionRate, 2),
            candidateVotes,
            constituencyName,
            topConstituencies,
            countryPopulation,
            populationParticipationRate = Math.Round(populationParticipationRate, 6),
            bestConstituency
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error fetching election statistics: {ex.Message}");
        return Results.Json(new
        {
            success = false,
            message = "Error fetching statistics",
            error = ex.Message
        }, statusCode: StatusCodes.Status500InternalServerError);
    }
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialGetElectionStatistics");

app.MapPost("/api/official/scan-duplicate-voter-fingerprints", async (
    ClaimsPrincipal user,
    ApplicationDbContext dbContext) =>
{
    const double MATCH_THRESHOLD = 40.0;

    var officialId = user.FindFirst("officialId")?.Value ?? "unknown";
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 Duplicate voter fingerprint scan started by official {officialId}");

    try
    {
        var allVoters = await dbContext.Voters
            .AsTracking()
            .Where(v => !string.IsNullOrWhiteSpace(v.Sdi))
            .ToListAsync();

        var candidateVoters = allVoters
            .Where(v => v.FingerprintScan != null && v.FingerprintScan.Length > 0)
            .ToList();

        if (candidateVoters.Count < 2)
        {
            return Results.Ok(new
            {
                success = true,
                message = "Not enough voter fingerprints to compare.",
                totalVotersConsidered = allVoters.Count,
                comparableVoters = candidateVoters.Count,
                comparisonsPerformed = 0,
                matchedGroupCount = 0,
                suspiciousRecordCount = 0,
                duplicateSdiGroups = Array.Empty<string>(),
                duplicateIdentityGroups = Array.Empty<List<string>>()
            });
        }

        var comparableEntries = new List<(Voter Voter, FingerprintTemplate Template)>();
        var failedDecryptions = 0;

        foreach (var voter in candidateVoters)
        {
            try
            {
                var fingerprintBytes = await DecryptVoterFingerprintAsync(voter);
                var image = new FingerprintImage(fingerprintBytes);
                var template = new FingerprintTemplate(image);
                comparableEntries.Add((voter, template));
            }
            catch (Exception ex)
            {
                failedDecryptions++;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Skipping voter {voter.VoterId} during duplicate scan: {ex.Message}");
            }
        }

        if (comparableEntries.Count < 2)
        {
            return Results.Ok(new
            {
                success = true,
                message = "Not enough decryptable voter fingerprints to compare.",
                totalVotersConsidered = allVoters.Count,
                comparableVoters = comparableEntries.Count,
                comparisonsPerformed = 0,
                matchedGroupCount = 0,
                suspiciousRecordCount = 0,
                failedDecryptions,
                duplicateSdiGroups = Array.Empty<string>(),
                duplicateIdentityGroups = Array.Empty<List<string>>()
            });
        }

        var count = comparableEntries.Count;
        var parent = Enumerable.Range(0, count).ToArray();
        int comparisonsPerformed = 0;

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        void Union(int a, int b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA != rootB)
            {
                parent[rootB] = rootA;
            }
        }

        for (int i = 0; i < count - 1; i++)
        {
            var matcher = new FingerprintMatcher(comparableEntries[i].Template);
            for (int j = i + 1; j < count; j++)
            {
                comparisonsPerformed++;
                var score = matcher.Match(comparableEntries[j].Template);
                if (score >= MATCH_THRESHOLD)
                {
                    Union(i, j);
                }
            }
        }

        var groupedByRoot = new Dictionary<int, List<Voter>>();
        for (int i = 0; i < count; i++)
        {
            var root = Find(i);
            if (!groupedByRoot.TryGetValue(root, out var voters))
            {
                voters = new List<Voter>();
                groupedByRoot[root] = voters;
            }

            voters.Add(comparableEntries[i].Voter);
        }

        var duplicateSdiGroups = new List<string>();
        var suspiciousSdiSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupedByRoot.Values.Where(g => g.Count > 1))
        {
            var sdis = group
                .Select(v => v.Sdi?.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sdis.Count < 2)
            {
                continue;
            }

            foreach (var sdi in sdis)
            {
                suspiciousSdiSet.Add(sdi);
            }

            duplicateSdiGroups.Add(string.Join(",", sdis));
        }

        int suspiciousRecordCount = 0;
        if (suspiciousSdiSet.Count > 0)
        {
            var suspiciousRows = allVoters
                .Where(v => !string.IsNullOrWhiteSpace(v.Sdi) && suspiciousSdiSet.Contains(v.Sdi!.Trim()))
                .ToList();

            foreach (var row in suspiciousRows)
            {
                if (!row.UnderSuspicion)
                {
                    row.UnderSuspicion = true;
                }
            }

            suspiciousRecordCount = suspiciousRows.Count;
            await dbContext.SaveChangesAsync();
        }

        var duplicateIdentityGroups = new List<List<string>>();
        if (duplicateSdiGroups.Count > 0)
        {
            var sdiQueueByValue = allVoters
                .Where(v => !string.IsNullOrWhiteSpace(v.Sdi))
                .GroupBy(v => v.Sdi!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => new Queue<Voter>(g),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var sdiGroup in duplicateSdiGroups)
            {
                var readableEntries = new List<string>();
                var sdiTokens = sdiGroup
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var sdiToken in sdiTokens)
                {
                    if (!sdiQueueByValue.TryGetValue(sdiToken, out var queue) || queue.Count == 0)
                    {
                        continue;
                    }

                    var voter = queue.Dequeue();
                    try
                    {
                        var (firstName, lastName, nationalInsuranceNumber) = await DecryptVoterIdentityAsync(voter);
                        var fullName = $"{firstName} {lastName}".Trim();
                        if (string.IsNullOrWhiteSpace(fullName))
                        {
                            fullName = "[Name unavailable]";
                        }

                        var niDisplay = string.IsNullOrWhiteSpace(nationalInsuranceNumber)
                            ? "[NI unavailable]"
                            : nationalInsuranceNumber;

                        readableEntries.Add($"{fullName} | NI: {niDisplay}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Failed to decrypt identity fields for SDI {sdiToken}: {ex.Message}");
                        readableEntries.Add($"[Decrypt failed] SDI: {sdiToken}");
                    }
                }

                if (readableEntries.Count > 0)
                {
                    duplicateIdentityGroups.Add(readableEntries);
                }
            }
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Duplicate scan complete. Compared {comparisonsPerformed} pairs, matched groups: {duplicateSdiGroups.Count}, suspicious rows: {suspiciousRecordCount}");

        return Results.Ok(new
        {
            success = true,
            message = duplicateSdiGroups.Count > 0
                ? "Duplicate fingerprint groups found and flagged."
                : "No duplicate fingerprints found.",
            totalVotersConsidered = allVoters.Count,
            comparableVoters = comparableEntries.Count,
            comparisonsPerformed,
            matchedGroupCount = duplicateSdiGroups.Count,
            suspiciousRecordCount,
            failedDecryptions,
            duplicateSdiGroups,
            duplicateIdentityGroups
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Duplicate voter fingerprint scan failed: {ex.Message}");
        return Results.Json(new
        {
            success = false,
            message = $"Duplicate fingerprint scan failed: {ex.Message}"
        }, statusCode: StatusCodes.Status500InternalServerError);
    }
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialScanDuplicateVoterFingerprints");


app.MapPost("/api/official/generate-code", (GenerateCodeRequest request, OfficialService officialService, ClaimsPrincipal user, IHubContext<VotingHub> hubContext) =>
{
    var officialId = user.FindFirst("officialId")?.Value ?? "Unknown";
    var stationId = user.FindFirst("station")?.Value ?? "Unknown";
    var county = user.FindFirst("county")?.Value;
    var constituency = user.FindFirst("constituency")?.Value;
    
    if (string.IsNullOrEmpty(county) || string.IsNullOrEmpty(constituency))
    {
        return Results.BadRequest(new { success = false, message = "County or constituency not found in authentication token" });
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} (Station: {stationId}) generating code for voter {request.VoterId} in {county}/{constituency}");
    
    var (success, code) = officialService.GenerateAccessCode(request.VoterId, county, constituency);
    
    if (success)
    {
        _ = hubContext.Clients.Group(RealtimeGroups.Voter(request.VoterId)).SendAsync("voter.v1.accessCodeGenerated", new
        {
            success = true,
            code,
            message = "Access code available"
        });

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} successfully generated code {code} for voter {request.VoterId} in {county}/{constituency}");
        return Results.Ok(new { success = true, code = code, voterId = request.VoterId });
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} failed to generate code for voter {request.VoterId} in {county}/{constituency}");
    return Results.BadRequest(new { success = false, message = "Failed to generate code" });
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialGenerateCode");

app.MapPost("/api/voter/request-access", async (VoterAccessRequest request, VoterService voterService, ClaimsPrincipal user,
    ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
    IHubContext<VotingHub> hubContext) =>
{
    var county = user.FindFirst("county")?.Value;
    var constituency = user.FindFirst("constituency")?.Value;
    if (string.IsNullOrEmpty(county) || string.IsNullOrEmpty(constituency))
    {
        return Results.BadRequest(new { success = false, message = "County or constituency not found in authentication token" });
    }

    var success = await voterService.RequestAccess(request.VoterId, county, constituency, request.DeviceName);
    
    if (success)
    {
        var requestMessage = $"Voter {request.VoterId} requesting access from {request.DeviceName}";
        var targetOfficials = activeOfficials
            .Where(o => o.Key.StartsWith($"{county}_", StringComparison.Ordinal) && o.Value.Constituency == constituency)
            .Select(o => o.Value.OfficialId)
            .Distinct()
            .ToList();

        foreach (var targetOfficialId in targetOfficials)
        {
            await hubContext.Clients.Group(RealtimeGroups.Official(targetOfficialId)).SendAsync("official.v1.voterRequestReceived", new
            {
                request = requestMessage,
                voterId = request.VoterId,
                deviceName = request.DeviceName,
                county,
                constituency,
                timestamp = DateTime.UtcNow
            });
        }

        return Results.Ok(new { success = true, message = "Access request sent to official" });
    }
    
    return Results.BadRequest(new { success = false, message = "Failed to process access request" });
})
.RequireAuthorization(policy => policy.RequireRole("voter"))
.WithName("VoterRequestAccess");

//===========================================
// API ENDPOINTS - HEALTH & TESTING
//===========================================
app.MapGet("/securevote", () =>
{
    return new { status = "connected", message = "SecureVote Server Ready", timestamp = DateTime.Now };
})
.WithName("GetSecureVoteData");

app.MapGet("/securevote/api/health", () =>
{
    return new { status = "healthy", timestamp = DateTime.Now };
})
.WithName("SecureVoteHealthCheck");

//===========================================
// API ENDPOINTS - DATA RETRIEVAL
//===========================================
app.MapGet("/api/official/database", async (DatabaseService db) =>
{
    var voters = await db.GetAllVotersAsync();
    return Results.Ok(voters);
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("GetAllVoters");

// Fetch all polling stations for dropdown in official creation
app.MapGet("/api/polling-stations", async (DatabaseService db) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] GET /api/polling-stations - Fetching polling stations for official app");
    
    var pollingStations = await db.GetAllPollingStationsAsync();
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Returning {pollingStations.Count} polling stations");
    return Results.Ok(pollingStations);
})
.WithName("GetPollingStations");

// Fetch candidates for the current election
app.MapGet("/api/candidates", async (DatabaseService db) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] GET /api/candidates - Fetching candidates for election ID: {currentElectionId}");
    
    var candidates = await db.GetCandidatesByElectionIdAsync(currentElectionId);
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Returning {candidates.Count} candidates");
    return Results.Ok(candidates);
})
.WithName("GetCandidates");

//===========================================
// API ENDPOINTS - OFFICIAL DATA UPDATE
//===========================================
app.MapPost("/api/official/upload-fingerprint", async (HttpContext httpContext, DatabaseService dbService) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ================== FINGERPRINT UPLOAD ENDPOINT CALLED ==================");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📸 Fingerprint upload request received");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Content-Type: {httpContext.Request.ContentType}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Content-Length: {httpContext.Request.ContentLength}");

    string username = string.Empty;
    string password = string.Empty;
    string encryptedFingerprintBase64 = string.Empty;
    
    try
    {
        // Parse raw body manually so this endpoint still runs even if body binding would fail.
        httpContext.Request.EnableBuffering();
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Raw JSON length: {rawBody.Length}");
        if (rawBody.Length > 0)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Raw JSON preview: {rawBody.Substring(0, Math.Min(300, rawBody.Length))}...");
        }

        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;

        if (!TryReadEncryptedEnvelope(root, out var wrappedDek, out var encryptedPayload))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Missing encrypted envelope");
            return Results.BadRequest(new
            {
                success = false,
                message = "Encrypted payload is required"
            });
        }

        var decryptedRequest = await DecryptEnvelopePayloadAsync<UpdateFingerprintRequest>(wrappedDek, encryptedPayload);

        username = decryptedRequest.Username ?? string.Empty;
        password = decryptedRequest.Password ?? string.Empty;
        encryptedFingerprintBase64 = decryptedRequest.EncryptedFingerPrintScan ?? string.Empty;

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Parsed username present: {!string.IsNullOrEmpty(username)}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Parsed password present: {!string.IsNullOrEmpty(password)}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Parsed encrypted payload and decrypted request");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Missing credentials");
            return Results.BadRequest(new {
                success = false,
                message = "Username and password are required"
            });
        }

        if (!string.Equals(decryptedRequest.EncryptionMode, "CLIENT_DEK_RSA", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(decryptedRequest.KeyVersion) ||
            string.IsNullOrWhiteSpace(decryptedRequest.KeyId) ||
            string.IsNullOrWhiteSpace(decryptedRequest.WrappedDek) ||
            string.IsNullOrWhiteSpace(encryptedFingerprintBase64))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Missing encrypted fingerprint data");
            return Results.BadRequest(new {
                success = false,
                message = "Encrypted fingerprint payload is required"
            });
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Updating encrypted fingerprint in database...");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Username: '{username}'");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Encrypted fingerprint payload size: {encryptedFingerprintBase64.Length} chars (base64)");
        
        bool updateSuccessful = await dbService.UpdateOfficialFingerprintAsync(
            username,
            password,
            decryptedRequest.KeyId!,
            decryptedRequest.WrappedDek!,
            encryptedFingerprintBase64
        );
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] DatabaseService returned: {updateSuccessful}");
        
        if (updateSuccessful)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Fingerprint upload successful for {username}");
            return Results.Ok(new { 
                success = true, 
                message = "Fingerprint uploaded successfully",
                dataSize = encryptedFingerprintBase64.Length
            });
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Authentication failed for {username}");
            return Results.Json(new {
                success = false, 
                message = "Invalid username or password" 
            }, statusCode: StatusCodes.Status401Unauthorized);
        }
    }
    catch (FormatException ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Invalid base64 format: {ex.Message}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Stack trace: {ex.StackTrace}");
        return Results.BadRequest(new { 
            success = false, 
            message = "FingerPrintScan must be valid base64",
            error = ex.Message
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error uploading fingerprint: {ex.Message}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Exception type: {ex.GetType().FullName}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Stack trace: {ex.StackTrace}");
        return Results.BadRequest(new { 
            success = false, 
            message = $"Fingerprint upload failed: {ex.Message}",
            error = ex.ToString()
        });
    }
    finally
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ================== FINGERPRINT UPLOAD ENDPOINT COMPLETE ==================");
    }
})
.WithName("OfficialUploadFingerprint");

//===========================================
// API ENDPOINTS - FINGERPRINT VERIFICATION
//===========================================
app.MapPost("/api/verify-prints", async (HttpContext httpContext, DatabaseService dbService) =>
{
    const double MATCH_THRESHOLD = 40.0;

    VerifyFingerprintsRequest request;
    try
    {
        httpContext.Request.EnableBuffering();
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;

        if (!TryReadEncryptedEnvelope(root, out var wrappedDek, out var encryptedPayload))
        {
            return Results.BadRequest(new
            {
                success = false,
                message = "Encrypted payload is required"
            });
        }

        request = await DecryptEnvelopePayloadAsync<VerifyFingerprintsRequest>(wrappedDek, encryptedPayload);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to decrypt verify-prints payload: {ex.Message}");
        return Results.BadRequest(new
        {
            success = false,
            message = "Invalid encrypted payload"
        });
    }
    
    try
    {
        // Validate UserType field
        if (string.IsNullOrEmpty(request.UserType))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Missing UserType indicator");
            return Results.BadRequest(new { 
                success = false, 
                message = "UserType (official/voter) is required" 
            });
        }

        if (request.UserType != "official" && request.UserType != "voter")
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Invalid UserType: {request.UserType}");
            return Results.BadRequest(new { 
                success = false, 
                message = "UserType must be either 'official' or 'voter'" 
            });
        }

        if (string.IsNullOrWhiteSpace(request.ScannedFingerprint))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Missing scanned fingerprint in decrypted payload");
            return Results.BadRequest(new { 
                success = false, 
                message = "ScannedFingerprint is required" 
            });
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fingerprint verification request - UserType: {request.UserType}");

        byte[]? storedFingerprintBytes = null;
        string userIdentifier = "";
        string userType = request.UserType;

        // Branch logic based on UserType
        if (request.UserType == "official")
        {
            // OFFICIAL VERIFICATION PATH
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔐 Processing OFFICIAL fingerprint verification");

            // Validate official credentials
            if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Missing username or password for official");
                return Results.BadRequest(new { 
                    success = false, 
                    message = "Username and password are required for officials" 
                });
            }

            // Fetch official from database
            var official = await dbService.GetOfficialByCredentialsAsync(request.Username, request.Password);
            
            if (official == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Record not found - no official with credentials for {request.Username}");
                return Results.BadRequest(new { 
                    success = false, 
                    message = "Record not found" 
                });
            }

            // Get stored fingerprint from database
            if (official.FingerPrintScan == null || official.FingerPrintScan.Length == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ No stored fingerprint found for official {request.Username}");
                return Results.BadRequest(new { 
                    success = false, 
                    message = "No stored fingerprint on record" 
                });
            }

                try
                {
                    storedFingerprintBytes = await DecryptOfficialFingerprintAsync(official);
                }
                catch (Exception ex)
                {
                    var wrappedDekLength = official.WrappedDek?.Length ?? 0;
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Failed to decrypt official fingerprint for official {official.OfficialId}: {ex.Message}");
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   KeyId={official.KeyId ?? "<null>"}, WrappedDekBytes={wrappedDekLength}");
                    return Results.Json(new
                    {
                        success = false,
                        message = "Official fingerprint decryption unavailable. Configure official encryption private key.",
                        code = "OFFICIAL_DECRYPTION_UNAVAILABLE"
                    }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                userIdentifier = official.OfficialId.ToString();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official found, retrieving stored fingerprint ({storedFingerprintBytes.Length} bytes)");
        }
        else if (request.UserType == "voter")
        {
            // VOTER VERIFICATION PATH
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🗳️  Processing VOTER fingerprint verification");

            var hasSingleVoterId = !string.IsNullOrWhiteSpace(request.VoterId);
            var candidateIdStrings = request.CandidateVoterIds ?? new List<string>();
            var hasCandidateList = candidateIdStrings.Count > 0;

            if (hasSingleVoterId == hasCandidateList)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Invalid voter verification payload - expected either VoterId or CandidateVoterIds");
                return Results.BadRequest(new { 
                    success = false, 
                    message = "Provide either VoterId or CandidateVoterIds for voter verification" 
                });
            }

            var candidateGuids = new List<Guid>();

            if (hasSingleVoterId)
            {
                if (!Guid.TryParse(request.VoterId, out Guid voterGuid))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Invalid VoterId format: {request.VoterId}");
                    return Results.BadRequest(new {
                        success = false,
                        message = "VoterId must be a valid GUID"
                    });
                }

                candidateGuids.Add(voterGuid);
            }
            else
            {
                var seen = new HashSet<Guid>();
                foreach (var candidateId in candidateIdStrings)
                {
                    if (!Guid.TryParse(candidateId, out var parsedId))
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Invalid candidate voter ID format: {candidateId}");
                        return Results.BadRequest(new
                        {
                            success = false,
                            message = "CandidateVoterIds must contain valid GUID values"
                        });
                    }

                    if (seen.Add(parsedId))
                    {
                        candidateGuids.Add(parsedId);
                    }
                }
            }

            if (candidateGuids.Count == 0)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = "No valid voter candidates provided"
                });
            }

            if (candidateGuids.Count > 10)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = "Too many candidate voters provided"
                });
            }

            // Decode scanned fingerprint once, then compare against all candidates.
            var scannedFingerprintBytesForCandidates = Convert.FromBase64String(request.ScannedFingerprint);
            var scannedImageForCandidates = new FingerprintImage(scannedFingerprintBytesForCandidates);
            var scannedTemplateForCandidates = new FingerprintTemplate(scannedImageForCandidates);
            var matcherForCandidates = new FingerprintMatcher(scannedTemplateForCandidates);

            var candidateVoters = await dbService.GetVotersByIdsAsync(candidateGuids);
            if (candidateVoters.Count == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Record not found - no voter records for supplied candidate IDs");
                return Results.BadRequest(new {
                    success = false,
                    message = "Record not found"
                });
            }

            var orderedCandidates = candidateGuids
                .Select(id => candidateVoters.FirstOrDefault(v => v.VoterId == id))
                .Where(v => v != null)
                .Cast<Voter>()
                .ToList();

            if (orderedCandidates.Count == 0)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = "Record not found"
                });
            }

            var bestScore = 0.0;
            var bestMargin = 0.0;

            foreach (var candidate in orderedCandidates)
            {
                if (candidate.FingerprintScan == null || candidate.FingerprintScan.Length == 0)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Candidate {candidate.VoterId} has no stored fingerprint; skipping");
                    continue;
                }

                byte[] decryptedCandidatePrint;
                try
                {
                    decryptedCandidatePrint = await DecryptVoterFingerprintAsync(candidate);
                }
                catch (Exception ex)
                {
                    var wrappedDekLength = candidate.WrappedDek?.Length ?? 0;
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Failed to decrypt voter fingerprint for candidate {candidate.VoterId}: {ex.Message}");
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   KeyId={candidate.KeyId ?? "<null>"}, WrappedDekBytes={wrappedDekLength}");
                    continue;
                }

                var candidateStoredImage = new FingerprintImage(decryptedCandidatePrint);
                var candidateStoredTemplate = new FingerprintTemplate(candidateStoredImage);
                var candidateScore = matcherForCandidates.Match(candidateStoredTemplate);
                var candidateIsMatch = candidateScore >= MATCH_THRESHOLD;
                var candidateMargin = candidateIsMatch ? candidateScore - MATCH_THRESHOLD : MATCH_THRESHOLD - candidateScore;

                if (candidateScore > bestScore)
                {
                    bestScore = candidateScore;
                    bestMargin = candidateMargin;
                }

                if (!candidateIsMatch)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Candidate no-match - VOTER: {candidate.VoterId} (Score: {candidateScore:F2})");
                    continue;
                }

                if (candidate.HasVoted)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ FINGERPRINT MATCHED ALREADY-VOTED VOTER: {candidate.VoterId}");
                    return Results.BadRequest(new
                    {
                        success = false,
                        isMatch = true,
                        userType = "voter",
                        matchedVoterId = candidate.VoterId,
                        message = "You have already voted. Please speak to an official.",
                        score = Math.Round(candidateScore, 2),
                        threshold = MATCH_THRESHOLD,
                        margin = Math.Round(candidateMargin, 2)
                    });
                }

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ FINGERPRINT MATCH - VOTER: {candidate.VoterId} (Score: {candidateScore:F2})");
                return Results.Ok(new
                {
                    success = true,
                    isMatch = true,
                    userType = "voter",
                    matchedVoterId = candidate.VoterId,
                    message = "Fingerprint match",
                    score = Math.Round(candidateScore, 2),
                    threshold = MATCH_THRESHOLD,
                    margin = Math.Round(candidateMargin, 2),
                    timestamp = DateTime.Now
                });
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ FINGERPRINT NO MATCH - VOTER candidates exhausted ({orderedCandidates.Count} checked)");
            return Results.BadRequest(new
            {
                success = false,
                isMatch = false,
                userType = "voter",
                message = "Fingerprint scan is not a match",
                score = Math.Round(bestScore, 2),
                threshold = MATCH_THRESHOLD,
                margin = Math.Round(bestMargin, 2)
            });
        }

        // COMMON FINGERPRINT COMPARISON LOGIC (applies to both official and voter)
        if (storedFingerprintBytes == null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Failed to retrieve stored fingerprint");
            return Results.BadRequest(new { success = false, message = "Failed to retrieve stored fingerprint" });
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Decoding scanned fingerprint from decrypted payload...");
        byte[] scannedFingerprintBytes = Convert.FromBase64String(request.ScannedFingerprint);

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Scanned fingerprint size: {scannedFingerprintBytes.Length} bytes");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Stored fingerprint size: {storedFingerprintBytes.Length} bytes");

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Loading fingerprint images...");
        
        // Load both fingerprints and create fingerprint objects
        var scannedImage = new FingerprintImage(scannedFingerprintBytes);
        var storedImage = new FingerprintImage(storedFingerprintBytes);

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Extracting fingerprint features...");
        var scannedTemplate = new FingerprintTemplate(scannedImage);
        var storedTemplate = new FingerprintTemplate(storedImage);

        // Compare fingerprints
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Comparing scanned fingerprint against stored fingerprint...");
        var matcher = new FingerprintMatcher(scannedTemplate);
        double score = matcher.Match(storedTemplate);

        // Determine if match
        bool isMatch = score >= MATCH_THRESHOLD;
        double margin = isMatch ? score - MATCH_THRESHOLD : MATCH_THRESHOLD - score;

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fingerprint comparison complete - Score: {score:F2}, Match: {isMatch}");

        if (isMatch)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ FINGERPRINT MATCH - {userType.ToUpper()}: {userIdentifier}");
            return Results.Ok(new 
            { 
                success = true, 
                isMatch = true,
                userType = userType,
                message = "Fingerprint match",
                score = Math.Round(score, 2),
                threshold = MATCH_THRESHOLD,
                margin = Math.Round(margin, 2),
                timestamp = DateTime.Now
            });
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ FINGERPRINT NO MATCH - {userType.ToUpper()}: {userIdentifier} (Score: {score:F2})");
            return Results.BadRequest(new { 
                success = false, 
                isMatch = false,
                userType = userType,
                message = "Fingerprint scan is not a match",
                score = Math.Round(score, 2),
                threshold = MATCH_THRESHOLD,
                margin = Math.Round(margin, 2)
            });
        }
    }
    catch (FormatException ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Invalid base64 format: {ex.Message}");
        return Results.BadRequest(new { 
            success = false, 
            message = "Invalid base64 format for scanned fingerprint" 
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Error during fingerprint verification: {ex.Message}");
        return Results.BadRequest(new { 
            success = false, 
            message = $"Fingerprint verification failed: {ex.Message}" 
        });
    }
})
.WithName("VerifyFingerprints");

app.MapHub<VotingHub>("/hubs/voting");

// Prevent production exception handler from returning 404 when /Error is invoked.
app.Map("/Error", () => Results.Problem("An internal server error occurred."));

//===========================================
// START APPLICATION
//===========================================
app.Run();

//===========================================
// DATA MODELS & RECORDS
//===========================================
record OfficialLoginRequest(
    string Username,
    string Password
);

record CreateVoterRequest(
    string NationalInsuranceNumber,
    string FirstName,
    string LastName,
    string DateOfBirth,
    string TownOfBirth,
    string PostCode,
    string County,
    string Constituency,
    string FingerPrintScan,
    string? CountyHash,
    string? ConstituencyHash,
    string? EncryptionMode,
    string? KeyVersion,
    string? KeyId,
    string? WrappedDek,
    string? EncryptedNationalInsuranceNumber,
    string? EncryptedFirstName,
    string? EncryptedLastName,
    string? EncryptedDateOfBirth,
    string? EncryptedTownOfBirth,
    string? EncryptedPostCode,
    string? EncryptedCounty,
    string? EncryptedConstituency,
    string? EncryptedFingerPrintScan
);

record CreateOfficialRequest(
    string Username,
    string Password,
    string AssignedPollingStationId,
    string? EncryptionMode,
    string? KeyVersion,
    string? KeyId,
    string? WrappedDek,
    string? EncryptedFingerPrintScan
);

record VoterAccessRequest(
    string VoterId,
    string DeviceName = "Unknown"
);

record GenerateCodeRequest(
    string VoterId
);

record UpdateFingerprintRequest(
    string Username,
    string Password,
    string? EncryptionMode,
    string? KeyVersion,
    string? KeyId,
    string? WrappedDek,
    string? EncryptedFingerPrintScan
);

record SetAccessCodeRequest(
    string AccessCode
);

record VerifyAccessCodeRequest(
    string AccessCode,
    string County,
    string Constituency
);

record VerifyAccessCodeResponse(
    bool Success,
    string Message
);

// Voter-Official Linking Request
record VoterLinkRequest(
    string PollingStationCode,  // Should match official's SystemCode
    string County,
    string Constituency
);

record VoterLinkResponse(
    bool Success,
    int AssignedVoterId,
    string ConnectedOfficialId,
    string ConnectedStationId,
    string Message,
    string? Token = null
);

// Vote casting models
record CastVoteRequest(
    int VoterId,
    Guid? VoterDatabaseId,
    Guid? ProxyVoterDatabaseId,
    string County,
    Guid PollingStationId,
    Guid CandidateId,
    string CandidateName,
    string PartyName,
    string Constituency
);

record CastVoteResponse(
    bool Success,
    string Message,
    DateTime Timestamp
);

// Device status tracking models
record SendDeviceStatusRequest(
    int VoterId,
    string DeviceId,
    string Status
);

record SendDeviceCommandRequest(
    int VoterId,
    string DeviceId,
    string CommandType
);

record VoteNotification(
    int VoterId,
    string CandidateName,
    string PartyName,
    DateTime Timestamp,
    string OfficialId,
    string StationId,
    string County,
    string Constituency
);

record DeviceStatusNotification(
    int VoterId,
    string DeviceId,
    string Status,
    DateTime Timestamp,
    string OfficialId,
    string StationId,
    string County,
    string Constituency
);

record DeviceCommandNotification(
    int VoterId,
    string DeviceId,
    string CommandType,
    DateTime Timestamp,
    string County,
    string Constituency,
    string OfficialId,
    string Message
);

// Fingerprint verification models
record VerifyFingerprintsRequest(
    string UserType,              // "official" or "voter" - identifies the type of user
    string? Username,             // Official username for database lookup (null for voters)
    string? Password,             // Official password for authentication (null for voters)
    string? VoterId,              // Voter unique ID as string (null for officials)
    List<string>? CandidateVoterIds, // Optional list of voter IDs for SDI collision disambiguation
    string? ScannedFingerprint    // Base64 encoded newly scanned fingerprint (PNG format)
);

// Voter authentication lookup models (flexible identification)
record VoterAuthLookupRequest(
    string? FirstName,
    string? LastName,
    string? DateOfBirth,
    string? PostCode,
    string? TownOfBirth,
    string? County,
    string? Constituency
);

record VoterAuthLookupResponse(
    bool Success,
    string Message,
    Guid? VoterId,
    string? FullName,
    byte[]? FingerprintScan,
    string? MatchedBy,
    bool RequiresDisambiguation,
    List<Guid>? CandidateVoterIds
);

record ProxyAuthorizationRequest(
    Guid RepresentedVoterId,
    Guid ProxyVoterId
);

record ProxyAuthorizationResponse(
    bool Success,
    string Message
);

record AssignProxyVoterRequest(
    string RepresentedFirstName,
    string RepresentedLastName,
    string RepresentedDateOfBirth,
    string RepresentedPostCode,
    string RepresentedTownOfBirth,
    string ProxyFirstName,
    string ProxyLastName,
    string ProxyDateOfBirth,
    string ProxyPostCode,
    string ProxyTownOfBirth,
    string ScannedFingerprint
);

record AssignProxyVoterResponse(
    bool Success,
    string Message,
    Guid? RepresentedVoterId,
    Guid? ProxyVoterId
);

// Thread-safe token counter for unique identities
public class TokenCounter
{
    private long _counter = 0;
    
    public long GetNextId()
    {
        return Interlocked.Increment(ref _counter);
    }
    
    public long CurrentCount => _counter;
}

// Certificate loading from AWS

// Certificate data model for JSON deserialization
public class CertificateSecret
{
    public string Certificate { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
}