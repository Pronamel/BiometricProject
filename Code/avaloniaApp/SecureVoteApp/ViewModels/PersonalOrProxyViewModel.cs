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

public partial class PersonalOrProxyViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;

   
    
    public PersonalOrProxyViewModel()
    {
        _navigationService = Navigation.Instance;
    }
    
    
    
    [RelayCommand]
    private void OpenNINEntry()
    {
        _navigationService.NavigateToNINEntry();
    }
    
    [RelayCommand]
    private void OpenProxyVote()
    {
        _navigationService.NavigateToProxyVoteDetails();
    }
}


