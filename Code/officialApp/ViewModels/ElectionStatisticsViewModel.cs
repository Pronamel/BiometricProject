using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using officialApp.Models;
using officialApp.Services;

namespace officialApp.ViewModels;

public partial class ElectionStatisticsViewModel : ViewModelBase
{
    // ==========================================
    // OBSERVABLE PROPERTIES
    // ==========================================

    [ObservableProperty]
    private string totalVotesText = "0";

    [ObservableProperty]
    private string pollingStationsConnectedText = "0/0";

    [ObservableProperty]
    private string registeredVotersText = "0";

    [ObservableProperty]
    private string turnoutRateText = "0.000000%";

    [ObservableProperty]
    private string pollingStationConnectionRateText = "0.0%";

    [ObservableProperty]
    private string countryPopulationText = "0";

    [ObservableProperty]
    private string nationalParticipationText = "0.000000%";

    [ObservableProperty]
    private string bestConstituencyText = "N/A";

    [ObservableProperty]
    private string constituencyName = "Loading...";

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private string statusMessage = "Loading election statistics...";

    [ObservableProperty]
    private string statusColor = "black";

    [ObservableProperty]
    private ObservableCollection<CandidateVoteInfo> candidateVotes = new();

    [ObservableProperty]
    private ObservableCollection<ConstituencyTurnoutInfo> topConstituencies = new();

    // For chart visualization
    [ObservableProperty]
    private double maxVoteCount = 100;

    [ObservableProperty]
    private string electionSummary = "";

    [ObservableProperty]
    private bool hasCandidateVotes;

    [ObservableProperty]
    private bool showNoCandidateMessage;

    [ObservableProperty]
    private bool isNotLoading = true;

    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;
    private readonly IServerHandler _serverHandler;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public ElectionStatisticsViewModel(IServerHandler serverHandler, INavigationService navigationService)
    {
        _navigationService = navigationService;
        _serverHandler = serverHandler;
    }

    // ==========================================
    // INITIALIZATION
    // ==========================================

    public async Task ActivateAsync()
    {
        await RefreshStatisticsAsync();
    }

    // ==========================================
    // COMMANDS
    // ==========================================

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateToOfficialMenu();
    }

    [RelayCommand]
    private async Task RefreshStatistics()
    {
        await RefreshStatisticsAsync();
    }

    // ==========================================
    // PRIVATE METHODS
    // ==========================================

    private async Task RefreshStatisticsAsync()
    {
        try
        {
            IsLoading = true;
            IsNotLoading = false;
            StatusMessage = "Fetching election statistics...";
            StatusColor = "black";

            var stats = await _serverHandler.GetElectionStatisticsAsync();

            if (stats == null || !stats.Success)
            {
                StatusMessage = "Failed to load election statistics";
                StatusColor = "#e74c3c";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Failed to fetch election statistics");
                return;
            }

            // Update observable properties
            TotalVotesText = stats.TotalVotes.ToString();
            RegisteredVotersText = stats.RegisteredVoters.ToString("N0");
            TurnoutRateText = $"{stats.TurnoutRate:F6}%";
            PollingStationsConnectedText = $"{stats.PollingStationsConnected}/{stats.TotalPollingStations}";
            PollingStationConnectionRateText = $"{stats.PollingStationConnectionRate:F1}%";
            CountryPopulationText = stats.CountryPopulation.ToString("N0");
            NationalParticipationText = $"{stats.PopulationParticipationRate:F6}%";
            ConstituencyName = stats.ConstituencyName ?? "Unknown Constituency";
            BestConstituencyText = stats.BestConstituency != null
                ? $"{stats.BestConstituency.ConstituencyName} ({stats.BestConstituency.TurnoutRate:F1}% turnout)"
                : "N/A";

            // Update candidate votes
            CandidateVotes.Clear();
            foreach (var candidate in stats.CandidateVotes)
            {
                CandidateVotes.Add(candidate);
            }

            HasCandidateVotes = CandidateVotes.Count > 0;
            ShowNoCandidateMessage = !HasCandidateVotes;

            TopConstituencies.Clear();
            foreach (var constituency in stats.TopConstituencies)
            {
                TopConstituencies.Add(constituency);
            }

            // Calculate max vote count for chart scaling
            if (CandidateVotes.Count > 0)
            {
                MaxVoteCount = Math.Max(100, CandidateVotes[0].VoteCount * 1.2);
            }

            // Build election summary
            BuildElectionSummary(stats);

            StatusMessage = "✅ Statistics updated successfully";
            StatusColor = "#27ae60";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Election statistics loaded successfully");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            StatusColor = "#e74c3c";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Exception loading election statistics: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            IsNotLoading = true;
        }
    }

    private void BuildElectionSummary(ElectionStatistics stats)
    {
        var summary = $"Election Summary\n";
        summary += $"Total Votes Cast: {stats.TotalVotes:N0}\n";
        summary += $"Registered Voters: {stats.RegisteredVoters:N0}\n";
        summary += $"Turnout vs Country Population: {stats.TurnoutRate:F6}%\n";
        summary += $"Polling Stations Connected: {stats.PollingStationsConnected}/{stats.TotalPollingStations} ({stats.PollingStationConnectionRate:F1}%)\n";
        summary += $"Country Population Baseline: {stats.CountryPopulation:N0}\n";
        summary += $"Population Participation: {stats.PopulationParticipationRate:F6}%\n";
        summary += $"Top Constituency by Turnout: {BestConstituencyText}\n";

        if (CandidateVotes.Count > 0)
        {
            summary += $"\nLeading: {CandidateVotes[0].FullName} ({CandidateVotes[0].Party}) - {CandidateVotes[0].VoteCount} votes";
        }

        ElectionSummary = summary;
    }
}
