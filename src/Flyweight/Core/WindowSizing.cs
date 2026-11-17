using System;

namespace Flyweight.Core
{
    /// <summary>
    /// Brings a window back at the width it was left at, as long as that makes sense on this screen.
    /// Its height is never brought back: it's always whatever the window's contents come to.
    /// </summary>
    public static class WindowSizing
    {
        /// <returns>The width to use, or null if nothing usable was remembered.</returns>
        public static double? Width(double saved, double min, double screen)
        {
            if (!(saved > 0) || double.IsInfinity(saved)) return null;
            return Math.Max(min, Math.Min(saved, screen));
        }
    }
}
