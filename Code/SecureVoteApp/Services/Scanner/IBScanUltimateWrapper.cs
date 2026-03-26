using System;
using System.Runtime.InteropServices;

namespace SecureVoteApp.Services.Scanner
{
    // P/Invoke wrapper for IBScanUltimate native DLL
    internal static class IBScanUltimateWrapper
    {
        // Path to the native DLL - will look in bin\x64 directory
        private const string DllName = "IBScanUltimate.dll";

        #region Constants

        // Image types (from SDK enums)
        public const int FLAT_FINGERPRINT = 2;      // ENUM_IBSU_FLAT_SINGLE_FINGER
        public const int ROLLED_FINGERPRINT = 1;    // ENUM_IBSU_ROLL_SINGLE_FINGER

        // Image resolution (from SDK enums)
        public const int IMAGE_RESOLUTION_500 = 500;    // ENUM_IBSU_IMAGE_RESOLUTION_500
        public const int IMAGE_RESOLUTION_1000 = 1000;  // ENUM_IBSU_IMAGE_RESOLUTION_1000

        // Capture options - MUST match IBSU_OPTION_* from IBScanUltimateApi_defs.h
        // These are bitwise flags used with BeginCaptureImage
        public const uint AUTO_CONTRAST = 0x00000001;  // IBSU_OPTION_AUTO_CONTRAST = 1
        public const uint AUTO_CAPTURE = 0x00000002;   // IBSU_OPTION_AUTO_CAPTURE = 2

        // Property IDs for scanner configuration
        public const int PROPERTY_SPOOF_DETECTION = 0x0024;
        public const int PROPERTY_MOTION_SENSITIVITY = 0x005C;  // Controls motion-based frame filtering

        // Event types - MUST match ENUM_IBSU_*_EVENT from IBScanUltimateApi_defs.h
        // These are callback event identifiers for RegisterCallbacks
        public const int DEVICE_COUNT_EVENT = 0;                    // ENUM_IBSU_ESSENTIAL_EVENT_DEVICE_COUNT
        public const int COMMUNICATION_BREAK_EVENT = 1;             // ENUM_IBSU_ESSENTIAL_EVENT_COMMUNICATION_BREAK
        public const int PREVIEW_IMAGE_EVENT = 2;                   // ENUM_IBSU_ESSENTIAL_EVENT_PREVIEW_IMAGE
        public const int TAKING_ACQUISITION_EVENT = 3;              // ENUM_IBSU_ESSENTIAL_EVENT_TAKING_ACQUISITION (rolled prints)
        public const int COMPLETE_ACQUISITION_EVENT = 4;            // ENUM_IBSU_ESSENTIAL_EVENT_COMPLETE_ACQUISITION (auto-capture completion)
        public const int RESULT_IMAGE_EVENT = 5;                    // ENUM_IBSU_ESSENTIAL_EVENT_RESULT_IMAGE (deprecated)
        public const int FINGER_QUALITY_EVENT = 6;                  // ENUM_IBSU_OPTIONAL_EVENT_FINGER_QUALITY
        public const int FINGER_COUNT_EVENT = 7;                    // ENUM_IBSU_OPTIONAL_EVENT_FINGER_COUNT
        public const int INIT_PROGRESS_EVENT = 8;                   // ENUM_IBSU_ESSENTIAL_EVENT_INIT_PROGRESS
        public const int CLEAR_PLATEN_AT_CAPTURE_EVENT = 9;         // ENUM_IBSU_OPTIONAL_EVENT_CLEAR_PLATEN_AT_CAPTURE
        public const int ASYNC_OPEN_DEVICE_EVENT = 10;              // ENUM_IBSU_ESSENTIAL_EVENT_ASYNC_OPEN_DEVICE
        public const int NOTIFY_MESSAGE_EVENT = 11;                 // ENUM_IBSU_OPTIONAL_EVENT_NOTIFY_MESSAGE
        public const int RESULT_IMAGE_EX_EVENT = 12;                // ENUM_IBSU_ESSENTIAL_EVENT_RESULT_IMAGE_EX
        public const int KEYBUTTON_EVENT = 13;                      // ENUM_IBSU_ESSENTIAL_EVENT_KEYBUTTON

        // Result codes
        public const int IBSU_STATUS_OK = 0;

        #endregion

        #region Structures

        // Image data structure from scanner - MUST match SDK struct IBSU_ImageData exactly
        // Critical: BOOL is 4 bytes in C, not 1! Use natural alignment, NOT Pack=1
        [StructLayout(LayoutKind.Sequential)]
        public struct IBSU_ImageData
        {
            public IntPtr Buffer;           // void* = 8 bytes (64-bit)

            public uint Width;              // DWORD = 4 bytes

            public uint Height;             // DWORD = 4 bytes

            public double ResolutionX;      // double = 8 bytes

            public double ResolutionY;      // double = 8 bytes

            public double FrameTime;        // double = 8 bytes

            public int Pitch;               // int = 4 bytes

            public byte BitsPerPixel;       // BYTE = 1 byte

            public uint Format;             // DWORD (IBSU_ImageFormat) = 4 bytes

            [MarshalAs(UnmanagedType.Bool)] // BOOL is 4 bytes in C SDK, not 1!
            public bool IsFinal;            // BOOL = 4 bytes

            public uint ProcessThres;       // DWORD = 4 bytes
        }

        // Device description structure
        // Note: IBSU_MAX_STR_LEN = 128 (from SDK headers)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct IBSU_DeviceDescription
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string SerialNumber;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string ProductName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string InterfaceType;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string FirmwareVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceRevision;

            public int Handle;

            [MarshalAs(UnmanagedType.I1)]
            public bool IsHandleOpened;

            [MarshalAs(UnmanagedType.I1)]
            public bool IsDeviceLocked;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string CustomerString;
        }

        // IBSM (IBScan Matcher) structures for template extraction
        // These match the SDK structures from IBScanUltimateApi_defs.h
        
        // Enumeration for finger position
        public enum IBSM_FingerPosition : int
        {
            IBSM_FINGER_POSITION_UNKNOWN = 0,
            IBSM_FINGER_POSITION_RIGHT_THUMB = 1,
            IBSM_FINGER_POSITION_RIGHT_INDEX_FINGER = 2,
            IBSM_FINGER_POSITION_RIGHT_MIDDLE_FINGER = 3,
            IBSM_FINGER_POSITION_RIGHT_RING_FINGER = 4,
            IBSM_FINGER_POSITION_RIGHT_LITTLE_FINGER = 5,
            IBSM_FINGER_POSITION_LEFT_THUMB = 6,
            IBSM_FINGER_POSITION_LEFT_INDEX_FINGER = 7,
            IBSM_FINGER_POSITION_LEFT_MIDDLE_FINGER = 8,
            IBSM_FINGER_POSITION_LEFT_RING_FINGER = 9,
            IBSM_FINGER_POSITION_LEFT_LITTLE_FINGER = 10
        }

        // Image format enumeration
        public enum IBSM_ImageFormat : int
        {
            IBSM_IMG_FORMAT_GRAY = 0
        }

        // Impression type enumeration
        public enum IBSM_ImpressionType : int
        {
            IBSM_IMPRESSION_TYPE_UNKNOWN = 0,
            IBSM_IMPRESSION_TYPE_LIVE_SCAN_PLAIN = 1,
            IBSM_IMPRESSION_TYPE_LIVE_SCAN_ROLLED = 2
        }

        // Capture device tech ID
        public enum IBSM_CaptureDeviceTechID : int
        {
            IBSM_CAPTURE_DEVICE_TECH_UNKNOWN = 0
        }

        // Standard format enumeration for template conversion
        public enum IBSM_StandardFormat : int
        {
            ENUM_IBSM_STANDARD_FORMAT_ISO_19794_2_2005 = 0,
            ENUM_IBSM_STANDARD_FORMAT_ISO_19794_4_2005 = 1,
            ENUM_IBSM_STANDARD_FORMAT_ISO_19794_2_2011 = 2,
            ENUM_IBSM_STANDARD_FORMAT_ISO_19794_4_2011 = 3,
            ENUM_IBSM_STANDARD_FORMAT_ANSI_INCITS_378_2004 = 4,
            ENUM_IBSM_STANDARD_FORMAT_ANSI_INCITS_381_2004 = 5,
            ENUM_IBSM_STANDARD_FORMAT_ISO_39794_4_2019 = 6
        }

        // IBSM Image Data structure for template extraction
        // MUST match SDK struct IBSM_ImageData exactly
        [StructLayout(LayoutKind.Sequential)]
        public struct IBSM_ImageData
        {
            public IBSM_ImageFormat ImageFormat;                    // int (4 bytes)
            public IBSM_ImpressionType ImpressionType;              // int (4 bytes)
            public IBSM_FingerPosition FingerPosition;              // int (4 bytes)
            public IBSM_CaptureDeviceTechID CaptureDeviceTechID;  // int (4 bytes)
            public ushort CaptureDeviceVendorID;                    // unsigned short (2 bytes)
            public ushort CaptureDeviceTypeID;                      // unsigned short (2 bytes)
            public ushort ScanSamplingX;                            // unsigned short (2 bytes)
            public ushort ScanSamplingY;                            // unsigned short (2 bytes)
            public ushort ImageSamplingX;                           // unsigned short (2 bytes)
            public ushort ImageSamplingY;                           // unsigned short (2 bytes)
            public ushort ImageSizeX;                               // unsigned short (2 bytes)
            public ushort ImageSizeY;                               // unsigned short (2 bytes)
            public byte ScaleUnit;                                  // unsigned char (1 byte)
            public byte BitDepth;                                   // unsigned char (1 byte)
            public uint ImageDataLength;                            // unsigned int (4 bytes)
            public IntPtr ImageData;                                // void* (8 bytes on 64-bit)
        }

        // Standard format data structure for template output
        [StructLayout(LayoutKind.Sequential)]
        public struct IBSM_StandardFormatData
        {
            public IntPtr Data;                 // void* - Pointer to data buffer
            public ulong DataLength;            // unsigned long - Data length in bytes
            public IBSM_StandardFormat Format;  // IBSM_StandardFormat - Standard format type
        }

        #endregion

        #region Delegates (Callbacks)

        // Preview image callback delegate - NOTE: Parameter order is deviceHandle, pContext, imageData
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void PreviewImageCallback(
            int deviceHandle,
            IntPtr pContext,
            IBSU_ImageData imageData);

        // Legacy result image callback (event 5)
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ResultImageCallback(
            int deviceHandle,
            IntPtr pContext,
            IBSU_ImageData imageData);

        // Extended result image callback (event 12)
        // MUST match SDK signature for ENUM_IBSU_ESSENTIAL_EVENT_RESULT_IMAGE_EX.
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ResultImageExCallback(
            int deviceHandle,
            IntPtr pContext,
            int imageStatus,
            IBSU_ImageData imageData,
            int imageType,
            int detectedFingerCount,
            int segmentImageArrayCount,
            IntPtr pSegmentImageArray,
            IntPtr pSegmentPositionArray);

        // Device count changed callback delegate
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void DeviceCountCallback(
            int deviceCount,
            IntPtr pContext);

        // Finger quality callback delegate - NOTE: pQualityArray is an array pointer, qualityArrayCount is the count
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void FingerQualityCallback(
            int deviceHandle,
            IntPtr pContext,
            IntPtr pQualityArray,
            int qualityArrayCount);

        // Complete acquisition callback delegate - fired when AUTO_CAPTURE completes
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void CompleteAcquisitionCallback(
            int deviceHandle,
            IntPtr pContext,
            int imageType);

        #endregion

        #region Native Functions

        // Gets the number of currently connected scanner devices
        // Returns IBSU_STATUS_OK if successful
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_GetDeviceCount(out int pDeviceCount);

        // Gets description of a specific device
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_GetDeviceDescription(
            int deviceIndex,
            out IBSU_DeviceDescription pDeviceDescription);

        // Opens a connection to a scanner device
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_OpenDevice(
            int deviceIndex,
            ref int pHandle);

        // Closes the connection to a scanner device
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_CloseDevice(int deviceHandle);

        // Closes all open devices
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_CloseAllDevices();

        // Checks if a device is currently open
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool IBSU_IsDeviceOpened(int deviceHandle);

        // Begins fingerprint image capture
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_BeginCaptureImage(
            int deviceHandle,
            int imageType,
            int imageResolution,
            uint captureOptions);

        // Cancels ongoing fingerprint capture
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_CancelCaptureImage(int deviceHandle);

        // Checks if image capture is currently active - Returns status code, sets pIsActive out parameter
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_IsCaptureActive(
            int deviceHandle,
            [MarshalAs(UnmanagedType.I1)] out bool pIsActive);

        // Registers callback functions for scanner events
        // Signature: IBSU_RegisterCallbacks(deviceHandle, event, pCallbackFunction, pContext)
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_RegisterCallbacks(
            int deviceHandle,
            int eventType,
            IntPtr pCallbackFunction,
            IntPtr pContext);

        // Checks if touched finger is detected on scanner
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool IBSU_IsTouchedFinger(int deviceHandle);

        // Gets NFIQ quality score for image buffer (SDK v4.3 signature)
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_GetNFIQScore(
            int deviceHandle,
            byte[] imgBuffer,
            uint width,
            uint height,
            byte bitsPerPixel,
            out int score);

        // Enables or disables spoof (fake finger) detection
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_SetPropertyInt(
            int deviceHandle,
            int propertyId,
            int propertyValue);

        // Gets spoof detection property
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_GetPropertyInt(
            int deviceHandle,
            int propertyId,
            ref int propertyValue);

        // Checks if spoof finger was detected in last capture
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool IBSU_IsSpoofFingerDetected(int deviceHandle);

        // Gets the last image result info with proper IBSM struct marshaling
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_GetIBSM_ResultImageInfo(
            int deviceHandle,
            IBSM_FingerPosition fingerPosition,
            ref IBSM_ImageData pResultImage,
            IntPtr pSplitResultImage,
            ref int pSplitResultImageCount);

        // Converts fingerprint image to ISO/ANSI template format
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_ConvertImageToISOANSI(
            int deviceHandle,
            ref IBSM_ImageData image,
            int imageCount,
            IBSM_ImageFormat imageFormat,
            IBSM_StandardFormat standardFormat,
            ref IBSM_StandardFormatData pdata);

        // Gets SDK version string
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern IntPtr IBSU_GetSDKVersion();

        #endregion

        #region Helper Methods

        // Converts unmanaged image buffer to managed byte array
        public static byte[]? MarshalImageBuffer(IntPtr buffer, int width, int height, uint bitsPerPixel)
        {
            if (buffer == IntPtr.Zero || width <= 0 || height <= 0)
                return null;

            int bytesPerPixel = (int)(bitsPerPixel / 8);
            if (bytesPerPixel == 0) bytesPerPixel = 1;

            int imageSize = width * height * bytesPerPixel;
            byte[] imageData = new byte[imageSize];

            Marshal.Copy(buffer, imageData, 0, imageSize);
            return imageData;
        }

        // Checks if native function call was successful
        public static bool IsSuccess(int resultCode)
        {
            return resultCode == IBSU_STATUS_OK;
        }

        #endregion
    }
}

