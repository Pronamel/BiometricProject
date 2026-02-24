using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SecureVoteApp.ViewModels;

public partial class CandidateButtonViewModel : ViewModelBase
{
    // ==========================================
    // OBSERVABLE PROPERTIES
    // ==========================================

    [ObservableProperty]
    private string candidateName = "";

    [ObservableProperty]
    private string partyName = "";

    [ObservableProperty]
    private bool isSelected = false;

    [ObservableProperty]
    private int candidateId;




    // ==========================================
    // COMPUTED PROPERTIES
    // ==========================================

    public string DisplayText => $"{CandidateName}  {PartyName}";
    
    public string ButtonBackground => IsSelected ? "LightGreen" : "White";




    // ==========================================
    // CONSTRUCTORS
    // ==========================================

    public CandidateButtonViewModel()
    {
        // Subscribe to selection changes from other buttons
        BallotPaperViewModel.SelectionChanged += OnSelectionChanged;
    }

    public CandidateButtonViewModel(int id, string name, string party)
    {
        CandidateId = id;
        CandidateName = name;
        PartyName = party;
        
        // Subscribe to selection changes from other buttons
        BallotPaperViewModel.SelectionChanged += OnSelectionChanged;
    }




    // ==========================================
    // EVENT HANDLING METHODS
    // ==========================================
    
    private void OnSelectionChanged()
    {
        // Update this button's selection state
        IsSelected = BallotPaperViewModel.SelectedCandidateId == CandidateId;
        OnPropertyChanged(nameof(ButtonBackground)); // Notify UI that background color changed
    }




    // ==========================================
    // COMMANDS
    // ==========================================

    [RelayCommand]
    private void SelectCandidate()
    {
        if (IsSelected)
        {
            // Deselect this candidate
            BallotPaperViewModel.ClearSelection();
        }
        else
        {
            // Select this candidate (will automatically deselect others)
            BallotPaperViewModel.SetSelectedCandidate(CandidateId, CandidateName, PartyName);
        }
    }
}