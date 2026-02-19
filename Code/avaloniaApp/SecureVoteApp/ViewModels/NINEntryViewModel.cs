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

public partial class NINEntryViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    
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
}