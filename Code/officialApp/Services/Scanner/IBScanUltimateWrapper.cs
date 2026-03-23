using System;
using System.Runtime.InteropServices;

namespace officialApp.Services.Scanner
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

        // Capture options
        public const uint AUTO_CAPTURE_ENABLED = 0x00000001;
        public const uint QUALITY_CHECK_ENABLED = 0x00000002;

        // Property IDs for scanner configuration
        public const int PROPERTY_SPOOF_DETECTION = 0x0024;
        public const int PROPERTY_MOTION_SENSITIVITY = 0x005C;  // Controls motion-based frame filtering

        // Event types
        public const int PREVIEW_IMAGE_EVENT = 0;
        public const int RESULT_IMAGE_EVENT = 1;
        public const int RESULT_IMAGE_EX_EVENT = 2;
        public const int FINGER_QUALITY_EVENT = 3;
        public const int DEVICE_COUNT_EVENT = 4;
        public const int FINGER_DETECTED_EVENT = 5;
        public const int SPOOF_IMAGE_EVENT = 6;

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

        #endregion

        #region Delegates (Callbacks)

        // Preview image callback delegate - NOTE: Parameter order is deviceHandle, pContext, imageData
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void PreviewImageCallback(
            int deviceHandle,
            IntPtr pContext,
            IBSU_ImageData imageData);

        // Result image callback delegate - NOTE: Parameter order is deviceHandle, pContext, imageData
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ResultImageCallback(
            int deviceHandle,
            IntPtr pContext,
            IBSU_ImageData imageData);

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

        // Gets NFIQ quality score for captured image
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_GetNFIQScore(int deviceHandle);

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

        // Gets the last image result info
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_GetIBSM_ResultImageInfo(
            int deviceHandle,
            out int pImageCount,
            out IntPtr pImageHandle);

        // Converts fingerprint image to ISO/ANSI template format
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_ConvertImageToISOANSI(
            int deviceHandle,
            int templateType,
            IntPtr imageHandle,
            out IntPtr pTemplateBuffer,
            out uint templateBufferSize);

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
