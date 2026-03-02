using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using System.Net.Http;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;

namespace officialApp.ViewModels;

public partial class OfficialVotingPollingManagerViewModel : ViewModelBase
{
    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;

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

    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public OfficialVotingPollingManagerViewModel()
    {
        _navigationService = Navigation.Instance;
        InitializeSystemStatus();
    }

    // ==========================================
    // COMMANDS
    // ==========================================
    
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