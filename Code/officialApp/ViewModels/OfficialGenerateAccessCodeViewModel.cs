using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using officialApp.Services;

namespace officialApp.ViewModels;

public partial class OfficialGenerateAccessCodeViewModel : ViewModelBase
{
    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string statusColor = "black";

    [ObservableProperty]
    private string accessCode = string.Empty;

    private readonly INavigationService _navigationService;
    private readonly IApiService _apiService;

    public OfficialGenerateAccessCodeViewModel()
    {
        _navigationService = Navigation.Instance;
        _apiService = ApiService.Instance;
    }

    [RelayCommand]
    private async Task Generate()
    {
        if (string.IsNullOrWhiteSpace(AccessCode))
        {
            StatusMessage = "Please enter an access code";
            StatusColor = "#e74c3c";
            return;
        }

        try
        {
            StatusMessage = "Setting access code...";
            StatusColor = "#3498db";

            // Call ApiService to hash and send the code
            var success = await _apiService.SetAccessCodeAsync(AccessCode);

            if (success)
            {
                StatusMessage = "✅ Access code set successfully!";
                StatusColor = "#27ae60";
                AccessCode = string.Empty;
            }
            else
            {
                StatusMessage = "❌ Failed to set access code. Please try again.";
                StatusColor = "#e74c3c";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error: {ex.Message}";
            StatusColor = "#e74c3c";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _navigationService.NavigateToOfficialMenu();
    }
}

