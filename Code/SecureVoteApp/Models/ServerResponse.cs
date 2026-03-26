using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SecureVoteApp.Models;

// Voter Authentication Models
public class VoterSessionRequest
{
    [JsonPropertyName("voterId")]
    public string VoterId { get; set; } = string.Empty;
    
    [JsonPropertyName("stationId")]
    public string? StationId { get; set; }
    
    [JsonPropertyName("county")]
    public string County { get; set; } = string.Empty;
    
    [JsonPropertyName("constituency")]
    public string Constituency { get; set; } = string.Empty;
}

public class VoterSessionResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
    
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
    
    [JsonPropertyName("voterId")]
    public string VoterId { get; set; } = string.Empty;
    
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;
    
    [JsonPropertyName("county")]
    public string County { get; set; } = string.Empty;
    
    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }
}

// Voter Access Management Models
public class VoterAccessRequest
{
    [JsonPropertyName("voterId")]
    public string VoterId { get; set; } = string.Empty;
    
    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;
    
    [JsonPropertyName("county")]
    public string County { get; set; } = string.Empty;
}

public class CodeWaitResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("code")]
    public string? Code { get; set; }
    
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

// Distributed Validation Models
public class CodeVerificationRequest
{
    [JsonPropertyName("voterId")]
    public string VoterId { get; set; } = string.Empty;
    
    [JsonPropertyName("accessCode")]
    public string AccessCode { get; set; } = string.Empty;
    
    [JsonPropertyName("stationId")]
    public string? StationId { get; set; }
}

public class VoterCommandResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("commandType")]
    public string CommandType { get; set; } = string.Empty;
    
    [JsonPropertyName("data")]
    public object? Data { get; set; }
    
    [JsonPropertyName("officialId")]
    public string? OfficialId { get; set; }
    
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class VoterStatusUpdate
{
    [JsonPropertyName("voterId")]
    public string VoterId { get; set; } = string.Empty;
    
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    
    [JsonPropertyName("additionalData")]
    public string? AdditionalData { get; set; }
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}

// Voter-Official Linking Models
public class VoterLinkRequest
{
    [JsonPropertyName("pollingStationCode")]
    public string PollingStationCode { get; set; } = string.Empty;
    
    [JsonPropertyName("county")]
    public string County { get; set; } = string.Empty;
    
    [JsonPropertyName("constituency")]
    public string Constituency { get; set; } = string.Empty;
}

public class VoterLinkResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("assignedVoterId")]
    public int AssignedVoterId { get; set; }
    
    [JsonPropertyName("connectedOfficialId")]
    public string ConnectedOfficialId { get; set; } = string.Empty;
    
    [JsonPropertyName("connectedStationId")]
    public string ConnectedStationId { get; set; } = string.Empty;
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

// Vote casting models
public class CastVoteRequest
{
    [JsonPropertyName("voterId")]
    public int VoterId { get; set; }
    
    [JsonPropertyName("county")]
    public string County { get; set; } = string.Empty;
    
    [JsonPropertyName("pollingStationCode")]
    public string PollingStationCode { get; set; } = string.Empty;
    
    [JsonPropertyName("candidateName")]
    public string CandidateName { get; set; } = string.Empty;
    
    [JsonPropertyName("partyName")]
    public string PartyName { get; set; } = string.Empty;

    [JsonPropertyName("constituency")]
    public string Constituency { get; set; } = string.Empty;
}

public class CastVoteResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}

// Fingerprint Verification Models
public class FingerprintVerificationRequest
{
    [JsonPropertyName("voterId")]
    public string VoterId { get; set; } = string.Empty;
    
    [JsonPropertyName("context")]
    public string Context { get; set; } = string.Empty;
    
    [JsonPropertyName("credential")]
    public string Credential { get; set; } = string.Empty;
    
    [JsonPropertyName("fingerprintImage")]
    public string FingerprintImage { get; set; } = string.Empty; // Base64 encoded
}

public class FingerprintVerificationResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("verified")]
    public bool Verified { get; set; }
    
    [JsonPropertyName("isMatch")]
    public bool IsMatch { get; set; }
    
    [JsonPropertyName("matchScore")]
    public double MatchScore { get; set; }
    
    [JsonPropertyName("score")]
    public double Score { get; set; }
    
    [JsonPropertyName("threshold")]
    public double Threshold { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}