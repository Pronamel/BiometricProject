using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using System.Net.Http;
using System.Reflection;
using SecureVoteApp.Views.OfficialUI;
using Avalonia.Controls;

namespace SecureVoteApp.ViewModels;

public partial class OfficialMenuViewModel : ViewModelBase
{
    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public OfficialMenuViewModel()
    {
        _navigationService = Navigation.Instance;
    }

    // ==========================================
    // COMMANDS
    // ==========================================
    
    [RelayCommand]
    private void VotingStart()
    {
        // TODO: Navigate to manage screen
        _navigationService.NavigateToPersonalOrProxy();
    }
    
    [RelayCommand]
    private void Monitor()
    {
        // TODO: Navigate to monitor screen
        // _navigationService.NavigateToMonitor();
    }
    
    [RelayCommand]
    private void Reports()
    {
        // TODO: Navigate to reports screen
        // _navigationService.NavigateToReports();
    }
}