using System;
using CommunityToolkit.Mvvm.Input;
using SecureVoteApp.Services;

namespace SecureVoteApp.ViewModels;

public partial class PersonalOrProxyViewModel : ViewModelBase
{
    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public PersonalOrProxyViewModel()
    {
        _navigationService = Navigation.Instance;
    }

    // ==========================================
    // COMMANDS
    // ==========================================
    
    [RelayCommand]
    private void OpenNINEntry()
    {
        // Navigate to NIN entry screen
        _navigationService.NavigateToNINEntry();
    }
    
    [RelayCommand]
    private void OpenProxyVote()
    {
        // Navigate to proxy vote details screen
        _navigationService.NavigateToProxyVoteDetails();
    }
}


