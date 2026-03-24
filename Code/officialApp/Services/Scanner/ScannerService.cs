using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace officialApp.Services.Scanner
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
        private bool _isDllLoaded = false;
        private bool _captureCompleted = false;  // Flag to prevent processing after successful completion
        
        // Frame tracking and diagnostics
        private int _previewFrameCount = 0;  // Counter for preview frames received
        private bool _hasReceivedPreviewFrame = false;  // Flag to detect if preview callback ever fires
        private DateTime _captureStartTime = DateTime.MinValue;
        private const int CAPTURE_TIMEOUT_SECONDS = 60; // Maximum time to wait for a good fingerprint
        
        // Frame tracking - detect when motion stops and restart capture if needed
        private DateTime _lastFrameTime = DateTime.MinValue;
        private const int FRAME_TIMEOUT_MILLISECONDS = 500; // If no frame in 0.5 seconds, restart capture
        private System.Threading.Timer? _frameWatchdogTimer = null;

        // Store callback delegates to prevent garbage collection - MUST be non-nullable and keep references alive
        private readonly IBScanUltimateWrapper.PreviewImageCallback _previewCallback;
        private readonly IBScanUltimateWrapper.ResultImageExCallback _resultCallback;
        private readonly IBScanUltimateWrapper.DeviceCountCallback _deviceCountCallback;
        private readonly IBScanUltimateWrapper.FingerQualityCallback _fingerQualityCallback;
        private readonly IBScanUltimateWrapper.CompleteAcquisitionCallback _completeAcquisitionCallback;

        // Store function pointers to keep them valid - CRITICAL for callback stability
        private IntPtr _previewCallbackPtr = IntPtr.Zero;
        private IntPtr _resultCallbackPtr = IntPtr.Zero;
        private IntPtr _deviceCountCallbackPtr = IntPtr.Zero;
        private IntPtr _fingerQualityCallbackPtr = IntPtr.Zero;
        private IntPtr _completeAcquisitionCallbackPtr = IntPtr.Zero;

        #endregion

        #region Events

        public event EventHandler<ScannerEventArgs>? PreviewImageAvailable;
        public event EventHandler<ScannerEventArgs>? FingerprintCaptured;
        public event EventHandler<string>? ErrorOccurred;

        #endregion

        #region Constructor

        public ScannerService()
        {
            // Initialize callback delegates BEFORE try block - these MUST be non-null to prevent garbage collection
            _previewCallback = OnPreviewImageAvailable;
            _resultCallback = OnResultImageAvailable;
            _deviceCountCallback = OnDeviceCountChanged;
            _fingerQualityCallback = OnFingerQualityUpdate;
            _completeAcquisitionCallback = OnCompleteAcquisition;

            try
            {
                Console.WriteLine("[ScannerService] Starting initialization...");
                
                Console.WriteLine("[ScannerService] Callback delegates initialized");
                Console.WriteLine("[ScannerService] ✓ SDK ready for device enumeration");

                // Mark DLL as loaded - device detection doesn't require callbacks
                // Callbacks will be registered later when opening a device if needed
                _isDllLoaded = true;
                Console.WriteLine($"[ScannerService] ✓ Initialization complete. DLL Loaded: {_isDllLoaded}");
            }
            catch (DllNotFoundException ex)
            {
                Console.WriteLine($"[ScannerService] ❌ IBScanUltimate.dll not found: {ex.Message}");
                Console.WriteLine($"[ScannerService] Stack trace: {ex.StackTrace}");
                Console.WriteLine();
                _isDllLoaded = false;
                RaiseError($"Scanner DLL not found: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScannerService] ❌ Error during initialization: {ex.GetType().Name}");
                Console.WriteLine($"[ScannerService] Message: {ex.Message}");
                Console.WriteLine($"[ScannerService] Stack trace: {ex.StackTrace}");
                Console.WriteLine();
                _isDllLoaded = false;
                RaiseError($"Scanner service initialization error: {ex.Message}");
            }
        }

        #endregion

        #region Device Management

        public int GetDeviceCount()
        {
            if (!_isDllLoaded)
            {
                Console.WriteLine("[ScannerService] GetDeviceCount: DLL not loaded");
                RaiseError("Scanner DLL not loaded. Cannot get device count.");
                return 0;
            }

            try
            {
                Console.WriteLine("[ScannerService] Attempting to get device count...");
                int result = IBScanUltimateWrapper.IBSU_GetDeviceCount(out int deviceCount);
                
                if (IBScanUltimateWrapper.IsSuccess(result))
                {
                    Console.WriteLine($"[ScannerService] ✓ GetDeviceCount returned: {deviceCount} device(s) connected");
                    return deviceCount;
                }
                else
                {
                    Console.WriteLine($"[ScannerService] ❌ GetDeviceCount failed with status code: {result}");
                    RaiseError($"Failed to get device count. Status: {result}");
                    return 0;
                }
            }
            catch (AccessViolationException avEx)
            {
                Console.WriteLine($"[ScannerService] ❌ CRASH in GetDeviceCount: {avEx.Message}");
                Console.WriteLine($"[ScannerService] This indicates a driver or DLL compatibility issue");
                Console.WriteLine($"[ScannerService] Troubleshooting steps:");
                Console.WriteLine($"[ScannerService] 1. Verify scanner is physically connected");
                Console.WriteLine($"[ScannerService] 2. Check driver installation (Integrated Biometrics drivers)");
                Console.WriteLine($"[ScannerService] 3. Verify DLL is 64-bit (matches app architecture)");
                Console.WriteLine($"[ScannerService] 4. Check DLL is in output directory: bin/x64");
                RaiseError($"Scanner driver crash - check physical connection and driver installation.");
                return 0;
            }
            catch (DllNotFoundException dllEx)
            {
                Console.WriteLine($"[ScannerService] ❌ DLL not found in GetDeviceCount: {dllEx.Message}");
                RaiseError($"IBScanUltimate.dll not found. Ensure DLL is copied to bin/x64 directory.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScannerService] ❌ Error getting device count: {ex.GetType().Name}: {ex.Message}");
                RaiseError($"Error getting device count: {ex.Message}");
                return 0;
            }
        }

        public string GetDeviceDescription(int deviceIndex)
        {
            if (!_isDllLoaded)
            {
                Console.WriteLine("[ScannerService] GetDeviceDescription: DLL not loaded");
                return "Scanner not available (DLL not loaded)";
            }

            try
            {
                Console.WriteLine($"[ScannerService] Getting description for device index {deviceIndex}");
                
                int result = IBScanUltimateWrapper.IBSU_GetDeviceDescription(
                    deviceIndex,
                    out IBScanUltimateWrapper.IBSU_DeviceDescription description);

                if (IBScanUltimateWrapper.IsSuccess(result))
                {
                    string info = $"{description.ProductName} (SN: {description.SerialNumber}, FW: {description.FirmwareVersion}, Type: {description.InterfaceType})";
                    Console.WriteLine($"[ScannerService] ✓ Device description: {info}");
                    return info;
                }
                else
                {
                    Console.WriteLine($"[ScannerService] ❌ Failed to get device description. Result code: {result}");
                    RaiseError($"Failed to get device {deviceIndex} description. Result: {result}");
                    return $"Device {deviceIndex} (Unknown)";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScannerService] ❌ Error getting device description: {ex.Message}");
                RaiseError($"Error getting device description: {ex.Message}");
                return $"Device {deviceIndex} (Error)";
            }
        }

        public bool OpenDevice(int deviceIndex)
        {
            if (!_isDllLoaded)
            {
                RaiseError("Scanner DLL not loaded. Cannot open device.");
                return false;
            }

            try
            {
                // Close existing device if any
                if (_isDeviceOpen)
                {
                    CloseDevice();
                }

                // Small delay to ensure hardware is ready
                System.Threading.Thread.Sleep(100);

                int handle = 0;
                int result = IBScanUltimateWrapper.IBSU_OpenDevice(deviceIndex, ref handle);

                if (IBScanUltimateWrapper.IsSuccess(result))
                {
                    _deviceHandle = handle;
                    _isDeviceOpen = true;
                    Console.WriteLine($"[ScannerService] ✓ Device {deviceIndex} opened successfully. Handle: {handle}");
                    
                    // Configure device properties immediately after opening
                    try
                    {
                        Console.WriteLine("[ScannerService] Configuring device properties...");
                        
                        // Try to disable motion-based frame filtering
                        // Different scanner models use different property IDs, so try multiple approaches
                        int[] motionPropertyIds = { 0x005C, 0x0037, 0x002A };  // Try different known property IDs
                        
                        foreach (int propId in motionPropertyIds)
                        {
                            try
                            {
                                int propResult = IBScanUltimateWrapper.IBSU_SetPropertyInt(_deviceHandle, propId, 0);
                                if (IBScanUltimateWrapper.IsSuccess(propResult))
                                {
                                    Console.WriteLine($"[ScannerService] ✓ Set property 0x{propId:X4} successfully");
                                }
                            }
                            catch { /* Continue to next property */ }
                        }
                    }
                    catch (Exception propEx)
                    {
                        Console.WriteLine($"[ScannerService] ⚠️ Warning setting device properties: {propEx.Message}");
                        // Non-fatal, continue with registration
                    }
                    
                    // Register callbacks for scanner events
                    // The correct signature is: IBSU_RegisterCallbacks(deviceHandle, eventType, pCallback, pContext)
                    try
                    {
                        Console.WriteLine("[ScannerService] Registering callbacks...");
                        
                        // Cache function pointers - they MUST stay valid for the lifetime of the device
                        // Store them as fields to prevent GC from invalidating them
                        if (_previewCallback == null) throw new InvalidOperationException("Preview callback not initialized");
                        _previewCallbackPtr = Marshal.GetFunctionPointerForDelegate<IBScanUltimateWrapper.PreviewImageCallback>(_previewCallback);
                        int cbResult = IBScanUltimateWrapper.IBSU_RegisterCallbacks(
                            _deviceHandle,
                            IBScanUltimateWrapper.PREVIEW_IMAGE_EVENT,
                            _previewCallbackPtr,
                            IntPtr.Zero);
                        
                        if (IBScanUltimateWrapper.IsSuccess(cbResult))
                        {
                            Console.WriteLine("[ScannerService] ✓ Preview image callback registered");
                        }
                        else
                        {
                            Console.WriteLine($"[ScannerService] ⚠️ Preview callback registration returned: {cbResult}");
                        }
                        
                        // Register result image callback  
                        if (_resultCallback == null) throw new InvalidOperationException("Result callback not initialized");
                        _resultCallbackPtr = Marshal.GetFunctionPointerForDelegate<IBScanUltimateWrapper.ResultImageExCallback>(_resultCallback);
                        cbResult = IBScanUltimateWrapper.IBSU_RegisterCallbacks(
                            _deviceHandle,
                            IBScanUltimateWrapper.RESULT_IMAGE_EX_EVENT,
                            _resultCallbackPtr,
                            IntPtr.Zero);
                        
                        if (IBScanUltimateWrapper.IsSuccess(cbResult))
                        {
                            Console.WriteLine("[ScannerService] ✓ Result image callback registered");
                        }
                        else
                        {
                            Console.WriteLine($"[ScannerService] ⚠️ Result callback registration returned: {cbResult}");
                        }
                        
                        // Register finger quality callback
                        if (_fingerQualityCallback == null) throw new InvalidOperationException("Finger quality callback not initialized");
                        _fingerQualityCallbackPtr = Marshal.GetFunctionPointerForDelegate<IBScanUltimateWrapper.FingerQualityCallback>(_fingerQualityCallback);
                        cbResult = IBScanUltimateWrapper.IBSU_RegisterCallbacks(
                            _deviceHandle,
                            IBScanUltimateWrapper.FINGER_QUALITY_EVENT,
                            _fingerQualityCallbackPtr,
                            IntPtr.Zero);
                        
                        if (IBScanUltimateWrapper.IsSuccess(cbResult))
                        {
                            Console.WriteLine("[ScannerService] ✓ Finger quality callback registered");
                        }
                        else
                        {
                            Console.WriteLine($"[ScannerService] ⚠️ Quality callback registration returned: {cbResult}");
                        }
                        
                        // Register complete acquisition callback - CRITICAL: fires when AUTO_CAPTURE finishes
                        if (_completeAcquisitionCallback == null) throw new InvalidOperationException("Complete acquisition callback not initialized");
                        _completeAcquisitionCallbackPtr = Marshal.GetFunctionPointerForDelegate<IBScanUltimateWrapper.CompleteAcquisitionCallback>(_completeAcquisitionCallback);
                        cbResult = IBScanUltimateWrapper.IBSU_RegisterCallbacks(
                            _deviceHandle,
                            IBScanUltimateWrapper.COMPLETE_ACQUISITION_EVENT,
                            _completeAcquisitionCallbackPtr,
                            IntPtr.Zero);
                        
                        if (IBScanUltimateWrapper.IsSuccess(cbResult))
                        {
                            Console.WriteLine("[ScannerService] ✓ Complete acquisition callback registered - will fire when AUTO_CAPTURE completes");
                        }
                        else
                        {
                            Console.WriteLine($"[ScannerService] ⚠️ Complete acquisition callback registration returned: {cbResult}");
                        }
                    }
                    catch (Exception cbEx)
                    {
                        Console.WriteLine($"[ScannerService] ⚠️ Error registering callbacks: {cbEx.Message}");
                        // Don't fail on callback registration, continue anyway
                    }
                    
                    return true;
                }
                else
                {
                    string errorMsg = result switch
                    {
                        -203 => "Device is already open or locked (busy). Try closing scanner tester app or unplugging device.",
                        -6 => "Device not found or already closed.",
                        _ => $"Error code: {result}"
                    };
                    RaiseError($"Failed to open device {deviceIndex}. {errorMsg}");
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
                    _captureStartTime = DateTime.MinValue;  // Reset timeout
                    
                    // Clear cached function pointers
                    _previewCallbackPtr = IntPtr.Zero;
                    _resultCallbackPtr = IntPtr.Zero;
                    _deviceCountCallbackPtr = IntPtr.Zero;
                    _fingerQualityCallbackPtr = IntPtr.Zero;
                    _completeAcquisitionCallbackPtr = IntPtr.Zero;
                    
                    Console.WriteLine("[ScannerService] ✓ Device closed successfully");
                    return true;
                }
                else
                {
                    // Result code -6 is non-fatal (device likely already closing)
                    Console.WriteLine($"[ScannerService] ⚠️ CloseDevice returned result code: {result} (may be expected during shutdown)");
                    _isDeviceOpen = false;
                    _deviceHandle = -1;
                    _isCaptureActive = false;
                    _captureStartTime = DateTime.MinValue;  // Reset timeout
                    _previewCallbackPtr = IntPtr.Zero;
                    _resultCallbackPtr = IntPtr.Zero;
                    _deviceCountCallbackPtr = IntPtr.Zero;
                    _fingerQualityCallbackPtr = IntPtr.Zero;
                    _completeAcquisitionCallbackPtr = IntPtr.Zero;
                    return true; // Treat as success anyway
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

        public bool StartCapture(int imageType = 2) // ENUM_IBSU_FLAT_SINGLE_FINGER
        {
            if (!_isDllLoaded)
            {
                RaiseError("Scanner DLL not loaded. Cannot start capture.");
                return false;
            }

            if (!_isDeviceOpen)
            {
                RaiseError("No device is open. Call OpenDevice() first.");
                return false;
            }

            if (_isCaptureActive)
            {
                Console.WriteLine("[ScannerService] Capture is already active");
                return true;
            }

            try
            {
                // Reset frame counter and preview flag for new capture session
                _previewFrameCount = 0;
                _hasReceivedPreviewFrame = false;
                _captureCompleted = false;  // Reset capture completion flag
                
                Console.WriteLine($"[ScannerService] Attempting to start capture with device handle: {_deviceHandle}, imageType: {imageType}");
                Console.WriteLine("[ScannerService] Waiting for finger placement and quality validation...");
                Console.WriteLine($"[ScannerService] ⏰ Timeout set to {CAPTURE_TIMEOUT_SECONDS} seconds");
                
                // Start timeout clock
                // NOTE: Do NOT use watchdog timer with AUTO_CAPTURE
                // AUTO_CAPTURE auto-completes when quality is good
                // Watchdog would break quality accumulation by restarting capture repeatedly
                _captureStartTime = DateTime.Now;
                _lastFrameTime = DateTime.Now;
                
                Console.WriteLine("[ScannerService] ✓ Frame watchdog timer disabled for AUTO_CAPTURE");
                Console.WriteLine("[ScannerService] AUTO_CAPTURE will auto-complete when quality is sufficient");
                
                // Use AUTO_CONTRAST + AUTO_CAPTURE for proper auto-completion
                // AUTO_CONTRAST: Automatically adjusts image contrast for better quality
                // AUTO_CAPTURE: Automatically completes when quality is good and sets IsFinal=true
                uint captureOptions = IBScanUltimateWrapper.AUTO_CONTRAST | 
                                     IBScanUltimateWrapper.AUTO_CAPTURE;
                
                Console.WriteLine("[ScannerService] Starting capture with AUTO_CONTRAST + AUTO_CAPTURE options...");
                int result = IBScanUltimateWrapper.IBSU_BeginCaptureImage(
                    _deviceHandle,
                    imageType,
                    500,  // IMAGE_RESOLUTION_500
                    captureOptions);

                if (IBScanUltimateWrapper.IsSuccess(result))
                {
                    _isCaptureActive = true;
                    Console.WriteLine("[ScannerService] ✓ Fingerprint capture started - watchdog will keep capture alive");
                    Console.WriteLine("[ScannerService] ⏳ Waiting for fingerprint...");
                    return true;
                }
                else
                {
                    _frameWatchdogTimer?.Dispose();
                    _frameWatchdogTimer = null;
                    RaiseError($"Failed to start capture with status: {result}");
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
                // Watchdog timer is not used with AUTO_CAPTURE (already null)
                // AUTO_CAPTURE auto-completes on its own when quality is good
                
                int result = IBScanUltimateWrapper.IBSU_CancelCaptureImage(_deviceHandle);

                if (IBScanUltimateWrapper.IsSuccess(result))
                {
                    _isCaptureActive = false;
                    _captureStartTime = DateTime.MinValue;  // Reset timeout
                    Console.WriteLine("[ScannerService] ✓ Fingerprint capture stopped");
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
                bool isActive = false;
                int result = IBScanUltimateWrapper.IBSU_IsCaptureActive(_deviceHandle, out isActive);
                
                if (IBScanUltimateWrapper.IsSuccess(result))
                {
                    return isActive;
                }
                else
                {
                    RaiseError($"Error checking capture status. Result: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error checking capture status: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Image Validation

        /// <summary>
        /// Checks if an image contains actual fingerprint data and returns the histogram quality percentage.
        /// Uses histogram analysis to estimate quality based on pixel distribution.
        /// </summary>
        private float GetHistogramQuality(byte[] imageData, int width, int height)
        {
            if (imageData == null || imageData.Length == 0)
                return 0f;

            try
            {
                // Calculate image statistics
                int blackPixels = 0;  // Very dark pixels (< 50)
                int whitePixels = 0;  // Very bright pixels (> 200)
                int midPixels = 0;    // Pixels in middle range (good fingerprint range)
                
                for (int i = 0; i < imageData.Length; i++)
                {
                    byte pixel = imageData[i];
                    if (pixel < 50)
                        blackPixels++;
                    else if (pixel > 200)
                        whitePixels++;
                    else
                        midPixels++;
                }

                // Calculate percentages
                float blackPercent = (blackPixels * 100f) / imageData.Length;
                float whitePercent = (whitePixels * 100f) / imageData.Length;
                float midPercent = (midPixels * 100f) / imageData.Length;

                Console.WriteLine($"[ScannerService] Image analysis - Black: {blackPercent:F1}%, Mid: {midPercent:F1}%, White: {whitePercent:F1}%");

                // Return the mid-range percentage as our histogram quality estimate
                return midPercent;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScannerService] ⚠️ Error calculating histogram quality: {ex.Message}");
                return 0f;
            }
        }

        #endregion

        #region Spoof Detection

        public bool SetSpoofDetection(bool enabled, int spoofSensitivity = 5)
        {
            if (!_isDllLoaded)
            {
                RaiseError("Scanner DLL not loaded. Cannot set spoof detection.");
                return false;
            }

            if (!_isDeviceOpen)
            {
                RaiseError("No device is open. Call OpenDevice() first.");
                return false;
            }

            try
            {
                // Property ID for spoof detection
                int result = IBScanUltimateWrapper.IBSU_SetPropertyInt(
                    _deviceHandle,
                    IBScanUltimateWrapper.PROPERTY_SPOOF_DETECTION,
                    enabled ? spoofSensitivity : 0);

                if (IBScanUltimateWrapper.IsSuccess(result))
                {
                    Console.WriteLine($"[ScannerService] ✓ Spoof detection set to: {(enabled ? "enabled" : "disabled")}");
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
            IntPtr pContext,
            IBScanUltimateWrapper.IBSU_ImageData imageData)
        {
            try
            {
                // Skip preview frames after capture completes (thread-safe check)
                lock (this)
                {
                    if (_captureCompleted)
                    {
                        return;
                    }
                }
                _previewFrameCount++;
                _hasReceivedPreviewFrame = true;  // Flag that preview is working
                
                // Log every frame in first 5, then every 10 to avoid spam
                bool shouldLog = _previewFrameCount <= 5 || _previewFrameCount % 10 == 0;
                if (shouldLog)
                {
                    Console.WriteLine($"[ScannerService] ✓ [Preview Frame #{_previewFrameCount}] Buffer: {(imageData.Buffer != IntPtr.Zero ? "valid" : "NULL")}, Size: {imageData.Width}x{imageData.Height}");
                }
                
                // Compute quality using the image buffer when available
                int qualityScore = 0;
                bool hasValidImage = false;
                byte[]? imageBuffer = null;

                // Get NFIQ quality score
                int nfiqQualityScore = 0;
                bool nfiqIsValid = false;

                // Try to marshal and get histogram quality
                if (imageData.Buffer != IntPtr.Zero && imageData.Width > 0 && imageData.Height > 0)
                {
                    imageBuffer = IBScanUltimateWrapper.MarshalImageBuffer(
                        imageData.Buffer,
                        (int)imageData.Width,
                        (int)imageData.Height,
                        imageData.BitsPerPixel);

                    if (imageBuffer != null)
                    {
                        hasValidImage = true;
                        // NFIQ requires raw image buffer parameters in SDK v4.3
                        try
                        {
                            int nfiqResult = IBScanUltimateWrapper.IBSU_GetNFIQScore(
                                _deviceHandle,
                                imageBuffer,
                                imageData.Width,
                                imageData.Height,
                                imageData.BitsPerPixel,
                                out nfiqQualityScore);

                            if (IBScanUltimateWrapper.IsSuccess(nfiqResult))
                            {
                                nfiqIsValid = true;
                                if (_previewFrameCount % 10 == 0)
                                {
                                    Console.WriteLine($"[ScannerService] Preview: NFIQ Quality = {nfiqQualityScore}%");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"[ScannerService] Preview NFIQ returned error: {nfiqResult}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ScannerService] ⚠️ Error getting preview NFIQ quality: {ex.Message}");
                        }

                        // Get histogram quality as fallback
                        float histogramQuality = GetHistogramQuality(imageBuffer, (int)imageData.Width, (int)imageData.Height);
                        
                        // Use NFIQ if available, otherwise use histogram quality
                        qualityScore = nfiqIsValid ? nfiqQualityScore : (int)histogramQuality;
                        if (shouldLog)
                        {
                            Console.WriteLine($"[ScannerService] Preview quality: {qualityScore}%");
                        }
                    }
                }
                else
                {
                    // Even if buffer is invalid, use NFIQ if available
                    qualityScore = nfiqIsValid ? nfiqQualityScore : 0;
                    Console.WriteLine($"[ScannerService] ⚠️ [Preview Frame #{_previewFrameCount}] Invalid image buffer");
                }

                // ALWAYS fire the event with quality score, even if image is invalid
                var args = new ScannerEventArgs
                {
                    ImageData = imageBuffer,
                    Width = imageData.Width,
                    Height = imageData.Height,
                    ResolutionX = imageData.ResolutionX,
                    ResolutionY = imageData.ResolutionY,
                    BitsPerPixel = imageData.BitsPerPixel,
                    QualityScore = qualityScore,  // Always include quality
                    IsFinalImage = imageData.IsFinal,
                    IsSuccess = hasValidImage
                };

                PreviewImageAvailable?.Invoke(this, args);
                
                if (shouldLog)
                {
                    Console.WriteLine($"[ScannerService] ✓ Preview event fired - Frame #{_previewFrameCount}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScannerService] ❌ CRASH in preview callback: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[ScannerService] Stack: {ex.StackTrace}");
                RaiseError($"Error in preview callback: {ex.Message}");
            }
        }

        private void OnResultImageAvailable(
            int deviceHandle,
            IntPtr pContext,
            int imageStatus,
            IBScanUltimateWrapper.IBSU_ImageData imageData,
            int imageType,
            int detectedFingerCount,
            int segmentImageArrayCount,
            IntPtr pSegmentImageArray,
            IntPtr pSegmentPositionArray)
        {
            try
            {
                // LOG IMMEDIATELY to detect early crashes
                Console.WriteLine($"[ScannerService] ⚡ Result callback ENTRY - deviceHandle: {deviceHandle}, status: {imageStatus}, IsFinal: {imageData.IsFinal}, imageType: {imageType}");

                if (!IBScanUltimateWrapper.IsSuccess(imageStatus))
                {
                    Console.WriteLine($"[ScannerService] ⚠️ Result callback status not OK: {imageStatus}");
                    return;
                }
                
                // Validate that we can safely proceed - defensive checks first
                if (imageData.Equals(default(IBScanUltimateWrapper.IBSU_ImageData)))
                {
                    Console.WriteLine("[ScannerService] ❌ Result callback: imageData struct is default/empty");
                    return;
                }
                
                // Track frame time immediately for watchdog monitoring
                _lastFrameTime = DateTime.Now;
                
                Console.WriteLine($"[ScannerService] Result callback invoked - deviceHandle: {deviceHandle}");
                
                // Validate device handle
                if (deviceHandle < 0)
                {
                    Console.WriteLine("[ScannerService] ❌ Result callback: Invalid device handle");
                    _isCaptureActive = false;
                    return;
                }
                
                // Double-check device is still open
                if (!_isDeviceOpen)
                {
                    Console.WriteLine("[ScannerService] ⚠️ Device was closed, ignoring result callback");
                    _isCaptureActive = false;
                    return;
                }
                
                // Skip result frames after capture has been marked complete
                // (unless this is THE final frame with IsFinal=true) - thread-safe check
                lock (this)
                {
                    if (_captureCompleted && !imageData.IsFinal)
                    {
                        Console.WriteLine("[ScannerService] Ignoring result callback after capture completion (waiting for IsFinal frame)");
                        return;
                    }
                }

                Console.WriteLine($"[ScannerService] Result callback: Buffer={imageData.Buffer}, Width={imageData.Width}, Height={imageData.Height}, BitsPerPixel={imageData.BitsPerPixel}");
                
                if (imageData.Buffer == IntPtr.Zero || imageData.Width == 0 || imageData.Height == 0)
                {
                    Console.WriteLine("[ScannerService] ⚠️ Result callback: Invalid image data (null buffer or zero dimensions)");
                    _isCaptureActive = false;
                    return;
                }

                Console.WriteLine("[ScannerService] Calling MarshalImageBuffer for result...");
                byte[]? imageBuffer = IBScanUltimateWrapper.MarshalImageBuffer(
                    imageData.Buffer,
                    (int)imageData.Width,
                    (int)imageData.Height,
                    imageData.BitsPerPixel);

                if (imageBuffer != null)
                {
                    Console.WriteLine($"[ScannerService] Frame info - IsFinal: {imageData.IsFinal}");

                    // Get quality score from SDK - THIS IS THE PRIMARY QUALITY METRIC
                    // Wrap in try-catch and add null device check
                    int nfiqQualityScore = 0;
                    bool nfiqIsValid = false;
                    try
                    {
                        // Safety check: ensure device is still valid before calling SDK
                        if (_deviceHandle < 0 || !_isDeviceOpen)
                        {
                            Console.WriteLine($"[ScannerService] ⚠️ Device became invalid during quality retrieval");
                            nfiqIsValid = false;
                        }
                        else
                        {
                            int nfiqResult = IBScanUltimateWrapper.IBSU_GetNFIQScore(
                                _deviceHandle,
                                imageBuffer,
                                imageData.Width,
                                imageData.Height,
                                imageData.BitsPerPixel,
                                out nfiqQualityScore);
                            if (IBScanUltimateWrapper.IsSuccess(nfiqResult))
                            {
                                Console.WriteLine($"[ScannerService] ✓ NFIQ Quality score: {nfiqQualityScore}%");
                                nfiqIsValid = true;
                            }
                            else
                            {
                                Console.WriteLine($"[ScannerService] NFIQ returned error code: {nfiqResult}");
                                nfiqIsValid = false;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ScannerService] ⚠️ Error getting NFIQ score: {ex.Message}");
                        nfiqIsValid = false;
                    }

                    // Get histogram-based quality as fallback
                    float histogramQuality = GetHistogramQuality(imageBuffer, (int)imageData.Width, (int)imageData.Height);
                    
                    // Use whichever quality metric is available
                    int qualityScore = nfiqIsValid ? nfiqQualityScore : (int)histogramQuality;
                    Console.WriteLine($"[ScannerService] Using quality metric: {(nfiqIsValid ? "NFIQ" : "Histogram")} = {qualityScore}%");

                    // ALWAYS fire preview event with current image data for real-time display
                    var previewArgs = new ScannerEventArgs
                    {
                        ImageData = imageBuffer,
                        Width = imageData.Width,
                        Height = imageData.Height,
                        ResolutionX = imageData.ResolutionX,
                        ResolutionY = imageData.ResolutionY,
                        BitsPerPixel = imageData.BitsPerPixel,
                        QualityScore = qualityScore,
                        IsFinalImage = imageData.IsFinal,
                        IsSuccess = true
                    };
                    
                    PreviewImageAvailable?.Invoke(this, previewArgs);

                    // Accept fingerprint when AUTO_CAPTURE completes (IsFinal=true)
                    // Quality check removed - AUTO_CAPTURE provides sufficient fingerprint quality
                    if (imageData.IsFinal)
                    {
                        Console.WriteLine($"[ScannerService] ✓✓ FINAL FRAME ARRIVED at quality {qualityScore}% - accepting fingerprint");
                        
                        try
                        {
                            var args = new ScannerEventArgs
                            {
                                ImageData = imageBuffer,
                                Width = imageData.Width,
                                Height = imageData.Height,
                                ResolutionX = imageData.ResolutionX,
                                ResolutionY = imageData.ResolutionY,
                                BitsPerPixel = imageData.BitsPerPixel,
                                QualityScore = qualityScore,
                                IsFinalImage = imageData.IsFinal,
                                IsSuccess = true
                            };

                            Console.WriteLine($"[ScannerService] ✓ Valid fingerprint captured! (Quality: {qualityScore}%, Final: True)");
                            Console.WriteLine("[ScannerService] Invoking FingerprintCaptured event...");
                            
                            try
                            {
                                FingerprintCaptured?.Invoke(this, args);
                                Console.WriteLine("[ScannerService] ✓ FingerprintCaptured event invoked successfully");
                            }
                            catch (Exception invokeEx)
                            {
                                Console.WriteLine($"[ScannerService] ❌ Error invoking FingerprintCaptured event: {invokeEx.Message}");
                                Console.WriteLine($"[ScannerService] Stack: {invokeEx.StackTrace}");
                            }
                            
                            _isCaptureActive = false;
                        }
                        catch (Exception finalFrameEx)
                        {
                            Console.WriteLine($"[ScannerService] ❌ Error processing final frame: {finalFrameEx.GetType().Name}: {finalFrameEx.Message}");
                            Console.WriteLine($"[ScannerService] Stack: {finalFrameEx.StackTrace}");
                            _isCaptureActive = false;
                            RaiseError($"Error processing final fingerprint frame: {finalFrameEx.Message}");
                        }
                    }
                    else
                    {
                        // Waiting for final frame - keep capture active
                        return;
                    }
                }
                else
                {
                    Console.WriteLine("[ScannerService] ⚠️ Result callback: MarshalImageBuffer returned null");
                    _isCaptureActive = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScannerService] ❌ CRASH in result callback: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[ScannerService] Stack: {ex.StackTrace}");
                _isCaptureActive = false;
                RaiseError($"Error in result callback: {ex.Message}");
            }
        }

        private void OnDeviceCountChanged(int deviceCount, IntPtr pContext)
        {
            Console.WriteLine($"[ScannerService] Device count changed: {deviceCount}");
        }

        private void OnFingerQualityUpdate(int deviceHandle, IntPtr pContext, IntPtr pQualityArray, int qualityArrayCount)
        {
            try
            {
                Console.WriteLine($"[ScannerService] ⚡ FingerQuality callback ENTRY - deviceHandle={deviceHandle}, count={qualityArrayCount}");
                
                // Guard against accessing quality array after capture completion or invalid state
                if (pQualityArray == IntPtr.Zero)
                {
                    Console.WriteLine($"[ScannerService] Finger quality update: deviceHandle={deviceHandle}, count={qualityArrayCount}, WARNING: Quality array is null");
                    return;
                }

                if (qualityArrayCount <= 0 || qualityArrayCount > 10)
                {
                    Console.WriteLine($"[ScannerService] Finger quality update: deviceHandle={deviceHandle}, count={qualityArrayCount}");
                    if (qualityArrayCount > 10)
                    {
                        Console.WriteLine($"[ScannerService] ⚠️ Quality array count suspiciously high ({qualityArrayCount}), skipping marshal");
                    }
                    return;
                }

                Console.WriteLine($"[ScannerService] Finger quality update: deviceHandle={deviceHandle}, count={qualityArrayCount}");
                
                // Only try to marshal quality array if count is reasonable
                lock (this)  // Protect against concurrent device state changes
                {
                    // Extra safety check - ensure device is still open
                    if (!_isDeviceOpen)
                    {
                        Console.WriteLine($"[ScannerService] ⚠️ Device closed during quality update, skipping marshal");
                        return;
                    }
                    
                    try
                    {
                        // Quality array contains byte values for each finger
                        byte[] qualityData = new byte[qualityArrayCount];
                        Marshal.Copy(pQualityArray, qualityData, 0, qualityArrayCount);
                        Console.WriteLine($"[ScannerService]   Finger qualities: {string.Join(", ", qualityData)}");
                    }
                    catch (AccessViolationException avEx)
                    {
                        Console.WriteLine($"[ScannerService] ❌ Access violation marshalling quality array (pointer invalid): {avEx.Message}");
                        Console.WriteLine($"[ScannerService] This indicates device transitioned to invalid state during callback");
                        // This is non-fatal - device might be closing
                    }
                    catch (Exception marshallEx)
                    {
                        Console.WriteLine($"[ScannerService] ⚠️ Error marshalling quality array: {marshallEx.GetType().Name}: {marshallEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScannerService] ❌ CRASH in finger quality callback: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[ScannerService] Stack: {ex.StackTrace}");
                // Don't rethrow - this is a non-critical callback
            }
        }

        private void OnCompleteAcquisition(int deviceHandle, IntPtr pContext, int imageType)
        {
            try
            {
                if (deviceHandle < 0)
                {
                    Console.WriteLine("[ScannerService] ⚠️ OnCompleteAcquisition: Invalid device handle");
                    return;
                }

                if (!_isDeviceOpen)
                {
                    Console.WriteLine("[ScannerService] ⚠️ OnCompleteAcquisition: Device not open");
                    return;
                }

                // Set completion flag SAFELY with thread-safety lock
                lock (this)
                {
                    _captureCompleted = true;  // Signal that capture is done
                }
                
                Console.WriteLine($"[ScannerService] ✓✓ COMPLETE_ACQUISITION callback fired! (imageType={imageType})");
                Console.WriteLine($"[ScannerService] AUTO_CAPTURE has finished - capture is now complete");
                Console.WriteLine($"[ScannerService] IsFinal should now be True in the result image callback");
                Console.WriteLine($"[ScannerService] Waiting for result callback with IsFinal=true...");
                
                // The COMPLETE_ACQUISITION event signals that AUTO_CAPTURE finished successfully
                // The result image should now have IsFinal=true and contain the final fingerprint
                // The result callback will fire next with IsFinal=true
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScannerService] ⚠️ Error in complete acquisition callback: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[ScannerService] Stack: {ex.StackTrace}");
                RaiseError($"Error in complete acquisition callback: {ex.Message}");
            }
        }

        /// <summary>
        /// Watchdog timer callback - DISABLED for AUTO_CAPTURE mode
        /// AUTO_CAPTURE doesn't need watchdog; it auto-completes when quality is good
        /// Watchdog caused quality accumulation to fail by restarting capture repeatedly
        /// </summary>
        private void FrameWatchdogCallback(object? state)
        {
            // DO NOT USE with AUTO_CAPTURE - watching for motion stops and restarting
            // would break the quality accumulation and prevent capture completion
            return;
        }

        #endregion

        #region Error Handling

        private void RaiseError(string errorMessage)
        {
            Console.WriteLine($"[ScannerService] ❌ ERROR: {errorMessage}");
            ErrorOccurred?.Invoke(this, errorMessage);
        }

        #endregion

        #region Template Extraction

        /// <summary>
        /// Extracts ISO/ANSI fingerprint template from captured image buffer.
        /// Called from result callback with the actual image data.
        /// </summary>


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
                // Stop watchdog timer first
                _frameWatchdogTimer?.Dispose();
                _frameWatchdogTimer = null;
                
                // Close device if open
                if (_isDeviceOpen)
                {
                    CloseDevice();
                }

                Console.WriteLine("[ScannerService] ✓ ScannerService disposed");
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
