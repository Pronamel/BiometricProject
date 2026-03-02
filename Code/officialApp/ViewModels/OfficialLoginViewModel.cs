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

    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public OfficialLoginViewModel()
    {
        _navigationService = Navigation.Instance;
        _apiService = ApiService.Instance;
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
            ServerStatus = "Testing connection...";
            
            var isConnected = await _apiService.TestConnectionAsync();
            
            if (isConnected)
            {
                ServerStatus = "✅ Server connection: SUCCESS";
                
                // Also test getting weather data as a full API test
                var weatherData = await _apiService.GetWeatherDataAsync();
                if (weatherData != null && weatherData.Count > 0)
                {
                    ServerStatus = $"✅ Server connected - Received {weatherData.Count} records";
                }
            }
            else
            {
                ServerStatus = "❌ Server connection: FAILED";
            }
        }
        catch (Exception ex)
        {
            ServerStatus = $"❌ Server error: {ex.Message}";
        }
    }
}