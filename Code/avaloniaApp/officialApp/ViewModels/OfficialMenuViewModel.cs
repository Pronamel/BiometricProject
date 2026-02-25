using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using System.Net.Http;
using System.Reflection;
using Avalonia.Controls;

namespace officialApp.ViewModels;

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
        // TODO: Navigate to manage screen or connect to voter app
        // For now, stay on current view
        // _navigationService.NavigateToPersonalOrProxy();
    }
    
    [RelayCommand]
    private void Manager()
    {
        _navigationService.NavigateToOfficialVotingPollingManager();
    }
    
    [RelayCommand]
    private void Reports()
    {
        // TODO: Implement reports functionality
        // _navigationService.NavigateToReports();
    }
}