using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SecureVoteApp.Services.Scanner;
using SecureVoteApp.Services;
using SecureVoteApp.Models;

namespace SecureVoteApp.ViewModels;

public partial class AuthenticateUserViewModel : ViewModelBase
{
    // ==========================================
    // PRIVATE FIELDS
    // ==========================================
    
    private readonly INavigationService _navigationService;
    private readonly IScannerService _scannerService;
    private readonly IServerHandler _serverHandler;
    private readonly BallotPaperViewModel _ballotPaperViewModel;
    private readonly DeviceLockState _deviceLockState;
    
    // Voter authentication fields
    private byte[]? _storedFingerprintBytes;
    private Guid? _currentVoterId;
    private List<string>? _candidateVoterIds;
    private bool _lockDueToAlreadyVoted;

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
    private string voterFullName = string.Empty;

    [ObservableProperty]
    private string voterStatusMessage = string.Empty;

    [ObservableProperty]
    private bool isBackEnabled = true;

    [ObservableProperty]
    private bool isStartScanningEnabled = true;

    [ObservableProperty]
    private bool isCooldownActive;

    [ObservableProperty]
    private int cooldownSecondsRemaining;

    // ==========================================
    // PUBLIC PROPERTIES
    // ==========================================

    public int scannAttempts = 0; // 0 = no attempts
    public bool validFingerPrintScan = false;
    
    // Quality threshold for feedback and acceptance (must match ScannerService MIN_QUALITY_THRESHOLD)
    private const int QUALITY_THRESHOLD = 10;
    private const int SCANNER_COOLDOWN_SECONDS = 5;
    private byte[]? _capturedFingerprintData = null;        // Raw image data (200,000 bytes) - for display
    private uint _capturedFingerprintWidth = 0;             // Width of captured fingerprint image
    private uint _capturedFingerprintHeight = 0;            // Height of captured fingerprint image
    private bool _scannerSessionActive;
    private bool _eventHandlersAttached;
    private bool _isProcessingFingerprint;
    private CancellationTokenSource? _cooldownCts;

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

    public void ResetAuthenticationState()
    {
        _storedFingerprintBytes = null;
        _currentVoterId = null;
        _capturedFingerprintData = null;
        _capturedFingerprintWidth = 0;
        _capturedFingerprintHeight = 0;
        _candidateVoterIds = null;
        _lockDueToAlreadyVoted = false;
        scannAttempts = 0;
        validFingerPrintScan = false;

        VoterFullName = string.Empty;
        VoterStatusMessage = string.Empty;
        StatusMessage = string.Empty;
        PreviewImage = null;
        QualityScore = 0;
        IsCapturing = false;
        CaptureStatusMessage = "Ready to scan";
        IsCooldownActive = false;
        CooldownSecondsRemaining = 0;
        ImageSource = LoadImage("fingerPrint.png");
        IsBackEnabled = !_deviceLockState.IsLocked;
        IsStartScanningEnabled = !_deviceLockState.IsLocked && !IsCapturing;
    }

    public async Task attemptHandler(int attempts, bool scanResult)
    {
        if (scanResult == true)
        {
            scannAttempts = 0;
            SetImageSource("fingerPrintCorrect.png");
            SetStatusMessage("Authentication successful. You may proceed to vote.");
            VoterStatusMessage = "✅ Voter Found"; // Show green success message
            _serverHandler.CurrentDeviceStatus = "Authentication successful";
            await _serverHandler.SendDeviceStatusAsync(_serverHandler.CurrentDeviceStatus);
            await _ballotPaperViewModel.LoadCandidatesAsync();
            await Task.Delay(750);
            await _navigationService.NavigateToBallot();

            ResetAuthenticationState();
            return;
        }

        if (attempts == 1)
        {
            SetImageSource("fingerPrintWrong.png");
            SetStatusMessage("You have 2 attempts left.");
            VoterStatusMessage = ""; // Clear status on mismatch
        }
        else if (attempts == 2)
        {
            SetImageSource("fingerPrintWrong.png");
            SetStatusMessage("You have 1 attempts left.");
            VoterStatusMessage = ""; // Clear status on mismatch
        }
        else if (attempts >= 3)
        {
            _deviceLockState.SetLocked(true);
            SetImageSource("fingerPrintWrong.png");
            if (_lockDueToAlreadyVoted)
            {
                SetStatusMessage("You have already voted. Please contact an official.");
                VoterStatusMessage = "❌ You have already voted. Official assistance is required.";
                _serverHandler.CurrentDeviceStatus = "Already voted - official assistance required";
            }
            else
            {
                SetStatusMessage("You have no attempts left. Please Contact an official.");
                VoterStatusMessage = "❌ Authentication failed after 3 attempts. You may have mistyped your details.";
                _serverHandler.CurrentDeviceStatus = "Authentication failed after 3 attempts";
            }

            await _serverHandler.SendDeviceStatusAsync(_serverHandler.CurrentDeviceStatus);
        }
    }

    // ==========================================
    // FINGERPRINT COMPARISON METHODS
    // ==========================================

    private async Task CompareFingerprints()
    {
        bool apiResponded = false;
        try
        {
            if (_capturedFingerprintData == null || _capturedFingerprintData.Length == 0)
            {
                Console.WriteLine("[AuthenticateUserViewModel] ❌ No captured fingerprint data available");
                return;
            }

            Console.WriteLine("[AuthenticateUserViewModel] Starting fingerprint verification via API...");
            
            // Convert captured grayscale data to PNG format
            Console.WriteLine($"[AuthenticateUserViewModel] Encoding scanner data to PNG format ({_capturedFingerprintWidth}x{_capturedFingerprintHeight})...");
            
            var scannedImagePng = ConvertGrayscaleToImageData(_capturedFingerprintData, _capturedFingerprintWidth, _capturedFingerprintHeight);
            if (scannedImagePng == null || scannedImagePng.Length == 0)
            {
                Console.WriteLine("[AuthenticateUserViewModel] ❌ Failed to encode captured fingerprint as PNG");
                CaptureStatusMessage = "Error: Could not process scanned fingerprint";
                validFingerPrintScan = false;
                return;
            }

            Console.WriteLine($"[AuthenticateUserViewModel] Encoded fingerprint size: {scannedImagePng.Length} bytes (PNG)");

            var hasCandidates = _candidateVoterIds != null && _candidateVoterIds.Count > 0;
            string? voterId;

            if (hasCandidates)
            {
                // Collision disambiguation must use candidate IDs only.
                voterId = null;
            }
            else
            {
                // Prefer voter ID from lookup initialization for single-match auth flow.
                // Fallback to API session voter ID only if needed.
                voterId = _currentVoterId?.ToString() ?? _serverHandler.CurrentVoterId;
            }

            if (string.IsNullOrEmpty(voterId) && !hasCandidates)
            {
                Console.WriteLine("[AuthenticateUserViewModel] ❌ No voter ID available for fingerprint verification");
                CaptureStatusMessage = "Error: Voter ID not found";
                validFingerPrintScan = false;
                return;
            }

            // Call the server verify-prints endpoint with voterId and scanned fingerprint
            // The server will fetch the stored fingerprint from the database and compare
            Console.WriteLine("[AuthenticateUserViewModel] Calling /api/verify-prints endpoint...");
            Console.WriteLine($"[AuthenticateUserViewModel] VoterId: {(string.IsNullOrWhiteSpace(voterId) ? "<collision-mode>" : voterId)}");
            Console.WriteLine($"[AuthenticateUserViewModel] Candidate IDs: {_candidateVoterIds?.Count ?? 0}");
            var verificationResult = await _serverHandler.VerifyFingerprintAsync(voterId, scannedImagePng, _candidateVoterIds);
            apiResponded = true;

            if (verificationResult == null)
            {
                Console.WriteLine("[AuthenticateUserViewModel] ❌ Fingerprint verification failed: empty response");
                CaptureStatusMessage = "Error: Fingerprint verification failed";
                validFingerPrintScan = false;
                return;
            }

            if (!string.IsNullOrWhiteSpace(verificationResult.Message) &&
                verificationResult.Message.Contains("already voted", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[AuthenticateUserViewModel] ⚠️ Matched voter has already voted - locking station for official assistance");
                _lockDueToAlreadyVoted = true;
                scannAttempts = 2;
                validFingerPrintScan = false;
                return;
            }

            if (!verificationResult.Success)
            {
                Console.WriteLine($"[AuthenticateUserViewModel] ❌ Fingerprint verification failed: {verificationResult.Message}");
                CaptureStatusMessage = $"Error: {verificationResult.Message}";
                validFingerPrintScan = false;
                return;
            }

            Console.WriteLine($"[AuthenticateUserViewModel] Fingerprint verification result:");
            Console.WriteLine($"[AuthenticateUserViewModel]   Match: {verificationResult.IsMatch}");
            Console.WriteLine($"[AuthenticateUserViewModel]   Score: {verificationResult.Score}");
            Console.WriteLine($"[AuthenticateUserViewModel]   Threshold: {verificationResult.Threshold}");

            // Set validation based on match result
            validFingerPrintScan = verificationResult.IsMatch;
            
            if (verificationResult.IsMatch)
            {
                if (verificationResult.MatchedVoterId.HasValue)
                {
                    _currentVoterId = verificationResult.MatchedVoterId.Value;
                }

                Console.WriteLine("[AuthenticateUserViewModel] ✓ FINGERPRINT VERIFIED - Authentication successful");
            }
            else
            {
                Console.WriteLine($"[AuthenticateUserViewModel] ❌ FINGERPRINT DOES NOT MATCH - Score {verificationResult.Score} below threshold {verificationResult.Threshold}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthenticateUserViewModel] ❌ Error verifying fingerprint: {ex.Message}");
            Console.WriteLine($"[AuthenticateUserViewModel] Stack: {ex.StackTrace}");
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

    private byte[]? ConvertGrayscaleToImageData(byte[] grayscaleData, uint width, uint height)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.WriteLine("[AuthenticateUserViewModel] ❌ Grayscale to PNG conversion is Windows only");
                return null;
            }

            Console.WriteLine($"[AuthenticateUserViewModel] Converting grayscale data to PNG ({width}x{height})...");

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
                    Console.WriteLine($"[AuthenticateUserViewModel] ✓ Converted to PNG: {pngData.Length} bytes");
                    return pngData;
                }
            }
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthenticateUserViewModel] ❌ Error converting grayscale to PNG: {ex.Message}");
            Console.WriteLine($"[AuthenticateUserViewModel] Stack: {ex.StackTrace}");
            return null;
        }
    }

    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public AuthenticateUserViewModel(INavigationService navigationService, IScannerService scannerService, IServerHandler serverHandler, BallotPaperViewModel ballotPaperViewModel, DeviceLockState deviceLockState)
    {
        _navigationService = navigationService;
        _scannerService = scannerService;
        _serverHandler = serverHandler;
        _ballotPaperViewModel = ballotPaperViewModel;
        _deviceLockState = deviceLockState;
        _deviceLockState.LockStateChanged += OnLockStateChanged;
        
        // Initialize with default fingerprint image
        ImageSource = LoadImage("fingerPrint.png");
        PreviewImage = null;
        QualityScore = 0;
        IsCapturing = false;
        CaptureStatusMessage = "Ready to scan";
        IsBackEnabled = !_deviceLockState.IsLocked;
        IsStartScanningEnabled = !_deviceLockState.IsLocked && !IsCapturing;
        
    }

    private void OnLockStateChanged(bool isLocked)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsBackEnabled = !isLocked;
            IsStartScanningEnabled = !isLocked && !IsCapturing;

            if (isLocked)
            {
                StatusMessage = "Device locked. Authentication controls are disabled until an official unlocks this device.";
                _cooldownCts?.Cancel();
                CleanupCapture(closeDevice: true);
            }
            else
            {
                scannAttempts = 0;
                validFingerPrintScan = false;
                _lockDueToAlreadyVoted = false;

                if (StatusMessage.Contains("Device locked", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = "Device unlocked. Authentication controls are enabled.";
                }

                if (_scannerSessionActive)
                {
                    _ = StartScanningInternalAsync();
                }
            }
        });
    }

    partial void OnIsCapturingChanged(bool value)
    {
        IsStartScanningEnabled = !_deviceLockState.IsLocked && !value;
    }

    public void ApplyPendingLookup()
    {
        if (_navigationService is NavigationService navService && navService.PendingVoterLookup != null)
        {
            Initialize(navService.PendingVoterLookup);
            navService.PendingVoterLookup = null; // Clear after use
        }
    }

    // ==========================================
    // INITIALIZATION METHODS
    // ==========================================

    public void Initialize(VoterAuthLookupResponse lookup)
    {
        if (lookup == null)
        {
            Console.WriteLine("[AuthenticateUserViewModel] ❌ Invalid lookup data");
            return;
        }

        var hasSingleVoterId = lookup.VoterId.HasValue;
        var hasCandidates = lookup.RequiresDisambiguation && lookup.CandidateVoterIds?.Count > 0;
        if (!hasSingleVoterId && !hasCandidates)
        {
            Console.WriteLine("[AuthenticateUserViewModel] ❌ Invalid lookup data - missing voter ID and candidate IDs");
            return;
        }

        ResetAuthenticationState();

        _storedFingerprintBytes = lookup.FingerprintScan;
        _currentVoterId = lookup.VoterId;
        _candidateVoterIds = hasCandidates
            ? lookup.CandidateVoterIds!.Select(id => id.ToString()).ToList()
            : null;
        VoterFullName = lookup.FullName ?? "Identity protected";

        Console.WriteLine($"[AuthenticateUserViewModel] ✓ Initialized with voter: {VoterFullName}");
        Console.WriteLine($"[AuthenticateUserViewModel]   Voter ID: {_currentVoterId}");
        Console.WriteLine($"[AuthenticateUserViewModel]   Candidate IDs: {_candidateVoterIds?.Count ?? 0}");
        Console.WriteLine($"[AuthenticateUserViewModel]   Fingerprint available: {(_storedFingerprintBytes?.Length ?? 0) > 0}");
    }

    // ==========================================
    // COMMANDS
    // ==========================================

    [RelayCommand]
    private async Task StartScanning()
    {
        await ActivateScannerAsync();
    }

    public async Task ActivateScannerAsync()
    {
        if (_scannerSessionActive)
        {
            return;
        }

        _scannerSessionActive = true;
        _isProcessingFingerprint = false;
        _cooldownCts?.Cancel();
        _cooldownCts = null;
        IsCooldownActive = false;
        CooldownSecondsRemaining = 0;

        ApplyPendingLookup();
        await StartScanningInternalAsync();
    }

    public void DeactivateScanner()
    {
        _scannerSessionActive = false;
        _isProcessingFingerprint = false;
        _cooldownCts?.Cancel();
        _cooldownCts = null;
        IsCooldownActive = false;
        CooldownSecondsRemaining = 0;
        CleanupCapture(closeDevice: true);
    }

    private async Task StartScanningInternalAsync()
    {
        Console.WriteLine("[AuthenticateUserViewModel] Start scanning command triggered");

        if (_deviceLockState.IsLocked)
        {
            CaptureStatusMessage = "Device locked by official. Wait for unlock.";
            return;
        }
        
        try
        {
            if (IsCapturing)
            {
                Console.WriteLine("[AuthenticateUserViewModel] Capture already in progress");
                return;
            }

            // Open device
            if (!_scannerService.OpenDevice(0))
            {
                CaptureStatusMessage = "Failed to open scanner device";
                Console.WriteLine("[AuthenticateUserViewModel] ❌ Failed to open device");
                _serverHandler.CurrentDeviceStatus = "Scanner not connected";
                await _serverHandler.SendDeviceStatusAsync(_serverHandler.CurrentDeviceStatus);
                return;
            }

            Console.WriteLine("[AuthenticateUserViewModel] ✓ Device opened");

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
                Console.WriteLine("[AuthenticateUserViewModel] ❌ Failed to start capture");
                _serverHandler.CurrentDeviceStatus = "Scanner capture failed";
                await _serverHandler.SendDeviceStatusAsync(_serverHandler.CurrentDeviceStatus);
                CleanupCapture(closeDevice: true);
                return;
            }

            IsCapturing = true;
            CaptureStatusMessage = "Place finger on scanner...";
            QualityScore = 0;
            Console.WriteLine("[AuthenticateUserViewModel] ✓ Capture started");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthenticateUserViewModel] ❌ Error starting scan: {ex.Message}");
            CaptureStatusMessage = $"Error: {ex.Message}";
            IsCapturing = false;
        }
    }

    private void OnPreviewImageAvailable(object? sender, ScannerEventArgs args)
    {
        try
        {
            Console.WriteLine($"[AuthenticateUserViewModel] Preview image received: {args.Width}x{args.Height}, Quality: {args.QualityScore}%, ImageData: {(args.ImageData != null ? args.ImageData.Length : 0)} bytes");
            
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
                    statusMessage = "Hold steady while scanner improves capture...";
                }
                else
                {
                    statusMessage = "Fingerprint accepted";
                }
                
                CaptureStatusMessage = statusMessage;

                // Convert and display preview
                if (args.ImageData != null && args.ImageData.Length > 0)
                {
                    Console.WriteLine($"[AuthenticateUserViewModel] Converting image data to bitmap...");
                    Bitmap? convertedBitmap = ConvertBytesToBitmap(args.ImageData, args.Width, args.Height);
                    PreviewImage = convertedBitmap;
                    Console.WriteLine($"[AuthenticateUserViewModel] ✓ PreviewImage updated (Bitmap: {(PreviewImage != null ? "valid" : "null")})");
                }
                else
                {
                    Console.WriteLine($"[AuthenticateUserViewModel] ⚠️ No image data to display");
                }
            }, DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthenticateUserViewModel] ❌ Error in preview handler: {ex.Message}");
            Console.WriteLine($"[AuthenticateUserViewModel] Stack trace: {ex.StackTrace}");
        }
    }

    private async void OnFingerprintCaptured(object? sender, ScannerEventArgs args)
    {
        if (_isProcessingFingerprint)
        {
            return;
        }

        _isProcessingFingerprint = true;

        try
        {
            Console.WriteLine($"[AuthenticateUserViewModel] Fingerprint captured: Success={args.IsSuccess}, Quality={args.QualityScore}");

            // Store fingerprint data immediately (thread-safe operation) - stored in memory only for security/privacy
            if (args.IsSuccess)
            {
                _capturedFingerprintData = args.ImageData;      // Raw image for display
            }

            // Update UI on the main thread
            _ = Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (args.IsSuccess)
                {
                    CaptureStatusMessage = "Fingerprint captured. Comparing...";
                    Console.WriteLine("[AuthenticateUserViewModel] ✓ Fingerprint data saved - starting comparison");
                }
                else
                {
                    CaptureStatusMessage = "Capture failed or incomplete";
                    Console.WriteLine("[AuthenticateUserViewModel] ❌ Capture was not successful");
                }
            }, DispatcherPriority.Input);

            CleanupCapture(closeDevice: false);

            if (args.IsSuccess)
            {
                await CompareFingerprints();
                await Task.Delay(1000);
                scannAttempts++;

                if (_navigationService != null)
                {
                    _ = Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await attemptHandler(scannAttempts, validFingerPrintScan);
                    }, DispatcherPriority.Input);
                }
                else
                {
                    Console.WriteLine("[AuthenticateUserViewModel] ❌ ERROR: NavigationService is null");
                }
            }

            if (_scannerSessionActive && !_deviceLockState.IsLocked && !validFingerPrintScan && scannAttempts < 3)
            {
                await StartCooldownAndResumeAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthenticateUserViewModel] ❌ Error in capture handler: {ex.Message}");
            Console.WriteLine($"[AuthenticateUserViewModel] Stack: {ex.StackTrace}");
            CleanupCapture(closeDevice: false);
        }
        finally
        {
            _isProcessingFingerprint = false;
        }
    }

    private void OnScannerError(object? sender, string errorMessage)
    {
        Console.WriteLine($"[AuthenticateUserViewModel] ⚠️ Scanner error: {errorMessage}");
        
        // Update UI on the main thread
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CaptureStatusMessage = $"Error: {errorMessage}";
            if (errorMessage.Contains("not connected", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("device", StringComparison.OrdinalIgnoreCase))
            {
                _serverHandler.CurrentDeviceStatus = "Scanner not connected";
                _ = _serverHandler.SendDeviceStatusAsync(_serverHandler.CurrentDeviceStatus);
            }
            CleanupCapture(closeDevice: false);
        }, DispatcherPriority.Input);
    }

    private async Task StartCooldownAndResumeAsync()
    {
        _cooldownCts?.Cancel();
        _cooldownCts = new CancellationTokenSource();
        var token = _cooldownCts.Token;

        IsCooldownActive = true;

        try
        {
            for (int i = SCANNER_COOLDOWN_SECONDS; i >= 1; i--)
            {
                CooldownSecondsRemaining = i;
                CaptureStatusMessage = $"Scanner cooldown: {i}s";
                await Task.Delay(1000, token);
            }

            if (!_scannerSessionActive || _deviceLockState.IsLocked || token.IsCancellationRequested)
            {
                return;
            }

            IsCooldownActive = false;
            CooldownSecondsRemaining = 0;
            CaptureStatusMessage = "Place finger on scanner...";
            await StartScanningInternalAsync();
        }
        catch (TaskCanceledException)
        {
        }
        finally
        {
            IsCooldownActive = false;
            CooldownSecondsRemaining = 0;
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
                Console.WriteLine($"[AuthenticateUserViewModel] ⚠️ StopCapture access violation (device may be in bad state): {ex.Message}");
            }

            if (closeDevice)
            {
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
                    Console.WriteLine($"[AuthenticateUserViewModel] ⚠️ CloseDevice access violation: {ex.Message}");
                }
            }

            // Update UI on main thread
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsCapturing = false;
            }, DispatcherPriority.Input);
            
            Console.WriteLine("[AuthenticateUserViewModel] ✓ Capture cleaned up");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthenticateUserViewModel] ⚠️ Error during cleanup: {ex.Message}");
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

            Console.WriteLine($"[AuthenticateUserViewModel] Converting {width}x{height} grayscale image to bitmap ({imageData.Length} bytes)");

            // Convert grayscale to ARGB and copy into bitmap buffer
            using (var buffer = bitmap.Lock())
            {
                Console.WriteLine($"[AuthenticateUserViewModel] Bitmap locked, stride: {buffer.RowBytes}");
                
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
                
                Console.WriteLine($"[AuthenticateUserViewModel] ✓ Bitmap data copied and inverted ({(int)width}x{(int)height})");
            }

            Console.WriteLine($"[AuthenticateUserViewModel] ✓ Bitmap conversion complete");
            return bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthenticateUserViewModel] ❌ Error converting bytes to bitmap: {ex.Message}");
            Console.WriteLine($"[AuthenticateUserViewModel] Stack trace: {ex.StackTrace}");
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
        if (_deviceLockState.IsLocked)
        {
            return;
        }

        DeactivateScanner();
        _navigationService.NavigateToMain();
    }

    [RelayCommand]
    private void SignOut()
    {
        DeactivateScanner();
        _serverHandler.Logout();
        _navigationService.NavigateToMain();
    }
}
