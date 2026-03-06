using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SecureVoteApp.Models;

namespace SecureVoteApp.Services;

public class ServerHandler : IServerHandler
{
    public static ServerHandler Instance { get; private set; }
    
    private readonly IApiService _apiService;
    private CancellationTokenSource? _listeningCancellation;
    private bool _isListening;
    
    // Events for real-time updates
    public event Action<string>? AccessCodeReceived;
    public event Action<VoterCommandResponse>? OfficialCommandReceived;
    public event Action<string>? VerificationResultReceived;
    public event Action<bool>? ConnectionStatusChanged;
    public event Action<string>? StatusMessageReceived;
    
    // Properties
    public bool IsAuthenticated => _apiService.IsAuthenticated;
    public string? CurrentVoterId => _apiService.CurrentVoterId;
    public string? AssignedStationId => _apiService.AssignedStationId;
    
    static ServerHandler()
    {
        Instance = new ServerHandler();
    }
    
    public ServerHandler()
    {
        _apiService = ApiService.Instance;
    }

    // ==========================================
    // AUTHENTICATION HELPER
    // ==========================================
    
    private void ThrowIfNotAuthenticated()
    {
        if (!IsAuthenticated)
        {
            throw new InvalidOperationException("Not authenticated. Please create voter session first.");
        }
    }
    
    // ==========================================
    // SERVER COMMUNICATION
    // ==========================================
    
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var isConnected = await _apiService.TestConnectionAsync();
            ConnectionStatusChanged?.Invoke(isConnected);
            return isConnected;
        }
        catch
        {
            ConnectionStatusChanged?.Invoke(false);
            return false;
        }
    }
    
    // ==========================================
    // VOTER AUTHENTICATION & SESSION MANAGEMENT
    // ==========================================
    
    public async Task<VoterSessionResponse?> CreateVoterSessionAsync(string voterId, string county, string? stationId = null)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Creating voter session for {voterId} in {county} at station {stationId ?? "unassigned"}");
            
            var result = await _apiService.CreateSessionAsync(voterId, county, stationId);
            
            if (result?.Success == true)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter session created successfully");
                StatusMessageReceived?.Invoke("Authenticated successfully");
                return result;
            }
            else
            {
                StatusMessageReceived?.Invoke("Authentication failed");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Session creation error: {ex.Message}");
            StatusMessageReceived?.Invoke($"Authentication error: {ex.Message}");
            return null;
        }
    }
    
    public void Logout()
    {
        StopContinuousListening();
        _apiService.Logout();
        StatusMessageReceived?.Invoke("Logged out successfully");
    }
    
    // ==========================================
    // VOTER ACCESS MANAGEMENT
    // ==========================================
    
    public async Task<bool> RequestAccessFromOfficialAsync(string? deviceName = null)
    {
        try
        {
            ThrowIfNotAuthenticated();
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Requesting access from official...");
            var success = await _apiService.RequestAccessAsync(deviceName);
            
            if (success)
            {
                StatusMessageReceived?.Invoke("Access request sent to official");
            }
            else
            {
                StatusMessageReceived?.Invoke("Failed to send access request");
            }
            
            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Access request error: {ex.Message}");
            StatusMessageReceived?.Invoke($"Access request failed: {ex.Message}");
            return false;
        }
    }
    
    public async Task<string?> WaitForAccessCodeFromOfficialAsync()
    {
        try
        {
            ThrowIfNotAuthenticated();
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Waiting for access code from official...");
            var response = await _apiService.WaitForAccessCodeAsync();
            
            if (response?.Success == true && !string.IsNullOrEmpty(response.Code))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Access code received: {response.Code}");
                AccessCodeReceived?.Invoke(response.Code);
                StatusMessageReceived?.Invoke($"Access code received: {response.Code}");
                return response.Code;
            }
            else
            {
                var message = response?.Message ?? "No access code received";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                StatusMessageReceived?.Invoke(message);
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Code waiting error: {ex.Message}");
            StatusMessageReceived?.Invoke($"Code waiting failed: {ex.Message}");
            return null;
        }
    }
    
    // ==========================================
    // DISTRIBUTED CODE VERIFICATION
    // ==========================================
    
    public async Task<bool> SubmitCodeForVerificationAsync(string accessCode)
    {
        try
        {
            ThrowIfNotAuthenticated();
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Submitting code for verification: {accessCode}");
            var success = await _apiService.SubmitCodeForVerificationAsync(accessCode);
            
            if (success)
            {
                StatusMessageReceived?.Invoke("Code submitted for verification");
            }
            else
            {
                StatusMessageReceived?.Invoke("Failed to submit code for verification");
            }
            
            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Code verification error: {ex.Message}");
            StatusMessageReceived?.Invoke($"Code verification failed: {ex.Message}");
            return false;
        }
    }
    
    // ==========================================
    // REAL-TIME COMMUNICATION
    // ==========================================
    
    public async Task<VoterCommandResponse?> ListenForOfficialCommandsAsync()
    {
        try
        {
            ThrowIfNotAuthenticated();
            
            var command = await _apiService.ListenForCommandsAsync();
            
            if (command?.Success == true)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received command: {command.CommandType}");
                OfficialCommandReceived?.Invoke(command);
                
                // Handle specific command types
                switch (command.CommandType?.ToLower())
                {
                    case "verification_result":
                        var result = command.Data?.ToString() ?? "Unknown result";
                        VerificationResultReceived?.Invoke(result);
                        break;
                    case "official_linked":
                        StatusMessageReceived?.Invoke($"Linked to official: {command.OfficialId}");
                        break;
                    case "voting_instructions":
                        StatusMessageReceived?.Invoke("Voting instructions received");
                        break;
                    default:
                        StatusMessageReceived?.Invoke($"Command received: {command.CommandType}");
                        break;
                }
                
                return command;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Command listening error: {ex.Message}");
            return null;
        }
    }
    
    public async Task<bool> StartContinuousListeningAsync(Action<VoterCommandResponse> onCommandReceived)
    {
        if (_isListening)
        {
            return false; // Already listening
        }
        
        _listeningCancellation = new CancellationTokenSource();
        _isListening = true;
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Starting continuous listening for official commands...");
        StatusMessageReceived?.Invoke("Starting continuous communication with official...");
        
        // Start listening loop in background
        _ = Task.Run(async () =>
        {
            while (!_listeningCancellation.Token.IsCancellationRequested)
            {
                try
                {
                    var command = await ListenForOfficialCommandsAsync();
                    if (command != null)
                    {
                        onCommandReceived(command);
                    }
                    
                    // Brief pause before next request to prevent overwhelming the server
                    await Task.Delay(100, _listeningCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    break; // Expected when cancellation is requested
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Listening loop error: {ex.Message}");
                    await Task.Delay(1000, _listeningCancellation.Token); // Wait longer on error
                }
            }
        }, _listeningCancellation.Token);
        
        return true;
    }
    
    public void StopContinuousListening()
    {
        if (_isListening)
        {
            _listeningCancellation?.Cancel();
            _isListening = false;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Stopped continuous listening");
            StatusMessageReceived?.Invoke("Stopped communication with official");
        }
    }
    
    // ==========================================
    // STATUS UPDATES TO OFFICIAL
    // ==========================================
    
    public async Task<bool> NotifyOfficialAsync(string status, string? additionalData = null)
    {
        try
        {
            ThrowIfNotAuthenticated();
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending status to official: {status}");
            var success = await _apiService.SendStatusUpdateAsync(status, additionalData);
            
            if (!success)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Failed to send status update");
            }
            
            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Status update error: {ex.Message}");
            return false;
        }
    }
}