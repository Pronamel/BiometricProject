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
    private readonly IServerHandler _serverHandler;
    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public PersonalOrProxyViewModel(INavigationService navigationService, IServerHandler serverHandler)
    {
        _navigationService = navigationService;
        _serverHandler = serverHandler;
    }

    // ==========================================
    // COMMANDS
    // ==========================================
    
    [RelayCommand]
    private void OpenNINEntry()
    {
        _serverHandler.ClearProxyVotingSession();
        // Navigate to NIN entry screen (fire and forget)
        _ = _navigationService.NavigateToNINEntry();
    }
    
    [RelayCommand]
    private void OpenProxyVote()
    {
        _serverHandler.ClearProxyVotingSession();
        // Navigate to proxy vote details screen
        _navigationService.NavigateToProxyVoteDetails();
    }
}


