using System.Runtime.Serialization;

namespace Flyweight.Core
{
    public enum LevelId { Light = 0, Balanced = 1, Maximum = 2 }

    public enum VideoCodec { H264 = 0, Hevc = 1, Av1 = 2 }

    /// <summary>What the user asked for. Auto means the graphics card, falling back to the processor.</summary>
    public enum EncoderChoice { Auto = 0, Cpu = 1 }

    /// <summary>The encoder actually used for an attempt.</summary>
    public enum EncoderKind { Nvenc, Cpu }

    /// <summary>
    /// Everything a compression level does. The Advanced panel edits these directly, so what the
    /// right-click menu runs and what the panel shows are always the same thing.
    /// </summary>
    [DataContract]
    public sealed class LevelSettings
    {
        public const int AudioKeep = 0;
        public const int AudioRemove = -1;

        [DataMember] public VideoCodec Codec { get; set; }
        [DataMember] public EncoderChoice Encoder { get; set; }

        /// <summary>On the GPU constant-quality scale; lower is better and bigger.</summary>
        [DataMember] public int Quality { get; set; }

        /// <summary>Limit for the shorter side in pixels, so portrait and landscape behave alike. 0 keeps the original.</summary>
        [DataMember] public int MaxShortSide { get; set; }

        /// <summary>0 keeps the original.</summary>
        [DataMember] public int MaxFps { get; set; }

        /// <summary>Stereo AAC bitrate limit, or <see cref="AudioKeep"/>, or <see cref="AudioRemove"/>.</summary>
        [DataMember] public int AudioKbps { get; set; }

        /// <summary>When above 0, aim for this file size instead of a quality.</summary>
        [DataMember] public int TargetSizeMb { get; set; }

        [DataMember] public string ExtraArgs { get; set; } = "";

        public LevelSettings Clone()
        {
            return (LevelSettings)MemberwiseClone();
        }

        public bool SameAs(LevelSettings other)
        {
            return other != null
                && Codec == other.Codec
                && Encoder == other.Encoder
                && Quality == other.Quality
                && MaxShortSide == other.MaxShortSide
                && MaxFps == other.MaxFps
                && AudioKbps == other.AudioKbps
                && TargetSizeMb == other.TargetSizeMb
                && (ExtraArgs ?? "") == (other.ExtraArgs ?? "");
        }
    }

    public static class Presets
    {
        private const VideoCodec DefaultCodec = VideoCodec.Hevc;
        private const int DefaultAudioKbps = 128;

        // One quality for each format, shared by every level: levels differ in the resolution and frame
        // rate they keep, and all of them should shrink a video properly. Chosen by scoring encodes of
        // real captures, phone video and downloads with VMAF, so each format looks about the same.
        // Follows the order of VideoCodec: H.264, H.265, AV1.
        private static readonly int[] QualityByCodec = { 32, 34, 44 };

        // Added to a GPU quality number to get the CRF that looks about the same on the CPU encoder.
        // Follows the order of VideoCodec.
        private static readonly int[] CpuCrfOffset = { -6, -7, 3 };

        public static int DefaultQuality(VideoCodec codec)
        {
            return QualityByCodec[(int)codec];
        }

        public static int CpuCrf(VideoCodec codec, int quality)
        {
            return quality + CpuCrfOffset[(int)codec];
        }

        /// <summary>True for the built-in quality number, as opposed to one the user set by hand.</summary>
        public static bool IsPresetQuality(VideoCodec codec, int quality)
        {
            return DefaultQuality(codec) == quality;
        }

        /// <summary>
        /// The first version saved every level even when untouched, so its defaults mean "never changed"
        /// rather than "chosen", and shouldn't hold anyone back from today's presets.
        /// </summary>
        public static bool IsRetiredDefault(LevelId level, LevelSettings settings)
        {
            var retired = new LevelSettings { Codec = VideoCodec.Hevc, Encoder = EncoderChoice.Auto, ExtraArgs = "" };
            switch (level)
            {
                case LevelId.Light:
                    retired.Quality = 28;
                    retired.AudioKbps = 192;
                    break;
                case LevelId.Balanced:
                    retired.Quality = 31;
                    retired.MaxShortSide = 1080;
                    retired.AudioKbps = 128;
                    break;
                default:
                    retired.Quality = 34;
                    retired.MaxShortSide = 720;
                    retired.MaxFps = 30;
                    retired.AudioKbps = 96;
                    break;
            }
            return settings.SameAs(retired);
        }

        public static LevelSettings Default(LevelId level)
        {
            var settings = new LevelSettings
            {
                Codec = DefaultCodec,
                Encoder = EncoderChoice.Auto,
                Quality = DefaultQuality(DefaultCodec),
                AudioKbps = DefaultAudioKbps,
                ExtraArgs = "",
            };

            if (level == LevelId.Balanced)
            {
                settings.MaxShortSide = 1080;
            }
            else if (level == LevelId.Maximum)
            {
                settings.MaxShortSide = 720;
                settings.MaxFps = 30;
            }

            return settings;
        }
    }
}
