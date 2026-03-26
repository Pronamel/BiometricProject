//===========================================
// USING STATEMENTS
//===========================================
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Http.Json;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Server.Services;
using Server.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourceAFIS;

//===========================================
// BUILDER CONFIGURATION
//===========================================
var builder = WebApplication.CreateBuilder(args);

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
var jwtSecret = "VerySecureSecretKey2026ForVotingSystem!MinimumOf256Bits";
var jwtKey = Encoding.ASCII.GetBytes(jwtSecret);

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
    });

builder.Services.AddAuthorization();

//===========================================
// DATABASE CONFIGURATION
//===========================================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

//===========================================
// IN-MEMORY STORAGE SETUP - COUNTY-BASED CHANNELS
//===========================================

// County+Constituency-based request channels: County -> Constituency -> List of voter requests
var countyChannels = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<string>>>();

// County+Constituency-based access codes: County -> Constituency -> (VoterId -> Code)
var countyVoterCodes = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>>();

// County+Constituency-based active waiting connections: County -> Constituency -> (OfficialId -> TaskCompletionSource)
var countyActiveConnections = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<List<string>>>>>();

// County+Constituency-based vote notifications: County -> Constituency -> List of vote notifications for officials
var countyVoteChannels = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<VoteNotification>>>();

// Global storage (still shared across all counties)
var activeVotingSessions = new ConcurrentDictionary<string, DateTime>(); // SessionId -> Expiry
var tokenCounter = new TokenCounter(); // Global unique token counter

// Official system tracking: (County + SystemCode) -> OfficialInfo (now includes Constituency)
var activeOfficials = new ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)>();

// Official polling station hashes: OfficialId -> (County, Constituency, HashedCode)
var officialPollingStationHashes = new ConcurrentDictionary<string, (string County, string Constituency, string HashedCode)>();

// Voter ID assignment counter
var voterIdCounter = 0;

//===========================================
// SERVICE REGISTRATION
//===========================================
builder.Services.AddSingleton(countyChannels);
builder.Services.AddSingleton(countyVoterCodes);
builder.Services.AddSingleton(countyActiveConnections);
builder.Services.AddSingleton(countyVoteChannels);
builder.Services.AddSingleton(activeVotingSessions);
builder.Services.AddSingleton(activeOfficials);
builder.Services.AddSingleton(officialPollingStationHashes);
builder.Services.AddSingleton(tokenCounter);
builder.Services.AddSingleton<VoterService>();
builder.Services.AddSingleton<OfficialService>();
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
    
    // Skip logging for long-polling GET requests to reduce console clutter
    if (!(method == "GET" && path.StartsWithSegments("/api/official/wait-for-requests")))
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var clientIP = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        
        Console.WriteLine($"[{timestamp}] {method} {path} from {clientIP}");
    }
    
    await next();
});

//===========================================
// HELPER FUNCTIONS
//===========================================
string GenerateJwtToken(string userId, string role, Dictionary<string, object>? additionalClaims = null)
{
    var tokenHandler = new JwtSecurityTokenHandler();
    var key = Encoding.ASCII.GetBytes("VerySecureSecretKey2026ForVotingSystem!MinimumOf256Bits");
    
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
        Expires = role == "official" ? DateTime.UtcNow.AddHours(24) : DateTime.UtcNow.AddHours(8),
        Issuer = "SecureVoteServer",
        Audience = "VotingClients",
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };
    
    var token = tokenHandler.CreateToken(tokenDescriptor);
    return tokenHandler.WriteToken(token);
}





//===========================================
// DATA INITIALIZATION
//===========================================
var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};






//===========================================
// API ENDPOINTS - AUTHENTICATION
//===========================================
// gets login from database and checks if official exists with those credentials, then generates JWT token with station and official info
app.MapPost("/auth/official-login", async (OfficialLoginRequest request, DatabaseService dbService, TokenCounter counter, 
    ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
    ConcurrentDictionary<string, (string County, string Constituency, string HashedCode)> officialPollingStationHashes) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received login request for user: {request.Username}");
    
    // Check if official with username and password exists
    var official = await dbService.GetOfficialByCredentialsAsync(request.Username, request.Password);
    
    if (official == null)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Authentication REJECTED - no matching official found for {request.Username}");
        return Results.Unauthorized();
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
    var systemCode = $"OFF-{pollingStation.PollingStationCode}";
    var uniqueTokenId = counter.GetNextId();
    var officialId = official.OfficialId.ToString();
    
    // Store the hashed polling station code with county/constituency (direct from DB - NO re-hashing)
    officialPollingStationHashes[officialId] = (county, constituency, pollingStation.PollingStationCode);
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Stored polling station hash for official {officialId}:");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   County: {county}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Constituency: {constituency}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Hash (length {pollingStation.PollingStationCode.Length}): {pollingStation.PollingStationCode}");
    
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
        expiresAt = DateTime.UtcNow.AddHours(24)
    });
})
.WithName("OfficialLogin");




// Voter session creation - validates voter ID, creates a voting session, and returns a JWT token with session info
app.MapPost("/auth/voter-session", (VoterSessionRequest request, VoterService voterService, TokenCounter counter) =>
{
    if (voterService.ValidateVoterId(request.VoterId))
    {
        var uniqueTokenId = counter.GetNextId();
        var sessionId = voterService.CreateVotingSession(request.VoterId);
        
        var additionalClaims = new Dictionary<string, object>
        {
            ["nin"] = request.VoterId,
            ["session"] = sessionId,
            ["county"] = request.County,
            ["constituency"] = request.Constituency,
            ["tokenId"] = uniqueTokenId
        };
        
        var token = GenerateJwtToken($"voter_{request.VoterId}_{uniqueTokenId}", "voter", additionalClaims);
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter session: {request.VoterId} with session {sessionId} (Token ID: {uniqueTokenId})");
        
        return Results.Ok(new { 
            success = true, 
            token = token,
            role = "voter",
            voterId = request.VoterId,
            sessionId = sessionId,
            county = request.County,
            tokenId = uniqueTokenId,
            expiresAt = DateTime.UtcNow.AddHours(8)
        });
    }
    
    return Results.BadRequest(new { success = false, message = "Invalid voter ID" });
})
.WithName("VoterSession");

//===========================================
// API ENDPOINTS - ACCESS CODE MANAGEMENT
//===========================================
// Official sets the access code for their polling station (code is pre-hashed from the app and stored directly in DB)
app.MapPost("/api/official/set-access-code", async (SetAccessCodeRequest request, ClaimsPrincipal user, 
    [FromServices] ApplicationDbContext dbContext) =>
{
    var stationId = user.FindFirst("station")?.Value;
    var officialId = user.FindFirst("officialId")?.Value ?? "Unknown";
    
    if (string.IsNullOrEmpty(stationId))
    {
        return Results.BadRequest(new { success = false, message = "Station ID not found in authentication token" });
    }
    
    if (string.IsNullOrEmpty(request.AccessCode))
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
        
        // Store the pre-hashed code directly from the app
        station.PollingStationCode = request.AccessCode;
        
        await dbContext.SaveChangesAsync();
        
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

app.MapPost("/api/voter/verify-access-code", async (VerifyAccessCodeRequest request, 
    [FromServices] ApplicationDbContext dbContext) =>
{
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

app.MapPost("/api/voter/cast-vote", (CastVoteRequest request,
    ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
    [FromServices] ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<VoteNotification>>> countyVoteChannels) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast attempt - Voter ID: {request.VoterId}, County: {request.County}, Constituency: {request.Constituency}, Station: {request.PollingStationCode}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote for: {request.CandidateName} - {request.PartyName}");
    
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
    
    if (officialsInConstituency.Count > 0)
    {
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






//===========================================
// API ENDPOINTS - LONG POLLING
//===========================================
app.MapGet("/api/voter/wait-for-code/{voterId}", async (string voterId, VoterService voterService, ClaimsPrincipal user) =>
{
    var county = user.FindFirst("county")?.Value;
    var constituency = user.FindFirst("constituency")?.Value;
    if (string.IsNullOrEmpty(county) || string.IsNullOrEmpty(constituency))
    {
        return Results.BadRequest(new { success = false, message = "County or constituency not found in authentication token" });
    }

    var timeout = TimeSpan.FromSeconds(20);
    var (success, code) = await voterService.WaitForAccessCode(voterId, county, constituency, timeout);

    if (success)
    {
        return Results.Ok(new { success = true, code = code });
    }

    return Results.Ok(new { success = false, message = "Timeout - no code available" });
})
.RequireAuthorization(policy => policy.RequireRole("voter"))
.WithName("VoterWaitForCode");



app.MapGet("/api/official/wait-for-votes", async (ClaimsPrincipal user,
    [FromServices] ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<VoteNotification>>> countyVoteChannels) =>
{
    var county = user.FindFirst("county")?.Value;
    var officialId = user.FindFirst("officialId")?.Value ?? "Unknown";
    
    if (string.IsNullOrEmpty(county))
    {
        return Results.BadRequest(new { success = false, message = "County not found in authentication token" });
    }
    
    // Get THIS OFFICIAL'S personal vote queue
    var votes = new List<object>();
    
    if (countyVoteChannels.TryGetValue(county, out var officialQueues))
    {
        if (officialQueues.TryGetValue(officialId, out var myQueue))
        {
            // Drain ONLY this official's queue
            while (myQueue.TryTake(out var vote))
            {
                votes.Add(new {
                    voterId = vote.VoterId,
                    candidateName = vote.CandidateName,
                    partyName = vote.PartyName,
                    timestamp = vote.Timestamp,
                    officialId = vote.OfficialId,
                    stationId = vote.StationId
                });
            }
            
            if (votes.Count > 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Delivering {votes.Count} votes to official {officialId} from their personal queue in {county}");
            }
        }
    }
    
    return Results.Ok(new { success = true, votes = votes, count = votes.Count });
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialWaitForVotes");




app.MapGet("/api/official/wait-for-requests", async (OfficialService officialService, ClaimsPrincipal user) =>
{
    var officialId = user.FindFirst("officialId")?.Value ?? "Unknown";
    var stationId = user.FindFirst("station")?.Value ?? "Unknown";
    var county = user.FindFirst("county")?.Value;
    var constituency = user.FindFirst("constituency")?.Value;
    
    if (string.IsNullOrEmpty(county) || string.IsNullOrEmpty(constituency))
    {
        return Results.BadRequest(new { success = false, message = "County or constituency not found in authentication token" });
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} (Station: {stationId}) waiting for voter requests in {county}/{constituency}");
    
    var timeout = TimeSpan.FromSeconds(30);
    var (success, requests) = await officialService.WaitForVoterRequests(county, constituency, officialId, timeout);
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} received {(success ? requests.Count : 0)} voter requests from {county}/{constituency}");
    
    return Results.Ok(new { success = success, requests = requests });
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialWaitForRequests");

app.MapPost("/api/official/generate-code", (GenerateCodeRequest request, OfficialService officialService, ClaimsPrincipal user) =>
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
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} successfully generated code {code} for voter {request.VoterId} in {county}/{constituency}");
        return Results.Ok(new { success = true, code = code, voterId = request.VoterId });
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} failed to generate code for voter {request.VoterId} in {county}/{constituency}");
    return Results.BadRequest(new { success = false, message = "Failed to generate code" });
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialGenerateCode");

app.MapPost("/api/voter/request-access", async (VoterAccessRequest request, VoterService voterService, ClaimsPrincipal user) =>
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
.WithName("OfficialDatabaseAccess");

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
    string fingerprintBase64 = string.Empty;
    
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

        username = root.TryGetProperty("username", out var usernameElement)
            ? usernameElement.GetString() ?? string.Empty
            : string.Empty;

        password = root.TryGetProperty("password", out var passwordElement)
            ? passwordElement.GetString() ?? string.Empty
            : string.Empty;

        // Support both field names
        if (root.TryGetProperty("fingerPrintScan", out var fpElement))
        {
            fingerprintBase64 = fpElement.GetString() ?? string.Empty;
        }
        else if (root.TryGetProperty("fingerprintData", out var legacyElement))
        {
            fingerprintBase64 = legacyElement.GetString() ?? string.Empty;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Parsed username present: {!string.IsNullOrEmpty(username)}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Parsed password present: {!string.IsNullOrEmpty(password)}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Parsed fingerprint data present: {!string.IsNullOrEmpty(fingerprintBase64)}");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Missing credentials");
            return Results.BadRequest(new {
                success = false,
                message = "Username and password are required"
            });
        }

        if (string.IsNullOrEmpty(fingerprintBase64))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Missing fingerprint data");
            return Results.BadRequest(new {
                success = false,
                message = "Fingerprint data is required (fingerPrintScan must be PNG format)"
            });
        }

        // Decode base64 to bytes
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Converting base64 to bytes...");
        byte[] fingerprintBytes = Convert.FromBase64String(fingerprintBase64);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Decoded: {fingerprintBytes.Length} bytes");
        
        // Validate PNG format (check magic bytes: 89 50 4E 47 = .PNG)
        if (fingerprintBytes.Length < 8 || 
            fingerprintBytes[0] != 0x89 || 
            fingerprintBytes[1] != 0x50 || 
            fingerprintBytes[2] != 0x4E || 
            fingerprintBytes[3] != 0x47)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Invalid PNG format - magic bytes not found");
            return Results.BadRequest(new {
                success = false,
                message = "Fingerprint must be in PNG format"
            });
        }
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Valid PNG format confirmed");
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Updating fingerprint in database...");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Username: '{username}'");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Fingerprint size: {fingerprintBytes.Length} bytes (PNG)");
        
        // Update the official's fingerprint in database
        bool updateSuccessful = await dbService.UpdateOfficialFingerprintAsync(
            username,
            password,
            fingerprintBytes
        );
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] DatabaseService returned: {updateSuccessful}");
        
        if (updateSuccessful)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Fingerprint upload successful for {username}");
            return Results.Ok(new { 
                success = true, 
                message = "Fingerprint uploaded successfully",
                dataSize = fingerprintBytes.Length
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
            message = "Fingerprint data must be valid base64 encoded",
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
app.MapPost("/api/verify-prints", async (VerifyFingerprintsRequest request, DatabaseService dbService) =>
{
    const double MATCH_THRESHOLD = 40.0;
    
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

        // Validate scanned fingerprint is present
        if (string.IsNullOrEmpty(request.ScannedFingerprint))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Missing scanned fingerprint");
            return Results.BadRequest(new { 
                success = false, 
                message = "Scanned fingerprint is required" 
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

            storedFingerprintBytes = official.FingerPrintScan;
            userIdentifier = official.OfficialId.ToString();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official found, retrieving stored fingerprint ({storedFingerprintBytes.Length} bytes)");
        }
        else if (request.UserType == "voter")
        {
            // VOTER VERIFICATION PATH
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🗳️  Processing VOTER fingerprint verification");

            // Validate voter ID
            if (string.IsNullOrEmpty(request.VoterId))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Missing VoterId for voter verification");
                return Results.BadRequest(new { 
                    success = false, 
                    message = "VoterId is required for voters" 
                });
            }

            // Parse VoterId as Guid
            if (!Guid.TryParse(request.VoterId, out Guid voterGuid))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Invalid VoterId format: {request.VoterId}");
                return Results.BadRequest(new { 
                    success = false, 
                    message = "VoterId must be a valid GUID" 
                });
            }

            // Fetch voter from database by VoterId
            var voter = await dbService.GetVoterByIdAsync(voterGuid);
            
            if (voter == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Record not found - no voter with ID {request.VoterId}");
                return Results.BadRequest(new { 
                    success = false, 
                    message = "Record not found" 
                });
            }

            // Get stored fingerprint from database
            if (voter.FingerprintScan == null || voter.FingerprintScan.Length == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ No stored fingerprint found for voter {voter.FirstName} {voter.LastName}");
                return Results.BadRequest(new { 
                    success = false, 
                    message = "No stored fingerprint on record" 
                });
            }

            storedFingerprintBytes = voter.FingerprintScan;
            userIdentifier = voter.VoterId.ToString();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter found, retrieving stored fingerprint ({storedFingerprintBytes.Length} bytes)");
        }

        // COMMON FINGERPRINT COMPARISON LOGIC (applies to both official and voter)
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Decoding scanned fingerprint from base64...");
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

record VoterSessionRequest(
    string VoterId,  // NIN or voter identifier
    string County,
    string Constituency
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
    string FingerPrintScan  // Base64 encoded fingerprint image data
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
    string County,
    string PollingStationCode,
    string CandidateName,
    string PartyName,
    string Constituency
);

record CastVoteResponse(
    bool Success,
    string Message,
    DateTime Timestamp
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

// Fingerprint verification models
record VerifyFingerprintsRequest(
    string UserType,              // "official" or "voter" - identifies the type of user
    string? Username,             // Official username for database lookup (null for voters)
    string? Password,             // Official password for authentication (null for voters)
    string? VoterId,              // Voter unique ID as string (null for officials)
    string ScannedFingerprint     // Base64 encoded newly scanned fingerprint (PNG format)
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