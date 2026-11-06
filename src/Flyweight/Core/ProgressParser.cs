using System;
using System.Globalization;

namespace Flyweight.Core
{
    public sealed class ProgressSnapshot
    {
        public double Seconds { get; set; }
        public double Speed { get; set; }

        public double Percent(double durationSeconds)
        {
            if (durationSeconds <= 0) return 0;
            return Math.Max(0, Math.Min(100, Seconds / durationSeconds * 100));
        }

        public TimeSpan? Remaining(double durationSeconds)
        {
            if (Speed <= 0) return null;
            return TimeSpan.FromSeconds(Math.Max(0, (durationSeconds - Seconds) / Speed));
        }
    }

    /// <summary>Reads the key=value lines that ffmpeg writes for "-progress pipe:1".</summary>
    public sealed class ProgressParser
    {
        private double _seconds;
        private double _speed;

        /// <summary>Returns a snapshot at the end of each progress block, otherwise null.</summary>
        public ProgressSnapshot Feed(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;

            int equals = line.IndexOf('=');
            if (equals <= 0) return null;

            string key = line.Substring(0, equals);
            string value = line.Substring(equals + 1).Trim();

            switch (key)
            {
                case "out_time_us":
                    if (TryNumber(value, out double micros)) _seconds = micros / 1000000.0;
                    return null;
                case "speed":
                    if (TryNumber(value.TrimEnd('x'), out double speed)) _speed = speed;
                    return null;
                case "progress":
                    return new ProgressSnapshot { Seconds = _seconds, Speed = _speed };
                default:
                    return null;
            }
        }

        private static bool TryNumber(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
