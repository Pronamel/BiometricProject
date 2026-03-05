using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using officialApp.Services;

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

            // Attempt JWT authentication
            var loginResponse = await _apiService.LoginAsync(OfficialId, StationId, Password);
            
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
            
            // Get device management info
            var deviceInfo = await _serverHandler.GetDeviceManagementInfoAsync();
            if (deviceInfo != null)
            {
                var deviceNames = deviceInfo.DeviceNames?.Count > 0 ? 
                    string.Join(", ", deviceInfo.DeviceNames) : "No devices";
                
                ServerStatus = $"✅ Connected | Station: {deviceInfo.PollingStationID} | " +
                             $"Devices: {deviceInfo.No_ConnectedDevices} ({deviceNames})";
            }
            else
            {
                ServerStatus = "✅ Connected, but no device info available";
            }
        }
        catch (Exception ex)
        {
            ServerStatus = $"❌ Error: {ex.Message}";
        }
    }
}