using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace officialApp.Models;

public class DeviceStatusResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("statuses")]
    public List<DeviceStatus> Statuses { get; set; } = new();

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public class DeviceStatus
{
    [JsonPropertyName("voterId")]
    public int VoterId { get; set; }

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("county")]
    public string County { get; set; } = "";

    [JsonPropertyName("constituency")]
    public string Constituency { get; set; } = "";
}
