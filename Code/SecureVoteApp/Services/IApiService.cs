using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SecureVoteApp.Models;

namespace SecureVoteApp.Services;

public interface IApiService
{
    // Authentication & Session Management
    Task<VoterLinkResponse> LinkToOfficialAsync(string pollingStationCode, string county, string constituency);
    Task<VoterAuthLookupResponse?> LookupVoterForAuthAsync(string? firstName, string? lastName, string? dateOfBirth, string? postCode, string county, string constituency);
    
    // Candidates
    Task<List<Candidate>> FetchCandidatesAsync();
    
    // Vote Casting
    Task<CastVoteResponse> CastVoteAsync(Guid candidateId, string candidateName, string partyName);
    
    // Voter State
    bool IsAuthenticated { get; }
    string? CurrentVoterId { get; }
    string? AssignedStationId { get; }
    int AssignedVoterId { get; }
    string SelectedCounty { get; }
    string PollingStationCode { get; }
    string DeviceId { get; }
    string CurrentDeviceStatus { get; set; }
    string? GetAuthToken();
    string GetRealtimeHubUrl();
    Task LogoutAsync();
    void Logout();
    
    // Connection Testing
    Task<bool> TestConnectionAsync();
    
    // Voter Access Management
    Task<bool> RequestAccessAsync(string? deviceName = null);
    
    // Real-time Communication (Distributed Validation)
    Task<bool> SubmitCodeForVerificationAsync(string accessCode);
    Task<bool> SendStatusUpdateAsync(string status, string? additionalData = null);
    
    // Device Status Tracking
    Task<bool> SendDeviceStatusAsync(string status);
    Task<List<VoterCommandResponse>> GetPendingDeviceCommandsAsync();
    
    // Fingerprint Verification
    Task<FingerprintVerificationResponse?> VerifyFingerprintAsync(string voterId, byte[] scannedFingerprint);
}