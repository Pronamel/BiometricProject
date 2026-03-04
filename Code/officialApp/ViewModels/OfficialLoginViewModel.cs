using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using System.Net.Http;
using System.Reflection;
using Avalonia.Controls;
using officialApp.Services;

namespace officialApp.ViewModels;

public partial class OfficialLoginViewModel : ViewModelBase
{
    // ==========================================
    // OBSERVABLE PROPERTIES
    // ==========================================

    [ObservableProperty]
    private string username = "";

    [ObservableProperty]  
    private string password = "";

    [ObservableProperty]
    private string serverStatus = "Server status: Not tested";

    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;
    private readonly ApiService _apiService;
    private readonly ApiTestService _apiTestService;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public OfficialLoginViewModel()
    {
        _navigationService = Navigation.Instance;
        _apiService = ApiService.Instance;
        _apiTestService = new ApiTestService(_apiService);
    }

    // ==========================================
    // COMMANDS
    // ==========================================

    [RelayCommand]
    private async Task Authenticate()
    {
        try
        {
            // TODO: Implement authentication logic
            // For now, just a placeholder
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                // Could show error message
                return;
            }

            // Simulate authentication delay
            await Task.Delay(1000);

            // TODO: Add actual authentication logic here
            // For now, accept any non-empty username/password
            if (Username.Length > 0 && Password.Length > 0)
            {
                // Authentication successful - proceed to biometric authentication
                Console.WriteLine($"Official {Username} authenticated successfully");
                
                // Navigate to official biometric authentication
                _navigationService.NavigateToOfficialAuthenticate();
            }
            else
            {
                // Authentication failed
                Console.WriteLine("Authentication failed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Authentication error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void AuthenticateOfficial()
    {
        _navigationService.NavigateToOfficialAuthenticate();
    }

    [RelayCommand]
    private async Task TestServer()
    {
        try
        {
            ServerStatus = "Fetching server data...";
            
            var dataResults = new List<string>();
            
            // Get weather data
            var weatherData = await _apiService.GetWeatherDataAsync();
            if (weatherData != null && weatherData.Count > 0)
            {
                var weatherInfo = string.Join(", ", weatherData.Select(w => $"Temp: {w.TemperatureC}°C Summary: {w.Summary}"));
                dataResults.Add($"Weather: [{weatherInfo}]");
            }
            else
            {
                dataResults.Add("Weather: No data");
            }
            
            // Get candidates data
            var candidates = await _apiService.GetCandidatesAsync();
            if (candidates != null && candidates.Count > 0)
            {
                var candidatesInfo = string.Join(", ", candidates);
                dataResults.Add($"Candidates: [{candidatesInfo}]");
            }
            else
            {
                dataResults.Add("Candidates: No data");
            }
            
            // Combine all data with spaces
            ServerStatus = string.Join(" | ", dataResults);
        }
        catch (Exception ex)
        {
            ServerStatus = $"❌ Error fetching data: {ex.Message}";
        }
    }
}