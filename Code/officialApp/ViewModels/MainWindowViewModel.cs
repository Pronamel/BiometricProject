using System;
using CommunityToolkit.Mvvm.ComponentModel;
using officialApp.Views;
using Avalonia.Controls;
using Avalonia.Threading;
using officialApp.Services;

namespace officialApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // ==========================================
    // OBSERVABLE PROPERTIES
    // ==========================================

    [ObservableProperty]
    private UserControl currentView;

    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================
    
    // Views
    private readonly OfficialLoginView _officialLoginView;
    private readonly OfficialAuthenticateView _officialAuthenticateView;
    private readonly OfficialMenuView _officialMenuView;
    private readonly OfficialGenerateAccessCodeView _officialGenerateAccessCodeView;
    private readonly OfficialVotingPollingManagerView _officialVotingPollingManagerView;
    private readonly OfficialAddVoterView _officialAddVoterView;
    
    // Navigation service
    private readonly INavigationService _navigationService;
    private readonly IRealtimeService _realtimeService;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public MainWindowViewModel(
        OfficialLoginViewModel officialLoginViewModel,
        OfficialAuthenticateViewModel officialAuthenticateViewModel,
        OfficialMenuViewModel officialMenuViewModel,
        OfficialGenerateAccessCodeViewModel officialGenerateAccessCodeViewModel,
        OfficialVotingPollingManagerViewModel officialVotingPollingManagerViewModel,
        OfficialAddVoterViewModel officialAddVoterViewModel,
        INavigationService navigationService,
        IRealtimeService realtimeService)
    {
        // Get navigation service instance
        _navigationService = navigationService;
        _realtimeService = realtimeService;
        
        // Subscribe to navigation events
        _navigationService.NavigationRequested += OnNavigationRequested;
        
        // Initialize views with their DataContexts
        _officialLoginView = new OfficialLoginView { DataContext = officialLoginViewModel };
        _officialAuthenticateView = new OfficialAuthenticateView { DataContext = officialAuthenticateViewModel };
        _officialMenuView = new OfficialMenuView { DataContext = officialMenuViewModel };
        _officialGenerateAccessCodeView = new OfficialGenerateAccessCodeView { DataContext = officialGenerateAccessCodeViewModel };
        _officialVotingPollingManagerView = new OfficialVotingPollingManagerView { DataContext = officialVotingPollingManagerViewModel };
        _officialAddVoterView = new OfficialAddVoterView { DataContext = officialAddVoterViewModel };
        
        // Initialize navigation service with all view factories
        ((NavigationService)_navigationService).Initialize(
            () => _officialLoginView,
            () => _officialAuthenticateView,
            () => _officialMenuView,
            () => _officialGenerateAccessCodeView,
            () => _officialVotingPollingManagerView,
            () => _officialAddVoterView
        );
        
        // Set initial view to Official Authenticate
        CurrentView = _officialLoginView;

        _realtimeService.ConnectionStateChanged += OnRealtimeConnectionStateChanged;
    }

    // ==========================================
    // EVENT HANDLERS
    // ==========================================

    private void OnNavigationRequested(UserControl view)
    {
        CurrentView = view;
    }

    private void OnRealtimeConnectionStateChanged(string state)
    {
        if (!state.StartsWith("Disconnected", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (CurrentView == _officialLoginView)
            {
                return;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Realtime disconnected. Returning to official login.");
            _navigationService.NavigateToOfficialLogin();
        });
    }
}