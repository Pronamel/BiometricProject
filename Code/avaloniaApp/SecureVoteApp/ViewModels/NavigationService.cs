using System;
using Avalonia.Controls;

namespace SecureVoteApp.ViewModels;

// ==========================================
// NAVIGATION SERVICE INTERFACE
// ==========================================

// Interface defining navigation methods
public interface INavigationService
{
    void NavigateToMain();
    void NavigateToNINEntry();
    void NavigateToPersonalOrProxy();
    void NavigateToProxyVoteDetails();
    void NavigateToAuthenticateUser();
    void NavigateToBallot();
    void NavigateToConfirmation();
    void NavigateToResults();
    void NavigateToSettings();
    void NavigateToView(UserControl view);
    
    // Events to notify when navigation happens
    event Action<UserControl>? NavigationRequested;
}




// ==========================================
// NAVIGATION SERVICE IMPLEMENTATION
// ==========================================

// Implementation of the navigation service
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
    private UserControl? _ninEntryView;
    private UserControl? _personalOrProxyView;
    private UserControl? _proxyVoteDetailsView;
    private UserControl? _authenticateUserView;
    private UserControl? _ballotView;
    private UserControl? _confirmationView;
    private UserControl? _resultsView;
    private UserControl? _settingsView;




    // ==========================================
    // PRIVATE FIELDS - VIEW FACTORY FUNCTIONS
    // ==========================================
    
    // Reference to MainWindowViewModel to access views
    private Func<UserControl>? _getNINEntryView;
    private Func<UserControl>? _getPersonalOrProxyView;
    private Func<UserControl>? _getProxyVoteDetailsView;
    private Func<UserControl>? _getAuthenticateUserView;
    private Func<UserControl>? _getBallotView;
    private Func<UserControl>? _getConfirmationView;
    private Func<UserControl>? _getResultsView;
    private Func<UserControl>? _getSettingsView;




    // ==========================================
    // INITIALIZATION METHODS
    // ==========================================
    
    // Initialize with view factory methods
    public void Initialize(
        Func<UserControl> getNINEntryView, 
        Func<UserControl> getPersonalOrProxyView,
        Func<UserControl> getProxyVoteDetailsView,
        Func<UserControl> getAuthenticateUserView,
        Func<UserControl> getBallotView,
        Func<UserControl> getConfirmationView,
        Func<UserControl> getResultsView,
        Func<UserControl> getSettingsView)
    {
        _getNINEntryView = getNINEntryView;
        _getPersonalOrProxyView = getPersonalOrProxyView;
        _getProxyVoteDetailsView = getProxyVoteDetailsView;
        _getAuthenticateUserView = getAuthenticateUserView;
        _getBallotView = getBallotView;
        _getConfirmationView = getConfirmationView;
        _getResultsView = getResultsView;
        _getSettingsView = getSettingsView;
    }




    // ==========================================
    // NAVIGATION METHODS
    // ==========================================
    
    public void NavigateToMain()
    {
        NavigateToPersonalOrProxy();
    }
    
    public void NavigateToNINEntry()
    {
        if (_ninEntryView == null && _getNINEntryView != null)
            _ninEntryView = _getNINEntryView();
            
        if (_ninEntryView != null)
            NavigationRequested?.Invoke(_ninEntryView);
    }
    
    public void NavigateToPersonalOrProxy()
    {
        if (_personalOrProxyView == null && _getPersonalOrProxyView != null)
            _personalOrProxyView = _getPersonalOrProxyView();
            
        if (_personalOrProxyView != null)
            NavigationRequested?.Invoke(_personalOrProxyView);
    }
    
    public void NavigateToProxyVoteDetails()
    {
        if (_proxyVoteDetailsView == null && _getProxyVoteDetailsView != null)
            _proxyVoteDetailsView = _getProxyVoteDetailsView();
            
        if (_proxyVoteDetailsView != null)
            NavigationRequested?.Invoke(_proxyVoteDetailsView);
    }

    public void NavigateToAuthenticateUser()
    {
        if (_authenticateUserView == null && _getAuthenticateUserView != null)
            _authenticateUserView = _getAuthenticateUserView();

        if (_authenticateUserView != null)
            NavigationRequested?.Invoke(_authenticateUserView);
    }
    
    public void NavigateToBallot()
    {
        if (_ballotView == null && _getBallotView != null)
            _ballotView = _getBallotView();
            
        if (_ballotView != null)
            NavigationRequested?.Invoke(_ballotView);
    }
    
    public void NavigateToConfirmation()
    {
        if (_confirmationView == null && _getConfirmationView != null)
            _confirmationView = _getConfirmationView();
            
        if (_confirmationView != null)
            NavigationRequested?.Invoke(_confirmationView);
    }
    
    public void NavigateToResults()
    {
        if (_resultsView == null && _getResultsView != null)
            _resultsView = _getResultsView();
            
        if (_resultsView != null)
            NavigationRequested?.Invoke(_resultsView);
    }
    
    public void NavigateToSettings()
    {
        if (_settingsView == null && _getSettingsView != null)
            _settingsView = _getSettingsView();
            
        if (_settingsView != null)
            NavigationRequested?.Invoke(_settingsView);
    }
    
    public void NavigateToView(UserControl view)
    {
        NavigationRequested?.Invoke(view);
    }
}




// ==========================================
// STATIC NAVIGATION ACCESS
// ==========================================

// Static access point for the navigation service
public static class Navigation
{
    public static INavigationService Instance { get; } = new NavigationService();
}