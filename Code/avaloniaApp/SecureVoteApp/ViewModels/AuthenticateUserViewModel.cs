using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SecureVoteApp.ViewModels;

public partial class AuthenticateUserViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private string statusMessage = "Use biometric verification to authenticate this voter.";

    public AuthenticateUserViewModel()
    {
        _navigationService = Navigation.Instance;
    }

    [RelayCommand]
    private void Back()
    {
        _navigationService.NavigateToMain();
    }

    [RelayCommand]
    private void Continue()
    {
        _navigationService.NavigateToMain();
    }
}
