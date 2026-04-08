using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureVoteApp.Services;

namespace SecureVoteApp.ViewModels;

public partial class ProxyVoteDetailsViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IServerHandler _serverHandler;
    private readonly CountyService _countyService;

    [ObservableProperty]
    private string representedFirstName = string.Empty;

    [ObservableProperty]
    private string representedLastName = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? representedDateOfBirth;

    [ObservableProperty]
    private string representedPostCode = string.Empty;

    [ObservableProperty]
    private string representedTownOfBirth = string.Empty;

    [ObservableProperty]
    private string proxyFirstName = string.Empty;

    [ObservableProperty]
    private string proxyLastName = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? proxyDateOfBirth;

    [ObservableProperty]
    private string proxyPostCode = string.Empty;

    [ObservableProperty]
    private string proxyTownOfBirth = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isLooking = false;

    public ProxyVoteDetailsViewModel(
        INavigationService navigationService,
        IServerHandler serverHandler,
        CountyService countyService)
    {
        _navigationService = navigationService;
        _serverHandler = serverHandler;
        _countyService = countyService;
    }

    public void ResetSensitiveFields()
    {
        RepresentedFirstName = string.Empty;
        RepresentedLastName = string.Empty;
        RepresentedDateOfBirth = null;
        RepresentedPostCode = string.Empty;
        RepresentedTownOfBirth = string.Empty;
        ProxyFirstName = string.Empty;
        ProxyLastName = string.Empty;
        ProxyDateOfBirth = null;
        ProxyPostCode = string.Empty;
        ProxyTownOfBirth = string.Empty;
        StatusMessage = string.Empty;
        IsLooking = false;
    }

    [RelayCommand]
    private void Back()
    {
        _navigationService.NavigateToMain();
    }

    [RelayCommand]
    private async Task Authenticate()
    {
        if (IsLooking)
        {
            return;
        }

        try
        {
            IsLooking = true;
            StatusMessage = "Verifying represented voter and proxy voter...";

            if (string.IsNullOrWhiteSpace(RepresentedFirstName) ||
                string.IsNullOrWhiteSpace(RepresentedLastName) ||
                string.IsNullOrWhiteSpace(RepresentedPostCode) ||
                string.IsNullOrWhiteSpace(RepresentedTownOfBirth) ||
                !RepresentedDateOfBirth.HasValue)
            {
                StatusMessage = "Enter all represented voter fields including date of birth.";
                return;
            }

            if (string.IsNullOrWhiteSpace(ProxyFirstName) ||
                string.IsNullOrWhiteSpace(ProxyLastName) ||
                string.IsNullOrWhiteSpace(ProxyPostCode) ||
                string.IsNullOrWhiteSpace(ProxyTownOfBirth) ||
                !ProxyDateOfBirth.HasValue)
            {
                StatusMessage = "Enter all proxy voter fields including date of birth.";
                return;
            }

            var selectedConstituency = "Unknown";
            var representedDob = RepresentedDateOfBirth.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var proxyDob = ProxyDateOfBirth.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var representedLookup = await _serverHandler.LookupVoterForAuthAsync(
                firstName: RepresentedFirstName,
                lastName: RepresentedLastName,
                dateOfBirth: representedDob,
                postCode: RepresentedPostCode,
                townOfBirth: RepresentedTownOfBirth,
                county: _countyService.SelectedCounty,
                constituency: selectedConstituency);

            if (representedLookup?.Success != true || !representedLookup.VoterId.HasValue)
            {
                StatusMessage = string.IsNullOrWhiteSpace(representedLookup?.Message)
                    ? "Represented voter lookup failed."
                    : representedLookup.Message;
                return;
            }

            var proxyLookup = await _serverHandler.LookupVoterForAuthAsync(
                firstName: ProxyFirstName,
                lastName: ProxyLastName,
                dateOfBirth: proxyDob,
                postCode: ProxyPostCode,
                townOfBirth: ProxyTownOfBirth,
                county: _countyService.SelectedCounty,
                constituency: selectedConstituency);

            if (proxyLookup?.Success != true || !proxyLookup.VoterId.HasValue)
            {
                StatusMessage = string.IsNullOrWhiteSpace(proxyLookup?.Message)
                    ? "Proxy voter lookup failed."
                    : proxyLookup.Message;
                return;
            }

            var proxyValidation = await _serverHandler.ValidateProxyAuthorizationAsync(
                representedLookup.VoterId.Value,
                proxyLookup.VoterId.Value);

            if (proxyValidation?.Success != true)
            {
                StatusMessage = string.IsNullOrWhiteSpace(proxyValidation?.Message)
                    ? "Proxy authorization failed."
                    : proxyValidation.Message;
                return;
            }

            _serverHandler.ConfigureProxyVotingSession(
                representedLookup.VoterId.Value,
                proxyLookup.VoterId.Value);

            StatusMessage = "Proxy authorized. Authenticate proxy fingerprint to continue.";
            await _navigationService.NavigateToAuthenticateUser(proxyLookup);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLooking = false;
        }
    }
}
