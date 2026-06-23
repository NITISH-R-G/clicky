using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace clicky_windows
{
    public class ScreenCapture
    {
        public byte[] ImageData { get; set; } = Array.Empty<byte>();
        public int Width { get; set; }
        public int Height { get; set; }
        public string Label { get; set; } = "";
        public bool IsPrimary { get; set; }
        // Monitor origin in device-pixel space (top-left of the physical monitor).
        // CompanionManager adds this offset to POINT/DRAW coordinates so the overlay
        // can map them back to a specific screen.
        public int X { get; set; }
        public int Y { get; set; }
    }

    public static class ScreenCapturer
    {
        /// <summary>
        /// Captures larger than this on their longest side are downscaled to it before
        /// JPEG encoding. Matches the macOS reference (CompanionScreenCaptureUtility
        /// caps at 1280). Keeps base64 payloads small and AI cost predictable while
        /// preserving enough detail for UI-element recognition.
        /// </summary>
        public const int MaxCaptureDimension = 1280;

        // JPEG quality for screen captures. ~85 is visually lossless for UI text while
        // shrinking payloads substantially vs the encoder default.
        private const int JpegQuality = 85;

        // Solid fill used to blank our own windows out of the capture so the AI never
        // sees the blue cursor, drawings, or the settings window.
        private static readonly SolidBrush OwnWindowBlankingBrush = new SolidBrush(Color.FromArgb(20, 20, 20));

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int Size;
            public RECT MonitorRect;
            public RECT WorkRect;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateDC(string lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr ho);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private const uint SRCCOPY = 0x00CC0020;
        private const uint MONITORINFOF_PRIMARY = 0x00000001;

        public static List<ScreenCapture> CaptureAllScreens()
        {
            var captures = new List<ScreenCapture>();
            int monitorIndex = 1;

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var monitorInfo = new MONITORINFOEX();
                monitorInfo.Size = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    int monitorX = monitorInfo.MonitorRect.Left;
                    int monitorY = monitorInfo.MonitorRect.Top;
                    int monitorWidth = monitorInfo.MonitorRect.Right - monitorInfo.MonitorRect.Left;
                    int monitorHeight = monitorInfo.MonitorRect.Bottom - monitorInfo.MonitorRect.Top;

                    bool isPrimary = (monitorInfo.Flags & MONITORINFOF_PRIMARY) != 0;

                    byte[]? jpegData = CaptureScreenRect(monitorInfo.DeviceName, monitorX, monitorY, monitorWidth, monitorHeight);
                    if (jpegData != null)
                    {
                        captures.Add(new ScreenCapture
                        {
                            ImageData = jpegData,
                            Width = monitorWidth,
                            Height = monitorHeight,
                            Label = isPrimary ? "primary focus" : $"screen {monitorIndex}",
                            IsPrimary = isPrimary,
                            X = monitorX,
                            Y = monitorY
                        });
                        monitorIndex++;
                    }
                }
                return true;
            }, IntPtr.Zero);

            return captures;
        }

        private static byte[]? CaptureScreenRect(string deviceName, int monitorX, int monitorY, int monitorWidth, int monitorHeight)
        {
            IntPtr hdcSource = CreateDC(deviceName, null, null, IntPtr.Zero);
            if (hdcSource == IntPtr.Zero)
            {
                Console.WriteLine($"ScreenCapturer: CreateDC failed for {deviceName}");
                return null;
            }

            IntPtr hdcMemory = CreateCompatibleDC(hdcSource);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcSource, monitorWidth, monitorHeight);
            IntPtr hOldSelection = SelectObject(hdcMemory, hBitmap);

            byte[]? jpegBytes = null;
            try
            {
                // BitBlt the physical monitor into our bitmap. Done before any
                // System.Drawing work so the GDI handles below are the only thing
                // needing explicit teardown.
                BitBlt(hdcMemory, 0, 0, monitorWidth, monitorHeight, hdcSource, 0, 0, SRCCOPY);

                #pragma warning disable CA1416 // System.Drawing.Common is Windows-only; this project targets net8.0-windows
                using (var sourceBitmap = Image.FromHbitmap(hBitmap))
                using (var workBitmap = new Bitmap(monitorWidth, monitorHeight, PixelFormat.Format24bppRgb))
                using (var graphics = Graphics.FromImage(workBitmap))
                {
                    graphics.DrawImage(sourceBitmap, 0, 0, monitorWidth, monitorHeight);

                    // Blank out our own app windows so the AI never sees the blue
                    // cursor, drawings, or the settings window. Enumerate on demand
                    // each capture so windows opened/closed since are handled.
                    BlankOwnWindows(graphics, monitorX, monitorY, monitorWidth, monitorHeight);

                    // Downscale if the longest side exceeds the cap, preserving aspect.
                    // The coordinate space stored on ScreenCapture stays at NATIVE
                    // monitor dimensions (CompanionManager maps POINT/DRAW coords
                    // against native res); only the encoded image is resized.
                    Bitmap bitmapToEncode = workBitmap;
                    if (Math.Max(monitorWidth, monitorHeight) > MaxCaptureDimension)
                    {
                        (int scaledWidth, int scaledHeight) = ComputeScaledDimensions(monitorWidth, monitorHeight, MaxCaptureDimension);
                        bitmapToEncode = ResizeImage(workBitmap, scaledWidth, scaledHeight);
                    }

                    try
                    {
                        jpegBytes = EncodeJpeg(bitmapToEncode, JpegQuality);
                    }
                    finally
                    {
                        if (bitmapToEncode != workBitmap)
                        {
                            bitmapToEncode.Dispose();
                        }
                    }
                }
                #pragma warning restore CA1416
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ScreenCapturer: capture/encode failed: {ex}");
            }
            finally
            {
                // Restore the original selection before deleting the bitmap, then
                // release every GDI handle on every exit path (success, encode
                // failure, exception). Previously these were only released on the
                // success path, leaking handles if Image.FromHbitmap threw.
                if (hOldSelection != IntPtr.Zero)
                {
                    SelectObject(hdcMemory, hOldSelection);
                }
                if (hBitmap != IntPtr.Zero)
                {
                    DeleteObject(hBitmap);
                }
                if (hdcMemory != IntPtr.Zero)
                {
                    DeleteDC(hdcMemory);
                }
                if (hdcSource != IntPtr.Zero)
                {
                    DeleteDC(hdcSource);
                }
            }

            return jpegBytes;
        }

        /// <summary>
        /// Fills the rectangles of all visible top-level windows owned by this process
        /// that intersect the captured monitor, so the screenshot never includes the
        /// app's own UI (blue cursor overlay, drawings, settings window).
        /// </summary>
        private static void BlankOwnWindows(Graphics graphics, int monitorX, int monitorY, int monitorWidth, int monitorHeight)
        {
            int currentProcessId = Process.GetCurrentProcess().Id;
            var ownWindowRects = new List<RECT>();

            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }
                GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                if ((int)windowProcessId != currentProcessId)
                {
                    return true;
                }
                if (GetWindowRect(hWnd, out RECT windowRect))
                {
                    ownWindowRects.Add(windowRect);
                }
                return true;
            }, IntPtr.Zero);

            // The graphics surface is in monitor-local coordinates (0,0 = monitor
            // origin), so translate each global window rect by the monitor origin.
            foreach (RECT windowRect in ownWindowRects)
            {
                int blankX = Math.Max(0, windowRect.Left - monitorX);
                int blankY = Math.Max(0, windowRect.Top - monitorY);
                int blankRight = Math.Min(monitorWidth, windowRect.Right - monitorX);
                int blankBottom = Math.Min(monitorHeight, windowRect.Bottom - monitorY);
                if (blankRight > blankX && blankBottom > blankY)
                {
                    graphics.FillRectangle(OwnWindowBlankingBrush, blankX, blankY, blankRight - blankX, blankBottom - blankY);
                }
            }
        }

        /// <summary>
        /// Computes the (width, height) that fit inside <paramref name="maxDimension"/>
        /// on the longest side, preserving aspect ratio. Pure/testable.
        /// </summary>
        public static (int Width, int Height) ComputeScaledDimensions(int originalWidth, int originalHeight, int maxDimension)
        {
            if (originalWidth <= 0 || originalHeight <= 0)
            {
                return (originalWidth, originalHeight);
            }
            int longestSide = Math.Max(originalWidth, originalHeight);
            if (longestSide <= maxDimension)
            {
                return (originalWidth, originalHeight);
            }
            double scale = (double)maxDimension / longestSide;
            return (
                Math.Max(1, (int)Math.Round(originalWidth * scale)),
                Math.Max(1, (int)Math.Round(originalHeight * scale)));
        }

        /// <summary>
        /// High-quality resize using bicubic interpolation. Returns a new Bitmap the
        /// caller must dispose.
        /// </summary>
        private static Bitmap ResizeImage(Bitmap source, int targetWidth, int targetHeight)
        {
            var resized = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
            try
            {
                using (var graphics = Graphics.FromImage(resized))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(source, 0, 0, targetWidth, targetHeight);
                }
                return resized;
            }
            catch
            {
                resized.Dispose();
                throw;
            }
        }

        private static byte[] EncodeJpeg(Bitmap bitmap, int quality)
        {
            var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);

            ImageCodecInfo? jpegEncoder = GetImageEncoder(ImageFormat.Jpeg);
            using var memoryStream = new MemoryStream();
            if (jpegEncoder != null)
            {
                bitmap.Save(memoryStream, jpegEncoder, encoderParameters);
            }
            else
            {
                // Fallback: default JPEG encoder (no quality control) if codec lookup fails.
                bitmap.Save(memoryStream, ImageFormat.Jpeg);
            }
            return memoryStream.ToArray();
        }

        private static ImageCodecInfo? GetImageEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }
}
