using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace officialApp.Models;

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

// Vote notification models for officials
public class VoteNotificationResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("votes")]
    public List<VoteInfo> Votes { get; set; } = new();
    
    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public class VoteInfo
{
    [JsonPropertyName("voterId")]
    public int VoterId { get; set; }
    
    [JsonPropertyName("candidateName")]
    public string CandidateName { get; set; } = string.Empty;
    
    [JsonPropertyName("partyName")]
    public string PartyName { get; set; } = string.Empty;
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [JsonPropertyName("officialId")]
    public string OfficialId { get; set; } = string.Empty;
}