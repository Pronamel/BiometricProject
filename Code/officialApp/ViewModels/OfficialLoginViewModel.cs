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
    private string officialId = "";

    [ObservableProperty]  
    private string stationId = "";

    [ObservableProperty]
    private string password = "";

    [ObservableProperty]
    private string selectedCounty = "";

    [ObservableProperty]
    private string serverStatus = "Server status: Not tested";

    [ObservableProperty]
    private string loginStatus = "";

    [ObservableProperty]
    private bool isLoggingIn = false;

    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;
    private readonly IServerHandler _serverHandler;
    private readonly IApiService _apiService;

    // County options for selection
    public List<string> CountyOptions => UKCounties.Counties;
    
    // ==========================================
    // SYSTEM IDENTIFICATION
    // ==========================================
    
    // Hardcoded unique system code for this official terminal
    // This identifies the specific official system within the county
    public const string SYSTEM_CODE = "OFF-SYS-2024-7891";  // Unique per official terminal
    
    [ObservableProperty]
    private List<int> connectedVoterIds = new();

    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public OfficialLoginViewModel()
    {
        _navigationService = Navigation.Instance;
        _serverHandler = ServerHandler.Instance;
        _apiService = ApiService.Instance;
    }

    // ==========================================
    // COMMANDS
    // ==========================================

    [RelayCommand]
    private async Task Authenticate()
    {
        if (IsLoggingIn) return;

        try
        {
            IsLoggingIn = true;
            LoginStatus = "Authenticating...";

            // Validate input
            if (string.IsNullOrWhiteSpace(OfficialId))
            {
                LoginStatus = "❌ Official ID is required";
                return;
            }

            if (string.IsNullOrWhiteSpace(StationId))
            {
                LoginStatus = "❌ Station ID is required";
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedCounty))
            {
                LoginStatus = "❌ County selection is required";
                return;
            }

            // Attempt JWT authentication
            var loginResponse = await _apiService.LoginAsync(OfficialId, StationId, SelectedCounty, SYSTEM_CODE, Password);
            
            if (loginResponse != null && loginResponse.Success)
            {
                LoginStatus = $"✅ Authentication successful! Welcome, {loginResponse.OfficialId}";
                Console.WriteLine($"Official {loginResponse.OfficialId} authenticated successfully at station {loginResponse.StationId}");
                
                // Wait a moment to show success message
                await Task.Delay(1500);
                
                // Navigate to biometric authentication
                _navigationService.NavigateToOfficialAuthenticate();
            }
            else
            {
                string errorMsg = loginResponse?.Success == false ? "Invalid credentials or unauthorized access" : "Authentication failed - no response";
                LoginStatus = $"❌ {errorMsg}";
                Console.WriteLine($"Authentication failed for {OfficialId}: {errorMsg}");
            }
        }
        catch (Exception ex)
        {
            LoginStatus = $"❌ Authentication error: {ex.Message}";
            Console.WriteLine($"Authentication error: {ex.Message}");
        }
        finally
        {
            IsLoggingIn = false;
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
            
            // Test connection first
            bool connected = await _serverHandler.TestConnectionAsync();
            if (!connected)
            {
                ServerStatus = "❌ Server connection failed";
                return;
            }
            
            // Device management has been removed from server - show simple connected status
            ServerStatus = "✅ Connected to local server (localhost:5000)";
        }
        catch (Exception ex)
        {
            ServerStatus = $"❌ Error: {ex.Message}";
        }
    }
}