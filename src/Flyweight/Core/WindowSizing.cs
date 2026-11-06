using System;

namespace Flyweight.Core
{
    /// <summary>Brings a window back at the size it was left at, as long as that makes sense on this screen.</summary>
    public static class WindowSizing
    {
        /// <returns>The size to use, or null if nothing usable was remembered.</returns>
        public static (double Width, double Height)? Fit(double savedWidth, double savedHeight, double minWidth, double minHeight, double screenWidth, double screenHeight)
        {
            if (!IsUsable(savedWidth) || !IsUsable(savedHeight)) return null;
            return (Clamp(savedWidth, minWidth, screenWidth), Clamp(savedHeight, minHeight, screenHeight));
        }

        private static bool IsUsable(double size) => size > 0 && !double.IsInfinity(size);

        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(value, max));
    }
}
