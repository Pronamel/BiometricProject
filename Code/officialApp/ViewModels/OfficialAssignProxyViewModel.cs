using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using officialApp.Services;
using officialApp.Services.Scanner;

namespace officialApp.ViewModels;

public partial class OfficialAssignProxyViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IServerHandler _serverHandler;
    private readonly IScannerService _scannerService;

    [ObservableProperty]
    private string representedFirstName = string.Empty;

    [ObservableProperty]
    private string representedLastName = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? representedDateOfBirth;

    [ObservableProperty]
    private string representedPostCode = string.Empty;

    [ObservableProperty]
    private string representedTownOfBirth = string.Empty;

    [ObservableProperty]
    private string proxyFirstName = string.Empty;

    [ObservableProperty]
    private string proxyLastName = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? proxyDateOfBirth;

    [ObservableProperty]
    private string proxyPostCode = string.Empty;

    [ObservableProperty]
    private string proxyTownOfBirth = string.Empty;

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

    private byte[]? _capturedFingerprintData;
    private uint _capturedFingerprintWidth;
    private uint _capturedFingerprintHeight;
    private const int QUALITY_THRESHOLD = 10;

    public OfficialAssignProxyViewModel(
        INavigationService navigationService,
        IServerHandler serverHandler,
        IScannerService scannerService)
    {
        _navigationService = navigationService;
        _serverHandler = serverHandler;
        _scannerService = scannerService;
        CheckScannerConnectivity();
    }

    public void ResetForm()
    {
        if (IsCapturing)
        {
            CleanupCapture();
        }

        RepresentedFirstName = string.Empty;
        RepresentedLastName = string.Empty;
        RepresentedDateOfBirth = null;
        RepresentedPostCode = string.Empty;
        RepresentedTownOfBirth = string.Empty;
        ProxyFirstName = string.Empty;
        ProxyLastName = string.Empty;
        ProxyDateOfBirth = null;
        ProxyPostCode = string.Empty;
        ProxyTownOfBirth = string.Empty;
        StatusMessage = string.Empty;
        StatusColor = "black";
        _capturedFingerprintData = null;
        PreviewImage = null;
        QualityScore = 0;
        CaptureStatusMessage = "Ready to scan";
    }

    [RelayCommand]
    private void Back()
    {
        _navigationService.NavigateToOfficialMenu();
    }

    [RelayCommand]
    private void StartScanning()
    {
        try
        {
            if (IsCapturing)
            {
                return;
            }

            if (!_scannerService.OpenDevice(0))
            {
                CaptureStatusMessage = "Failed to open scanner device";
                return;
            }

            _scannerService.PreviewImageAvailable += OnPreviewImageAvailable;
            _scannerService.FingerprintCaptured += OnFingerprintCaptured;
            _scannerService.ErrorOccurred += OnScannerError;

            if (!_scannerService.StartCapture())
            {
                CaptureStatusMessage = "Failed to start capture";
                CleanupCapture();
                return;
            }

            _capturedFingerprintData = null;
            _capturedFingerprintWidth = 0;
            _capturedFingerprintHeight = 0;
            IsCapturing = true;
            CaptureStatusMessage = "Place the represented voter finger on the scanner...";
            QualityScore = 0;
        }
        catch (Exception ex)
        {
            CaptureStatusMessage = $"Error: {ex.Message}";
            IsCapturing = false;
        }
    }

    [RelayCommand]
    private async Task AssignProxy()
    {
        if (_capturedFingerprintData == null || _capturedFingerprintData.Length == 0)
        {
            StatusMessage = "Please scan the represented voter fingerprint first";
            StatusColor = "#e74c3c";
            return;
        }

        if (_capturedFingerprintWidth == 0 || _capturedFingerprintHeight == 0)
        {
            StatusMessage = "Captured fingerprint metadata is incomplete. Please rescan represented voter.";
            StatusColor = "#e74c3c";
            return;
        }

        string? representedDob = RepresentedDateOfBirth?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string? proxyDob = ProxyDateOfBirth?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (string.IsNullOrWhiteSpace(RepresentedFirstName) ||
            string.IsNullOrWhiteSpace(RepresentedLastName) ||
            string.IsNullOrWhiteSpace(representedDob) ||
            string.IsNullOrWhiteSpace(RepresentedPostCode) ||
            string.IsNullOrWhiteSpace(RepresentedTownOfBirth) ||
            string.IsNullOrWhiteSpace(ProxyFirstName) ||
            string.IsNullOrWhiteSpace(ProxyLastName) ||
            string.IsNullOrWhiteSpace(proxyDob) ||
            string.IsNullOrWhiteSpace(ProxyPostCode) ||
            string.IsNullOrWhiteSpace(ProxyTownOfBirth))
        {
            StatusMessage = "Please complete both voter identity panels";
            StatusColor = "#e74c3c";
            return;
        }

        try
        {
            StatusMessage = "Assigning proxy voter...";
            StatusColor = "#3498db";

            var pngFingerprintData = ConvertGrayscaleToPngBytes(
                _capturedFingerprintData,
                _capturedFingerprintWidth,
                _capturedFingerprintHeight);

            if (pngFingerprintData.Length == 0)
            {
                StatusMessage = "Could not encode captured fingerprint. Please rescan represented voter.";
                StatusColor = "#e74c3c";
                return;
            }

            var result = await _serverHandler.AssignProxyVoterAsync(
                RepresentedFirstName,
                RepresentedLastName,
                representedDob!,
                RepresentedPostCode,
                RepresentedTownOfBirth,
                ProxyFirstName,
                ProxyLastName,
                proxyDob!,
                ProxyPostCode,
                ProxyTownOfBirth,
                pngFingerprintData);

            if (result?.Success == true)
            {
                StatusMessage = result.Message;
                StatusColor = "#27ae60";
                ClearAssignmentFormFields();
                CaptureStatusMessage = "Proxy assignment complete. Capture cleared.";
            }
            else
            {
                StatusMessage = result?.Message ?? "Proxy assignment failed";
                StatusColor = "#e74c3c";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            StatusColor = "#e74c3c";
        }
    }

    private void ClearAssignmentFormFields()
    {
        RepresentedFirstName = string.Empty;
        RepresentedLastName = string.Empty;
        RepresentedDateOfBirth = null;
        RepresentedPostCode = string.Empty;
        RepresentedTownOfBirth = string.Empty;

        ProxyFirstName = string.Empty;
        ProxyLastName = string.Empty;
        ProxyDateOfBirth = null;
        ProxyPostCode = string.Empty;
        ProxyTownOfBirth = string.Empty;

        _capturedFingerprintData = null;
        _capturedFingerprintWidth = 0;
        _capturedFingerprintHeight = 0;
        PreviewImage = null;
        QualityScore = 0;
    }

    public void CheckScannerConnectivity()
    {
        try
        {
            int deviceCount = _scannerService.GetDeviceCount();

            if (deviceCount > 0)
            {
                IsScannerConnected = true;
                DeviceStatus = $"Scanner Connected: {_scannerService.GetDeviceDescription(0)}";
            }
            else
            {
                IsScannerConnected = false;
                DeviceStatus = "No scanner device detected";
            }
        }
        catch (Exception ex)
        {
            IsScannerConnected = false;
            DeviceStatus = $"Error checking scanner: {ex.Message}";
        }
    }

    private void OnPreviewImageAvailable(object? sender, ScannerEventArgs args)
    {
        try
        {
            _capturedFingerprintWidth = args.Width;
            _capturedFingerprintHeight = args.Height;

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                QualityScore = args.QualityScore;
                CaptureStatusMessage = args.QualityScore == 0
                    ? "Scanning... Place the represented voter finger on the scanner"
                    : args.QualityScore < QUALITY_THRESHOLD
                        ? "Hold steady while the scanner improves the capture..."
                        : "Fingerprint accepted";

                if (args.ImageData != null && args.ImageData.Length > 0)
                {
                    PreviewImage = ConvertBytesToBitmap(args.ImageData, args.Width, args.Height);
                }
            }, DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAssignProxyViewModel] Preview error: {ex.Message}");
        }
    }

    private void OnFingerprintCaptured(object? sender, ScannerEventArgs args)
    {
        try
        {
            if (args.IsSuccess)
            {
                _capturedFingerprintData = args.ImageData;
                _capturedFingerprintWidth = args.Width;
                _capturedFingerprintHeight = args.Height;
            }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                CaptureStatusMessage = args.IsSuccess
                    ? "Fingerprint captured"
                    : "Capture failed or incomplete";

                CleanupCapture();
            }, DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAssignProxyViewModel] Capture handler error: {ex.Message}");
            CleanupCapture();
        }
    }

    private void OnScannerError(object? sender, string errorMessage)
    {
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
                Console.WriteLine($"[OfficialAssignProxyViewModel] StopCapture access violation: {ex.Message}");
            }

            try
            {
                _scannerService.CloseDevice();
            }
            catch (AccessViolationException ex)
            {
                Console.WriteLine($"[OfficialAssignProxyViewModel] CloseDevice access violation: {ex.Message}");
            }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsCapturing = false;
            }, DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OfficialAssignProxyViewModel] Cleanup error: {ex.Message}");
            IsCapturing = false;
        }
    }

    private Bitmap ConvertBytesToBitmap(byte[] imageData, uint width, uint height)
    {
        var pixelSize = new PixelSize((int)width, (int)height);
        var bitmap = new WriteableBitmap(
            pixelSize,
            new Vector(96, 96),
            PixelFormat.Rgba8888);

        using var buffer = bitmap.Lock();
        const int bytesPerPixel = 4;
        IntPtr bufferPtr = buffer.Address;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte grayValue = imageData[y * (int)width + x];
                byte invertedValue = (byte)(255 - grayValue);
                int pixelOffset = y * buffer.RowBytes + x * bytesPerPixel;

                Marshal.WriteByte(bufferPtr, pixelOffset + 0, invertedValue);
                Marshal.WriteByte(bufferPtr, pixelOffset + 1, invertedValue);
                Marshal.WriteByte(bufferPtr, pixelOffset + 2, invertedValue);
                Marshal.WriteByte(bufferPtr, pixelOffset + 3, 255);
            }
        }

        return bitmap;
    }

    private byte[] ConvertGrayscaleToPngBytes(byte[] grayscaleData, uint width, uint height)
    {
        try
        {
            if (width == 0 || height == 0)
            {
                return Array.Empty<byte>();
            }

            if (grayscaleData.Length < (int)(width * height))
            {
                return Array.Empty<byte>();
            }

            if (!OperatingSystem.IsWindows())
            {
                return grayscaleData;
            }

#pragma warning disable CA1416
            using var bitmap = new System.Drawing.Bitmap((int)width, (int)height, System.Drawing.Imaging.PixelFormat.Format8bppIndexed);
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
                Marshal.Copy(grayscaleData, 0, bitmapData.Scan0, grayscaleData.Length);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
#pragma warning restore CA1416
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }
}