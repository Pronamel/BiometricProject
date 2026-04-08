using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using officialApp.Models;
using officialApp.Services;
using officialApp.Services.Scanner;

namespace officialApp.ViewModels;

public partial class OfficialAddVoterViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool isCreateVoterMode = true;

    [ObservableProperty]
    private bool isCreateOfficialMode = false;

    [ObservableProperty]
    private string nationalInsuranceNumber = string.Empty;

    [ObservableProperty]
    private string firstName = string.Empty;

    [ObservableProperty]
    private string lastName = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? selectedDateOfBirth;

    [ObservableProperty]
    private string addressLine1 = string.Empty;

    [ObservableProperty]
    private string addressLine2 = string.Empty;

    [ObservableProperty]
    private string postCode = string.Empty;

    [ObservableProperty]
    private string selectedCounty = string.Empty;

    [ObservableProperty]
    private string selectedConstituency = string.Empty;

    [ObservableProperty]
    private string officialUsername = string.Empty;

    [ObservableProperty]
    private string selectedPollingStation = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string statusColor = "black";

    [ObservableProperty]
    private string deviceStatus = "Checking scanner...";

    [ObservableProperty]
    private bool isScannerConnected = false;

    [ObservableProperty]
    private Bitmap? previewImage = null;

    [ObservableProperty]
    private int qualityScore = 0;

    [ObservableProperty]
    private bool isCapturing = false;

    [ObservableProperty]
    private string captureStatusMessage = "Ready to scan";

    private readonly INavigationService _navigationService;
    private readonly IServerHandler _serverHandler;
    private readonly IScannerService _scannerService;

    public List<string> CountyOptions => UKCounties.Counties
        .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
        .ToList();
    public List<string> ConstituencyOptions => UKConstituencies.Constituencies
        .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
        .ToList();
    
    // Polling stations - store full objects internally
    private List<PollingStationOption> _allPollingStations = new List<PollingStationOption>();
    
    // Polling station options list - display strings for UI
    private List<string> _pollingStationOptions = new List<string>();
    public List<string> PollingStationOptions 
    { 
        get => _pollingStationOptions;
        set => SetProperty(ref _pollingStationOptions, value);
    }

    private byte[]? _capturedFingerprintData = null;
    private uint _capturedFingerprintWidth = 0;
    private uint _capturedFingerprintHeight = 0;
    private const int QUALITY_THRESHOLD = 10;

    public OfficialAddVoterViewModel(IServerHandler serverHandler, INavigationService navigationService, IScannerService scannerService)
    {
        _navigationService = navigationService;
        _serverHandler = serverHandler;
        _scannerService = scannerService;
        CheckScannerConnectivity();
    }

    private async Task LoadPollingStations()
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔄 Fetching polling stations from server...");
            StatusMessage = "Loading polling stations...";
            StatusColor = "black";
            
            var pollingStations = await _serverHandler.GetAllPollingStationsAsync();
            
            if (pollingStations != null && pollingStations.Count > 0)
            {
                // Store full objects internally for later lookup
                _allPollingStations = pollingStations;
                
                // Convert to display format (Code - County - Constituency)
                var options = pollingStations
                    .Select((ps, index) => $"{ps.DisplayName}")
                    .ToList();
                
                PollingStationOptions = options;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Loaded {options.Count} polling stations");
                
                // Auto-select first option if available
                if (options.Count > 0)
                {
                    SelectedPollingStation = options[0];
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Auto-selected: {SelectedPollingStation}");
                }
                
                StatusMessage = "";
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  No polling stations returned from server");
                PollingStationOptions = new List<string> { "No stations available" };
                StatusMessage = "No polling stations available";
                StatusColor = "#e67e22";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error loading polling stations: {ex.Message}");
            PollingStationOptions = new List<string> { $"Error: {ex.Message}" };
            StatusMessage = $"Error: {ex.Message}";
            StatusColor = "#e74c3c";
        }
    }

    [RelayCommand]
    private void SwitchToCreateVoterMode()
    {
        IsCreateVoterMode = true;
        IsCreateOfficialMode = false;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SwitchToCreateOfficialMode()
    {
        // Load polling stations when user clicks Create Official button
        await LoadPollingStations();
        
        // Switch to official mode after loading
        IsCreateOfficialMode = true;
        IsCreateVoterMode = false;
    }

    [RelayCommand]
    private async Task Submit()
    {
        if (_capturedFingerprintData == null || _capturedFingerprintData.Length == 0)
        {
            StatusMessage = "Please capture a fingerprint";
            StatusColor = "#e74c3c";
            return;
        }

        byte[] pngFingerprintData = ConvertGrayscaleToPngBytes(_capturedFingerprintData, _capturedFingerprintWidth, _capturedFingerprintHeight);
        string? formattedDateOfBirth = SelectedDateOfBirth?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (IsCreateVoterMode)
        {
            if (string.IsNullOrWhiteSpace(FirstName) ||
                string.IsNullOrWhiteSpace(LastName) ||
                string.IsNullOrWhiteSpace(formattedDateOfBirth) ||
                string.IsNullOrWhiteSpace(AddressLine1) ||
                string.IsNullOrWhiteSpace(PostCode) ||
                string.IsNullOrWhiteSpace(SelectedCounty) ||
                string.IsNullOrWhiteSpace(SelectedConstituency))
            {
                StatusMessage = "Please complete all required voter fields";
                StatusColor = "#e74c3c";
                return;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(OfficialUsername) || string.IsNullOrWhiteSpace(Password))
            {
                StatusMessage = "Please enter official username and password";
                StatusColor = "#e74c3c";
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedPollingStation))
            {
                StatusMessage = "Please select a polling station";
                StatusColor = "#e74c3c";
                return;
            }
        }

        try
        {
            StatusMessage = IsCreateVoterMode ? "Creating voter..." : "Creating official...";
            StatusColor = "#3498db";

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Starting submit flow. Mode: {(IsCreateVoterMode ? "CreateVoter" : "CreateOfficial")}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fingerprint data size (raw): {_capturedFingerprintData.Length} bytes");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fingerprint data size (PNG encoded): {pngFingerprintData.Length} bytes");

            (bool voterSuccess, string voterMessage) = IsCreateVoterMode
                ? await _serverHandler.CreateVoterWithFingerprintAsync(
                    NationalInsuranceNumber,
                    FirstName,
                    LastName,
                    formattedDateOfBirth!,
                    AddressLine1,
                    AddressLine2,
                    PostCode,
                    SelectedCounty,
                    SelectedConstituency,
                    pngFingerprintData)
                : (false, string.Empty);

            bool submitSuccess = IsCreateVoterMode ? voterSuccess : await CreateOfficialWithPollingStation(pngFingerprintData);

            if (submitSuccess)
            {
                StatusMessage = IsCreateVoterMode
                    ? "Create voter succeeded"
                    : "Create official succeeded";
                StatusColor = "#27ae60";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Submit successful");

                ClearForm();
            }
            else
            {
                // For CreateVoter, display the error message from the server
                if (IsCreateVoterMode)
                {
                    StatusMessage = voterMessage;
                    StatusColor = "#e74c3c";
                }
                // For CreateOfficial, the StatusMessage is already set by CreateOfficialWithPollingStation
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Submit failed");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error: {ex.Message}";
            StatusColor = "#e74c3c";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Exception during upload: {ex.Message}");
        }
    }

    private async Task<bool> CreateOfficialWithPollingStation(byte[] fingerprintData)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SelectedPollingStation) || _allPollingStations.Count == 0)
            {
                StatusMessage = "No polling station selected";
                StatusColor = "#e74c3c";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ No polling station selected");
                return false;
            }

            // Find the selected polling station by display name
            var selectedStation = _allPollingStations
                .FirstOrDefault(ps => ps.DisplayName == SelectedPollingStation);

            if (selectedStation == null)
            {
                StatusMessage = "Selected polling station not found";
                StatusColor = "#e74c3c";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Selected polling station not found: {SelectedPollingStation}");
                return false;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🏛️  Creating official with polling station:");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Station ID: {selectedStation.PollingStationId}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   County: {selectedStation.County}");

            if (string.IsNullOrWhiteSpace(selectedStation.County))
            {
                StatusMessage = "Selected polling station has no county assigned";
                StatusColor = "#e74c3c";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ County is not assigned to polling station");
                return false;
            }

            // Call the API with extracted polling station data
            var (success, message) = await _serverHandler.CreateOfficialWithFingerprintAsync(
                OfficialUsername,
                Password,
                selectedStation.PollingStationId.ToString(),
                selectedStation.County,
                fingerprintData);

            if (!success)
            {
                StatusMessage = message;
                StatusColor = "#e74c3c";
            }

            return success;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error creating official: {ex.Message}";
            StatusColor = "#e74c3c";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Exception in CreateOfficialWithPollingStation: {ex.Message}");
            return false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _navigationService.NavigateToOfficialMenu();
    }

    private void ClearForm()
    {
        NationalInsuranceNumber = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
        SelectedDateOfBirth = null;
        AddressLine1 = string.Empty;
        AddressLine2 = string.Empty;
        PostCode = string.Empty;
        SelectedCounty = string.Empty;
        SelectedConstituency = string.Empty;

        OfficialUsername = string.Empty;
        Password = string.Empty;
        SelectedPollingStation = string.Empty;
        
        _capturedFingerprintData = null;
        PreviewImage = null;
        StatusMessage = "Ready for next entry";
        StatusColor = "black";
    }

    // ==========================================
    // SCANNER MANAGEMENT
    // ==========================================

    public void CheckScannerConnectivity()
    {
        try
        {
            Console.WriteLine("[OfficialAddVoterViewModel] CheckScannerConnectivity started");
            
            int deviceCount = _scannerService.GetDeviceCount();
            Console.WriteLine($"[OfficialAddVoterViewModel] Device count: {deviceCount}");

            if (deviceCount > 0)
            {
                IsScannerConnected = true;
                string deviceInfo = _scannerService.GetDeviceDescription(0);
                DeviceStatus = $"Scanner Connected: {deviceInfo}";
                Console.WriteLine($"[OfficialAddVoterViewModel] ✓ Scanner connected: {deviceInfo}");
            }
            else
            {
                IsScannerConnected = false;
                DeviceStatus = "No scanner device detected";
                Console.WriteLine("[OfficialAddVoterViewModel] ❌ No scanner devices found");
            }
        }
        catch (Exception ex)
        {
            IsScannerConnected = false;
            DeviceStatus = $"Error checking scanner: {ex.Message}";
            Console.WriteLine($"[OfficialAddVoterViewModel] ❌ Error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void StartScanning()
    {
        Console.WriteLine("[OfficialAddVoterViewModel] Start scanning command triggered");
        
        try
        {
            if (IsCapturing)
            {
                Console.WriteLine("[OfficialAddVoterViewModel] Capture already in progress");
                return;
            }

            // Open device
            if (!_scannerService.OpenDevice(0))
            {
                CaptureStatusMessage = "Failed to open scanner device";
                Console.WriteLine("[OfficialAddVoterViewModel] ❌ Failed to open device");
                return;
            }

            Console.WriteLine("[OfficialAddVoterViewModel] ✓ Device opened");

            // Subscribe to events
            _scannerService.PreviewImageAvailable += OnPreviewImageAvailable;
            _scannerService.FingerprintCaptured += OnFingerprintCaptured;
            _scannerService.ErrorOccurred += OnScannerError;

            // Start capture
            if (!_scannerService.StartCapture())
            {
                CaptureStatusMessage = "Failed to start capture";
                Console.WriteLine("[OfficialAddVoterViewModel] ❌ Failed to start capture");
                CleanupCapture();
                return;
            }

            IsCapturing = true;
            CaptureStatusMessage = "Place finger on scanner...";
            QualityScore = 0;
            Console.WriteLine("[OfficialAddVoterViewModel] ✓ Capture started");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAddVoterViewModel] ❌ Error starting scan: {ex.Message}");
            CaptureStatusMessage = $"Error: {ex.Message}";
            IsCapturing = false;
        }
    }

    private void OnPreviewImageAvailable(object? sender, ScannerEventArgs args)
    {
        try
        {
            Console.WriteLine($"[OfficialAddVoterViewModel] Preview image received: {args.Width}x{args.Height}, Quality: {args.QualityScore}%, ImageData: {(args.ImageData != null ? args.ImageData.Length : 0)} bytes");
            
            _capturedFingerprintWidth = args.Width;
            _capturedFingerprintHeight = args.Height;
            
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                QualityScore = args.QualityScore;
                
                string statusMessage;
                if (args.QualityScore == 0)
                {
                    statusMessage = "Scanning... Place your finger on the scanner";
                }
                else if (args.QualityScore < QUALITY_THRESHOLD)
                {
                    statusMessage = "Hold steady while scanner improves capture...";
                }
                else
                {
                    statusMessage = "Fingerprint accepted";
                }
                
                CaptureStatusMessage = statusMessage;

                if (args.ImageData != null && args.ImageData.Length > 0)
                {
                    Console.WriteLine($"[OfficialAddVoterViewModel] Converting image data to bitmap...");
                    Bitmap? convertedBitmap = ConvertBytesToBitmap(args.ImageData, args.Width, args.Height);
                    PreviewImage = convertedBitmap;
                    Console.WriteLine($"[OfficialAddVoterViewModel] ✓ PreviewImage updated");
                }
            }, DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAddVoterViewModel] ❌ Error in preview handler: {ex.Message}");
        }
    }

    private void OnFingerprintCaptured(object? sender, ScannerEventArgs args)
    {
        try
        {
            Console.WriteLine($"[OfficialAddVoterViewModel] Fingerprint captured: Success={args.IsSuccess}, Quality={args.QualityScore}");

            if (args.IsSuccess)
            {
                _capturedFingerprintData = args.ImageData;
            }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (args.IsSuccess)
                {
                    CaptureStatusMessage = "Fingerprint captured";
                    Console.WriteLine("[OfficialAddVoterViewModel] ✓ Fingerprint data saved");
                }
                else
                {
                    CaptureStatusMessage = "Capture failed or incomplete";
                    Console.WriteLine("[OfficialAddVoterViewModel] ❌ Capture was not successful");
                }

                CleanupCapture();
            }, DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAddVoterViewModel] ❌ Error in capture handler: {ex.Message}");
            CleanupCapture();
        }
    }

    private void OnScannerError(object? sender, string errorMessage)
    {
        Console.WriteLine($"[OfficialAddVoterViewModel] ⚠️ Scanner error: {errorMessage}");
        
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CaptureStatusMessage = $"Error: {errorMessage}";
            CleanupCapture();
        }, DispatcherPriority.Input);
    }

    private void CleanupCapture()
    {
        try
        {
            _scannerService.PreviewImageAvailable -= OnPreviewImageAvailable;
            _scannerService.FingerprintCaptured -= OnFingerprintCaptured;
            _scannerService.ErrorOccurred -= OnScannerError;

            try
            {
                _scannerService.StopCapture();
            }
            catch (AccessViolationException ex)
            {
                Console.WriteLine($"[OfficialAddVoterViewModel] ⚠️ StopCapture access violation: {ex.Message}");
            }

            try
            {
                _scannerService.CloseDevice();
            }
            catch (AccessViolationException ex)
            {
                Console.WriteLine($"[OfficialAddVoterViewModel] ⚠️ CloseDevice access violation: {ex.Message}");
            }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsCapturing = false;
            }, DispatcherPriority.Input);
            
            Console.WriteLine("[OfficialAddVoterViewModel] ✓ Capture cleaned up");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAddVoterViewModel] ⚠️ Error during cleanup: {ex.Message}");
            IsCapturing = false;
        }
    }

    private Bitmap ConvertBytesToBitmap(byte[] imageData, uint width, uint height)
    {
        try
        {
            var pixelSize = new Avalonia.PixelSize((int)width, (int)height);
            
            var bitmap = new WriteableBitmap(
                pixelSize,
                new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888
            );

            Console.WriteLine($"[OfficialAddVoterViewModel] Converting {width}x{height} grayscale image to bitmap ({imageData.Length} bytes)");

            using (var buffer = bitmap.Lock())
            {
                Console.WriteLine($"[OfficialAddVoterViewModel] Bitmap locked, stride: {buffer.RowBytes}");
                
                int bytesPerPixel = 4;
                IntPtr bufferPtr = buffer.Address;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        byte grayValue = imageData[y * (int)width + x];
                        byte invertedValue = (byte)(255 - grayValue);
                        int pixelOffset = y * buffer.RowBytes + x * bytesPerPixel;
                        
                        System.Runtime.InteropServices.Marshal.WriteByte(bufferPtr, pixelOffset + 0, invertedValue);
                        System.Runtime.InteropServices.Marshal.WriteByte(bufferPtr, pixelOffset + 1, invertedValue);
                        System.Runtime.InteropServices.Marshal.WriteByte(bufferPtr, pixelOffset + 2, invertedValue);
                        System.Runtime.InteropServices.Marshal.WriteByte(bufferPtr, pixelOffset + 3, 255);
                    }
                }
                
                Console.WriteLine($"[OfficialAddVoterViewModel] ✓ Bitmap data copied and inverted ({(int)width}x{(int)height})");
            }

            Console.WriteLine($"[OfficialAddVoterViewModel] ✓ Bitmap conversion complete");
            return bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAddVoterViewModel] ❌ Error converting bytes to bitmap: {ex.Message}");
            return new WriteableBitmap(
                new Avalonia.PixelSize((int)width, (int)height),
                new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888
            );
        }
    }

    /// <summary>
    /// Converts raw grayscale byte array to PNG-encoded bytes
    /// </summary>
    private byte[] ConvertGrayscaleToPngBytes(byte[] grayscaleData, uint width, uint height)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.WriteLine("[OfficialAddVoterViewModel] ⚠️ PNG conversion is Windows only");
                return grayscaleData; // Fallback to raw data if not Windows
            }

            Console.WriteLine($"[OfficialAddVoterViewModel] Converting grayscale to PNG format ({width}x{height})...");

#pragma warning disable CA1416
            using (var bitmap = new System.Drawing.Bitmap((int)width, (int)height, System.Drawing.Imaging.PixelFormat.Format8bppIndexed))
            {
                // Set up grayscale color palette
                var palette = bitmap.Palette;
                for (int i = 0; i < 256; i++)
                {
                    palette.Entries[i] = System.Drawing.Color.FromArgb(i, i, i);
                }
                bitmap.Palette = palette;

                // Copy raw grayscale data to bitmap
                var rect = new System.Drawing.Rectangle(0, 0, (int)width, (int)height);
                var bitmapData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format8bppIndexed);
                
                try
                {
                    System.Runtime.InteropServices.Marshal.Copy(grayscaleData, 0, bitmapData.Scan0, grayscaleData.Length);
                }
                finally
                {
                    bitmap.UnlockBits(bitmapData);
                }

                // Convert bitmap to PNG bytes
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    byte[] pngBytes = ms.ToArray();
                    Console.WriteLine($"[OfficialAddVoterViewModel] ✓ Converted to PNG bytes: {pngBytes.Length} bytes");
                    return pngBytes;
                }
            }
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAddVoterViewModel] ❌ Error converting to PNG: {ex.Message}");
            Console.WriteLine($"[OfficialAddVoterViewModel] Stack: {ex.StackTrace}");
            return grayscaleData; // Return raw data as fallback
        }
    }

}
