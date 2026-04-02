using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SecureVoteApp.Models;

namespace SecureVoteApp.Services;

public interface IApiService
{
    // Authentication & Session Management
    Task<VoterLinkResponse> LinkToOfficialAsync(string pollingStationCode, string county, string constituency);
    Task<VoterAuthLookupResponse?> LookupVoterForAuthAsync(string? nin, string? firstName, string? lastName, string? dateOfBirth, string county, string constituency);
    
    // Vote Casting
    Task<CastVoteResponse> CastVoteAsync(string candidateName, string partyName);
    
    // Voter State
    bool IsAuthenticated { get; }
    string? CurrentVoterId { get; }
    string? AssignedStationId { get; }
    int AssignedVoterId { get; }
    string SelectedCounty { get; }
    string PollingStationCode { get; }
    string CurrentDeviceStatus { get; set; }
    Task LogoutAsync();
    void Logout();
    
    // Connection Testing
    Task<bool> TestConnectionAsync();
    
    // Voter Access Management
    Task<bool> RequestAccessAsync(string? deviceName = null);
    Task<CodeWaitResponse?> WaitForAccessCodeAsync();
    
    // Real-time Communication (Distributed Validation)
    Task<bool> SubmitCodeForVerificationAsync(string accessCode);
    Task<VoterCommandResponse?> ListenForCommandsAsync();
    Task<bool> SendStatusUpdateAsync(string status, string? additionalData = null);
    
    // Device Status Tracking
    Task<bool> SendDeviceStatusAsync(string status);
    
    // Fingerprint Verification
    Task<FingerprintVerificationResponse?> VerifyFingerprintAsync(string voterId, byte[] scannedFingerprint);
}