using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureVoteApp.Services;
using SecureVoteApp.Models;
using System.Net.Http;

namespace SecureVoteApp.ViewModels;

public partial class VoterLoginViewModel : ViewModelBase
{
    // ==========================================
    // OBSERVABLE PROPERTIES
    // ==========================================

    [ObservableProperty]
    private string selectedConstituency = "";

    [ObservableProperty]
    private string selectedCounty = "";

    [ObservableProperty]
    private string pollingStationCode = "";
    
    [ObservableProperty]
    private string statusMessage = "";

    [ObservableProperty]
    private string serverStatus = "Server status: Not tested";
    
    [ObservableProperty]
    private bool isConnecting = false;
    
    [ObservableProperty]
    private int assignedVoterId = 0;
    
    [ObservableProperty]
    private string connectedOfficialId = "";
    
    [ObservableProperty]
    private string connectedStationId = "";

    // County options for selection
    public List<string> CountyOptions => UKCounties.Counties
        .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    // Constituency options for selection
    public List<string> ConstituencyOptions => UKConstituencies.Constituencies
        .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;
    private readonly CountyService _countyService;
    private readonly IApiService _apiService;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public VoterLoginViewModel(IApiService apiService, INavigationService navigationService, CountyService countyService)
    {
        _navigationService = navigationService;
        _countyService = countyService;
        _apiService = apiService;
    }

    // ==========================================
    // PROPERTY CHANGE HANDLING
    // ==========================================
    
    partial void OnSelectedCountyChanged(string value)
    {
        // Update the shared county service when selection changes
        _countyService.SelectedCounty = value;
        StatusMessage = ""; // Clear any previous messages
    }

    // ==========================================
    // COMMANDS
    // ==========================================

    [RelayCommand]
    private async Task TestConnection()
    {
        if (IsConnecting) return;
        
        try
        {
            IsConnecting = true;
            ServerStatus = "Testing connection...";
            
            // Test connection to server
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var response = await client.GetAsync("http://localhost:5000/securevote");
            
            if (response.IsSuccessStatusCode)
            {
                ServerStatus = "✅ Connected to local server (localhost:5000)";
            }
            else
            {
                ServerStatus = "❌ Server connection failed";
            }
        }
        catch (Exception ex)
        {
            ServerStatus = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task Continue()
    {
        if (IsConnecting) return;
        
        try
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(SelectedCounty))
            {
                StatusMessage = "❌ Please select a county";
                return;
            }

            if (string.IsNullOrWhiteSpace(PollingStationCode))
            {
                StatusMessage = "❌ Please enter polling station code";
                return;
            }

            IsConnecting = true;
            StatusMessage = "🔗 Linking to polling station...";
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Attempting voter link: County={SelectedCounty}, Station={PollingStationCode}");
            
            // Call the voter linking API
            var linkResponse = await _apiService.LinkToOfficialAsync(PollingStationCode, SelectedCounty, SelectedConstituency);
            
            if (linkResponse.Success)
            {
                // Store the linking information
                AssignedVoterId = linkResponse.AssignedVoterId;
                ConnectedOfficialId = linkResponse.ConnectedOfficialId;
                ConnectedStationId = linkResponse.ConnectedStationId;
                
                StatusMessage = $"✅ Connected! Voter ID: {AssignedVoterId}";
                
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter linked successfully: ID={AssignedVoterId}, Official={ConnectedOfficialId}");
                
                // Navigate to the personal or proxy selection
                _navigationService.NavigateToPersonalOrProxy();
            }
            else
            {
                StatusMessage = $"❌ {linkResponse.Message}";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter linking failed: {linkResponse.Message}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Connection error: {ex.Message}";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter linking exception: {ex.Message}");
        }
        finally
        {
            IsConnecting = false;
        }
    }
}