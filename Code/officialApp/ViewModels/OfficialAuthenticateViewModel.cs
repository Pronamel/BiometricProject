using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using System;
using System.Threading.Tasks;

namespace officialApp.ViewModels;

public partial class OfficialAuthenticateViewModel : ViewModelBase
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
                new Uri($"avares://officialApp/Assets/{fileName}")
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
            SetStatusMessage("Authentication successful. Welcome, Official.");
            await Task.Delay(750);
            _navigationService.NavigateToOfficialMenu();
        }
    }

    public void imageHandler()
    {
        
    }

    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public OfficialAuthenticateViewModel()
    {
        _navigationService = Navigation.Instance;
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
        _navigationService.NavigateToOfficialLogin();
    }
}