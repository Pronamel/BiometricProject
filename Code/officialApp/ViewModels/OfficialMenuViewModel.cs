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