using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureVoteApp.Services;
using SecureVoteApp.Models;

namespace SecureVoteApp.ViewModels;

public partial class BallotPaperViewModel : ViewModelBase
{
    // ==========================================
    // PRIVATE FIELDS
    // ==========================================
    
    private readonly INavigationService _navigationService;
    private readonly IServerHandler _serverHandler;
    
    [ObservableProperty]
    private ObservableCollection<Candidate> candidates = new ObservableCollection<Candidate>();

    [ObservableProperty]
    private ObservableCollection<CandidateButtonViewModel> leftCandidates = new ObservableCollection<CandidateButtonViewModel>();

    [ObservableProperty]
    private ObservableCollection<CandidateButtonViewModel> rightCandidates = new ObservableCollection<CandidateButtonViewModel>();
    
    [ObservableProperty]
    private bool isLoadingCandidates = false;
    
    [ObservableProperty]
    private bool isCastingVote = false;
    
    [ObservableProperty]
    private string voteStatus = "";

    [ObservableProperty]
    private bool showVoteCastPopup = false;


    // ==========================================
    // STATIC PROPERTIES - VOTE TRACKING
    // ==========================================

    // Track which candidate is selected (null = none selected)
    public static Guid? SelectedCandidateId { get; set; } = null;
    
    // Track the voting result - candidate and party information
    
    public static string? SelectedCandidateName { get; set; } = null;
    public static string? SelectedParty { get; set; } = null;

    [ObservableProperty]
    private string readingCandidateName = SelectedCandidateName ?? "No candidate selected";
    
    // Get the complete voting result
    public static string VotingResult => 
        SelectedCandidateName != null && SelectedParty != null 
            ? $"{SelectedCandidateName} - {SelectedParty}" : "No candidate selected";
    
    // Check if someone has voted
    public static bool HasVoted => SelectedCandidateId.HasValue;




    // ==========================================
    // EVENT SYSTEM
    // ==========================================
    
    // Event to notify all buttons when selection changes
    public static event Action? SelectionChanged;




    // ==========================================
    // STATIC METHODS - VOTE MANAGEMENT  
    // ==========================================
    
    // Method to set selection and notify all buttons
    public static void SetSelectedCandidate(Guid candidateId, string candidateName, string partyName)
    {
        SelectedCandidateId = candidateId;
        SelectedCandidateName = candidateName;
        SelectedParty = partyName;
        SelectionChanged?.Invoke(); // Notify all buttons to update
    }
    
    // Method to clear selection
    public static void ClearSelection()
    {
        SelectedCandidateId = null;
        SelectedCandidateName = null;
        SelectedParty = null;
        SelectionChanged?.Invoke();
    }




    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public BallotPaperViewModel(INavigationService navigationService, IServerHandler serverHandler)
    {
        _navigationService = navigationService;
        _serverHandler = serverHandler;
        
        // Subscribe to selection changes to update the readable text
        SelectionChanged += UpdateReadingCandidateName;
    }

    // ==========================================
    // PRIVATE METHODS
    // ==========================================

    // Update the observable property when selection changes
    private void UpdateReadingCandidateName()
    {
        ReadingCandidateName = SelectedCandidateName ?? "No candidate selected";
    }

    private void PopulateCandidateColumns(IReadOnlyList<Candidate> candidateList)
    {
        LeftCandidates.Clear();
        RightCandidates.Clear();

        var splitIndex = (candidateList.Count + 1) / 2;
        var leftSideCandidates = candidateList.Take(splitIndex).ToList();
        var rightSideCandidates = candidateList.Skip(splitIndex).ToList();

        foreach (var candidate in leftSideCandidates)
        {
            LeftCandidates.Add(CreateCandidateButtonViewModel(candidate));
        }

        foreach (var candidate in rightSideCandidates)
        {
            RightCandidates.Add(CreateCandidateButtonViewModel(candidate));
        }
    }

    private static CandidateButtonViewModel CreateCandidateButtonViewModel(Candidate candidate)
    {
        return new CandidateButtonViewModel(
            candidate.CandidateId,
            $"{candidate.FirstName} {candidate.LastName}",
            candidate.Party ?? "Independent",
            candidate.Bio ?? string.Empty);
    }
    
    // Load candidates from the API
    public async Task LoadCandidatesAsync()
    {
        // Always clear all candidate collections at the very start to prevent duplication
        // across multiple voters using the reused singleton ViewModel
        // This must happen BEFORE any guards, so even if a second voter authenticates
        // while the first is still loading, the old candidates are cleared
        Candidates.Clear();
        LeftCandidates.Clear();
        RightCandidates.Clear();
        
        if (IsLoadingCandidates) return;
        
        if (!_serverHandler.IsAuthenticated)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Skipping candidate load because authentication is not complete");
            return;
        }
        
        try
        {
            IsLoadingCandidates = true;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📥 Loading candidates from server...");
            
            var candidateList = await _serverHandler.FetchCandidatesAsync();
            
            if (candidateList.Count == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ No candidates received from server");
                VoteStatus = "⚠️ No candidates available";
                return;
            }
            
            // Populate the candidates collection
            foreach (var candidate in candidateList)
            {
                Candidates.Add(candidate);
            }

            PopulateCandidateColumns(candidateList);
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Loaded {Candidates.Count} candidates");
            VoteStatus = $"Candidates loaded ({Candidates.Count} available)";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error loading candidates: {ex.Message}");
            VoteStatus = $"❌ Error loading candidates: {ex.Message}";
        }
        finally
        {
            IsLoadingCandidates = false;
        }
    }




    // ==========================================
    // COMMANDS
    // ==========================================

    [RelayCommand]
    private async Task CastVote()
    {
        if (IsCastingVote) return;
        
        try
        {
            // Check if a candidate is selected
            if (!HasVoted)
            {
                VoteStatus = "❌ Please select a candidate first";
                return;
            }
            
            IsCastingVote = true;
            VoteStatus = "🗳️ Casting vote...";
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Attempting to cast vote for: {SelectedCandidateName} - {SelectedParty}");
            
            // Cast the vote through the API
            var response = await _serverHandler.CastVoteAsync(SelectedCandidateId!.Value, SelectedCandidateName!, SelectedParty!);
            
            if (response.Success)
            {
                VoteStatus = "✅ Vote successfully cast!";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast successfully: {response.Message}");
                
                // Update device status so officials see the completed vote immediately
                _serverHandler.CurrentDeviceStatus = $"Status 5: Vote cast for {SelectedCandidateName}";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device status updated: {_serverHandler.CurrentDeviceStatus}");
                await _serverHandler.SendDeviceStatusAsync(_serverHandler.CurrentDeviceStatus);

                ShowVoteCastPopup = true;
                await Task.Delay(3000);
                ShowVoteCastPopup = false;
                ClearSelection();
                await _navigationService.NavigateToPersonalOrProxy();
            }
            else
            {
                VoteStatus = $"❌ Vote failed: {response.Message}";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote casting failed: {response.Message}");
            }
        }
        catch (Exception ex)
        {
            VoteStatus = $"❌ Error: {ex.Message}";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote casting exception: {ex.Message}");
        }
        finally
        {
            IsCastingVote = false;
        }
    }

    [RelayCommand]
    private void Back()
    {
        _navigationService.NavigateToMain();
    }

    [RelayCommand]
    private void Continue()
    {
        _navigationService.NavigateToConfirmation();
    }
}