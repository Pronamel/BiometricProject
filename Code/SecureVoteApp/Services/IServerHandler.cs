using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SecureVoteApp.Models;

namespace SecureVoteApp.Services;

public interface IServerHandler
{
    // Basic Server Communication
    Task<bool> TestConnectionAsync();
    
    // Voter Authentication & Session Management
    Task<VoterSessionResponse?> CreateVoterSessionAsync(string voterId, string county, string? stationId = null);
    bool IsAuthenticated { get; }
    string? CurrentVoterId { get; }
    string? AssignedStationId { get; }
    void Logout();
    
    // Voter Access Management
    Task<bool> RequestAccessFromOfficialAsync(string? deviceName = null);
    Task<string?> WaitForAccessCodeFromOfficialAsync();
    
    // Distributed Code Verification
    Task<bool> SubmitCodeForVerificationAsync(string accessCode);
    
    // Real-time Communication Loop
    Task<VoterCommandResponse?> ListenForOfficialCommandsAsync();
    Task<bool> StartContinuousListeningAsync(Action<VoterCommandResponse> onCommandReceived);
    void StopContinuousListening();
    
    // Status Updates to Official
    Task<bool> NotifyOfficialAsync(string status, string? additionalData = null);
    
    // Events for real-time updates
    event Action<string>? AccessCodeReceived;
    event Action<VoterCommandResponse>? OfficialCommandReceived;
    event Action<string>? VerificationResultReceived;
    event Action<bool>? ConnectionStatusChanged;
    event Action<string>? StatusMessageReceived;
}