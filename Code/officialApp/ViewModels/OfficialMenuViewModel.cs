using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using System.Net.Http;
using System.Reflection;
using Avalonia.Controls;
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
    private readonly IApiService _apiService;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public OfficialMenuViewModel(IApiService apiService, INavigationService navigationService)
    {
        _navigationService = navigationService;
        _apiService = apiService;
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
    private void Reports()
    {
        _navigationService.NavigateToOfficialAddVoter();
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
            bool success = await _apiService.LogoutAsync();

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

    [RelayCommand]
    private async Task FetchVotersTest()
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ===== FETCHING VOTERS FROM DATABASE =====");
        
        var voters = await _apiService.GetAllVotersAsync();
        
        if (voters != null && voters.Count > 0)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Successfully retrieved {voters.Count} voters:");
            
            foreach (var voter in voters)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter: {JsonSerializer.Serialize(voter)}");
            }
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No voters found or API call failed");
        }
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ===== END VOTER FETCH =====\n");
    }
}