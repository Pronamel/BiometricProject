//===========================================
// USING STATEMENTS
//===========================================
using System.Collections.Concurrent;
using System.Text;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Server.Services;

//===========================================
// BUILDER CONFIGURATION
//===========================================
var builder = WebApplication.CreateBuilder(args);

// Force port 5000 in all environments
builder.WebHost.UseUrls("http://0.0.0.0:5000");

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
// IN-MEMORY STORAGE SETUP - COUNTY-BASED CHANNELS
//===========================================
// County-based request channels: County -> List of voter requests
var countyChannels = new ConcurrentDictionary<string, ConcurrentBag<string>>();

// County-based access codes: County -> (VoterId -> Code)
var countyVoterCodes = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();

// County-based active waiting connections: County -> (OfficialId -> TaskCompletionSource)
var countyActiveConnections = new ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<List<string>>>>();

// County-based vote notifications: County -> List of vote notifications for officials
var countyVoteChannels = new ConcurrentDictionary<string, ConcurrentBag<VoteNotification>>();

// Global storage (still shared across all counties)
var activeVotingSessions = new ConcurrentDictionary<string, DateTime>(); // SessionId -> Expiry
var tokenCounter = new TokenCounter(); // Global unique token counter

// Official system tracking: (County + SystemCode) -> OfficialInfo
var activeOfficials = new ConcurrentDictionary<string, (string OfficialId, string StationId, DateTime LoginTime, List<int> ConnectedVoters)>();

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

//===========================================
// CORS CONFIGURATION
//===========================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("ProductionCors", policy =>
    {
        // Allow local development and production domains
        policy.WithOrigins(
                "http://yourdomain.com", 
                "http://www.yourdomain.com",
                "http://localhost:5000",
                "https://localhost:5000",
                "http://127.0.0.1:5000",
                "https://127.0.0.1:5000"
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

// Authentication and Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Request logging middleware
app.Use(async (context, next) =>
{
    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    var method = context.Request.Method;
    var path = context.Request.Path;
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
app.MapPost("/auth/official-login", (OfficialLoginRequest request, OfficialService officialService, TokenCounter counter, 
    ConcurrentDictionary<string, (string OfficialId, string StationId, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received login request:");
    
    if (officialService.ValidateOfficialLogin(request.OfficialId, request.StationId, request.Password))
    {
        var uniqueTokenId = counter.GetNextId();
        
        // Register this official system with their unique code
        var systemKey = $"{request.County}_{request.SystemCode}";
        activeOfficials[systemKey] = (request.OfficialId, request.StationId, DateTime.UtcNow, new List<int>());
        
        var additionalClaims = new Dictionary<string, object>
        {
            ["station"] = request.StationId,
            ["officialId"] = request.OfficialId,
            ["county"] = request.County,
            ["systemCode"] = request.SystemCode,
            ["tokenId"] = uniqueTokenId
        };
        
        var token = GenerateJwtToken($"official_{request.OfficialId}_{uniqueTokenId}", "official", additionalClaims);
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official login: {request.OfficialId} at {request.StationId} with system code {request.SystemCode} (Token ID: {uniqueTokenId})");
        
        return Results.Ok(new { 
            success = true, 
            token = token,
            role = "official",
            stationId = request.StationId,
            officialId = request.OfficialId,
            county = request.County,
            systemCode = request.SystemCode,
            tokenId = uniqueTokenId,
            expiresAt = DateTime.UtcNow.AddHours(24)
        });
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Authentication REJECTED for {request.OfficialId}");
    return Results.Unauthorized();
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
// API ENDPOINTS - VOTER-OFFICIAL LINKING
//===========================================
app.MapPost("/api/voter/link-to-official", (VoterLinkRequest request, 
    ConcurrentDictionary<string, (string OfficialId, string StationId, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
    TokenCounter voterIdCounter) =>
{
    var systemKey = $"{request.County}_{request.PollingStationCode}";
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter link attempt - County: {request.County}, Station Code: {request.PollingStationCode}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Looking for system key: {systemKey}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Active officials: {string.Join(", ", activeOfficials.Keys)}");
    
    if (activeOfficials.TryGetValue(systemKey, out var officialInfo))
    {
        var assignedVoterId = (int)voterIdCounter.GetNextId();
        
        // Update the official's connected voters list
        officialInfo.ConnectedVoters.Add(assignedVoterId);
        activeOfficials[systemKey] = officialInfo with { ConnectedVoters = officialInfo.ConnectedVoters };
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter linked successfully! Assigned ID: {assignedVoterId} to Official: {officialInfo.OfficialId}");
        
        return Results.Ok(new VoterLinkResponse(
            true,
            assignedVoterId,
            officialInfo.OfficialId,
            officialInfo.StationId,
            "Successfully linked to official"
        ));
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No matching official found for county '{request.County}' and polling station code '{request.PollingStationCode}'");
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
    ConcurrentDictionary<string, (string OfficialId, string StationId, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
    ConcurrentDictionary<string, ConcurrentBag<VoteNotification>> countyVoteChannels) =>
{
    var systemKey = $"{request.County}_{request.PollingStationCode}";
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast attempt - Voter ID: {request.VoterId}, County: {request.County}, Station: {request.PollingStationCode}");
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote for: {request.CandidateName} - {request.PartyName}");
    
    // Verify voter is linked to the official system
    if (activeOfficials.TryGetValue(systemKey, out var officialInfo) && 
        officialInfo.ConnectedVoters.Contains(request.VoterId))
    {
        // Create vote notification for the official
        var voteNotification = new VoteNotification(
            request.VoterId,
            request.CandidateName,
            request.PartyName,
            DateTime.UtcNow,
            officialInfo.OfficialId,
            officialInfo.StationId
        );
        
        // Add vote to the county channel for the official to receive
        var channel = countyVoteChannels.GetOrAdd(request.County, _ => new ConcurrentBag<VoteNotification>());
        channel.Add(voteNotification);
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote successfully cast! Voter {request.VoterId} voted for {request.CandidateName}");
        
        return Results.Ok(new CastVoteResponse(
            true,
            "Vote successfully cast",
            DateTime.UtcNow
        ));
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast failed - Voter {request.VoterId} not linked to official system {systemKey}");
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
    if (string.IsNullOrEmpty(county))
    {
        return Results.BadRequest(new { success = false, message = "County not found in authentication token" });
    }

    var timeout = TimeSpan.FromSeconds(20);
    var (success, code) = await voterService.WaitForAccessCode(voterId, county, timeout);
    
    if (success)
    {
        return Results.Ok(new { success = true, code = code });
    }
    
    return Results.Ok(new { success = false, message = "Timeout - no code available" });
})
.RequireAuthorization(policy => policy.RequireRole("voter"))
.WithName("VoterWaitForCode");

app.MapGet("/api/official/wait-for-votes", async (ClaimsPrincipal user,
    ConcurrentDictionary<string, ConcurrentBag<VoteNotification>> countyVoteChannels) =>
{
    var county = user.FindFirst("county")?.Value;
    var officialId = user.FindFirst("officialId")?.Value ?? "Unknown";
    
    if (string.IsNullOrEmpty(county))
    {
        return Results.BadRequest(new { success = false, message = "County not found in authentication token" });
    }
    
    // Get votes for this county
    var votes = new List<object>();
    if (countyVoteChannels.TryGetValue(county, out var voteChannel))
    {
        var allVotes = new List<VoteNotification>();
        
        // Drain all votes from the channel
        while (voteChannel.TryTake(out var vote))
        {
            allVotes.Add(vote);
        }
        
        // Only log if there are actually votes to process
        if (allVotes.Count > 0)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Delivering {allVotes.Count} votes to official {officialId} in {county}");
        }
        
        foreach (var vote in allVotes)
        {
            votes.Add(new {
                voterId = vote.VoterId,
                candidateName = vote.CandidateName,
                partyName = vote.PartyName,
                timestamp = vote.Timestamp,
                officialId = vote.OfficialId
            });
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
    
    if (string.IsNullOrEmpty(county))
    {
        return Results.BadRequest(new { success = false, message = "County not found in authentication token" });
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} (Station: {stationId}) waiting for voter requests in {county}");
    
    var timeout = TimeSpan.FromSeconds(30);
    var (success, requests) = await officialService.WaitForVoterRequests(county, officialId, timeout);
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} received {(success ? requests.Count : 0)} voter requests from {county}");
    
    return Results.Ok(new { success = success, requests = requests });
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialWaitForRequests");

app.MapPost("/api/official/generate-code", (GenerateCodeRequest request, OfficialService officialService, ClaimsPrincipal user) =>
{
    var officialId = user.FindFirst("officialId")?.Value ?? "Unknown";
    var stationId = user.FindFirst("station")?.Value ?? "Unknown";
    var county = user.FindFirst("county")?.Value;
    
    if (string.IsNullOrEmpty(county))
    {
        return Results.BadRequest(new { success = false, message = "County not found in authentication token" });
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} (Station: {stationId}) generating code for voter {request.VoterId} in {county}");
    
    var (success, code) = officialService.GenerateAccessCode(request.VoterId, county);
    
    if (success)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} successfully generated code {code} for voter {request.VoterId} in {county}");
        return Results.Ok(new { success = true, code = code, voterId = request.VoterId });
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} failed to generate code for voter {request.VoterId} in {county}");
    return Results.BadRequest(new { success = false, message = "Failed to generate code" });
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialGenerateCode");

app.MapPost("/api/voter/request-access", async (VoterAccessRequest request, VoterService voterService, ClaimsPrincipal user) =>
{
    var county = user.FindFirst("county")?.Value;
    if (string.IsNullOrEmpty(county))
    {
        return Results.BadRequest(new { success = false, message = "County not found in authentication token" });
    }

    var success = await voterService.RequestAccess(request.VoterId, county, request.DeviceName);
    
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
// START APPLICATION
//===========================================
app.Run();

//===========================================
// DATA MODELS & RECORDS
//===========================================
record OfficialLoginRequest(
    string OfficialId,  
    string StationId,
    string County,
    string SystemCode,
    string? Password = null  // Optional for now
);

record VoterSessionRequest(
    string VoterId,  // NIN or voter identifier
    string County
);

record VoterAccessRequest(
    string VoterId,
    string DeviceName = "Unknown"
);

record GenerateCodeRequest(
    string VoterId
);

// Voter-Official Linking Request
record VoterLinkRequest(
    string PollingStationCode,  // Should match official's SystemCode
    string County
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
    string PartyName
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
    string StationId
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