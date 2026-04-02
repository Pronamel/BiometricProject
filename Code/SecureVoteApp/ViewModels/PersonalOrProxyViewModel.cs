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
    
    public PersonalOrProxyViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    // ==========================================
    // COMMANDS
    // ==========================================
    
    [RelayCommand]
    private void OpenNINEntry()
    {
        // Navigate to NIN entry screen (fire and forget)
        _ = _navigationService.NavigateToNINEntry();
    }
    
    [RelayCommand]
    private void OpenProxyVote()
    {
        // Navigate to proxy vote details screen
        _navigationService.NavigateToProxyVoteDetails();
    }
}


