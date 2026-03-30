using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using System.Net.Http;
using System.Reflection;
using SecureVoteApp.Views.VoterUI;
using SecureVoteApp.Services;
using SecureVoteApp.Models;
using Avalonia.Controls;

namespace SecureVoteApp.ViewModels;

public partial class NINEntryViewModel : ViewModelBase
{
    // ==========================================
    // PRIVATE READONLY FIELDS
    // ==========================================

    private readonly INavigationService _navigationService;
    private readonly IApiService _apiService;
    private readonly CountyService _countyService;



    // ==========================================
    // OBSERVABLE PROPERTIES
    // ==========================================

    // Public properties for compiled bindings
    [ObservableProperty]
    private string firstName = string.Empty;

    [ObservableProperty]
    private string lastName = string.Empty;

    [ObservableProperty]
    private string dateOfBirth = string.Empty;

    [ObservableProperty]
    private string nationalInsuranceNumber = string.Empty;

    [ObservableProperty]
    private string blueTextHave = "I don't have a National Insurance Number";
    
    //I do have a National Insurance Number

    [ObservableProperty]
    private bool dateOfBirthVisible = false;

    [ObservableProperty]
    private bool textBoxEnabled = true;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isLooking = false;




    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public NINEntryViewModel(INavigationService navigationService, IApiService apiService, CountyService countyService)
    {
        _navigationService = navigationService;
        _apiService = apiService;
        _countyService = countyService;
        
        // Populate with TestVoter data
        FirstName = "TestVoter";
        LastName = "BiometricTest";
        DateOfBirth = "1985-11-23 00:00:00+00";
        NationalInsuranceNumber = "AB123456C";
    }




    // ==========================================
    // COMMANDS
    // ==========================================
    
    [RelayCommand]
    private void Back()
    {
        _navigationService.NavigateToMain();
    }
    
    [RelayCommand]
    private async Task Continue()
    {
        if (IsLooking) return;

        try
        {
            IsLooking = true;
            StatusMessage = "🔍 Searching for voter...";

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] NINEntry Continue - Starting voter lookup");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   FirstName: {FirstName}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   LastName: {LastName}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   County: {_countyService.SelectedCounty}");

            var selectedConstituency = "Unknown"; // TODO: Add constituency selection to UI if needed
            
            var lookup = await _apiService.LookupVoterForAuthAsync(
                nin: string.IsNullOrWhiteSpace(NationalInsuranceNumber) ? null : NationalInsuranceNumber,
                firstName: string.IsNullOrWhiteSpace(FirstName) ? null : FirstName,
                lastName: string.IsNullOrWhiteSpace(LastName) ? null : LastName,
                dateOfBirth: DateOfBirthVisible && !string.IsNullOrWhiteSpace(DateOfBirth) ? DateOfBirth : null,
                county: _countyService.SelectedCounty,
                constituency: selectedConstituency);

            if (lookup?.Success == true && lookup.VoterId.HasValue)
            {
                StatusMessage = "✅ Voter found!";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter lookup successful: {lookup.FullName}");
                
                // Navigate to authenticate user view, passing the lookup data
                _navigationService.NavigateToAuthenticateUser(lookup);
            }
            else
            {
                StatusMessage = "❌ Voter not found. Check your details and try again.";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Voter lookup failed: {lookup?.Message}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error: {ex.Message}";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Lookup error: {ex.Message}");
        }
        finally
        {
            IsLooking = false;
        }
    }

    [RelayCommand]
    private void BlueTextPress()
    {
        if(DateOfBirthVisible == false)
        {
            DateOfBirthVisible = true;
            TextBoxEnabled = false;
            BlueTextHave = "I do have a National Insurance Number";
        }
        else
        {
            DateOfBirthVisible = false;
            TextBoxEnabled = true;
            BlueTextHave = "I don't have a National Insurance Number";
        }
    }
}
