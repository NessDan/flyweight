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

        /// <summary>An exact time as the trim controls show it: minutes, seconds and a fraction.</summary>
        public static string Timecode(double seconds, int decimals = 3)
        {
            double rounded = Math.Round(Math.Max(seconds, 0), decimals, MidpointRounding.AwayFromZero);
            int whole = (int)Math.Floor(rounded);
            int hours = whole / 3600;
            int minutes = whole / 60 % 60;
            string rest = (whole % 60 + rounded - whole).ToString("00." + new string('0', decimals), CultureInfo.InvariantCulture);
            return hours > 0 ? $"{hours}:{minutes:00}:{rest}" : $"{minutes}:{rest}";
        }

        /// <summary>Reads a typed-in time: seconds alone, or with minutes or hours in front.</summary>
        public static bool TryParseTimecode(string text, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] parts = text.Trim().Replace(',', '.').Split(':');
            if (parts.Length > 3) return false;

            double total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                // Only the seconds can have a fraction, and only a number with nothing in front can pass 59.
                var style = i == parts.Length - 1 ? NumberStyles.AllowDecimalPoint : NumberStyles.None;
                if (!double.TryParse(parts[i], style, CultureInfo.InvariantCulture, out double value)) return false;
                if (i > 0 && value >= 60) return false;
                total = total * 60 + value;
            }

            seconds = total;
            return true;
        }

        /// <summary>A video's name, along with which part of it is meant when that isn't all of it.</summary>
        public static string Part(string name, TrimRange part)
        {
            return part == null
                ? name
                : $"{name} · {Clock(TimeSpan.FromSeconds(part.StartSeconds))} to {Clock(TimeSpan.FromSeconds(part.EndSeconds))}";
        }

        public static string Trimmed(double keptSeconds, long bytes)
        {
            return $"Trimmed to {Clock(TimeSpan.FromSeconds(keptSeconds))} · {Size(bytes)}";
        }

        public static string Savings(long before, long after)
        {
            int percent = before > 0 ? (int)Math.Round((1 - after / (double)before) * 100) : 0;
            return $"{Size(before)} → {Size(after)} · {percent}% smaller";
        }
    }
}
