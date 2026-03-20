using System;

namespace SecureVoteApp.Services.Scanner
{
    // Event arguments for fingerprint scanner events
    public class ScannerEventArgs : EventArgs
    {
        public byte[]? ImageData { get; set; } // Raw image data bytes from the scanner

        public uint Width { get; set; } // Image width in pixels

        public uint Height { get; set; } // Image height in pixels

        public double ResolutionX { get; set; } // Horizontal resolution (DPI)

        public double ResolutionY { get; set; } // Vertical resolution (DPI)

        public uint BitsPerPixel { get; set; } // Bits per pixel (usually 8 for grayscale)

        public int QualityScore { get; set; } // Image quality score (0-100)

        public bool IsFinalImage { get; set; } // Whether this is the final image or a preview

        public string? ErrorMessage { get; set; } // Error message if capture failed

        public bool IsSuccess { get; set; } // Indicates if capture was successful
    }
}
