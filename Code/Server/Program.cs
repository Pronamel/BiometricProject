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
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
app.MapPost("/auth/official-login", async (OfficialLoginRequest request, DatabaseService dbService, TokenCounter counter, 
    ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials) =>
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
    
    // Register this official system with their unique code
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
    ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<string>>> countyChannels,
    ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<List<string>>>>> countyActiveConnections,
    TokenCounter voterIdCounter) =>
{
    var stationPrefix = $"{request.County}_{request.PollingStationCode}_";
    
    Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] ===== VOTER LINK ATTEMPT =====");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter request - County: {request.County}, PollingStationCode: {request.PollingStationCode}, Constituency: {request.Constituency}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Looking for officials with key prefix: {stationPrefix}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Active officials registered: {string.Join(" | ", activeOfficials.Keys)}");
    
    // Find the first official at this polling station
    var officialsAtStation = activeOfficials
        .Where(o => o.Key.StartsWith(stationPrefix))
        .FirstOrDefault();
    
    if (officialsAtStation.Value != default)
    {
        var officialInfo = officialsAtStation.Value;
        var systemKey = officialsAtStation.Key;
        var assignedVoterId = (int)voterIdCounter.GetNextId();
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Found official: {officialInfo.OfficialId} at station {officialInfo.StationId}");
        
        // Update the official's connected voters list
        officialInfo.ConnectedVoters.Add(assignedVoterId);
        activeOfficials[systemKey] = officialInfo with { ConnectedVoters = officialInfo.ConnectedVoters };
        
        // NOTIFY THE WAITING OFFICIAL
        var voterRequestMessage = $"New voter link: VoterId={assignedVoterId}, Constituency={request.Constituency}";
        
        // Add to county channels for the official to receive
        var countyDict = countyChannels.GetOrAdd(request.County, _ => new ConcurrentDictionary<string, ConcurrentBag<string>>());
        var countyRequests = countyDict.GetOrAdd(request.Constituency, _ => new ConcurrentBag<string>());
        countyRequests.Add(voterRequestMessage);
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Added to countyChannels[{request.County}][{request.Constituency}]");
        
        // Signal any waiting official task completion source
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Checking countyActiveConnections for waiting officials...");
        var countyConnDict = countyActiveConnections.GetOrAdd(request.County, _ => new ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<List<string>>>?>());
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Counties in activeConnections: {string.Join(" | ", countyActiveConnections.Keys)}");
        
        if (countyConnDict.TryGetValue(request.Constituency, out var constituencyConnDict) && constituencyConnDict != null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Found constituencies: {string.Join(" | ", constituencyConnDict.Keys)}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Waiting officials in {request.County}/{request.Constituency}: {string.Join(" | ", constituencyConnDict.Keys)}");
            
            int notifiedCount = 0;
            foreach (var kvp in constituencyConnDict)
            {
                var officialId = kvp.Key;
                var tcs = kvp.Value;
                
                // Collect all pending requests for this official
                var requests = new List<string>();
                while (countyRequests.TryTake(out string? req))
                {
                    if (req != null) requests.Add(req);
                }
                
                if (requests.Count > 0 && tcs.TrySetResult(requests))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Notified official {officialId} about {requests.Count} voter request(s)");
                    notifiedCount++;
                }
            }
            
            if (notifiedCount == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ No waiting officials were notified!");
            }
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ No constituencies found in activeConnections for {request.County}");
        }
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter {assignedVoterId} linked successfully!\n");
        
        return Results.Ok(new VoterLinkResponse(
            true,
            assignedVoterId,
            officialInfo.OfficialId,
            officialInfo.StationId,
            "Successfully linked to official"
        ));
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ No matching official found");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ===== END VOTER LINK ATTEMPT =====\n");
    return Results.BadRequest(new VoterLinkResponse(
        false,
        0,
        "",
        "",
        $"No official found for county '{request.County}' with polling station code '{request.PollingStationCode}'. Please verify the codes with election staff."
    ));
})
.WithName("VoterLinkToOfficial");

app.MapPost("/api/voter/cast-vote", (CastVoteRequest request,
    ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
    [FromServices] ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<VoteNotification>>> countyVoteChannels) =>
{
    var stationPrefix = $"{request.County}_{request.PollingStationCode}_";
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast attempt - Voter ID: {request.VoterId}, County: {request.County}, Constituency: {request.Constituency}, Station: {request.PollingStationCode}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote for: {request.CandidateName} - {request.PartyName}");
    
    // Find all officials at this polling station
    var officialsAtStation = activeOfficials
        .Where(o => o.Key.StartsWith(stationPrefix))
        .ToList();
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Found {officialsAtStation.Count} officials at station {request.PollingStationCode}");
    
    // Verify voter is linked to at least one official at this station
    var voterLinked = officialsAtStation.Any(o => o.Value.ConnectedVoters.Contains(request.VoterId));
    
    if (voterLinked && officialsAtStation.Count > 0)
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
        
        // Add vote to ONLY OFFICIALS IN THE SAME CONSTITUENCY - with safe iteration
        var officialQueues = countyVoteChannels.GetOrAdd(request.County, _ => new ConcurrentDictionary<string, ConcurrentBag<VoteNotification>>());
        
        // Find all officials at this station in the same constituency
        var officialsInConstituency = officialsAtStation
            .Where(o => o.Value.Constituency == request.Constituency)
            .ToList();
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Broadcasting vote to {officialsInConstituency.Count} officials in constituency {request.Constituency}");
        
        // Add vote to ALL officials in this constituency at this station
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
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast failed - Voter {request.VoterId} not linked to any official at station {request.PollingStationCode}");
    return Results.BadRequest(new CastVoteResponse(
        false,
        "Vote failed: Voter not properly linked to official system",
        DateTime.UtcNow
    ));
})
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
    string Message
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