using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using SecureVoteApp.Models;

namespace SecureVoteApp.ViewModels;

// ==========================================
// NAVIGATION SERVICE INTERFACE
// ==========================================

// Interface defining navigation methods
public interface INavigationService
{
    Task NavigateToVoterLogin();
    Task NavigateToMain();
    Task NavigateToNINEntry();
    Task NavigateToPersonalOrProxy();
    Task NavigateToProxyVoteDetails();
    Task NavigateToAuthenticateUser();
    Task NavigateToAuthenticateUser(VoterAuthLookupResponse lookup);
    Task NavigateToBallot();
    Task NavigateToConfirmation();
    Task NavigateToResults();
    Task NavigateToSettings();
    Task NavigateToView(UserControl view);
    
    // Property to pass data between view models
    VoterAuthLookupResponse? PendingVoterLookup { get; set; }
    
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
    // PROPERTIES
    // ==========================================

    public VoterAuthLookupResponse? PendingVoterLookup { get; set; }




    // ==========================================
    // PRIVATE FIELDS - VIEW STORAGE
    // ==========================================
    
    // Store references to views for reuse
    private UserControl? _voterLoginView;
    private UserControl? _ninEntryView;
    private UserControl? _personalOrProxyView;
    private UserControl? _proxyVoteDetailsView;
    private UserControl? _authenticateUserView;
    private UserControl? _ballotView;
    private UserControl? _confirmationView;
    private UserControl? _resultsView;
    private UserControl? _settingsView;




    // ==========================================

    // ==========================================
    // PRIVATE FIELDS - VIEWMODEL STORAGE
    // ==========================================
    




    // ==========================================
    // PRIVATE FIELDS - VIEW FACTORY FUNCTIONS
    // ==========================================
    
    // Reference to MainWindowViewModel to access views
    private Func<UserControl>? _getVoterLoginView;
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
    
    // Initialize with view factory methods and ViewModels
    public void Initialize(
        Func<UserControl> getVoterLoginView,
        Func<UserControl> getNINEntryView, 
        Func<UserControl> getPersonalOrProxyView,
        Func<UserControl> getProxyVoteDetailsView,
        Func<UserControl> getAuthenticateUserView,
        Func<UserControl> getBallotView,
        Func<UserControl> getConfirmationView,
        Func<UserControl> getResultsView,
        Func<UserControl> getSettingsView)
    {
        _getVoterLoginView = getVoterLoginView;
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
    
    public Task NavigateToVoterLogin()
    {
        if (_voterLoginView == null && _getVoterLoginView != null)
            _voterLoginView = _getVoterLoginView();
            
        if (_voterLoginView != null)
            NavigationRequested?.Invoke(_voterLoginView);

        return Task.CompletedTask;
    }
    
    public Task NavigateToMain()
    {
        return NavigateToPersonalOrProxy();
    }
    
    public Task NavigateToNINEntry()
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔄 NavigationService: NavigateToNINEntry called");
        if (_ninEntryView == null && _getNINEntryView != null)
            _ninEntryView = _getNINEntryView();
            
        if (_ninEntryView != null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔄 NavigationService: Calling OnNavigatedTo for NINEntry");
            NavigationRequested?.Invoke(_ninEntryView);
        }

        return Task.CompletedTask;
    }
    
    public Task NavigateToPersonalOrProxy()
    {
        if (_personalOrProxyView == null && _getPersonalOrProxyView != null)
            _personalOrProxyView = _getPersonalOrProxyView();
            
        if (_personalOrProxyView != null)
            NavigationRequested?.Invoke(_personalOrProxyView);

        return Task.CompletedTask;
    }
    
    public Task NavigateToProxyVoteDetails()
    {
        if (_proxyVoteDetailsView == null && _getProxyVoteDetailsView != null)
            _proxyVoteDetailsView = _getProxyVoteDetailsView();
            
        if (_proxyVoteDetailsView != null)
            NavigationRequested?.Invoke(_proxyVoteDetailsView);

        return Task.CompletedTask;
    }

    public Task NavigateToAuthenticateUser()
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔄 NavigationService: NavigateToAuthenticateUser() called");
        if (_authenticateUserView == null && _getAuthenticateUserView != null)
            _authenticateUserView = _getAuthenticateUserView();

        if (_authenticateUserView != null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔄 NavigationService: Calling OnNavigatedTo for Authenticate");
            NavigationRequested?.Invoke(_authenticateUserView);
        }

        return Task.CompletedTask;
    }

    public Task NavigateToAuthenticateUser(VoterAuthLookupResponse lookup)
    {
        // Store the lookup data for the ViewModel to retrieve
        PendingVoterLookup = lookup;
        
        if (_authenticateUserView == null && _getAuthenticateUserView != null)
            _authenticateUserView = _getAuthenticateUserView();

        if (_authenticateUserView != null)
            NavigationRequested?.Invoke(_authenticateUserView);

        return Task.CompletedTask;
    }
    
    public Task NavigateToBallot()
    {
        if (_ballotView == null && _getBallotView != null)
            _ballotView = _getBallotView();
            
        if (_ballotView != null)
            NavigationRequested?.Invoke(_ballotView);

        return Task.CompletedTask;
    }
    
    public Task NavigateToConfirmation()
    {
        if (_confirmationView == null && _getConfirmationView != null)
            _confirmationView = _getConfirmationView();
            
        if (_confirmationView != null)
            NavigationRequested?.Invoke(_confirmationView);

        return Task.CompletedTask;
    }
    
    public Task NavigateToResults()
    {
        if (_resultsView == null && _getResultsView != null)
            _resultsView = _getResultsView();
            
        if (_resultsView != null)
            NavigationRequested?.Invoke(_resultsView);

        return Task.CompletedTask;
    }
    
    public Task NavigateToSettings()
    {
        if (_settingsView == null && _getSettingsView != null)
            _settingsView = _getSettingsView();
            
        if (_settingsView != null)
            NavigationRequested?.Invoke(_settingsView);

        return Task.CompletedTask;
    }
    
    public Task NavigateToView(UserControl view)
    {
        NavigationRequested?.Invoke(view);

        return Task.CompletedTask;
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