using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Threading;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using officialApp.Services.Scanner;
using officialApp.Services;
using Console = officialApp.ViewModels.OfficialAuthScannerConsole;

namespace officialApp.ViewModels;

internal static class OfficialAuthScannerConsole
{
    public static bool Enabled { get; set; } = false;

    public static void WriteLine()
    {
        if (Enabled)
        {
            System.Console.WriteLine();
        }
    }

    public static void WriteLine(string message)
    {
        if (Enabled)
        {
            System.Console.WriteLine(message);
        }
    }
}

public partial class OfficialAuthenticateViewModel : ViewModelBase
{
    // ==========================================
    // PRIVATE FIELDS
    // ==========================================
    
    private readonly INavigationService _navigationService;
    private readonly IScannerService _scannerService;
    private readonly IServerHandler _serverHandler;

    // ==========================================
    // OBSERVABLE PROPERTIES
    // ==========================================

    [ObservableProperty]
    private string statusMessage = "";

    [ObservableProperty]
    private Bitmap? imageSource;

    [ObservableProperty]
    private Bitmap? previewImage = null;

    [ObservableProperty]
    private int qualityScore = 0;

    [ObservableProperty]
    private bool isCapturing = false;

    [ObservableProperty]
    private string captureStatusMessage = "Ready to scan";

    [ObservableProperty]
    private bool isCooldownActive = false;

    [ObservableProperty]
    private int cooldownSecondsRemaining = 0;

    // Credentials received from login
    [ObservableProperty]
    private string username = "";

    [ObservableProperty]
    private string password = "";

    // ==========================================
    // PUBLIC PROPERTIES
    // ==========================================

    public int scannAttempts = 0; // 0 = no attempts
    public bool validFingerPrintScan = false;
    
    // Quality threshold for feedback and acceptance (must match ScannerService MIN_QUALITY_THRESHOLD)
    private const int QUALITY_THRESHOLD = 10;
    private byte[]? _capturedFingerprintData = null;        // Raw image data (200,000 bytes) - for display
    private uint _capturedFingerprintWidth = 0;             // Width of captured fingerprint image
    private uint _capturedFingerprintHeight = 0;            // Height of captured fingerprint image
    private bool _scannerSessionActive = false;
    private bool _eventHandlersAttached = false;
    private bool _isProcessingFingerprint = false;
    private CancellationTokenSource? _cooldownCts = null;
    private const int SCANNER_COOLDOWN_SECONDS = 5;

    private static bool IsBenignScannerCleanupError(string errorMessage)
    {
        return errorMessage.Contains("stop capture", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("CancelCaptureImage", StringComparison.OrdinalIgnoreCase);
    }

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
            scannAttempts = 0;
            SetImageSource("fingerPrintCorrect.png");
            SetStatusMessage("Authentication successful. Welcome, Official.");
            await Task.Delay(750);
            SetImageSource("fingerPrint.png");
            SetStatusMessage(string.Empty);
            _navigationService.NavigateToOfficialMenu();
        }
    }

    // ==========================================
    // FINGERPRINT COMPARISON METHODS
    // ==========================================

    private byte[]? LoadBaselineFingerprintFromAssets()
    {
        try
        {
            Console.WriteLine("[OfficialAuthenticateViewModel] Loading baseline fingerprint from Assets...");
            
            // Load the baseline fingerprint PNG image from Assets - return PNG binary data directly
            using (var stream = AssetLoader.Open(new Uri("avares://officialApp/Assets/fingerprint_BaseLine.png")))
            {
                using (var memoryStream = new MemoryStream())
                {
                    stream.CopyTo(memoryStream);
                    byte[] pngData = memoryStream.ToArray();
                    
                    Console.WriteLine($"[OfficialAuthenticateViewModel] ✓ Baseline fingerprint loaded: {pngData.Length} bytes (PNG format)");
                    return pngData;  // Return PNG file data, not extracted grayscale bytes
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAuthenticateViewModel] ❌ Error loading baseline fingerprint: {ex.Message}");
            Console.WriteLine($"[OfficialAuthenticateViewModel] Stack: {ex.StackTrace}");
            return null;
        }
    }

    private byte[]? ConvertGrayscaleToImageData(byte[] grayscaleData, uint width, uint height)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.WriteLine("[OfficialAuthenticateViewModel] ❌ Grayscale to PNG conversion is Windows only");
                return null;
            }

            Console.WriteLine($"[OfficialAuthenticateViewModel] Converting grayscale data to PNG ({width}x{height})...");

#pragma warning disable CA1416
            // Create 8-bit indexed bitmap from grayscale data
            using (var bitmap = new System.Drawing.Bitmap((int)width, (int)height, System.Drawing.Imaging.PixelFormat.Format8bppIndexed))
            {
                // Set grayscale palette (0-255)
                var palette = bitmap.Palette;
                for (int i = 0; i < 256; i++)
                {
                    palette.Entries[i] = System.Drawing.Color.FromArgb(i, i, i);
                }
                bitmap.Palette = palette;

                // Copy grayscale data to bitmap
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

                // Save bitmap as PNG to byte array
                using (var memoryStream = new MemoryStream())
                {
                    bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                    byte[] pngData = memoryStream.ToArray();
                    Console.WriteLine($"[OfficialAuthenticateViewModel] ✓ Converted to PNG: {pngData.Length} bytes");
                    return pngData;
                }
            }
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAuthenticateViewModel] ❌ Error converting grayscale to PNG: {ex.Message}");
            Console.WriteLine($"[OfficialAuthenticateViewModel] Stack: {ex.StackTrace}");
            return null;
        }
    }

    private async Task CompareFingerprints()
    {
        bool apiResponded = false;
        try
        {
            if (_capturedFingerprintData == null || _capturedFingerprintData.Length == 0)
            {
                Console.WriteLine("[OfficialAuthenticateViewModel] ❌ No captured fingerprint data available");
                return;
            }

            if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
            {
                Console.WriteLine("[OfficialAuthenticateViewModel] ❌ No credentials available for fingerprint verification");
                CaptureStatusMessage = "Error: Missing credentials";
                validFingerPrintScan = false;
                return;
            }

            Console.WriteLine("[OfficialAuthenticateViewModel] Starting fingerprint verification via API...");
            Console.WriteLine($"[OfficialAuthenticateViewModel] Username: {Username}");
            
            // Convert captured grayscale data to PNG format
            Console.WriteLine($"[OfficialAuthenticateViewModel] Encoding scanner data to PNG format ({_capturedFingerprintWidth}x{_capturedFingerprintHeight})...");
            
            var scannedImagePng = ConvertGrayscaleToImageData(_capturedFingerprintData, _capturedFingerprintWidth, _capturedFingerprintHeight);
            if (scannedImagePng == null || scannedImagePng.Length == 0)
            {
                Console.WriteLine("[OfficialAuthenticateViewModel] ❌ Failed to encode captured fingerprint as PNG");
                CaptureStatusMessage = "Error: Could not process scanned fingerprint";
                validFingerPrintScan = false;
                return;
            }

            Console.WriteLine($"[OfficialAuthenticateViewModel] Encoded fingerprint size: {scannedImagePng.Length} bytes (PNG)");

            // Call the server verify-prints endpoint with username, password, and scanned fingerprint
            // The server will fetch the stored fingerprint from the database and compare
            Console.WriteLine("[OfficialAuthenticateViewModel] Calling /api/verify-prints endpoint...");
            var verificationResult = await _serverHandler.VerifyFingerprintAsync(Username, Password, scannedImagePng);
            apiResponded = true;

            if (verificationResult == null || !verificationResult.Success)
            {
                Console.WriteLine($"[OfficialAuthenticateViewModel] ❌ Fingerprint verification failed: {verificationResult?.Message}");
                CaptureStatusMessage = $"Error: {verificationResult?.Message}";
                validFingerPrintScan = false;
                return;
            }

            Console.WriteLine($"[OfficialAuthenticateViewModel] Fingerprint verification result:");
            Console.WriteLine($"[OfficialAuthenticateViewModel]   Match: {verificationResult.IsMatch}");
            Console.WriteLine($"[OfficialAuthenticateViewModel]   Score: {verificationResult.Score}");
            Console.WriteLine($"[OfficialAuthenticateViewModel]   Threshold: {verificationResult.Threshold}");

            // Set validation based on match result
            validFingerPrintScan = verificationResult.IsMatch;
            
            if (verificationResult.IsMatch)
            {
                Console.WriteLine("[OfficialAuthenticateViewModel] ✓ FINGERPRINT VERIFIED - Authentication successful");
            }
            else
            {
                Console.WriteLine($"[OfficialAuthenticateViewModel] ❌ FINGERPRINT DOES NOT MATCH - Score {verificationResult.Score} below threshold {verificationResult.Threshold}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAuthenticateViewModel] ❌ Error verifying fingerprint: {ex.Message}");
            Console.WriteLine($"[OfficialAuthenticateViewModel] Stack: {ex.StackTrace}");
            validFingerPrintScan = false;
        }
        finally
        {
            if (apiResponded)
            {
                ScrubCapturedFingerprint();
            }
        }
    }

    private void ScrubCapturedFingerprint()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _capturedFingerprintData = null;
            _capturedFingerprintWidth = 0;
            _capturedFingerprintHeight = 0;
            PreviewImage = null;
            QualityScore = 0;
        }, DispatcherPriority.Input);
    }

    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public OfficialAuthenticateViewModel(INavigationService navigationService, IScannerService scannerService, IServerHandler serverHandler)
    {
        _navigationService = navigationService;
        _scannerService = scannerService;
        _serverHandler = serverHandler;
        
        // Initialize with default fingerprint image
        ImageSource = LoadImage("fingerPrint.png");
        PreviewImage = null;
        QualityScore = 0;
        IsCapturing = false;
        CaptureStatusMessage = "Preparing scanner...";
    }

    // ==========================================
    // VIEW LIFECYCLE
    // ==========================================

    public async Task ActivateScannerAsync()
    {
        if (_scannerSessionActive)
        {
            return;
        }

        SetImageSource("fingerPrint.png");
        SetStatusMessage(string.Empty);

        _scannerSessionActive = true;
        await StartScanningInternalAsync();
    }

    public void DeactivateScanner()
    {
        _scannerSessionActive = false;
        _isProcessingFingerprint = false;

        try
        {
            _cooldownCts?.Cancel();
            _cooldownCts?.Dispose();
            _cooldownCts = null;
        }
        catch
        {
            // Ignore cancellation races during teardown.
        }

        IsCooldownActive = false;
        CooldownSecondsRemaining = 0;
        CaptureStatusMessage = "Scanner paused";
        CleanupCapture(closeDevice: true);
    }

    // ==========================================
    // SCANNER SESSION CONTROL
    // ==========================================

    private async Task StartScanningInternalAsync()
    {
        Console.WriteLine("[OfficialAuthenticateViewModel] Start scanning command triggered");
        
        try
        {
            if (!_scannerSessionActive || IsCapturing || IsCooldownActive || _isProcessingFingerprint)
            {
                return;
            }

            // Open device
            if (!_scannerService.IsDeviceOpen() && !_scannerService.OpenDevice(0))
            {
                CaptureStatusMessage = "Failed to open scanner device";
                Console.WriteLine("[OfficialAuthenticateViewModel] ❌ Failed to open device");
                return;
            }

            Console.WriteLine("[OfficialAuthenticateViewModel] ✓ Device opened");

            // Subscribe to events
            if (!_eventHandlersAttached)
            {
                _scannerService.PreviewImageAvailable += OnPreviewImageAvailable;
                _scannerService.FingerprintCaptured += OnFingerprintCaptured;
                _scannerService.ErrorOccurred += OnScannerError;
                _eventHandlersAttached = true;
            }

            // Start capture
            if (!_scannerService.StartCapture())
            {
                CaptureStatusMessage = "Failed to start capture";
                Console.WriteLine("[OfficialAuthenticateViewModel] ❌ Failed to start capture");
                CleanupCapture(closeDevice: true);
                return;
            }

            IsCapturing = true;
            CaptureStatusMessage = "Place finger on scanner...";
            QualityScore = 0;
            Console.WriteLine("[OfficialAuthenticateViewModel] ✓ Capture started");

            await Task.CompletedTask;
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
            
            // Store scanner dimensions for later use in fingerprint conversion
            _capturedFingerprintWidth = args.Width;
            _capturedFingerprintHeight = args.Height;
            
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

    private async void OnFingerprintCaptured(object? sender, ScannerEventArgs args)
    {
        try
        {
            if (!_scannerSessionActive || _isProcessingFingerprint)
            {
                return;
            }

            _isProcessingFingerprint = true;
            Console.WriteLine($"[OfficialAuthenticateViewModel] Fingerprint captured: Success={args.IsSuccess}, Quality={args.QualityScore}");

            // Store fingerprint data immediately (thread-safe operation) - stored in memory only for security/privacy
            if (args.IsSuccess)
            {
                _capturedFingerprintData = args.ImageData;      // Raw image for display
            }

            IsCapturing = false;

            // Update UI on the main thread
            _ = Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (args.IsSuccess)
                {
                    CaptureStatusMessage = $"Fingerprint captured! Quality: {args.QualityScore}% - Comparing...";
                    Console.WriteLine("[OfficialAuthenticateViewModel] ✓ Fingerprint data saved - starting comparison");
                }
                else
                {
                    CaptureStatusMessage = "Capture failed or incomplete";
                    Console.WriteLine("[OfficialAuthenticateViewModel] ❌ Capture was not successful");
                }
            }, DispatcherPriority.Input);

            if (args.IsSuccess)
            {
                await CompareFingerprints();
                await Task.Delay(1000);
                scannAttempts++;

                if (_navigationService != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await attemptHandler(scannAttempts, validFingerPrintScan);
                    }, DispatcherPriority.Input);
                }
                else
                {
                    Console.WriteLine("[OfficialAuthenticateViewModel] ❌ ERROR: NavigationService is null");
                }

                CleanupCapture(closeDevice: false);

                // Release processing lock before scheduling the next scan attempt.
                _isProcessingFingerprint = false;

                // Success should pass straight through to the app with no cooldown delay.
                if (!validFingerPrintScan)
                {
                    await StartCooldownAndResumeAsync();
                }
            }
            else
            {
                CleanupCapture(closeDevice: false);
                _isProcessingFingerprint = false;
                await StartCooldownAndResumeAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAuthenticateViewModel] ❌ Error in capture handler: {ex.Message}");
            Console.WriteLine($"[OfficialAuthenticateViewModel] Stack: {ex.StackTrace}");
            _isProcessingFingerprint = false;
            CleanupCapture(closeDevice: false);
            await StartCooldownAndResumeAsync();
        }
    }

    private void OnScannerError(object? sender, string errorMessage)
    {
        Console.WriteLine($"[OfficialAuthenticateViewModel] ⚠️ Scanner error: {errorMessage}");

        if (!_scannerSessionActive)
        {
            return;
        }

        // Scanner SDK can emit a stop-capture error during normal cleanup after a completed frame.
        if (_isProcessingFingerprint && IsBenignScannerCleanupError(errorMessage))
        {
            Console.WriteLine("[OfficialAuthenticateViewModel] Ignoring benign cleanup scanner error during retry flow");
            return;
        }
        
        // Update UI on the main thread
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CaptureStatusMessage = $"Error: {errorMessage}";
            CleanupCapture(closeDevice: false);
            _ = StartCooldownAndResumeAsync();
        }, DispatcherPriority.Input);
    }

    private async Task StartCooldownAndResumeAsync()
    {
        if (!_scannerSessionActive)
        {
            return;
        }

        _cooldownCts?.Cancel();
        _cooldownCts?.Dispose();
        _cooldownCts = new CancellationTokenSource();
        var token = _cooldownCts.Token;

        IsCooldownActive = true;

        try
        {
            for (int seconds = SCANNER_COOLDOWN_SECONDS; seconds > 0; seconds--)
            {
                CooldownSecondsRemaining = seconds;
                await Task.Delay(1000, token);
            }
        }
        catch (TaskCanceledException)
        {
            return;
        }
        finally
        {
            IsCooldownActive = false;
            CooldownSecondsRemaining = 0;
        }

        if (_scannerSessionActive)
        {
            await StartScanningInternalAsync();
        }
    }

    private void CleanupCapture(bool closeDevice)
    {
        try
        {
            // Try to stop capture (don't check if active first, as that can crash on invalid state)
            try
            {
                _scannerService.StopCapture();
            }
            catch (AccessViolationException ex)
            {
                Console.WriteLine($"[OfficialAuthenticateViewModel] ⚠️ StopCapture access violation (device may be in bad state): {ex.Message}");
            }

            if (closeDevice)
            {
                // Unsubscribe from events and close device when leaving this view.
                if (_eventHandlersAttached)
                {
                    _scannerService.PreviewImageAvailable -= OnPreviewImageAvailable;
                    _scannerService.FingerprintCaptured -= OnFingerprintCaptured;
                    _scannerService.ErrorOccurred -= OnScannerError;
                    _eventHandlersAttached = false;
                }

                try
                {
                    _scannerService.CloseDevice();
                }
                catch (AccessViolationException ex)
                {
                    Console.WriteLine($"[OfficialAuthenticateViewModel] ⚠️ CloseDevice access violation: {ex.Message}");
                }
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
    private void Back()
    {
        DeactivateScanner();
        _navigationService.NavigateToOfficialLogin();
    }

    [RelayCommand]
    private async Task SignOut()
    {
        DeactivateScanner();
        await _serverHandler.LogoutAsync();
        _navigationService.NavigateToOfficialLogin();
    }


}