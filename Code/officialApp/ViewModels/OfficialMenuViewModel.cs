using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using officialApp.Services;

namespace officialApp.ViewModels;

public partial class OfficialMenuViewModel : ViewModelBase
{
    // ==========================================
    // OBSERVABLE PROPERTIES
    // ==========================================
    
    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string statusColor = "black";

    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;
    private readonly IServerHandler _serverHandler;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public OfficialMenuViewModel(IServerHandler serverHandler, INavigationService navigationService)
    {
        _navigationService = navigationService;
        _serverHandler = serverHandler;
    }

    // ==========================================
    // COMMANDS
    // ==========================================
    
    [RelayCommand]
    private void VotingStart()
    {
        _navigationService.NavigateToOfficialGenerateAccessCode();
    }
    
    [RelayCommand]
    private void Manager()
    {
        _navigationService.NavigateToOfficialVotingPollingManager();
    }
    
    [RelayCommand]
    private void Statistics()
    {
        _navigationService.NavigateToElectionStatistics();
    }
    
    [RelayCommand]
    private void Reports()
    {
        _navigationService.NavigateToOfficialAddVoter();
    }

    [RelayCommand]
    private void AssignProxy()
    {
        _navigationService.NavigateToOfficialAssignProxy();
    }

    [RelayCommand]
    private void DuplicateFingerprintScan()
    {
        _navigationService.NavigateToOfficialDuplicateFingerprintScan();
    }

    [RelayCommand]
    private async Task Logout()
    {
        try
        {
            StatusMessage = "Logging out...";
            StatusColor = "#3498db";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔄 Official logout initiated");

            // Call server logout endpoint
            bool success = await _serverHandler.LogoutAsync();

            if (success)
            {
                StatusMessage = "Logged out successfully";
                StatusColor = "#27ae60";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Logout successful");

                // Wait a moment for user to see success message, then navigate
                await Task.Delay(500);
                _navigationService.NavigateToOfficialLogin();
            }
            else
            {
                StatusMessage = "Logout partially completed - session cleared locally";
                StatusColor = "#e67e22";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  Server logout failed but local session cleared");

                // Still navigate after delay even if server logout failed
                await Task.Delay(1000);
                _navigationService.NavigateToOfficialLogin();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            StatusColor = "#e74c3c";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Logout error: {ex.Message}");
        }
    }
}