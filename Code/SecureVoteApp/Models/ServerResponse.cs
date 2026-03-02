using System.Text.Json.Serialization;

namespace SecureVoteApp.Models;

public class ServerResponse
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;
    
    [JsonPropertyName("temperatureC")]
    public int TemperatureC { get; set; }
    
    [JsonPropertyName("temperatureF")]
    public int TemperatureF { get; set; }
    
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}