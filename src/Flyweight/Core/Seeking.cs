using System;

namespace Flyweight.Core
{
    /// <summary>
    /// How to get ffmpeg to the exact frame at a moment. Most video files list where their keyframes
    /// are, so ffmpeg can jump to the one before the moment and decode forward from it. Broadcast and
    /// camcorder streams (.ts, .mts, .mpg, .vob) have no such list: a jump lands mid-way between
    /// keyframes, and everything up to the next one is lost. For those, reading starts some way
    /// earlier and the unwanted lead-in is thrown away after decoding.
    /// </summary>
    public static class Seeking
    {
        /// <summary>Longer than keyframes are ever apart in practice, short enough to decode in a moment.</summary>
        public const double LongestRunUp = 10;

        /// <summary>How many seconds before a moment reading has to start, for this file.</summary>
        public static double RunUp(MediaInfo media, double seconds)
        {
            return media != null && media.HasKeyframeIndex ? 0 : Math.Max(0, Math.Min(seconds, LongestRunUp));
        }
    }
}
