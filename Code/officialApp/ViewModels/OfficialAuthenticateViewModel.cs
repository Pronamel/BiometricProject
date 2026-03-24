using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Threading;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using officialApp.Services.Scanner;

namespace officialApp.ViewModels;

public partial class OfficialAuthenticateViewModel : ViewModelBase
{
    // ==========================================
    // PRIVATE FIELDS
    // ==========================================
    
    private readonly INavigationService _navigationService;
    private readonly IScannerService _scannerService;

    // ==========================================
    // OBSERVABLE PROPERTIES
    // ==========================================

    [ObservableProperty]
    private string statusMessage = "";

    [ObservableProperty]
    private Bitmap? imageSource;

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

    // ==========================================
    // PUBLIC PROPERTIES
    // ==========================================

    public int scannAttempts = 0; // 0 = no attempts
    public bool validFingerPrintScan = false;
    
    // Quality threshold for feedback and acceptance (must match ScannerService MIN_QUALITY_THRESHOLD)
    private const int QUALITY_THRESHOLD = 10;
    private byte[]? _capturedFingerprintData = null;        // Raw image data (200,000 bytes) - for display

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
    // SCANNER DEVICE MANAGEMENT
    // ==========================================

    public void CheckScannerConnectivity()
    {
        try
        {
            Console.WriteLine("[OfficialAuthenticateViewModel] CheckScannerConnectivity started");
            
            int deviceCount = _scannerService.GetDeviceCount();
            Console.WriteLine($"[OfficialAuthenticateViewModel] Device count: {deviceCount}");

            if (deviceCount > 0)
            {
                IsScannerConnected = true;
                string deviceInfo = _scannerService.GetDeviceDescription(0);
                DeviceStatus = $"Scanner Connected: {deviceInfo}";
                Console.WriteLine($"[OfficialAuthenticateViewModel] ✓ Scanner connected: {deviceInfo}");
            }
            else
            {
                IsScannerConnected = false;
                DeviceStatus = "No scanner device detected";
                Console.WriteLine("[OfficialAuthenticateViewModel] ❌ No scanner devices found");
            }
        }
        catch (Exception ex)
        {
            IsScannerConnected = false;
            DeviceStatus = $"Error checking scanner: {ex.Message}";
            Console.WriteLine($"[OfficialAuthenticateViewModel] ❌ Error: {ex.Message}");
        }
    }

    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public OfficialAuthenticateViewModel(INavigationService navigationService, IScannerService scannerService)
    {
        _navigationService = navigationService;
        _scannerService = scannerService;
        
        // Initialize with default fingerprint image
        ImageSource = LoadImage("fingerPrint.png");
        PreviewImage = null;
        QualityScore = 0;
        IsCapturing = false;
        CaptureStatusMessage = "Ready to scan";
    }

    // ==========================================
    // COMMANDS
    // ==========================================

    [RelayCommand]
    private void RefreshScannerStatus()
    {
        CheckScannerConnectivity();
    }

    [RelayCommand]
    private void StartScanning()
    {
        Console.WriteLine("[OfficialAuthenticateViewModel] Start scanning command triggered");
        
        try
        {
            if (IsCapturing)
            {
                Console.WriteLine("[OfficialAuthenticateViewModel] Capture already in progress");
                return;
            }

            // Open device
            if (!_scannerService.OpenDevice(0))
            {
                CaptureStatusMessage = "Failed to open scanner device";
                Console.WriteLine("[OfficialAuthenticateViewModel] ❌ Failed to open device");
                return;
            }

            Console.WriteLine("[OfficialAuthenticateViewModel] ✓ Device opened");

            // Subscribe to events
            _scannerService.PreviewImageAvailable += OnPreviewImageAvailable;
            _scannerService.FingerprintCaptured += OnFingerprintCaptured;
            _scannerService.ErrorOccurred += OnScannerError;

            // Start capture
            if (!_scannerService.StartCapture())
            {
                CaptureStatusMessage = "Failed to start capture";
                Console.WriteLine("[OfficialAuthenticateViewModel] ❌ Failed to start capture");
                CleanupCapture();
                return;
            }

            IsCapturing = true;
            CaptureStatusMessage = "Place finger on scanner...";
            QualityScore = 0;
            Console.WriteLine("[OfficialAuthenticateViewModel] ✓ Capture started");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAuthenticateViewModel] ❌ Error starting scan: {ex.Message}");
            CaptureStatusMessage = $"Error: {ex.Message}";
            IsCapturing = false;
        }
    }

    private void OnPreviewImageAvailable(object? sender, ScannerEventArgs args)
    {
        try
        {
            Console.WriteLine($"[OfficialAuthenticateViewModel] Preview image received: {args.Width}x{args.Height}, Quality: {args.QualityScore}%, ImageData: {(args.ImageData != null ? args.ImageData.Length : 0)} bytes");
            
            // Dispatch all UI updates to the main thread to ensure proper Avalonia binding notifications
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Update quality score
                QualityScore = args.QualityScore;
                
                // Provide helpful feedback based on quality level
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

                // Convert and display preview
                if (args.ImageData != null && args.ImageData.Length > 0)
                {
                    Console.WriteLine($"[OfficialAuthenticateViewModel] Converting image data to bitmap...");
                    Bitmap? convertedBitmap = ConvertBytesToBitmap(args.ImageData, args.Width, args.Height);
                    PreviewImage = convertedBitmap;
                    Console.WriteLine($"[OfficialAuthenticateViewModel] ✓ PreviewImage updated (Bitmap: {(PreviewImage != null ? "valid" : "null")})");
                }
                else
                {
                    Console.WriteLine($"[OfficialAuthenticateViewModel] ⚠️ No image data to display");
                }
            }, DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAuthenticateViewModel] ❌ Error in preview handler: {ex.Message}");
            Console.WriteLine($"[OfficialAuthenticateViewModel] Stack trace: {ex.StackTrace}");
        }
    }

    private void OnFingerprintCaptured(object? sender, ScannerEventArgs args)
    {
        try
        {
            Console.WriteLine($"[OfficialAuthenticateViewModel] Fingerprint captured: Success={args.IsSuccess}, Quality={args.QualityScore}");

            // Store fingerprint data immediately (thread-safe operation) - stored in memory only for security/privacy
            if (args.IsSuccess)
            {
                _capturedFingerprintData = args.ImageData;      // Raw image for display
            }

            // Update UI on the main thread
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (args.IsSuccess)
                {
                    CaptureStatusMessage = $"Fingerprint captured! Quality: {args.QualityScore}%";
                    Console.WriteLine("[OfficialAuthenticateViewModel] ✓ Fingerprint data saved");
                    
                    // Auto-trigger successful scan
                    validFingerPrintScan = true;
                    scannAttempts++;
                    
                    // Schedule UI update and navigation with null-check
                    Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        if (_navigationService != null)
                        {
                            await attemptHandler(scannAttempts, validFingerPrintScan);
                        }
                        else
                        {
                            Console.WriteLine("[OfficialAuthenticateViewModel] ❌ ERROR: NavigationService is null");
                            CleanupCapture();
                        }
                    });
                }
                else
                {
                    CaptureStatusMessage = "Capture failed or incomplete";
                    Console.WriteLine("[OfficialAuthenticateViewModel] ❌ Capture was not successful");
                }

                CleanupCapture();
            }, DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAuthenticateViewModel] ❌ Error in capture handler: {ex.Message}");
            Console.WriteLine($"[OfficialAuthenticateViewModel] Stack: {ex.StackTrace}");
            CleanupCapture();
        }
    }

    private void OnScannerError(object? sender, string errorMessage)
    {
        Console.WriteLine($"[OfficialAuthenticateViewModel] ⚠️ Scanner error: {errorMessage}");
        
        // Update UI on the main thread
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
            // Unsubscribe from events
            _scannerService.PreviewImageAvailable -= OnPreviewImageAvailable;
            _scannerService.FingerprintCaptured -= OnFingerprintCaptured;
            _scannerService.ErrorOccurred -= OnScannerError;

            // Try to stop capture (don't check if active first, as that can crash on invalid state)
            try
            {
                _scannerService.StopCapture();
            }
            catch (AccessViolationException ex)
            {
                Console.WriteLine($"[OfficialAuthenticateViewModel] ⚠️ StopCapture access violation (device may be in bad state): {ex.Message}");
            }

            // Close device
            try
            {
                _scannerService.CloseDevice();
            }
            catch (AccessViolationException ex)
            {
                Console.WriteLine($"[OfficialAuthenticateViewModel] ⚠️ CloseDevice access violation: {ex.Message}");
            }

            // Update UI on main thread
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsCapturing = false;
            }, DispatcherPriority.Input);
            
            Console.WriteLine("[OfficialAuthenticateViewModel] ✓ Capture cleaned up");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAuthenticateViewModel] ⚠️ Error during cleanup: {ex.Message}");
            IsCapturing = false;
        }
    }

    private Bitmap ConvertBytesToBitmap(byte[] imageData, uint width, uint height)
    {
        try
        {
            var pixelSize = new PixelSize((int)width, (int)height);
            
            // Create WriteableBitmap with ARGB32 format (Avalonia doesn't support direct grayscale)
            var bitmap = new WriteableBitmap(
                pixelSize,
                new Vector(96, 96),
                PixelFormat.Rgba8888
            );

            Console.WriteLine($"[OfficialAuthenticateViewModel] Converting {width}x{height} grayscale image to bitmap ({imageData.Length} bytes)");

            // Convert grayscale to ARGB and copy into bitmap buffer
            using (var buffer = bitmap.Lock())
            {
                Console.WriteLine($"[OfficialAuthenticateViewModel] Bitmap locked, stride: {buffer.RowBytes}");
                
                // IMPORTANT: Use stride to properly handle padding between rows
                // The bitmap buffer may have padding, so we can't just copy all data at once
                int bytesPerPixel = 4; // RGBA
                IntPtr bufferPtr = buffer.Address;

                // Copy row by row, handling stride properly and INVERTING the image
                // Fingerprint scanners typically return mostly white with dark fingerprint lines,
                // so we invert to show dark lines on light background
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        byte grayValue = imageData[y * (int)width + x];
                        // INVERT the image so white becomes black and vice versa
                        byte invertedValue = (byte)(255 - grayValue);
                        int pixelOffset = y * buffer.RowBytes + x * bytesPerPixel;
                        
                        // Write ARGB values directly to buffer - using inverted value
                        Marshal.WriteByte(bufferPtr, pixelOffset + 0, invertedValue); // R
                        Marshal.WriteByte(bufferPtr, pixelOffset + 1, invertedValue); // G
                        Marshal.WriteByte(bufferPtr, pixelOffset + 2, invertedValue); // B
                        Marshal.WriteByte(bufferPtr, pixelOffset + 3, 255);           // A
                    }
                }
                
                Console.WriteLine($"[OfficialAuthenticateViewModel] ✓ Bitmap data copied and inverted ({(int)width}x{(int)height})");
            }

            Console.WriteLine($"[OfficialAuthenticateViewModel] ✓ Bitmap conversion complete");
            return bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAuthenticateViewModel] ❌ Error converting bytes to bitmap: {ex.Message}");
            Console.WriteLine($"[OfficialAuthenticateViewModel] Stack trace: {ex.StackTrace}");
            return new WriteableBitmap(
                new PixelSize((int)width, (int)height),
                new Vector(96, 96),
                PixelFormat.Rgba8888
            );
        }
    }

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

    [RelayCommand]
    private void TestPreviewImage()
    {
        // Generate a test fingerprint pattern (grayscale)
        int testWidth = 256;
        int testHeight = 256;
        byte[] testImageData = new byte[testWidth * testHeight];
        
        // Create a simple gradient pattern for testing
        for (int y = 0; y < testHeight; y++)
        {
            for (int x = 0; x < testWidth; x++)
            {
                // Create a radial gradient with some fingerprint-like pattern
                int dx = x - testWidth / 2;
                int dy = y - testHeight / 2;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                byte value = (byte)(Math.Sin(distance / 10) * 127 + 128);
                testImageData[y * testWidth + x] = value;
            }
        }
        
        QualityScore = 75;
        CaptureStatusMessage = "Test image loaded";
        Console.WriteLine("[OfficialAuthenticateViewModel] Generating test preview image...");
        
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            PreviewImage = ConvertBytesToBitmap(testImageData, (uint)testWidth, (uint)testHeight);
            Console.WriteLine($"[OfficialAuthenticateViewModel] ✓ Test image displayed");
        }, DispatcherPriority.Input);
    }
}