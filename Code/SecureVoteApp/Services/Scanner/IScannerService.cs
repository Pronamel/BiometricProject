using System;
using System.Threading.Tasks;

namespace SecureVoteApp.Services.Scanner
{
    /// <summary>
    /// Interface for fingerprint scanner service operations
    /// </summary>
    public interface IScannerService
    {
        /// <summary>
        /// Event raised when a fingerprint preview image is available
        /// </summary>
        event EventHandler<ScannerEventArgs>? PreviewImageAvailable;

        event EventHandler<ScannerEventArgs>? FingerprintCaptured; // Fired when a final fingerprint image is captured

        /// <summary>
        /// Event raised when an error occurs
        /// </summary>
        event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// Gets the number of available scanner devices
        /// </summary>
        /// <returns>Number of connected scanners</returns>
        int GetDeviceCount();

        /// <summary>
        /// Gets the description of a specific device
        /// </summary>
        /// <param name="deviceIndex">Index of the device</param>
        /// <returns>Device description string</returns>
        string GetDeviceDescription(int deviceIndex);

        /// <summary>
        /// Opens a connection to a scanner device
        /// </summary>
        /// <param name="deviceIndex">Index of the device to open</param>
        /// <returns>True if successful, false otherwise</returns>
        bool OpenDevice(int deviceIndex);

        /// <summary>
        /// Closes the currently open scanner device
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        bool CloseDevice();

        /// <summary>
        /// Checks if a device is currently open
        /// </summary>
        /// <returns>True if device is open, false otherwise</returns>
        bool IsDeviceOpen();

        /// <summary>
        /// Begins fingerprint capture with the specified options
        /// </summary>
        /// <param name="imageType">Type of capture (2 = flat single finger, 1 = rolled finger)</param>
        /// <returns>True if capture started successfully, false otherwise</returns>
        bool StartCapture(int imageType = 2);

        /// <summary>
        /// Cancels the ongoing fingerprint capture
        /// </summary>
        /// <returns>True if successfully cancelled, false otherwise</returns>
        bool StopCapture();

        /// <summary>
        /// Checks if capture is currently active
        /// </summary>
        /// <returns>True if capture is in progress, false otherwise</returns>
        bool IsCaptureActive();

        /// <summary>
        /// Enables or disables spoof detection
        /// </summary>
        /// <param name="enabled">True to enable spoof detection</param>
        /// <param name="threshold">Spoof threshold value (0-10)</param>
        /// <returns>True if successful, false otherwise</returns>
        bool SetSpoofDetection(bool enabled, int threshold = 5);

        /// <summary>
        /// Checks if the last scanned finger is a spoof (fake)
        /// </summary>
        /// <returns>True if spoof detected, false if real finger</returns>
        bool IsSpoofFingerDetected();

        /// <summary>
        /// Disposes the scanner service and releases resources
        /// </summary>
        void Dispose();
    }
}
