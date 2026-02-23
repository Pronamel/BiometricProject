using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SecureVoteApp.ViewModels;

public partial class BallotPaperViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;

    // Track which candidate is selected (null = none selected)
    public static int? SelectedCandidateId { get; set; } = null;
    
    // Event to notify all buttons when selection changes
    public static event Action? SelectionChanged;
    
    // Method to set selection and notify all buttons
    public static void SetSelectedCandidate(int candidateId)
    {
        SelectedCandidateId = candidateId;
        SelectionChanged?.Invoke(); // Notify all buttons to update
    }
    
    // Method to clear selection
    public static void ClearSelection()
    {
        SelectedCandidateId = null;
        SelectionChanged?.Invoke();
    }

    public BallotPaperViewModel()
    {
        _navigationService = Navigation.Instance;
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