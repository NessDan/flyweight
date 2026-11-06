using System.Collections.Generic;

namespace Flyweight.Core
{
    /// <summary>How levels are described to the user.</summary>
    public static class LevelText
    {
        /// <summary>A few words for a custom preset's card.</summary>
        public static string Caption(LevelSettings settings)
        {
            if (settings.TargetSizeMb > 0) return $"Under {settings.TargetSizeMb} MB";

            string size = settings.MaxShortSide > 0 ? $"Up to {settings.MaxShortSide}p" : "Full resolution";
            return settings.MaxFps > 0 ? $"{size}, {settings.MaxFps} fps" : size;
        }

        public static string Summary(LevelId level, LevelSettings settings)
        {
            if (settings.SameAs(Presets.Default(level)))
            {
                switch (level)
                {
                    case LevelId.Light: return "Keeps the full resolution and frame rate, and still shrinks the file a lot.";
                    case LevelId.Balanced: return "Keeps the frame rate, and brings anything larger down to 1080p.";
                    default: return "The smallest files: brings video down to 720p at 30 fps.";
                }
            }

            return "Custom: " + Describe(settings);
        }

        /// <summary>Exactly what these settings will do.</summary>
        public static string Describe(LevelSettings settings)
        {
            var parts = new List<string> { CodecName(settings.Codec) };
            parts.Add(settings.MaxShortSide > 0 ? $"up to {settings.MaxShortSide}p" : "full resolution");
            if (settings.MaxFps > 0) parts.Add($"{settings.MaxFps} fps");
            parts.Add(settings.AudioKbps == LevelSettings.AudioRemove ? "no audio"
                : settings.AudioKbps == LevelSettings.AudioKeep ? "original audio"
                : $"{settings.AudioKbps} kbps audio");
            if (settings.TargetSizeMb > 0) parts.Add($"under {settings.TargetSizeMb} MB");
            if (settings.Encoder == EncoderChoice.Cpu) parts.Add("on the processor");

            return string.Join(" · ", parts);
        }

        private static string CodecName(VideoCodec codec)
        {
            switch (codec)
            {
                case VideoCodec.H264: return "H.264";
                case VideoCodec.Hevc: return "H.265";
                default: return "AV1";
            }
        }
    }
}
