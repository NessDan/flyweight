using System;
using System.Globalization;

namespace Flyweight.Core
{
    /// <summary>The part of a video to keep, in seconds from its start, to the millisecond.</summary>
    public sealed class TrimRange
    {
        private const double ShortestSeconds = 0.001;

        public TrimRange(double startSeconds, double endSeconds)
        {
            if (double.IsNaN(startSeconds) || double.IsInfinity(startSeconds) || startSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startSeconds));
            }
            if (double.IsNaN(endSeconds) || double.IsInfinity(endSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(endSeconds));
            }

            StartSeconds = Round(startSeconds);
            EndSeconds = Round(endSeconds);
            if (EndSeconds - StartSeconds < ShortestSeconds / 2)
            {
                throw new ArgumentException("The end has to come after the start.", nameof(endSeconds));
            }
        }

        public double StartSeconds { get; }
        public double EndSeconds { get; }
        public double LengthSeconds => Round(EndSeconds - StartSeconds);

        /// <summary>The same range cut back to a video's real length, or null when none of it is left.</summary>
        public TrimRange ClampTo(double durationSeconds)
        {
            if (durationSeconds <= 0 || EndSeconds <= durationSeconds) return this;

            double end = Round(durationSeconds);
            return end - StartSeconds < ShortestSeconds ? null : new TrimRange(StartSeconds, end);
        }

        /// <summary>A time the way ffmpeg reads it.</summary>
        public static string Seconds(double seconds)
        {
            return seconds.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static double Round(double seconds) => Math.Round(seconds, 3, MidpointRounding.AwayFromZero);
    }
}
