using System;
using System.Collections.Generic;
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
        public int X { get; set; }
        public int Y { get; set; }
    }

    public static class ScreenCapturer
    {
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
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        private const uint SRCCOPY = 0x00CC0020;
        private const uint MONITORINFOF_PRIMARY = 0x00000001;

        public static List<ScreenCapture> CaptureAllScreens()
        {
            var captures = new List<ScreenCapture>();
            int monitorIndex = 1;

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new MONITORINFOEX();
                mi.Size = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    int x = mi.MonitorRect.Left;
                    int y = mi.MonitorRect.Top;
                    int width = mi.MonitorRect.Right - mi.MonitorRect.Left;
                    int height = mi.MonitorRect.Bottom - mi.MonitorRect.Top;

                    bool isPrimary = (mi.Flags & MONITORINFOF_PRIMARY) != 0;

                    byte[]? jpegData = CaptureScreenRect(mi.DeviceName, x, y, width, height);
                    if (jpegData != null)
                    {
                        captures.Add(new ScreenCapture
                        {
                            ImageData = jpegData,
                            Width = width,
                            Height = height,
                            Label = isPrimary ? "primary focus" : $"screen {monitorIndex}",
                            IsPrimary = isPrimary,
                            X = x,
                            Y = y
                        });
                        monitorIndex++;
                    }
                }
                return true;
            }, IntPtr.Zero);

            return captures;
        }

        private static byte[]? CaptureScreenRect(string deviceName, int x, int y, int width, int height)
        {
            IntPtr hdcSrc = CreateDC(deviceName, null, null, IntPtr.Zero);
            if (hdcSrc == IntPtr.Zero) return null;

            IntPtr hdcMem = CreateCompatibleDC(hdcSrc);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
            IntPtr hOld = SelectObject(hdcMem, hBitmap);

            BitBlt(hdcMem, 0, 0, width, height, hdcSrc, 0, 0, SRCCOPY);

            // Convert Bitmap to byte array (JPEG)
            byte[]? jpegBytes = null;
            try
            {
                using (var ms = new MemoryStream())
                {
                    // Save GDI bitmap as JPEG using System.Drawing (since GDI is Windows-only, we can use Bitmap class safely)
                    #pragma warning disable CA1416
                    using (var bmp = System.Drawing.Image.FromHbitmap(hBitmap))
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                    #pragma warning restore CA1416
                    jpegBytes = ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting bitmap: {ex}");
            }

            SelectObject(hdcMem, hOld);
            DeleteObject(hBitmap);
            DeleteDC(hdcMem);
            DeleteDC(hdcSrc);

            return jpegBytes;
        }
    }
}
