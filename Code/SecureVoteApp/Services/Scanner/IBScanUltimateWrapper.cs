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

        // Image types
        public const int FLAT_FINGERPRINT = 0;
        public const int ROLLED_FINGERPRINT = 1;

        // Image resolution
        public const int IMAGE_RESOLUTION_500 = 0;
        public const int IMAGE_RESOLUTION_1000 = 1;

        // Capture options
        public const uint AUTO_CAPTURE_ENABLED = 0x00000001;
        public const uint QUALITY_CHECK_ENABLED = 0x00000002;

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

        // Image data structure from scanner
        [StructLayout(LayoutKind.Sequential)]
        public struct IBSU_ImageData
        {
            public IntPtr Buffer; // Pointer to image buffer (grayscale pixel data)

            public uint Width; // Image width in pixels

            public uint Height; // Image height in pixels

            public double ResolutionX; // Horizontal resolution (DPI)

            public double ResolutionY; // Vertical resolution (DPI)

            public uint BitsPerPixel; // Bits per pixel (usually 8)

            public uint Format; // Image format

            [MarshalAs(UnmanagedType.I1)]
            public bool IsFinal; // Whether this is final image or preview

            public ulong FrameTime; // Frame capture time

            public uint FrameIndex; // Sequence of frame in current capture
        }

        // Device description structure
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct IBSU_DeviceDescription
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string ProductName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string SerialNumber;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string FirmwareVersion;

            public uint ProductId;
            public uint VendorId;
        }

        #endregion

        #region Delegates (Callbacks)

        // Preview image callback delegate
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void PreviewImageCallback(
            int deviceHandle,
            IBSU_ImageData imageData,
            IntPtr pContext);

        // Result image callback delegate
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ResultImageCallback(
            int deviceHandle,
            IBSU_ImageData imageData,
            IntPtr pContext);

        // Device count changed callback delegate
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void DeviceCountCallback(
            int deviceCount,
            IntPtr pContext);

        // Finger quality callback delegate
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void FingerQualityCallback(
            int deviceHandle,
            int fingerQuality,
            IntPtr pContext);

        #endregion

        #region Native Functions

        // Gets the number of currently connected scanner devices
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_GetDeviceCount();

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

        // Checks if image capture is currently active
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool IBSU_IsCaptureActive(int deviceHandle);

        // Registers callback functions for scanner events
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int IBSU_RegisterCallbacks(
            int eventType,
            PreviewImageCallback previewCallback,
            ResultImageCallback resultCallback,
            DeviceCountCallback deviceCountCallback,
            FingerQualityCallback fingerQualityCallback,
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
