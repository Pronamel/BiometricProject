using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using officialApp.Services;
using officialApp.Models;
using System.Threading;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;

namespace officialApp.ViewModels;

public partial class OfficialVotingPollingManagerViewModel : ViewModelBase
{
    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;
    private readonly IServerHandler _serverHandler;
    private readonly IRealtimeService _realtimeService;
    private CancellationTokenSource? _voteListeningCancellation;
    private bool _realtimeSubscriptionsRegistered;
    private bool _isConnectingRealtime;
    private readonly Dictionary<string, CancellationTokenSource> _pendingDisconnectRemovals = new();
    private const int DisconnectedTemplateRemovalDelaySeconds = 15;

    // ==========================================
    // OBSERVABLE PROPERTIES
    // ==========================================

    // System Health Properties
    [ObservableProperty]
    private IBrush systemHealthColor = Brushes.Green;

    [ObservableProperty]
    private string healthStatusText = "System operating normally. All polling stations connected.";

    [ObservableProperty]
    private string statusMessages = "System initialized successfully.\nPolling stations online: 45/45\nLast update: " + DateTime.Now.ToString("HH:mm:ss");

    [ObservableProperty]
    private string commandInput = "";

    // Voting Statistics Properties
    [ObservableProperty]
    private string totalVotes = "0";

    [ObservableProperty]
    private string validVotes = "0";

    [ObservableProperty]
    private string invalidVotes = "0";

    [ObservableProperty]
    private string devicesLocked = "0";

    [ObservableProperty]
    private string registeredVoters = "0";

    [ObservableProperty]
    private string expectedVoters = "0";

    [ObservableProperty]
    private string turnoutRate = "0.0%";

    [ObservableProperty]
    private string pollingStartTime = "08:00 AM";

    [ObservableProperty]
    private string pollingEndTime = "06:00 PM";

    [ObservableProperty]
    private string systemStatus = "Online";
    
    [ObservableProperty]
    private int totalVotesCast = 0;
    
    [ObservableProperty]
    private string lastVoteInfo = "No votes cast yet";
    
    [ObservableProperty] 
    private bool isListeningForVotes = false;

    private int _devicesLockedCount;

    [ObservableProperty]
    private ObservableCollection<ConnectedVoterDevice> connectedDevices = new();

    private int _nextDeviceNumber = 1;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public OfficialVotingPollingManagerViewModel(IServerHandler serverHandler, INavigationService navigationService, IRealtimeService realtimeService)
    {
        _navigationService = navigationService;
        _serverHandler = serverHandler;
        _realtimeService = realtimeService;
        InitializeSystemStatus();

        RegisterRealtimeSubscriptions();
    }

    public async Task ActivateAsync()
    {
        SystemStatus = "Syncing polling data...";
        await StartVoteListening();
        await RefreshPollingStationVoteCountAsync();
        StatusMessages = $"Live feed ready. Device templates update in real-time.\nLast sync: {DateTime.Now:HH:mm:ss}\nConnected devices: {ConnectedDevices.Count}";
    }

    public async Task WarmupRealtimeAsync()
    {
        await StartVoteListening();
    }

    // ==========================================
    // COMMANDS
    // ==========================================
    
    [RelayCommand]
    private async Task StartVoteListening()
    {
        if (IsListeningForVotes)
        {
            return;
        }

        if (_isConnectingRealtime)
        {
            return;
        }

        if (_realtimeService.IsConnected)
        {
            IsListeningForVotes = true;
            SystemStatus = "Connected (live)";
            return;
        }
        
        IsListeningForVotes = true;
        _isConnectingRealtime = true;
        _voteListeningCancellation = new CancellationTokenSource();
        SystemStatus = "Connecting live device feed...";
        StatusMessages = $"Connecting to real-time channel...\nLast sync attempt: {DateTime.Now:HH:mm:ss}";
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official connecting to realtime channel for voter requests, votes, and device statuses...");
        
        try
        {
            var connected = await _realtimeService.ConnectAsync(_voteListeningCancellation.Token);
            if (!connected)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Realtime connection could not be established");
                IsListeningForVotes = false;
                SystemStatus = "Realtime unavailable";
                StatusMessages = $"Realtime connection failed. Retrying when you reopen manager.\nLast attempt: {DateTime.Now:HH:mm:ss}";
                return;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official realtime connection established");
            SystemStatus = "Connected (live)";
            StatusMessages = $"Realtime connected. Waiting for device status updates...\nConnected devices: {ConnectedDevices.Count}\nLast update: {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote listening was cancelled");
            IsListeningForVotes = false;
            SystemStatus = "Realtime cancelled";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote listening error: {ex.Message}");
            IsListeningForVotes = false;
            SystemStatus = "Realtime error";
            StatusMessages = $"Realtime error: {ex.Message}\nLast attempt: {DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            _isConnectingRealtime = false;
        }
    }

    // Method to be called when a vote is received
    public async Task OnVoteReceivedAsync(string candidateName, string partyName, int voterId)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Realtime vote received from voter {voterId}: {candidateName} - {partyName}");
        await RefreshPollingStationVoteCountAsync();
    }

    // Method to handle device status updates from voters
    public void OnDeviceStatusReceived(int voterId, string deviceId, string status)
    {
        var normalizedStatus = status.Trim().ToLowerInvariant();
        var isLocked = normalizedStatus == "locked by official" || normalizedStatus == "device locked by official";
        var isDisconnected = normalizedStatus == "disconnected";

        if (isDisconnected)
        {
            ScheduleDisconnectedTemplateRemoval(deviceId);
        }
        else
        {
            CancelDisconnectedTemplateRemoval(deviceId);
        }

        // Check if device already exists
        var existingDevice = ConnectedDevices.FirstOrDefault(d => d.DeviceIdentifier == deviceId);
        
        if (existingDevice != null)
        {
            // Update existing device status and timestamp
            existingDevice.VoterId = voterId;
            existingDevice.IsLockedByOfficial = isLocked;
            existingDevice.Status = status;
            existingDevice.LastStatusTime = DateTime.Now; // Record when status was received
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Updated Device #{existingDevice.DeviceNumber} (Voter {voterId}) status to: {status}");
        }
        else
        {
            // Add new device
            var device = new ConnectedVoterDevice
            {
                VoterId = voterId,
                DeviceNumber = _nextDeviceNumber++,
                IsLockedByOfficial = isLocked,
                Status = status,
                ConnectedAtTime = DateTime.Now,
                LastStatusTime = DateTime.Now, // Track when device first sent status
                DeviceIdentifier = deviceId
            };
            
            ConnectedDevices.Add(device);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] New device #{device.DeviceNumber} (Voter {voterId}) added with status: {status}");
        }

        StatusMessages = $"Live device update received.\nConnected devices: {ConnectedDevices.Count}\nLast update: {DateTime.Now:HH:mm:ss}";
    }

    public void OnDevicePresenceChanged(int voterId, string deviceId, bool isOnline, string status)
    {
        var existingDevice = ConnectedDevices.FirstOrDefault(d => d.DeviceIdentifier == deviceId);
        var resolvedStatus = string.IsNullOrWhiteSpace(status)
            ? (isOnline ? "Connected" : "Disconnected")
            : status;
        var normalizedStatus = resolvedStatus.Trim().ToLowerInvariant();
        var isLocked = normalizedStatus == "locked by official" || normalizedStatus == "device locked by official";

        if (existingDevice != null)
        {
            existingDevice.VoterId = voterId;
            existingDevice.IsLockedByOfficial = isLocked;
            existingDevice.Status = isOnline ? resolvedStatus : "Disconnected";
            existingDevice.LastStatusTime = DateTime.Now;

            if (isOnline)
            {
                CancelDisconnectedTemplateRemoval(deviceId);
            }
            else
            {
                ScheduleDisconnectedTemplateRemoval(deviceId);
            }

            StatusMessages = $"Presence update received.\nConnected devices: {ConnectedDevices.Count}\nLast update: {DateTime.Now:HH:mm:ss}";
            return;
        }

        if (!isOnline)
        {
            return;
        }

        var device = new ConnectedVoterDevice
        {
            VoterId = voterId,
            DeviceNumber = _nextDeviceNumber++,
            IsLockedByOfficial = isLocked,
            Status = resolvedStatus,
            ConnectedAtTime = DateTime.Now,
            LastStatusTime = DateTime.Now,
            DeviceIdentifier = deviceId
        };

        ConnectedDevices.Add(device);
        CancelDisconnectedTemplateRemoval(deviceId);
        StatusMessages = $"Device joined polling session.\nConnected devices: {ConnectedDevices.Count}\nLast update: {DateTime.Now:HH:mm:ss}";
    }

    // Method to add a new connected voter device
    public void OnVoterDeviceConnected(string deviceIdentifier)
    {
        var device = new ConnectedVoterDevice
        {
            DeviceNumber = _nextDeviceNumber++,
            Status = "Connected",
            ConnectedAtTime = DateTime.Now,
            DeviceIdentifier = deviceIdentifier
        };
        
        ConnectedDevices.Add(device);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device #{device.DeviceNumber} connected: {deviceIdentifier}");
    }

    // Method to update device status
    public void UpdateDeviceStatus(int deviceNumber, string newStatus)
    {
        var device = ConnectedDevices.FirstOrDefault(d => d.DeviceNumber == deviceNumber);
        if (device != null)
        {
            device.Status = newStatus;
            device.LastStatusTime = DateTime.Now; // Reset inactivity timer when status is updated
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device #{deviceNumber} status updated to: {newStatus}");
        }
    }

    [RelayCommand]
    private async Task LockDeviceAsync(ConnectedVoterDevice? device)
    {
        if (device == null || device.VoterId <= 0 || string.IsNullOrWhiteSpace(device.DeviceIdentifier))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cannot lock device: invalid template data");
            return;
        }

        var previousStatus = device.Status;
        device.Status = "Lock command sent";

        var success = await _serverHandler.SendDeviceCommandAsync(new SendDeviceCommandRequest
        {
            VoterId = device.VoterId,
            DeviceId = device.DeviceIdentifier,
            CommandType = "lock_device"
        });

        if (success)
        {
            device.IsLockedByOfficial = true;
            device.Status = "Locked by official";
            _devicesLockedCount++;
            DevicesLocked = _devicesLockedCount.ToString();
            return;
        }

        device.Status = previousStatus;
    }

    [RelayCommand]
    private async Task UnlockDeviceAsync(ConnectedVoterDevice? device)
    {
        if (device == null || device.VoterId <= 0 || string.IsNullOrWhiteSpace(device.DeviceIdentifier))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cannot unlock device: invalid template data");
            return;
        }

        var previousStatus = device.Status;
        device.Status = "Unlock command sent";

        var success = await _serverHandler.SendDeviceCommandAsync(new SendDeviceCommandRequest
        {
            VoterId = device.VoterId,
            DeviceId = device.DeviceIdentifier,
            CommandType = "unlock_device"
        });

        if (success)
        {
            device.IsLockedByOfficial = false;
            device.Status = "Unlocked";
            return;
        }

        device.Status = previousStatus;
    }

    // Method to remove a disconnected device
    public void OnVoterDeviceDisconnected(int deviceNumber)
    {
        var device = ConnectedDevices.FirstOrDefault(d => d.DeviceNumber == deviceNumber);
        if (device != null)
        {
            CancelDisconnectedTemplateRemoval(device.DeviceIdentifier);
            ConnectedDevices.Remove(device);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device #{deviceNumber} disconnected");
        }
    }

    private void ScheduleDisconnectedTemplateRemoval(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        CancelDisconnectedTemplateRemoval(deviceId);

        var cts = new CancellationTokenSource();
        _pendingDisconnectRemovals[deviceId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(DisconnectedTemplateRemovalDelaySeconds), cts.Token);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var device = ConnectedDevices.FirstOrDefault(d => d.DeviceIdentifier == deviceId);
                    if (device == null)
                    {
                        return;
                    }

                    var status = device.Status?.Trim().ToLowerInvariant() ?? string.Empty;
                    var disconnectedFor = DateTime.Now - device.LastStatusTime;
                    if (status == "disconnected" && disconnectedFor >= TimeSpan.FromSeconds(DisconnectedTemplateRemovalDelaySeconds))
                    {
                        ConnectedDevices.Remove(device);
                        StatusMessages = $"Removed stale disconnected device template after {DisconnectedTemplateRemovalDelaySeconds}s.\nConnected devices: {ConnectedDevices.Count}\nLast update: {DateTime.Now:HH:mm:ss}";
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Removed disconnected device template: {deviceId}");
                    }
                }, DispatcherPriority.Input);
            }
            catch (TaskCanceledException)
            {
            }
            finally
            {
                if (_pendingDisconnectRemovals.TryGetValue(deviceId, out var pending) && pending == cts)
                {
                    _pendingDisconnectRemovals.Remove(deviceId);
                }

                cts.Dispose();
            }
        });
    }

    private void CancelDisconnectedTemplateRemoval(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        if (_pendingDisconnectRemovals.TryGetValue(deviceId, out var cts))
        {
            _pendingDisconnectRemovals.Remove(deviceId);
            cts.Cancel();
            cts.Dispose();
        }
    }
    
    [RelayCommand]
    private void StopVoteListening()
    {
        IsListeningForVotes = false;
        _voteListeningCancellation?.Cancel();
        _ = _realtimeService.DisconnectAsync();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official stopped listening for votes");
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateToOfficialMenu();
    }
    
    [RelayCommand]
    private void Execute()
    {
        if (!string.IsNullOrWhiteSpace(CommandInput))
        {
            // Process command input
            StatusMessages += $"\n> {CommandInput}";
            StatusMessages += "\nCommand processed successfully.";
            CommandInput = "";
        }
    }

    // ==========================================
    // PRIVATE METHODS
    // ==========================================

    private void InitializeSystemStatus()
    {
        // Initialize with default values
        SystemHealthColor = Brushes.Green;
        // Connected devices will be populated when voters connect via API
    }

    private async Task RefreshPollingStationVoteCountAsync()
    {
        var stats = await _serverHandler.GetPollingStationVoteCountAsync();
        if (stats == null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Could not refresh polling station vote count");
            return;
        }

        TotalVotes = stats.TotalVotes.ToString();
        ExpectedVoters = stats.ExpectedVotes.ToString();

        if (stats.ExpectedVotes > 0)
        {
            var turnout = (double)stats.TotalVotes / stats.ExpectedVotes * 100;
            TurnoutRate = $"{turnout:F1}%";
        }
        else
        {
            TurnoutRate = "0.0%";
        }

        StatusMessages = $"System initialized successfully.\nPolling stations online: 45/45\nLast update: {DateTime.Now:HH:mm:ss}\n\nPolling Station Votes (VoteRecords): {TotalVotes}";
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Polling station stats refreshed from VoteRecords: total={TotalVotes}, expected={ExpectedVoters}, turnout={TurnoutRate}");
    }

    // Add methods here for updating statistics in real-time
    public void UpdateSystemHealth(bool isHealthy)
    {
        SystemHealthColor = isHealthy ? Brushes.Green : Brushes.Red;
        HealthStatusText = isHealthy ? 
            "System operating normally. All polling stations connected." : 
            "System issues detected. Please check polling station connections.";
    }

    public void UpdateVotingStatistics(int total, int valid, int invalid, int registered)
    {
        TotalVotes = total.ToString();
        ValidVotes = valid.ToString();
        InvalidVotes = invalid.ToString();
        RegisteredVoters = registered.ToString();
        
        if (registered > 0)
        {
            double turnout = (double)total / registered * 100;
            TurnoutRate = $"{turnout:F1}%";
        }
    }

    private void RegisterRealtimeSubscriptions()
    {
        if (_realtimeSubscriptionsRegistered)
        {
            return;
        }

        _realtimeService.VoterRequestsReceived += requests =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received {requests.Count} realtime voter link requests");
                foreach (var request in requests)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] New voter link request: {request}");
                }
            });
        };

        _realtimeService.VoteReceived += vote =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Realtime vote from Voter {vote.VoterId}: {vote.CandidateName} - {vote.PartyName}");
                _ = OnVoteReceivedAsync(vote.CandidateName, vote.PartyName, vote.VoterId);
            });
        };

        _realtimeService.DeviceStatusReceived += deviceStatus =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Realtime device status from Voter {deviceStatus.VoterId}: {deviceStatus.Status} (Device: {deviceStatus.DeviceId})");
                OnDeviceStatusReceived(deviceStatus.VoterId, deviceStatus.DeviceId, deviceStatus.Status);
            });
        };

        _realtimeService.DevicePresenceChanged += presence =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var isOnline = string.Equals(presence.State, "online", StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Presence update for device {presence.DeviceId}: {presence.State}");
                OnDevicePresenceChanged(presence.VoterId, presence.DeviceId, isOnline, presence.Status);
            });
        };

        _realtimeService.ConnectionStateChanged += state =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                SystemStatus = state;
            });
        };

        _realtimeSubscriptionsRegistered = true;
    }
}