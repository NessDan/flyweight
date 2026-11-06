using System;
using System.Globalization;

namespace Flyweight.Core
{
    /// <summary>Text the user reads.</summary>
    public static class Format
    {
        private const double Mb = 1024.0 * 1024;
        private const double Gb = Mb * 1024;

        public static string Size(long bytes)
        {
            var inv = CultureInfo.InvariantCulture;
            if (bytes >= Gb) return (bytes / Gb).ToString("0.0", inv) + " GB";
            if (bytes >= 10 * Mb) return (bytes / Mb).ToString("0", inv) + " MB";
            if (bytes >= Mb) return (bytes / Mb).ToString("0.0", inv) + " MB";
            return Math.Round(bytes / 1024.0, MidpointRounding.AwayFromZero).ToString("0", inv) + " KB";
        }

        public static string Clock(TimeSpan time)
        {
            int total = (int)Math.Round(time.TotalSeconds);
            int hours = total / 3600;
            int minutes = total / 60 % 60;
            int seconds = total % 60;
            return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes}:{seconds:00}";
        }

        public static string Savings(long before, long after)
        {
            int percent = before > 0 ? (int)Math.Round((1 - after / (double)before) * 100) : 0;
            return $"{Size(before)} → {Size(after)} · {percent}% smaller";
        }
    }
}
