using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace officialApp.Models;

public class DeviceManagementInfo
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty;
    
    [JsonPropertyName("pollingStationID")]
    public string PollingStationID { get; set; } = string.Empty;
    
    [JsonPropertyName("noConnectedDevices")]
    public int No_ConnectedDevices { get; set; }
    
    [JsonPropertyName("deviceNames")]
    public List<string> DeviceNames { get; set; } = new List<string>();
}

// Long Polling Response Models
public class OfficialRequestsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("requests")]
    public List<string> Requests { get; set; } = new List<string>();
}

public class GenerateCodeRequest
{
    [JsonPropertyName("voterId")]
    public string VoterId { get; set; } = string.Empty;
}

// JWT Authentication Models
public class OfficialLoginRequest
{
    [JsonPropertyName("officialId")]
    public string OfficialId { get; set; } = string.Empty;

    [JsonPropertyName("stationId")]
    public string StationId { get; set; } = string.Empty;

    [JsonPropertyName("county")]
    public string County { get; set; } = string.Empty;

    [JsonPropertyName("constituency")]
    public string Constituency { get; set; } = string.Empty;

    [JsonPropertyName("systemCode")]
    public string SystemCode { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string? Password { get; set; } = null;
}

public class OfficialLoginResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
    
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
    
    [JsonPropertyName("stationId")]
    public string StationId { get; set; } = string.Empty;
    
    [JsonPropertyName("officialId")]
    public string OfficialId { get; set; } = string.Empty;
    
    [JsonPropertyName("county")]
    public string County { get; set; } = string.Empty;
        [JsonPropertyName("systemCode")]
    public string SystemCode { get; set; } = string.Empty;
        [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }
}