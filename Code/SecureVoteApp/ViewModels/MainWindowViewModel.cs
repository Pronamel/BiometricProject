using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SecureVoteApp.Views.VoterUI;
using Avalonia.Controls;
using Avalonia.Threading;
using SecureVoteApp.Services;
using System.Threading.Tasks;

namespace SecureVoteApp.ViewModels;

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
    private readonly VoterLoginView _voterLoginView;
    private readonly NINEntryView _ninEntryView;
    private readonly PersonalOrProxyView _personalOrProxyView;
    private readonly ProxyVoteDetailsView _proxyVoteDetailsView;
    private readonly AuthenticateUserView _authenticateUserView;
    private readonly BallotPaperView _ballotPaperView;
    
    // Navigation service
    private readonly INavigationService _navigationService;
    private readonly IServerHandler _serverHandler;




    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public MainWindowViewModel(
        VoterLoginViewModel voterLoginViewModel,
        NINEntryViewModel ninEntryViewModel,
        PersonalOrProxyViewModel personalOrProxyViewModel,
        ProxyVoteDetailsViewModel proxyVoteDetailsViewModel,
        AuthenticateUserViewModel authenticateUserViewModel,
        BallotPaperViewModel ballotPaperViewModel,
        INavigationService navigationService,
        IServerHandler serverHandler)
    {
        _navigationService = navigationService;
        _serverHandler = serverHandler;
        
        // Subscribe to navigation events
        _navigationService.NavigationRequested += OnNavigationRequested;
        
        // Initialize views with injected ViewModels
        _voterLoginView = new VoterLoginView { DataContext = voterLoginViewModel };
        _ninEntryView = new NINEntryView { DataContext = ninEntryViewModel };
        _personalOrProxyView = new PersonalOrProxyView { DataContext = personalOrProxyViewModel };
        _proxyVoteDetailsView = new ProxyVoteDetailsView { DataContext = proxyVoteDetailsViewModel };
        _authenticateUserView = new AuthenticateUserView { DataContext = authenticateUserViewModel };
        _ballotPaperView = new BallotPaperView { DataContext = ballotPaperViewModel };
        
        // Initialize navigation service with all view factories and ViewModels
        ((NavigationService)_navigationService).Initialize(
            () => _voterLoginView,
            () => _ninEntryView,
            () => _personalOrProxyView,
            () => _proxyVoteDetailsView,
            () => _authenticateUserView,
            () => _ballotPaperView,
            () => throw new NotImplementedException("ConfirmationView not implemented yet"),
            () => throw new NotImplementedException("ResultsView not implemented yet"),
            () => throw new NotImplementedException("SettingsView not implemented yet")
        );
        
        // Set initial view to Voter Login
        CurrentView = _voterLoginView;

        // If the realtime channel dies (e.g., server crash), force UI back to login.
        _serverHandler.ConnectionStatusChanged += OnServerConnectionStatusChanged;
    }




    // ==========================================
    // EVENT HANDLERS
    // ==========================================

    private void OnNavigationRequested(UserControl view)
    {
        CurrentView = view;
    }

    private void OnServerConnectionStatusChanged(bool isConnected)
    {
        if (isConnected)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (CurrentView == _voterLoginView)
            {
                return;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Server disconnected. Returning to voter login.");
            _navigationService.NavigateToVoterLogin();

            // Run logout off the UI thread so a dead server cannot freeze the window.
            _ = Task.Run(() => _serverHandler.Logout());
        });
    }
}


