using System;
using System.Diagnostics;

namespace SecureVoteApp.Services.Scanner
{
    // Scanner service implementation for fingerprint capture
    public class ScannerService : IScannerService
    {
        #region Fields

        private int _deviceHandle = -1;
        private bool _isDeviceOpen = false;
        private bool _isCaptureActive = false;
        private bool _isSpoofDetected = false;
        private bool _disposed = false;

        // Store callback delegates to prevent garbage collection
        private IBScanUltimateWrapper.PreviewImageCallback _previewCallback;
        private IBScanUltimateWrapper.ResultImageCallback _resultCallback;
        private IBScanUltimateWrapper.DeviceCountCallback _deviceCountCallback;
        private IBScanUltimateWrapper.FingerQualityCallback _fingerQualityCallback;

        #endregion

        #region Events

        public event EventHandler<ScannerEventArgs>? PreviewImageAvailable;
        public event EventHandler<ScannerEventArgs>? FingerprintCaptured;
        public event EventHandler<string>? ErrorOccurred;

        #endregion

        #region Constructor

        public ScannerService()
        {
            // Initialize callback delegates
            _previewCallback = OnPreviewImageAvailable;
            _resultCallback = OnResultImageAvailable;
            _deviceCountCallback = OnDeviceCountChanged;
            _fingerQualityCallback = OnFingerQualityUpdate;

            // Register callbacks with the SDK
            int result = IBScanUltimateWrapper.IBSU_RegisterCallbacks(
                IBScanUltimateWrapper.PREVIEW_IMAGE_EVENT,
                _previewCallback,
                _resultCallback,
                _deviceCountCallback,
                _fingerQualityCallback,
                IntPtr.Zero);

            if (!IBScanUltimateWrapper.IsSuccess(result))
            {
                RaiseError($"Failed to register SDK callbacks. Result: {result}");
            }

            Debug.WriteLine("ScannerService initialized");
        }

        #endregion

        #region Device Management

        public int GetDeviceCount()
        {
            try
            {
                int count = IBScanUltimateWrapper.IBSU_GetDeviceCount();
                Debug.WriteLine($"Scanner device count: {count}");
                return count;
            }
            catch (Exception ex)
            {
                RaiseError($"Error getting device count: {ex.Message}");
                return 0;
            }
        }

        public string GetDeviceDescription(int deviceIndex)
        {
            try
            {
                int result = IBScanUltimateWrapper.IBSU_GetDeviceDescription(
                    deviceIndex,
                    out IBScanUltimateWrapper.IBSU_DeviceDescription description);

                if (IBScanUltimateWrapper.IsSuccess(result))
                {
                    return $"{description.ProductName} (SN: {description.SerialNumber}, FW: {description.FirmwareVersion})";
                }
                else
                {
                    RaiseError($"Failed to get device {deviceIndex} description. Result: {result}");
                    return $"Device {deviceIndex} (Unknown)";
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error getting device description: {ex.Message}");
                return $"Device {deviceIndex} (Error)";
            }
        }

        public bool OpenDevice(int deviceIndex)
        {
            try
            {
                // Close existing device if any
                if (_isDeviceOpen)
                {
                    CloseDevice();
                }

                int handle = 0;
                int result = IBScanUltimateWrapper.IBSU_OpenDevice(deviceIndex, ref handle);

                if (IBScanUltimateWrapper.IsSuccess(result))
                {
                    _deviceHandle = handle;
                    _isDeviceOpen = true;
                    Debug.WriteLine($"Device {deviceIndex} opened successfully. Handle: {handle}");
                    return true;
                }
                else
                {
                    RaiseError($"Failed to open device {deviceIndex}. Result: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error opening device: {ex.Message}");
                return false;
            }
        }

        public bool CloseDevice()
        {
            if (!_isDeviceOpen)
                return true;

            try
            {
                // Stop capture if active
                if (_isCaptureActive)
                {
                    StopCapture();
                }

                int result = IBScanUltimateWrapper.IBSU_CloseDevice(_deviceHandle);

                if (IBScanUltimateWrapper.IsSuccess(result))
                {
                    _isDeviceOpen = false;
                    _deviceHandle = -1;
                    _isCaptureActive = false;
                    Debug.WriteLine("Device closed successfully");
                    return true;
                }
                else
                {
                    RaiseError($"Failed to close device. Result: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error closing device: {ex.Message}");
                return false;
            }
        }

        public bool IsDeviceOpen()
        {
            if (!_isDeviceOpen)
                return false;

            try
            {
                bool isOpen = IBScanUltimateWrapper.IBSU_IsDeviceOpened(_deviceHandle);
                return isOpen;
            }
            catch (Exception ex)
            {
                RaiseError($"Error checking device status: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Capture Control

        public bool StartCapture(int imageType = IBScanUltimateWrapper.FLAT_FINGERPRINT)
        {
            if (!_isDeviceOpen)
            {
                RaiseError("No device is open. Call OpenDevice() first.");
                return false;
            }

            if (_isCaptureActive)
            {
                Debug.WriteLine("Capture is already active");
                return true;
            }

            try
            {
                int result = IBScanUltimateWrapper.IBSU_BeginCaptureImage(
                    _deviceHandle,
                    imageType,
                    IBScanUltimateWrapper.IMAGE_RESOLUTION_500,
                    IBScanUltimateWrapper.AUTO_CAPTURE_ENABLED | IBScanUltimateWrapper.QUALITY_CHECK_ENABLED);

                if (IBScanUltimateWrapper.IsSuccess(result))
                {
                    _isCaptureActive = true;
                    Debug.WriteLine("Fingerprint capture started");
                    return true;
                }
                else
                {
                    RaiseError($"Failed to start capture. Result: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error starting capture: {ex.Message}");
                return false;
            }
        }

        public bool StopCapture()
        {
            if (!_isCaptureActive)
                return true;

            try
            {
                int result = IBScanUltimateWrapper.IBSU_CancelCaptureImage(_deviceHandle);

                if (IBScanUltimateWrapper.IsSuccess(result))
                {
                    _isCaptureActive = false;
                    Debug.WriteLine("Fingerprint capture stopped");
                    return true;
                }
                else
                {
                    RaiseError($"Failed to stop capture. Result: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error stopping capture: {ex.Message}");
                return false;
            }
        }

        public bool IsCaptureActive()
        {
            if (!_isDeviceOpen)
                return false;

            try
            {
                bool isActive = IBScanUltimateWrapper.IBSU_IsCaptureActive(_deviceHandle);
                return isActive;
            }
            catch (Exception ex)
            {
                RaiseError($"Error checking capture status: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Spoof Detection

        public bool SetSpoofDetection(bool enabled, int spoofSensitivity = 5)
        {
            if (!_isDeviceOpen)
            {
                RaiseError("No device is open. Call OpenDevice() first.");
                return false;
            }

            try
            {
                // Property ID for spoof detection is typically property 0x0024
                const int SPOOF_DETECTION_PROPERTY_ID = 0x0024;

                int result = IBScanUltimateWrapper.IBSU_SetPropertyInt(
                    _deviceHandle,
                    SPOOF_DETECTION_PROPERTY_ID,
                    enabled ? spoofSensitivity : 0);

                if (IBScanUltimateWrapper.IsSuccess(result))
                {
                    Debug.WriteLine($"Spoof detection set to: {(enabled ? "enabled" : "disabled")}");
                    return true;
                }
                else
                {
                    RaiseError($"Failed to set spoof detection. Result: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error setting spoof detection: {ex.Message}");
                return false;
            }
        }

        public bool IsSpoofFingerDetected()
        {
            if (!_isDeviceOpen)
                return false;

            try
            {
                bool isSpoofed = IBScanUltimateWrapper.IBSU_IsSpoofFingerDetected(_deviceHandle);
                return isSpoofed;
            }
            catch (Exception ex)
            {
                RaiseError($"Error checking spoof detection: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Callbacks

        private void OnPreviewImageAvailable(
            int deviceHandle,
            IBScanUltimateWrapper.IBSU_ImageData imageData,
            IntPtr pContext)
        {
            try
            {
                byte[] imageBuffer = IBScanUltimateWrapper.MarshalImageBuffer(
                    imageData.Buffer,
                    (int)imageData.Width,
                    (int)imageData.Height,
                    imageData.BitsPerPixel);

                if (imageBuffer != null)
                {
                    var args = new ScannerEventArgs
                    {
                        ImageData = imageBuffer,
                        Width = imageData.Width,
                        Height = imageData.Height,
                        ResolutionX = imageData.ResolutionX,
                        ResolutionY = imageData.ResolutionY,
                        BitsPerPixel = imageData.BitsPerPixel,
                        QualityScore = IBScanUltimateWrapper.IBSU_GetNFIQScore(deviceHandle),
                        IsFinalImage = imageData.IsFinal,
                        IsSuccess = true
                    };

                    PreviewImageAvailable?.Invoke(this, args);
                    Debug.WriteLine($"Preview image received: {imageData.Width}x{imageData.Height} (Quality: {args.QualityScore})");
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error in preview callback: {ex.Message}");
            }
        }

        private void OnResultImageAvailable(
            int deviceHandle,
            IBScanUltimateWrapper.IBSU_ImageData imageData,
            IntPtr pContext)
        {
            try
            {
                byte[] imageBuffer = IBScanUltimateWrapper.MarshalImageBuffer(
                    imageData.Buffer,
                    (int)imageData.Width,
                    (int)imageData.Height,
                    imageData.BitsPerPixel);

                if (imageBuffer != null)
                {
                    _isSpoofDetected = IBScanUltimateWrapper.IBSU_IsSpoofFingerDetected(deviceHandle);

                    var args = new ScannerEventArgs
                    {
                        ImageData = imageBuffer,
                        Width = imageData.Width,
                        Height = imageData.Height,
                        ResolutionX = imageData.ResolutionX,
                        ResolutionY = imageData.ResolutionY,
                        BitsPerPixel = imageData.BitsPerPixel,
                        QualityScore = IBScanUltimateWrapper.IBSU_GetNFIQScore(deviceHandle),
                        IsFinalImage = imageData.IsFinal,
                        IsSuccess = !_isSpoofDetected
                    };

                    FingerprintCaptured?.Invoke(this, args);
                    _isCaptureActive = false;
                    Debug.WriteLine($"Result image received: {imageData.Width}x{imageData.Height} (Spoof: {_isSpoofDetected})");
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error in result callback: {ex.Message}");
            }
        }

        private void OnDeviceCountChanged(int deviceCount, IntPtr pContext)
        {
            Debug.WriteLine($"Device count changed: {deviceCount}");
        }

        private void OnFingerQualityUpdate(int deviceHandle, int fingerQuality, IntPtr pContext)
        {
            Debug.WriteLine($"Finger quality update: {fingerQuality}");
        }

        #endregion

        #region Error Handling

        private void RaiseError(string errorMessage)
        {
            Debug.WriteLine($"[ScannerService Error] {errorMessage}");
            ErrorOccurred?.Invoke(this, errorMessage);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Close device if open
                if (_isDeviceOpen)
                {
                    CloseDevice();
                }

                Debug.WriteLine("ScannerService disposed");
            }

            _disposed = true;
        }

        ~ScannerService()
        {
            Dispose(false);
        }

        #endregion
    }
}
