using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using System.Net.Http;
using System.Globalization;
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
    private readonly IServerHandler _serverHandler;
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
    private DateTimeOffset? selectedDateOfBirth;

    [ObservableProperty]
    private string postCode = string.Empty;

    [ObservableProperty]
    private string townOfBirth = string.Empty;

    [ObservableProperty]
    private string nationalInsuranceNumber = string.Empty;

    [ObservableProperty]
    private bool dateOfBirthVisible = true;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    [ObservableProperty]
    private bool showAlreadyVotedMessage = false;

    [ObservableProperty]
    private string alreadyVotedMessage = string.Empty;

    [ObservableProperty]
    private bool isLooking = false;

    partial void OnStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatusMessage));
    }




    // ==========================================
    // CONSTRUCTOR
    // ==========================================
    
    public NINEntryViewModel(INavigationService navigationService, IServerHandler serverHandler, CountyService countyService)
    {
        _navigationService = navigationService;
        _serverHandler = serverHandler;
        _countyService = countyService;
    }

    public void ResetSensitiveFields()
    {
        FirstName = string.Empty;
        LastName = string.Empty;
        SelectedDateOfBirth = null;
        PostCode = string.Empty;
        TownOfBirth = string.Empty;
        NationalInsuranceNumber = string.Empty;
        DateOfBirthVisible = true;
        StatusMessage = string.Empty;
        ShowAlreadyVotedMessage = false;
        AlreadyVotedMessage = string.Empty;
        IsLooking = false;
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   PostCode: {PostCode}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   County: {_countyService.SelectedCounty}");

            var selectedConstituency = "Unknown"; // TODO: Add constituency selection to UI if needed

            if (string.IsNullOrWhiteSpace(FirstName) ||
                string.IsNullOrWhiteSpace(LastName) ||
                string.IsNullOrWhiteSpace(PostCode) ||
                string.IsNullOrWhiteSpace(TownOfBirth))
            {
                StatusMessage = "❌ Enter First Name, Last Name, Post Code, and Town Of Birth.";
                return;
            }

            string? normalizedDob = null;
            if (DateOfBirthVisible)
            {
                if (!SelectedDateOfBirth.HasValue)
                {
                    StatusMessage = "❌ Select Date of Birth.";
                    return;
                }

                normalizedDob = SelectedDateOfBirth.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            
            var lookup = await _serverHandler.LookupVoterForAuthAsync(
                firstName: string.IsNullOrWhiteSpace(FirstName) ? null : FirstName,
                lastName: string.IsNullOrWhiteSpace(LastName) ? null : LastName,
                dateOfBirth: normalizedDob,
                postCode: string.IsNullOrWhiteSpace(PostCode) ? null : PostCode,
                townOfBirth: string.IsNullOrWhiteSpace(TownOfBirth) ? null : TownOfBirth,
                county: _countyService.SelectedCounty,
                constituency: selectedConstituency);

            if (lookup?.Success == true && lookup.VoterId.HasValue)
            {
                StatusMessage = "✅ Voter found!";
                ShowAlreadyVotedMessage = false;
                AlreadyVotedMessage = string.Empty;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter lookup successful: {lookup.FullName}");
                
                // Navigate to authenticate user view, passing the lookup data
                await _navigationService.NavigateToAuthenticateUser(lookup);
            }
            else if (lookup?.RequiresDisambiguation == true && lookup.CandidateVoterIds?.Count > 0)
            {
                StatusMessage = "✅ Multiple matches found. Continue with fingerprint scan.";
                ShowAlreadyVotedMessage = false;
                AlreadyVotedMessage = string.Empty;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Voter lookup collision: {lookup.CandidateVoterIds.Count} candidates");

                await _navigationService.NavigateToAuthenticateUser(lookup);
            }
            else if (!string.IsNullOrWhiteSpace(lookup?.Message) &&
                     lookup.Message.Contains("already voted", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = string.Empty;
                ShowAlreadyVotedMessage = true;
                AlreadyVotedMessage = lookup.Message;
                _serverHandler.CurrentDeviceStatus = "Already voted - official assistance required";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Voter already voted: {lookup.Message}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔄 Device status set for heartbeat delivery: {_serverHandler.CurrentDeviceStatus}");
            }
            else
            {
                ShowAlreadyVotedMessage = false;
                AlreadyVotedMessage = string.Empty;
                StatusMessage = string.IsNullOrWhiteSpace(lookup?.Message)
                    ? "❌ Voter not found. Check your details and try again."
                    : $"❌ {lookup.Message}";
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

}
