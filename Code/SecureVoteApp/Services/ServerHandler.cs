using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SecureVoteApp.Models;

namespace SecureVoteApp.Services;

public class ServerHandler : IServerHandler
{
    private readonly IApiService _apiService;
    private readonly IVoterRealtimeService _realtimeService;
    private CancellationTokenSource? _listeningCancellation;
    private bool _isListening;
    private Action<VoterCommandResponse>? _externalCommandHandler;
    private Task? _fallbackPollingTask;
    
    // Events for real-time updates
    public event Action<string>? AccessCodeReceived;
    public event Action<VoterCommandResponse>? OfficialCommandReceived;
    public event Action<bool>? ConnectionStatusChanged;
    public event Action<string>? StatusMessageReceived;
    
    // Properties
    public bool IsAuthenticated => _apiService.IsAuthenticated;
    public string? CurrentVoterId => _apiService.CurrentVoterId;
    public string? AssignedStationId => _apiService.AssignedStationId;
    public string CurrentDeviceStatus
    {
        get => _apiService.CurrentDeviceStatus;
        set => _apiService.CurrentDeviceStatus = value;
    }
    
    public ServerHandler(IApiService apiService, IVoterRealtimeService realtimeService)
    {
        _apiService = apiService;
        _realtimeService = realtimeService;

        _realtimeService.CommandReceived += command =>
        {
            OfficialCommandReceived?.Invoke(command);
            _externalCommandHandler?.Invoke(command);
        };

        _realtimeService.AccessCodeReceived += response =>
        {
            if (response?.Success == true && !string.IsNullOrWhiteSpace(response.Code))
            {
                AccessCodeReceived?.Invoke(response.Code);
                StatusMessageReceived?.Invoke($"Access code received: {response.Code}");
            }
        };

        _realtimeService.ConnectionStateChanged += state =>
        {
            ConnectionStatusChanged?.Invoke(state.Equals("Connected", StringComparison.OrdinalIgnoreCase));
            StatusMessageReceived?.Invoke($"Realtime state: {state}");
        };
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

    public Task<VoterLinkResponse> LinkToOfficialAsync(string pollingStationCode, string county, string constituency)
        => _apiService.LinkToOfficialAsync(pollingStationCode, county, constituency);

    public Task<VoterAuthLookupResponse?> LookupVoterForAuthAsync(string? firstName, string? lastName, string? dateOfBirth, string? postCode, string county, string constituency)
        => _apiService.LookupVoterForAuthAsync(firstName, lastName, dateOfBirth, postCode, county, constituency);

    public Task<List<Candidate>> FetchCandidatesAsync()
        => _apiService.FetchCandidatesAsync();

    public Task<CastVoteResponse> CastVoteAsync(Guid candidateId, string candidateName, string partyName)
        => _apiService.CastVoteAsync(candidateId, candidateName, partyName);

    public Task<FingerprintVerificationResponse?> VerifyFingerprintAsync(string voterId, byte[] scannedFingerprint)
        => _apiService.VerifyFingerprintAsync(voterId, scannedFingerprint);
    
    // ==========================================
    // VOTER AUTHENTICATION & SESSION MANAGEMENT
    // ==========================================
    
    public void Logout()
    {
        StopContinuousListening();
        _ = _apiService.LogoutAsync();
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
    
    public async Task<bool> StartContinuousListeningAsync(Action<VoterCommandResponse> onCommandReceived)
    {
        if (_isListening)
        {
            return false; // Already listening
        }

        _listeningCancellation = new CancellationTokenSource();
        _isListening = true;

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Starting realtime listening for official commands...");
        StatusMessageReceived?.Invoke("Starting realtime communication with official...");

        _externalCommandHandler = onCommandReceived;

        try
        {
            var connected = await _realtimeService.ConnectAsync(_apiService.DeviceId, _listeningCancellation.Token);
            if (!connected)
            {
                _isListening = false;
                StatusMessageReceived?.Invoke("Failed to start realtime communication");
                return false;
            }

            _fallbackPollingTask = Task.Run(async () =>
            {
                while (_isListening && _listeningCancellation != null && !_listeningCancellation.Token.IsCancellationRequested)
                {
                    try
                    {
                        // Realtime is primary. Poll fallback only when realtime is down.
                        if (!_realtimeService.IsConnected)
                        {
                            var pendingCommands = await _apiService.GetPendingDeviceCommandsAsync();
                            foreach (var pending in pendingCommands)
                            {
                                OfficialCommandReceived?.Invoke(pending);
                                _externalCommandHandler?.Invoke(pending);
                            }
                        }

                        await Task.Delay(2000, _listeningCancellation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fallback command poll error: {ex.Message}");
                    }
                }
            }, _listeningCancellation.Token);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Start realtime listening error: {ex.Message}");
            _isListening = false;
            StatusMessageReceived?.Invoke($"Realtime start failed: {ex.Message}");
            return false;
        }
    }
    
    public void StopContinuousListening()
    {
        if (_isListening)
        {
            _listeningCancellation?.Cancel();
            _ = _realtimeService.DisconnectAsync();
            _fallbackPollingTask = null;
            _externalCommandHandler = null;
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

    public Task<bool> SendDeviceStatusAsync(string status)
        => _apiService.SendDeviceStatusAsync(status);
}