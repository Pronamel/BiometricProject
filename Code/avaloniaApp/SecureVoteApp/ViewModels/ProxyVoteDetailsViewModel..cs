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

public partial class ProxyVoteDetailsViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;

    // Public properties for compiled bindings
    [ObservableProperty]
    private string blueTextHave = "I don't have a National Insurance Number";
    
    //I do have a National Insurance Number

    [ObservableProperty]
    private bool dateOfBirthVisible = false;

    [ObservableProperty]
    private bool textBoxEnabled = true;
    
    
    public ProxyVoteDetailsViewModel()
    {
        _navigationService = Navigation.Instance;
    }
    
    [RelayCommand]
    private void Back()
    {
        _navigationService.NavigateToMain();
    }
    
    [RelayCommand]
    private void Authenticate()
    {
        _navigationService.NavigateToAuthenticateUser();
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
