using System;
using Avalonia.Controls;

namespace officialApp.ViewModels;

// ==========================================
// NAVIGATION SERVICE INTERFACE
// ==========================================

// Interface defining navigation methods for official app
public interface INavigationService
{
    void NavigateToOfficialLogin();
    void NavigateToOfficialAuthenticate(string username = "", string password = "");
    void NavigateToOfficialMenu();
    void NavigateToOfficialGenerateAccessCode();
    void NavigateToOfficialVotingPollingManager();
    void NavigateToOfficialAddVoter();
    void NavigateToView(UserControl view);
    
    // Events to notify when navigation happens
    event Action<UserControl>? NavigationRequested;
}

// ==========================================
// NAVIGATION SERVICE IMPLEMENTATION
// ==========================================

// Implementation of the navigation service for official app
public class NavigationService : INavigationService
{
    // ==========================================
    // EVENTS
    // ==========================================

    // Event that the MainWindowViewModel will subscribe to
    public event Action<UserControl>? NavigationRequested;

    // ==========================================
    // PRIVATE FIELDS - VIEW STORAGE
    // ==========================================
    
    // Store references to views for reuse
    private UserControl? _officialLoginView;
    private UserControl? _officialAuthenticateView;
    private UserControl? _officialMenuView;
    private UserControl? _officialGenerateAccessCodeView;
    private UserControl? _officialVotingPollingManagerView;
    private UserControl? _officialAddVoterView;

    // ==========================================
    // PRIVATE FIELDS - VIEW FACTORY FUNCTIONS
    // ==========================================
    
    // Reference to MainWindowViewModel to access views
    private Func<UserControl>? _getOfficialLoginView;
    private Func<UserControl>? _getOfficialAuthenticateView;
    private Func<UserControl>? _getOfficialMenuView;
    private Func<UserControl>? _getOfficialGenerateAccessCodeView;
    private Func<UserControl>? _getOfficialVotingPollingManagerView;
    private Func<UserControl>? _getOfficialAddVoterView;

    // ==========================================
    // INITIALIZATION METHODS
    // ==========================================
    
    // Initialize with view factory methods
    public void Initialize(
        Func<UserControl> getOfficialLoginView,
        Func<UserControl> getOfficialAuthenticateView,
        Func<UserControl> getOfficialMenuView,
        Func<UserControl> getOfficialGenerateAccessCodeView,
        Func<UserControl> getOfficialVotingPollingManagerView,
        Func<UserControl> getOfficialAddVoterView)
    {
        _getOfficialLoginView = getOfficialLoginView;
        _getOfficialAuthenticateView = getOfficialAuthenticateView;
        _getOfficialMenuView = getOfficialMenuView;
        _getOfficialGenerateAccessCodeView = getOfficialGenerateAccessCodeView;
        _getOfficialVotingPollingManagerView = getOfficialVotingPollingManagerView;
        _getOfficialAddVoterView = getOfficialAddVoterView;
    }

    // ==========================================
    // NAVIGATION METHODS
    // ==========================================
    
    public void NavigateToOfficialLogin()
    {
        if (_officialLoginView == null && _getOfficialLoginView != null)
            _officialLoginView = _getOfficialLoginView();
            
        if (_officialLoginView != null)
            NavigationRequested?.Invoke(_officialLoginView);
    }
    
    public void NavigateToOfficialAuthenticate(string username = "", string password = "")
    {
        if (_officialAuthenticateView == null && _getOfficialAuthenticateView != null)
            _officialAuthenticateView = _getOfficialAuthenticateView();

        // Pass credentials to the viewmodel if provided
        if (_officialAuthenticateView != null && _officialAuthenticateView.DataContext is OfficialAuthenticateViewModel vm)
        {
            vm.Username = username;
            vm.Password = password;
            Console.WriteLine($"[NavigationService] Set credentials for OfficialAuthenticateViewModel: {username}");
        }

        if (_officialAuthenticateView != null)
            NavigationRequested?.Invoke(_officialAuthenticateView);
    }
    
    public void NavigateToOfficialMenu()
    {
        if (_officialMenuView == null && _getOfficialMenuView != null)
            _officialMenuView = _getOfficialMenuView();

        if (_officialMenuView != null)
            NavigationRequested?.Invoke(_officialMenuView);
    }
    
    public void NavigateToOfficialGenerateAccessCode()
    {
        if (_officialGenerateAccessCodeView == null && _getOfficialGenerateAccessCodeView != null)
            _officialGenerateAccessCodeView = _getOfficialGenerateAccessCodeView();

        if (_officialGenerateAccessCodeView != null)
            NavigationRequested?.Invoke(_officialGenerateAccessCodeView);
    }
    
    public void NavigateToOfficialVotingPollingManager()
    {
        if (_officialVotingPollingManagerView == null && _getOfficialVotingPollingManagerView != null)
            _officialVotingPollingManagerView = _getOfficialVotingPollingManagerView();

        if (_officialVotingPollingManagerView != null)
            NavigationRequested?.Invoke(_officialVotingPollingManagerView);
    }

    public void NavigateToOfficialAddVoter()
    {
        if (_officialAddVoterView == null && _getOfficialAddVoterView != null)
            _officialAddVoterView = _getOfficialAddVoterView();

        if (_officialAddVoterView != null)
            NavigationRequested?.Invoke(_officialAddVoterView);
    }

    public void NavigateToView(UserControl view)
    {
        NavigationRequested?.Invoke(view);
    }
}

// ==========================================
// NAVIGATION SINGLETON PATTERN
// ==========================================

// Singleton pattern for global access to navigation service
public class Navigation
{
    private static NavigationService? _instance;
    public static NavigationService Instance => _instance ??= new NavigationService();
}