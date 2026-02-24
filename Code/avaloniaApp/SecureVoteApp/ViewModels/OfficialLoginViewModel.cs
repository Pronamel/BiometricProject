using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using System.Net.Http;
using System.Reflection;
using Avalonia.Controls;

namespace SecureVoteApp.ViewModels;

public partial class OfficialLoginViewModel : ViewModelBase
{
    // ==========================================
    // OBSERVABLE PROPERTIES
    // ==========================================

    [ObservableProperty]
    private string username = "";

    [ObservableProperty]  
    private string password = "";

    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public OfficialLoginViewModel()
    {
        _navigationService = Navigation.Instance;
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
}