using System.Collections.Concurrent;
using System.Text;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Force port 80 in all environments
builder.WebHost.UseUrls("http://0.0.0.0:80");

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// JWT Configuration
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

// Add CORS for production
builder.Services.AddCors(options =>
{
    options.AddPolicy("ProductionCors", policy =>
    {
        // Allow local development and production domains
        policy.WithOrigins(
                "http://yourdomain.com", 
                "http://www.yourdomain.com",
                "http://localhost",
                "https://localhost",
                "http://127.0.0.1",
                "https://127.0.0.1"
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    
    // Use permissive CORS in development
    app.UseCors("DevelopmentCors");
}
else
{
    // Production configuration
    app.UseExceptionHandler("/Error");
    // app.UseHsts(); // Disabled for HTTP-only configuration
    
    // Use secure CORS in production
    app.UseCors("ProductionCors");
}

// Enable HTTPS redirection in production (only if HTTPS is configured)
// Commented out since we're running HTTP-only
// if (!app.Environment.IsDevelopment())
// {
//     app.UseHttpsRedirection();
// }

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Add request logging middleware
app.Use(async (context, next) =>
{
    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    var method = context.Request.Method;
    var path = context.Request.Path;
    var clientIP = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    
    Console.WriteLine($"[{timestamp}] {method} {path} from {clientIP}");
    
    await next();
});

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

// Device Management Storage (simple in-memory storage)
var deviceManagementInfo = new DeviceManagementInfo(
    "VOTING_SYS_001",
    "PollingStation_Central_A1", 
    3,
    new List<string> { "BiometricScanner_001", "VotingTablet_002", "BackupTablet_003" }
);

// Long Polling Storage (thread-safe)
var pendingVoterCodes = new ConcurrentDictionary<string, string>(); // VoterId -> Code
var voterRequests = new ConcurrentBag<string>(); // Pending voter requests
var officialNotifications = new ConcurrentBag<string>(); // Notifications to official
var activeVotingSessions = new ConcurrentDictionary<string, DateTime>(); // SessionId -> Expiry

// JWT Token Generation Helper
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
        Expires = role == "official" ? DateTime.UtcNow.AddHours(24) : DateTime.UtcNow.AddHours(4),
        Issuer = "SecureVoteServer",
        Audience = "VotingClients",
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };
    
    var token = tokenHandler.CreateToken(tokenDescriptor);
    return tokenHandler.WriteToken(token);
}

// Authentication Endpoints
app.MapPost("/auth/official-login", (OfficialLoginRequest request) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received login request:");
    Console.WriteLine($"  Official ID: '{request.OfficialId ?? "NULL"}'");
    Console.WriteLine($"  Station ID: '{request.StationId ?? "NULL"}'");
    Console.WriteLine($"  Password: '{request.Password ?? "NULL"}'");
    
    // Simple validation - in production, check against database/directory
    var stationValid = request.StationId?.StartsWith("PollingStation") == true;
    var officialValid = !string.IsNullOrEmpty(request.OfficialId);
    
    Console.WriteLine($"  Station ID valid (starts with 'PollingStation'): {stationValid}");
    Console.WriteLine($"  Official ID valid (not empty): {officialValid}");
    
    if (stationValid && officialValid)
    {
        var additionalClaims = new Dictionary<string, object>
        {
            ["station"] = request.StationId,
            ["officialId"] = request.OfficialId
        };
        
        var token = GenerateJwtToken($"official_{request.OfficialId}", "official", additionalClaims);
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official login: {request.OfficialId} at {request.StationId}");
        
        return Results.Ok(new { 
            success = true, 
            token = token,
            role = "official",
            stationId = request.StationId,
            officialId = request.OfficialId,
            expiresAt = DateTime.UtcNow.AddHours(24)
        });
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Authentication REJECTED for {request.OfficialId}");
    return Results.Unauthorized();
})
.WithName("OfficialLogin");

app.MapPost("/auth/voter-session", (VoterSessionRequest request) =>
{
    // Simple validation - in production, verify NIN against voter registry
    if (!string.IsNullOrEmpty(request.VoterId) && request.VoterId.Length >= 5)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..16];
        
        var additionalClaims = new Dictionary<string, object>
        {
            ["nin"] = request.VoterId,
            ["session"] = sessionId
        };
        
        var token = GenerateJwtToken($"voter_{request.VoterId}", "voter", additionalClaims);
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter session: {request.VoterId} with session {sessionId}");
        
        return Results.Ok(new { 
            success = true, 
            token = token,
            role = "voter",
            voterId = request.VoterId,
            sessionId = sessionId,
            expiresAt = DateTime.UtcNow.AddHours(4)
        });
    }
    
    return Results.BadRequest(new { success = false, message = "Invalid voter ID" });
})
.WithName("VoterSession");

app.MapGet("/securevote", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetSecureVoteData");

// Device Management Endpoints
app.MapGet("/api/devices/management-info", () =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] GET /api/devices/management-info - Returning device info");
    return deviceManagementInfo;
})
.WithName("GetDeviceManagementInfo");

app.MapPost("/api/devices/management-info", (DeviceManagementInfo newDeviceInfo) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] POST /api/devices/management-info - Received device info");
    Console.WriteLine($"  Identifier: {newDeviceInfo.Identifier}");
    Console.WriteLine($"  PollingStationID: {newDeviceInfo.PollingStationID}");
    Console.WriteLine($"  Connected Devices: {newDeviceInfo.No_ConnectedDevices}");
    Console.WriteLine($"  Device Names: {string.Join(", ", newDeviceInfo.DeviceNames ?? new List<string>())}");
    
    // Update stored device info
    deviceManagementInfo = newDeviceInfo;
    
    return Results.Ok(new { success = true, message = "Device management info updated successfully" });
})
.WithName("SetDeviceManagementInfo");

// ==========================================
// LONG POLLING ENDPOINTS
// ==========================================

// Voter waits for access code (long polling)
app.MapGet("/api/voter/wait-for-code/{voterId}", async (string voterId) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter {voterId} waiting for access code");
    
    var timeout = TimeSpan.FromSeconds(20);
    var startTime = DateTime.Now;
    
    while (DateTime.Now - startTime < timeout)
    {
        // Check if voter has a pending code
        if (pendingVoterCodes.TryGetValue(voterId, out string? code))
        {
            pendingVoterCodes.TryRemove(voterId, out _); // One-time use
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending code {code} to voter {voterId}");
            return Results.Ok(new { success = true, code = code });
        }
        
        await Task.Delay(500); // Check every 0.5 seconds
    }
    
    return Results.Ok(new { success = false, message = "Timeout - no code available" });
})
.RequireAuthorization(policy => policy.RequireRole("voter"))
.WithName("VoterWaitForCode");

// Official waits for voter requests (long polling)
app.MapGet("/api/official/wait-for-requests", async () =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official waiting for voter requests");
    
    var timeout = TimeSpan.FromSeconds(30);
    var startTime = DateTime.Now;
    
    while (DateTime.Now - startTime < timeout)
    {
        if (!voterRequests.IsEmpty)
        {
            var requests = new List<string>();
            while (voterRequests.TryTake(out string? request))
            {
                if (request != null) requests.Add(request);
            }
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending {requests.Count} voter requests to official");
            return Results.Ok(new { success = true, requests = requests });
        }
        
        await Task.Delay(1000);
    }
    
    return Results.Ok(new { success = false, requests = new List<string>() });
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialWaitForRequests");

// Official generates access code for specific voter
app.MapPost("/api/official/generate-code", (GenerateCodeRequest request) =>
{
    var code = Random.Shared.Next(100000, 999999).ToString();
    pendingVoterCodes[request.VoterId] = code;
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official generated code {code} for voter {request.VoterId}");
    
    return Results.Ok(new { success = true, code = code, voterId = request.VoterId });
})
.RequireAuthorization(policy => policy.RequireRole("official"))
.WithName("OfficialGenerateCode");

// Voter requests access (notifies official)
app.MapPost("/api/voter/request-access", (VoterAccessRequest request) =>
{
    voterRequests.Add($"Voter {request.VoterId} requesting access from {request.DeviceName}");
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter {request.VoterId} requested access from {request.DeviceName}");
    
    return Results.Ok(new { success = true, message = "Access request sent to official" });
})
.RequireAuthorization(policy => policy.RequireRole("voter"))
.WithName("VoterRequestAccess");

// Add the endpoints your client is expecting
app.MapGet("/securevote/api/health", () =>
{
    return new { status = "healthy", timestamp = DateTime.Now };
})
.WithName("SecureVoteHealthCheck");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

// Device Management Info record to match your client model
record DeviceManagementInfo(
    string Identifier,
    string PollingStationID,
    int No_ConnectedDevices,
    List<string>? DeviceNames
);

// Authentication Request Models
record OfficialLoginRequest(
    string OfficialId,  
    string StationId,
    string? Password = null  // Optional for now
);

record VoterSessionRequest(
    string VoterId  // NIN or voter identifier
);

// Long Polling Request Models  
record VoterAccessRequest(
    string VoterId,
    string DeviceName = "Unknown"
);

record GenerateCodeRequest(
    string VoterId
);