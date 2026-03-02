using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using System.Net.Http;
using System.Reflection;
using SecureVoteApp.Views.VoterUI;
using SecureVoteApp.Services;
using Avalonia.Controls;

namespace SecureVoteApp.ViewModels;

public partial class PersonalOrProxyViewModel : ViewModelBase
{
    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;




    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public PersonalOrProxyViewModel()
    {
        _navigationService = Navigation.Instance;
    }




    // ==========================================
    // COMMANDS
    // ==========================================
    
    [RelayCommand]
    private void OpenNINEntry()
    {
        _navigationService.NavigateToNINEntry();
    }
    
    [RelayCommand]
    private void OpenProxyVote()
    {
        _navigationService.NavigateToProxyVoteDetails();
    }
    
    [RelayCommand]
    private async Task TestServer()
    {
        System.Diagnostics.Debug.WriteLine("=== TESTING SERVER CONNECTION ===");
        Console.WriteLine("=== TESTING SERVER CONNECTION ===");
        
        try
        {
            System.Diagnostics.Debug.WriteLine("Button clicked! Starting server test...");
            Console.WriteLine("Button clicked! Starting server test...");
            
            // Test basic connection
            bool connected = await ApiService.Instance.TestConnectionAsync();
            System.Diagnostics.Debug.WriteLine($"Server Connected: {connected}");
            Console.WriteLine($"Server Connected: {connected}");
            
            if (connected)
            {
                // Try to get weather data
                var weatherData = await ApiService.Instance.GetWeatherDataAsync();
                
                if (weatherData != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Weather Data Retrieved: {weatherData.Count} records");
                    Console.WriteLine($"Weather Data Retrieved: {weatherData.Count} records");
                    
                    foreach (var item in weatherData)
                    {
                        string msg = $"  Date: {item.Date}, Temp: {item.TemperatureC}°C, Summary: {item.Summary}";
                        System.Diagnostics.Debug.WriteLine(msg);
                        Console.WriteLine(msg);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Weather Data: NULL or no data received");
                    Console.WriteLine("Weather Data: NULL or no data received");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine($"ERROR: {ex.Message}");
        }
        
        System.Diagnostics.Debug.WriteLine("=== TEST COMPLETE ===");
        Console.WriteLine("=== TEST COMPLETE ===");
    }
}


