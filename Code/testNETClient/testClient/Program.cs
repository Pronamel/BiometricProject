// See https://aka.ms/new-console-template for more information

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        string serverUrl = "http://192.168.0.33:5000/weatherforecast"; // change if your server uses a different URL

        using HttpClient client = new HttpClient();

        try
        {
            string response = await client.GetStringAsync(serverUrl);

            // Parse JSON
            var forecasts = JsonSerializer.Deserialize<WeatherForecast[]>(response);

            Console.WriteLine("Received data from server:");
            foreach (var f in forecasts)
            {
                Console.WriteLine($"{f.Date}: {f.Summary}, {f.TemperatureC}°C / {f.TemperatureF}°F");
                Console.WriteLine("This is coming from the client as the server cant print shit on ur terminal dumbass");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error connecting to server: {ex.Message}");
        }
    }
}

// Class to match server JSON
public class WeatherForecast
{
    public DateTime Date { get; set; }
    public int TemperatureC { get; set; }
    public string Summary { get; set; }
    public int TemperatureF { get; set; }
}
