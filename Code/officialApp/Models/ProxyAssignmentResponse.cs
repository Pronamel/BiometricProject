using System;
using System.Text.Json.Serialization;

namespace officialApp.Models;

public class ProxyAssignmentResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("representedVoterId")]
    public Guid? RepresentedVoterId { get; set; }

    [JsonPropertyName("proxyVoterId")]
    public Guid? ProxyVoterId { get; set; }
}