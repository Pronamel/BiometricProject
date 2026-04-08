using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SecureVoteApp.Models;

namespace SecureVoteApp.Services;

public interface IServerHandler
{
    // Voter Authentication & Session Management
    bool IsAuthenticated { get; }
    string? CurrentVoterId { get; }
    string? AssignedStationId { get; }
    string CurrentDeviceStatus { get; set; }
    Task<VoterLinkResponse> LinkToOfficialAsync(string pollingStationCode, string county, string constituency);
    Task<VoterAuthLookupResponse?> LookupVoterForAuthAsync(string? firstName, string? lastName, string? dateOfBirth, string? postCode, string? townOfBirth, string county, string constituency);
    Task<List<Candidate>> FetchCandidatesAsync();
    Task<CastVoteResponse> CastVoteAsync(Guid candidateId, string candidateName, string partyName);
    Task<ProxyAuthorizationResponse?> ValidateProxyAuthorizationAsync(Guid representedVoterId, Guid proxyVoterId);
    void ConfigureProxyVotingSession(Guid representedVoterId, Guid proxyVoterId);
    void ClearProxyVotingSession();
    Task<FingerprintVerificationResponse?> VerifyFingerprintAsync(string? voterId, byte[] scannedFingerprint, List<string>? candidateVoterIds = null);
    void Logout();
    
    // Voter Access Management
    Task<bool> RequestAccessFromOfficialAsync(string? deviceName = null);
    
    // Distributed Code Verification
    Task<bool> SubmitCodeForVerificationAsync(string accessCode);
    
    // Real-time Communication Loop
    Task<bool> StartContinuousListeningAsync(Action<VoterCommandResponse> onCommandReceived);
    void StopContinuousListening();
    
    // Status Updates to Official
    Task<bool> NotifyOfficialAsync(string status, string? additionalData = null);
    Task<bool> SendDeviceStatusAsync(string status);
    
    // Events for real-time updates
    event Action<string>? AccessCodeReceived;
    event Action<VoterCommandResponse>? OfficialCommandReceived;
    event Action<bool>? ConnectionStatusChanged;
    event Action<string>? StatusMessageReceived;
}