var builder = WebApplication.CreateBuilder(args);

// Force port 80 in all environments
builder.WebHost.UseUrls("http://0.0.0.0:80");

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS for production
builder.Services.AddCors(options =>
{
    options.AddPolicy("ProductionCors", policy =>
    {
        // Replace with your actual domain(s) and client applications
        policy.WithOrigins(
                "http://yourdomain.com", 
                "http://www.yourdomain.com"
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

app.MapGet("/weatherforecast", () =>
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
.WithName("GetWeatherForecast");

// Add the endpoints your client is expecting
app.MapGet("/weatherforecast/api/weather", () =>
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
.WithName("GetWeatherForecastApi");

app.MapGet("/weatherforecast/api/health", () =>
{
    return new { status = "healthy", timestamp = DateTime.Now };
})
.WithName("HealthCheck");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
