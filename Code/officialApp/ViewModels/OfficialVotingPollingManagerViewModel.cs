using System;
using System.Threading.Tasks;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using officialApp.Services;
using officialApp.Models;
using System.Threading;
using System.Collections.ObjectModel;
using Avalonia.Media;

namespace officialApp.ViewModels;

public partial class OfficialVotingPollingManagerViewModel : ViewModelBase
{
    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;
    private readonly IApiService _apiService;
    private CancellationTokenSource? _voteListeningCancellation;
    private CancellationTokenSource? _deviceStatusListeningCancellation;

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
    private string registeredVoters = "0";

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

    [ObservableProperty]
    private ObservableCollection<ConnectedVoterDevice> connectedDevices = new();

    private int _nextDeviceNumber = 1;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public OfficialVotingPollingManagerViewModel(IApiService apiService, INavigationService navigationService)
    {
        _navigationService = navigationService;
        _apiService = apiService;
        InitializeSystemStatus();
        
        // Start listening for votes when this view model is created
        _ = StartVoteListening();
        _ = StartDeviceStatusListening();
    }

    // ==========================================
    // COMMANDS
    // ==========================================
    
    [RelayCommand]
    private async Task StartVoteListening()
    {
        if (IsListeningForVotes) return;
        
        IsListeningForVotes = true;
        _voteListeningCancellation = new CancellationTokenSource();
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official starting to listen for voter requests, votes, and device statuses...");
        
        try
        {
            while (IsListeningForVotes && !_voteListeningCancellation.Token.IsCancellationRequested)
            {
                // First, check for voter link requests
                var voterRequests = await _apiService.WaitForVoterRequestsAsync();
                
                if (voterRequests?.Requests.Count > 0)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received {voterRequests.Requests.Count} voter link requests");
                    
                    // Process each voter request
                    foreach (var request in voterRequests.Requests)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] New voter link request: {request}");
                    }
                }
                
                // Then check for new votes through API
                var voteResponse = await _apiService.CheckForVotesAsync();
                
                if (voteResponse?.Success == true && voteResponse.Count > 0)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received {voteResponse.Count} new votes");
                    
                    // Process each vote
                    foreach (var vote in voteResponse.Votes)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Processing vote from Voter {vote.VoterId}: {vote.CandidateName} - {vote.PartyName}");
                        OnVoteReceived(vote.CandidateName, vote.PartyName, vote.VoterId);
                    }
                }
                
                // Wait 2 seconds before checking again
                await Task.Delay(2000, _voteListeningCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote listening was cancelled");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote listening error: {ex.Message}");
        }
        finally
        {
            IsListeningForVotes = false;
        }
    }

    private async Task StartDeviceStatusListening()
    {
        if (_deviceStatusListeningCancellation != null)
        {
            return;
        }

        _deviceStatusListeningCancellation = new CancellationTokenSource();

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official starting to listen for device statuses...");

        try
        {
            while (!_deviceStatusListeningCancellation.Token.IsCancellationRequested)
            {
                var deviceStatusResponse = await _apiService.GetDeviceStatusesAsync();

                if (deviceStatusResponse?.Success == true && deviceStatusResponse.Count > 0)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received {deviceStatusResponse.Count} device status updates");

                    foreach (var deviceStatus in deviceStatusResponse.Statuses)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device status update from Voter {deviceStatus.VoterId}: {deviceStatus.Status} (Device: {deviceStatus.DeviceId})");
                        OnDeviceStatusReceived(deviceStatus.VoterId, deviceStatus.DeviceId, deviceStatus.Status);
                    }
                }

                CleanupInactiveDevices();

                await Task.Delay(1000, _deviceStatusListeningCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device status listening was cancelled");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device status listening error: {ex.Message}");
        }
    }
    
    // Method to be called when a vote is received
    public void OnVoteReceived(string candidateName, string partyName, int voterId)
    {
        TotalVotesCast++;
        
        // Update the UI-bound properties
        TotalVotes = TotalVotesCast.ToString();
        ValidVotes = TotalVotesCast.ToString(); // Assuming all votes are valid for now
        
        LastVoteInfo = $"Vote #{TotalVotesCast}: {candidateName} - {partyName} (Voter ID: {voterId})";
        
        // Update status messages to show total votes
        StatusMessages = $"System initialized successfully.\nPolling stations online: 45/45\nLast update: {DateTime.Now:HH:mm:ss}\n\nTotal Votes: {TotalVotesCast}";
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote received: {LastVoteInfo}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Total votes now: {TotalVotesCast}");
    }

    // Method to handle device status updates from voters
    public void OnDeviceStatusReceived(int voterId, string deviceId, string status)
    {
        // Check if device already exists
        var existingDevice = ConnectedDevices.FirstOrDefault(d => d.DeviceIdentifier == deviceId);
        
        if (existingDevice != null)
        {
            // Update existing device status and timestamp
            existingDevice.Status = status;
            existingDevice.LastStatusTime = DateTime.Now; // Record when status was received
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Updated Device #{existingDevice.DeviceNumber} (Voter {voterId}) status to: {status}");
        }
        else
        {
            // Add new device
            var device = new ConnectedVoterDevice
            {
                DeviceNumber = _nextDeviceNumber++,
                Status = status,
                ConnectedAtTime = DateTime.Now,
                LastStatusTime = DateTime.Now, // Track when device first sent status
                DeviceIdentifier = deviceId
            };
            
            ConnectedDevices.Add(device);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] New device #{device.DeviceNumber} (Voter {voterId}) added with status: {status}");
        }
    }

    // Method to clean up inactive devices (not heard from in 15+ seconds)
    private void CleanupInactiveDevices()
    {
        var now = DateTime.Now;
        var inactiveThreshold = TimeSpan.FromSeconds(15); // Remove devices inactive for 15+ seconds
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 Cleanup check: {ConnectedDevices.Count} devices, threshold: {inactiveThreshold.TotalSeconds}s");
        
        var devicesToRemove = ConnectedDevices
            .Where(d => (now - d.LastStatusTime) > inactiveThreshold)
            .ToList();
        
        if (devicesToRemove.Count > 0)
        {
            foreach (var device in devicesToRemove)
            {
                var inactiveFor = (now - device.LastStatusTime).TotalSeconds;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⏱ Removing inactive Device #{device.DeviceNumber} (Device ID: {device.DeviceIdentifier}, inactive for {inactiveFor:F1}s, last status: {device.LastStatusTime:HH:mm:ss})");
                ConnectedDevices.Remove(device);
            }
        }
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

    // Method to remove a disconnected device
    public void OnVoterDeviceDisconnected(int deviceNumber)
    {
        var device = ConnectedDevices.FirstOrDefault(d => d.DeviceNumber == deviceNumber);
        if (device != null)
        {
            ConnectedDevices.Remove(device);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device #{deviceNumber} disconnected");
        }
    }
    
    [RelayCommand]
    private void StopVoteListening()
    {
        IsListeningForVotes = false;
        _voteListeningCancellation?.Cancel();
        _deviceStatusListeningCancellation?.Cancel();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official stopped listening for votes");
    }

    [RelayCommand]
    private void GoBack()
    {
        StopVoteListening();
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
}