using System;

namespace clicky_windows
{
    /// <summary>
    /// Pure helpers for converting between the Win32 device-pixel coordinate space
    /// (used by MONITORINFO screen origins, GetCursorPos, and CompanionManager
    /// pointing coordinates) and the Avalonia DIP (device-independent pixel) space
    /// used by Canvas.SetLeft/SetTop.
    ///
    /// Why this exists: Screen.Bounds is a PixelRect in device pixels, but the old
    /// overlay code multiplied the screen origin by Scaling and then divided by
    /// Scaling. That multiply-then-divide collapses to the identity at 100% scale
    /// (so single-monitor setups worked) but produced drift on any multi-DPI setup
    /// because the origin was being scaled twice. All inputs here are device pixels
    /// except the final output, which is divided by Scaling exactly once.
    /// </summary>
    public static class CoordinateMath
    {
        /// <summary>
        /// Converts a single axis value from global device pixels to the overlay
        /// window's local DIP space. <paramref name="globalDevicePixel"/> is an
        /// absolute screen coordinate (e.g. from GetCursorPos or a resolved POINT
        /// tag), <paramref name="screenOriginDevicePixel"/> is the owning monitor's
        /// top-left on that axis (Screen.Bounds.X or .Y), and
        /// <paramref name="scaling"/> is Screen.Scaling.
        /// </summary>
        public static double ToLocalDip(double globalDevicePixel, double screenOriginDevicePixel, double scaling)
        {
            // Subtract in device pixels (both inputs are device px), then divide by
            // scaling once to land in DIPs for Canvas positioning.
            return (globalDevicePixel - screenOriginDevicePixel) / scaling;
        }
    }
}
