using System;
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
    private readonly IApiService _apiService;
    
    [ObservableProperty]
    private bool isCastingVote = false;
    
    [ObservableProperty]
    private string voteStatus = "";


    // ==========================================
    // STATIC PROPERTIES - VOTE TRACKING
    // ==========================================

    // Track which candidate is selected (null = none selected)
    public static int? SelectedCandidateId { get; set; } = null;
    
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
    public static void SetSelectedCandidate(int candidateId, string candidateName, string partyName)
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

    public BallotPaperViewModel(INavigationService navigationService, IApiService apiService)
    {
        _navigationService = navigationService;
        _apiService = apiService;
        
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
            var response = await _apiService.CastVoteAsync(SelectedCandidateName!, SelectedParty!);
            
            if (response.Success)
            {
                VoteStatus = "✅ Vote successfully cast!";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Vote cast successfully: {response.Message}");
                
                // Update device status for heartbeat loop to send continuously
                _apiService.CurrentDeviceStatus = $"Status 5: Vote cast for {SelectedCandidateName}";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Device status updated: {_apiService.CurrentDeviceStatus}");
                await _apiService.SendDeviceStatusAsync(_apiService.CurrentDeviceStatus);
                
                // Keep showing success message instead of navigating
                await Task.Delay(3000); // Show success message for longer
                VoteStatus = "Vote completed - thank you for voting!";
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