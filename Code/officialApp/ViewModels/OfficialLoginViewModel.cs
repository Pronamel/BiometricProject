using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using officialApp.Services;
using officialApp.Models;

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

    // Partial method that's automatically called when Username changes
    partial void OnUsernameChanged(string value)
    {
        OnPropertyChanged(nameof(CanLogin));
    }

    // Partial method that's automatically called when Password changes
    partial void OnPasswordChanged(string value)
    {
        OnPropertyChanged(nameof(CanLogin));
    }

    [ObservableProperty]
    private string loginStatus = "";

    [ObservableProperty]
    private bool isLoggingIn = false;

    // Partial method that's automatically called when IsLoggingIn changes
    partial void OnIsLoggingInChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLogin));
    }

    // ==========================================
    // COMPUTED PROPERTIES
    // ==========================================

    /// <summary>
    /// Determines if the login button should be enabled.
    /// Both username and password must be filled in and we must not be currently logging in.
    /// </summary>
    public bool CanLogin => !string.IsNullOrWhiteSpace(Username) 
                           && !string.IsNullOrWhiteSpace(Password) 
                           && !IsLoggingIn;

    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;
    private readonly IApiService _apiService;

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
        if (IsLoggingIn || !CanLogin) return;

        try
        {
            IsLoggingIn = true;
            LoginStatus = "Authenticating...";

            // Call the API with username and password
            // Server will validate against database and return all official details
            var loginResponse = await _apiService.LoginAsync(Username, Password);
            
            if (loginResponse != null && loginResponse.Success)
            {
                LoginStatus = $"✅ Login successful!";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {loginResponse.OfficialId} logged in successfully");
                Console.WriteLine($"  County: {loginResponse.County}");
                Console.WriteLine($"  Constituency: {loginResponse.Constituency}");
                Console.WriteLine($"  Station: {loginResponse.StationId}");
                
                // Wait a moment to show success message
                await Task.Delay(1000);
                
                // Navigate to biometric authentication
                _navigationService.NavigateToOfficialAuthenticate();
            }
            else
            {
                LoginStatus = "❌ Invalid username or password";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Login failed for {Username}");
            }
        }
        catch (Exception ex)
        {
            LoginStatus = $"❌ Login error: {ex.Message}";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Login error: {ex.Message}");
        }
        finally
        {
            IsLoggingIn = false;
        }
    }
}