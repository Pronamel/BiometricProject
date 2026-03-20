using System;
using CommunityToolkit.Mvvm.ComponentModel;
using officialApp.Views;
using Avalonia.Controls;

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
    
    // Navigation service
    private readonly INavigationService _navigationService;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public MainWindowViewModel(
        OfficialLoginViewModel officialLoginViewModel,
        OfficialAuthenticateViewModel officialAuthenticateViewModel,
        OfficialMenuViewModel officialMenuViewModel,
        OfficialGenerateAccessCodeViewModel officialGenerateAccessCodeViewModel,
        OfficialVotingPollingManagerViewModel officialVotingPollingManagerViewModel,
        INavigationService navigationService)
    {
        // Get navigation service instance
        _navigationService = navigationService;
        
        // Subscribe to navigation events
        _navigationService.NavigationRequested += OnNavigationRequested;
        
        // Initialize views with their DataContexts
        _officialLoginView = new OfficialLoginView { DataContext = officialLoginViewModel };
        _officialAuthenticateView = new OfficialAuthenticateView { DataContext = officialAuthenticateViewModel };
        _officialMenuView = new OfficialMenuView { DataContext = officialMenuViewModel };
        _officialGenerateAccessCodeView = new OfficialGenerateAccessCodeView { DataContext = officialGenerateAccessCodeViewModel };
        _officialVotingPollingManagerView = new OfficialVotingPollingManagerView { DataContext = officialVotingPollingManagerViewModel };
        
        // Initialize navigation service with all view factories
        ((NavigationService)_navigationService).Initialize(
            () => _officialLoginView,
            () => _officialAuthenticateView,
            () => _officialMenuView,
            () => _officialGenerateAccessCodeView,
            () => _officialVotingPollingManagerView
        );
        
        // Set initial view to Official Login
        CurrentView = _officialLoginView;
    }

    // ==========================================
    // EVENT HANDLERS
    // ==========================================

    private void OnNavigationRequested(UserControl view)
    {
        CurrentView = view;
    }
}