using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using System;
using System.Threading.Tasks;

namespace SecureVoteApp.ViewModels;

public partial class AuthenticateUserViewModel : ViewModelBase
{
    // ==========================================
    // PRIVATE FIELDS
    // ==========================================
    
    private readonly INavigationService _navigationService;




    // ==========================================
    // OBSERVABLE PROPERTIES
    // ==========================================

    [ObservableProperty]
    private string statusMessage = "";

    [ObservableProperty]
    private Bitmap? imageSource;




    // ==========================================
    // PUBLIC PROPERTIES
    // ==========================================

    public int scannAttempts = 0; // 0 = no attempts
    public bool validFingerPrintScan = false;




    // ==========================================
    // IMAGE MANAGEMENT METHODS
    // ==========================================

    private Bitmap LoadImage(string fileName)
    {
        return new Bitmap(
            AssetLoader.Open(
                new Uri($"avares://SecureVoteApp/Assets/{fileName}")
            )
        );
    }

    public void SetImageSource(string source)
    {
       ImageSource = LoadImage(source);
    }




    // ==========================================
    // STATUS AND ATTEMPT MANAGEMENT
    // ==========================================

    public void SetStatusMessage(string message)
    {
        StatusMessage = message;
    }

    public void setScannAttempts(int type)
    {
        scannAttempts = type;
    }

    public async Task attemptHandler(int attempts, bool scanResult)
    {
        if (attempts == 1 && scanResult == false)
        {
            SetImageSource("fingerPrintWrong.png");
            SetStatusMessage("You have 2 attempts left.");
        }
        else if (attempts == 2 && scanResult == false)
        {
            SetImageSource("fingerPrintWrong.png");
            SetStatusMessage("You have 1 attempts left.");
        }
        else if (attempts == 3 && scanResult == false)
        {
            SetImageSource("fingerPrintWrong.png");
            SetStatusMessage("You have no attempts left. Please Contact an official.");
        }
        else if (scanResult == true)
        {
            SetImageSource("fingerPrintCorrect.png");
            SetStatusMessage("Authentication successful. You may proceed to vote.");
            await Task.Delay(750);
            _navigationService.NavigateToBallot();
        }
    }

    public void imageHandler()
    {
        
    }




    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public AuthenticateUserViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
        // Initialize with default fingerprint image
        ImageSource = LoadImage("fingerPrint.png");
    }




    // ==========================================
    // COMMANDS
    // ==========================================

    [RelayCommand]
    private async Task ScanFingerPrintValid()
    {
        scannAttempts++;
        validFingerPrintScan = true;
        await attemptHandler(scannAttempts, validFingerPrintScan);
    }

    [RelayCommand]
    private async Task ScanFingerPrintInvalid()
    {
        scannAttempts++;
        validFingerPrintScan = false;
        await attemptHandler(scannAttempts, validFingerPrintScan);
    }

    [RelayCommand]
    private void Back()
    {
        _navigationService.NavigateToMain();
    }
}
