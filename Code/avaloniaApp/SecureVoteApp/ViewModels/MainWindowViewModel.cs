using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SecureVoteApp.Views;
using Avalonia.Controls;

namespace SecureVoteApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private UserControl currentView;
    
    // Views
    private readonly NINEntryView _ninEntryView;
    private readonly PersonalOrProxyView _personalOrProxyView;
    private readonly ProxyVoteDetailsView _proxyVoteDetailsView;
    private readonly AuthenticateUserView _authenticateUserView;
    private readonly BallotPaperView _ballotPaperView;
    
    // Navigation service
    private readonly INavigationService _navigationService;
    


    private void OnNavigationRequested(UserControl view)
    {
        CurrentView = view;
    }


    public MainWindowViewModel()
    {
        // Get navigation service instance
        _navigationService = Navigation.Instance;
        
        // Subscribe to navigation events
        _navigationService.NavigationRequested += OnNavigationRequested;
        
        // Initialize views
        _ninEntryView = new NINEntryView { DataContext = new NINEntryViewModel() };
        _personalOrProxyView = new PersonalOrProxyView { DataContext = new PersonalOrProxyViewModel() };
        _proxyVoteDetailsView = new ProxyVoteDetailsView { DataContext = new ProxyVoteDetailsViewModel() };
        _authenticateUserView = new AuthenticateUserView { DataContext = new AuthenticateUserViewModel() };
        _ballotPaperView = new BallotPaperView { DataContext = new BallotPaperViewModel() };
        
        // TODO: Initialize navigation service with all view factories when ready
        // For now, simplified initialization
        ((NavigationService)_navigationService).Initialize(
            () => _ninEntryView,
            () => _personalOrProxyView,
            () => _proxyVoteDetailsView,
            () => _authenticateUserView,
            () => _ballotPaperView,
            () => throw new NotImplementedException("ConfirmationView not implemented yet"),
            () => throw new NotImplementedException("ResultsView not implemented yet"),
            () => throw new NotImplementedException("SettingsView not implemented yet")
        );
        
        // Set initial view
        CurrentView = _personalOrProxyView;
    }
    
    
    
}


