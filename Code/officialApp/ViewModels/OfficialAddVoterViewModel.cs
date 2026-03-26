using System;
using System.Collections.Generic;
using System.IO;
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
    private string firstName = string.Empty;

    [ObservableProperty]
    private string lastName = string.Empty;

    [ObservableProperty]
    private string dateOfBirth = string.Empty;

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
    private readonly IApiService _apiService;
    private readonly IScannerService _scannerService;

    public List<string> CountyOptions => UKCounties.Counties;
    public List<string> ConstituencyOptions => UKConstituencies.Constituencies;

    private byte[]? _capturedFingerprintData = null;
    private uint _capturedFingerprintWidth = 0;
    private uint _capturedFingerprintHeight = 0;
    private const int QUALITY_THRESHOLD = 10;

    public OfficialAddVoterViewModel(IApiService apiService, INavigationService navigationService, IScannerService scannerService)
    {
        _navigationService = navigationService;
        _apiService = apiService;
        _scannerService = scannerService;
        CheckScannerConnectivity();
    }

    [RelayCommand]
    private void SwitchToCreateVoterMode()
    {
        IsCreateVoterMode = true;
        IsCreateOfficialMode = false;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void SwitchToCreateOfficialMode()
    {
        IsCreateOfficialMode = true;
        IsCreateVoterMode = false;
        StatusMessage = string.Empty;
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

        if (IsCreateVoterMode)
        {
            if (string.IsNullOrWhiteSpace(FirstName) ||
                string.IsNullOrWhiteSpace(LastName) ||
                string.IsNullOrWhiteSpace(DateOfBirth) ||
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
        }

        try
        {
            StatusMessage = IsCreateVoterMode ? "Creating voter..." : "Creating official...";
            StatusColor = "#3498db";

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Starting submit flow. Mode: {(IsCreateVoterMode ? "CreateVoter" : "CreateOfficial")}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fingerprint data size (raw): {_capturedFingerprintData.Length} bytes");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fingerprint data size (PNG encoded): {pngFingerprintData.Length} bytes");

            bool submitSuccess = IsCreateVoterMode
                ? await _apiService.CreateVoterWithFingerprintAsync(
                    FirstName,
                    LastName,
                    DateOfBirth,
                    AddressLine1,
                    AddressLine2,
                    PostCode,
                    SelectedCounty,
                    SelectedConstituency,
                    pngFingerprintData)
                : await _apiService.CreateOfficialWithFingerprintAsync(
                    OfficialUsername,
                    Password,
                    pngFingerprintData);

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
                StatusMessage = IsCreateVoterMode
                    ? "Create voter failed. Check server endpoint and input values."
                    : "Create official failed. Check server endpoint and input values.";
                StatusColor = "#e74c3c";
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

    [RelayCommand]
    private void Cancel()
    {
        _navigationService.NavigateToOfficialMenu();
    }

    private void ClearForm()
    {
        FirstName = string.Empty;
        LastName = string.Empty;
        DateOfBirth = string.Empty;
        AddressLine1 = string.Empty;
        AddressLine2 = string.Empty;
        PostCode = string.Empty;
        SelectedCounty = string.Empty;
        SelectedConstituency = string.Empty;

        OfficialUsername = string.Empty;
        Password = string.Empty;

        PreviewImage = null;
        _capturedFingerprintData = null;
        QualityScore = 0;
        CaptureStatusMessage = "Ready to scan";
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
    private void RefreshScannerStatus()
    {
        CheckScannerConnectivity();
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
                    statusMessage = $"Quality: {args.QualityScore}% - Building quality, keep steady...";
                }
                else
                {
                    statusMessage = $"✓ Excellent! Quality: {args.QualityScore}% - Fingerprint accepted";
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
                
                // Save fingerprint as PNG
                if (args.ImageData != null)
                {
                    SaveFingerprintAsPng(args.ImageData, args.Width, args.Height);
                }
            }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (args.IsSuccess)
                {
                    CaptureStatusMessage = $"✓ Fingerprint captured! Quality: {args.QualityScore}%";
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

    private void SaveFingerprintAsPng(byte[] grayscaleData, uint width, uint height)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.WriteLine("[OfficialAddVoterViewModel] ❌ PNG saving is Windows only");
                return;
            }

            Console.WriteLine($"[OfficialAddVoterViewModel] Saving fingerprint as PNG ({width}x{height})...");

#pragma warning disable CA1416
            using (var bitmap = new System.Drawing.Bitmap((int)width, (int)height, System.Drawing.Imaging.PixelFormat.Format8bppIndexed))
            {
                var palette = bitmap.Palette;
                for (int i = 0; i < 256; i++)
                {
                    palette.Entries[i] = System.Drawing.Color.FromArgb(i, i, i);
                }
                bitmap.Palette = palette;

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

                string fingerprintDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BiometricVoting", "Fingerprints");
                Directory.CreateDirectory(fingerprintDir);

                string fileName = $"fingerprint_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
                string filePath = Path.Combine(fingerprintDir, fileName);
                
                bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
                Console.WriteLine($"[OfficialAddVoterViewModel] ✓ Fingerprint saved to: {filePath}");
            }
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAddVoterViewModel] ❌ Error saving fingerprint as PNG: {ex.Message}");
            Console.WriteLine($"[OfficialAddVoterViewModel] Stack: {ex.StackTrace}");
        }
    }

    [RelayCommand]
    private void TestPreviewImage()
    {
        int testWidth = 256;
        int testHeight = 256;
        byte[] testImageData = new byte[testWidth * testHeight];
        
        for (int y = 0; y < testHeight; y++)
        {
            for (int x = 0; x < testWidth; x++)
            {
                int dx = x - testWidth / 2;
                int dy = y - testHeight / 2;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                byte value = (byte)(Math.Sin(distance / 10) * 127 + 128);
                testImageData[y * testWidth + x] = value;
            }
        }
        
        _capturedFingerprintData = testImageData;
        QualityScore = 75;
        CaptureStatusMessage = "Test image loaded";
        Console.WriteLine("[OfficialAddVoterViewModel] Generating test preview image...");
        
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            PreviewImage = ConvertBytesToBitmap(testImageData, (uint)testWidth, (uint)testHeight);
            Console.WriteLine($"[OfficialAddVoterViewModel] ✓ Test image displayed");
        }, DispatcherPriority.Input);
    }
}
