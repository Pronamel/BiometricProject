using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SecureVoteApp.ViewModels;

public partial class BallotPaperViewModel : ViewModelBase
{
    // ==========================================
    // PRIVATE FIELDS
    // ==========================================
    
    private readonly INavigationService _navigationService;


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

    public BallotPaperViewModel()
    {
        _navigationService = Navigation.Instance;
        
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
    // NAVIGATION COMMANDS
    // ==========================================

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