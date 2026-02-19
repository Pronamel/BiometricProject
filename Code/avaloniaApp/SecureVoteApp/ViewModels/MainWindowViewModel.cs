using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using System.Net.Http;
using System.Reflection;
using SecureVoteApp.Views;
using Avalonia.Controls;

namespace SecureVoteApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] 
    private string greeting = "Welcome to Avalonia!";
    
    [ObservableProperty]
    private int clickCounter = 0;
    
    [ObservableProperty]
    private UserControl currentView;
    
    // Views
    private readonly MainView _mainView;
    private readonly NINEntryView _ninEntryView;
    
    // Navigation service
    private readonly INavigationService _navigationService;
    
    public MainWindowViewModel()
    {
        // Get navigation service instance
        _navigationService = Navigation.Instance;
        
        // Subscribe to navigation events
        _navigationService.NavigationRequested += OnNavigationRequested;
        
        // Initialize views
        _mainView = new MainView { DataContext = this };
        _ninEntryView = new NINEntryView { DataContext = new NINEntryViewModel() };
        
        // Initialize navigation service with view factories
        ((NavigationService)_navigationService).Initialize(
            () => _mainView,
            () => _ninEntryView
        );
        
        // Set initial view
        CurrentView = _mainView;
    }
    
    private void OnNavigationRequested(UserControl view)
    {
        CurrentView = view;
        
        // Update greeting based on current view
        if (view == _mainView)
            Greeting = "Welcome back to the main view!";
        else if (view == _ninEntryView)
            Greeting = "Navigate to NIN Entry View";
    }

    // DEFINE CLASSES HERE 
    public class WeatherForecast
    {
        public DateTime Date { get; set; }
        public string? Summary { get; set; }
        public int TemperatureC { get; set; }
        public int TemperatureF { get; set; }
    }




    [RelayCommand] 
    private void Click()
    {
        ClickCounter++; // Increment the counter
        
        // Use if statements to react based on counter value
        if (ClickCounter == 1)
        {
            Greeting = "First click! 🎉";
        }
        else if (ClickCounter == 5)
        {
            Greeting = "You've clicked 5 times! 🔥";
        }
        else if (ClickCounter == 10)
        {
            Greeting = "Wow, 10 clicks! You're dedicated! 💪";
        }
        else if (ClickCounter % 10 == 0) // Every 10th click
        {
            Greeting = $"Milestone reached: {ClickCounter} clicks! 🚀";
        }
        else if (ClickCounter > 20)
        {
            Greeting = $"Click #{ClickCounter} - You're unstoppable! 🎯";
        }
        else
        {
            Greeting = $"Click count: {ClickCounter}";
        }
    }



    [RelayCommand]
    private void Reset()
    {
        Greeting = "Get out of here you filthy animal!";
        ClickCounter = 0; // Reset counter too
    }

    [RelayCommand]
    private async Task GetServerInfo()
    {
        Greeting = "Getting server info...";
        await LoadDataFromServer();
    }

    [RelayCommand]
    private async Task LoadDataFromServer()
    {
        string serverUrl = "http://192.168.0.33:5000/weatherforecast"; // change if your server uses a different URL
        using HttpClient client = new HttpClient();

        try
        {
            string response = await client.GetStringAsync(serverUrl);

            // Parse JSON
            var forecasts = JsonSerializer.Deserialize<WeatherForecast[]>(response);

            Greeting = "Received data from server:";
            Console.WriteLine("Received data from server:");
            
            if (forecasts != null)
            {
                foreach (var f in forecasts)
                {
                    Console.WriteLine($"{f.Date}: {f.Summary}, {f.TemperatureC}°C / {f.TemperatureF}°F");
                    Console.WriteLine("This is coming from the client as the server cant print shit on ur terminal dumbass");
                    Greeting = "well this is now the server working";
                    Greeting = $"Date: {f.Date}, Summary: {f.Summary}, Temp: {f.TemperatureC}°C / {f.TemperatureF}°F";
                }
            }
        }
        catch (Exception ex)
        {
            Greeting = $"Error connecting to server: {ex.Message}";
            Console.WriteLine($"Error connecting to server: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenNINEntry()
    {
        try
        {
            _navigationService.NavigateToNINEntry();
        }
        catch (Exception ex)
        {
            Greeting = $"Error opening NIN entry: {ex.Message}";
        }
    }
    
    [RelayCommand]
    private void NavigateToMain()
    {
        _navigationService.NavigateToMain();
    }
}


