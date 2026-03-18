using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using officialApp.Services;
using officialApp.Models;
using System.Threading;
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

    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public OfficialVotingPollingManagerViewModel()
    {
        _navigationService = Navigation.Instance;
        _apiService = ApiService.Instance;
        InitializeSystemStatus();
        
        // Start listening for votes when this view model is created
        _ = StartVoteListening();
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
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official starting to listen for voter requests and votes...");
        
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
    
    // Method to be called when a vote is received
    public void OnVoteReceived(string candidateName, string partyName, int voterId)
    {
        TotalVotesCast++;
        
        // Update the UI-bound properties
        TotalVotes = TotalVotesCast.ToString();
        ValidVotes = TotalVotesCast.ToString(); // Assuming all votes are valid for now
        
        LastVoteInfo = $"Vote #{TotalVotesCast}: {candidateName} - {partyName} (Voter ID: {voterId})";
        
        // Update status messages to show latest vote
        StatusMessages = $"System initialized successfully.\nPolling stations online: 45/45\nLast update: {DateTime.Now:HH:mm:ss}\n\nLATEST VOTE:\n{LastVoteInfo}\nTotal Votes: {TotalVotesCast}";
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote received: {LastVoteInfo}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Total votes now: {TotalVotesCast}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] UI TotalVotes updated to: {TotalVotes}");
    }
    
    [RelayCommand]
    private void StopVoteListening()
    {
        IsListeningForVotes = false;
        _voteListeningCancellation?.Cancel();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official stopped listening for votes");
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
        
        // You can add methods here to update these values from your polling system
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