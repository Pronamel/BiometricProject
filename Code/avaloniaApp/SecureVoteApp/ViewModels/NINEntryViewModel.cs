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

public partial class NINEntryViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    // Observable properties for proper data binding
    [ObservableProperty]
    private string blueTextHave = "I don't have a National Insurance Number";
    
    [ObservableProperty]
    private bool dateOfBirthVisible = false;

    //NAVIGATION FUNCTIONS
    public NINEntryViewModel()
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



    //BUTTON FUNCTIONS

    [RelayCommand]
    private void BlueTextPress()
    {
        if (!DateOfBirthVisible)
        {
            // Show the date of birth field and change text to toggle back
            DateOfBirthVisible = true;
            BlueTextHave = "I do have a National Insurance Number";
        }
        else
        {
            // Hide the date of birth field and change text to original
            DateOfBirthVisible = false;
            BlueTextHave = "I don't have a National Insurance Number";
        }
    }
}