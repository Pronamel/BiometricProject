var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi


builder.WebHost.UseUrls("http://0.0.0.0:5000"); //listening on all interfaces on port 5000


builder.Services.AddOpenApi(); //Adds Services to the container

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

//app.UseHttpsRedirection();  //used for local lan testing, will fail unless you have a valid certificate, so commenting out for now

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", (HttpContext context) =>
{
    // Get sender information
    var senderIP = context.Connection.RemoteIpAddress?.ToString();
    var userAgent = context.Request.Headers.UserAgent.ToString();
    var method = context.Request.Method;
    var path = context.Request.Path;
    
    Console.WriteLine($"Something has been sent to (IP: {senderIP}!)");
    Console.WriteLine($"User Agent: {userAgent}");
    Console.WriteLine($"Method: {method}, Path: {path}");
    
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

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
