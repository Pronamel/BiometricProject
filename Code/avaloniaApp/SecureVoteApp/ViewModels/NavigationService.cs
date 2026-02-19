using System;
using Avalonia.Controls;

namespace SecureVoteApp.ViewModels;

// Interface defining navigation methods
public interface INavigationService
{
    void NavigateToMain();
    void NavigateToNINEntry();
    void NavigateToView(UserControl view);
    
    // Events to notify when navigation happens
    event Action<UserControl>? NavigationRequested;
}

// Implementation of the navigation service
public class NavigationService : INavigationService
{
    // Event that the MainWindowViewModel will subscribe to
    public event Action<UserControl>? NavigationRequested;
    
    // Store references to views for reuse
    private UserControl? _mainView;
    private UserControl? _ninEntryView;
    
    // Reference to MainWindowViewModel to access views
    private Func<UserControl>? _getMainView;
    private Func<UserControl>? _getNINEntryView;
    
    // Initialize with view factory methods
    public void Initialize(Func<UserControl> getMainView, Func<UserControl> getNINEntryView)
    {
        _getMainView = getMainView;
        _getNINEntryView = getNINEntryView;
    }
    
    public void NavigateToMain()
    {
        if (_mainView == null && _getMainView != null)
            _mainView = _getMainView();
            
        if (_mainView != null)
            NavigationRequested?.Invoke(_mainView);
    }
    
    public void NavigateToNINEntry()
    {
        if (_ninEntryView == null && _getNINEntryView != null)
            _ninEntryView = _getNINEntryView();
            
        if (_ninEntryView != null)
            NavigationRequested?.Invoke(_ninEntryView);
    }
    
    public void NavigateToView(UserControl view)
    {
        NavigationRequested?.Invoke(view);
    }
}

// Static access point for the navigation service
public static class Navigation
{
    public static INavigationService Instance { get; } = new NavigationService();
}