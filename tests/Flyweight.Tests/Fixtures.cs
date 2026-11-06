using System.Collections.Generic;
using System.Linq;
using Flyweight.Core;

namespace Flyweight.Tests
{
    /// <summary>Builds MediaInfo values for command-building tests.</summary>
    internal static class Fixtures
    {
        public static VideoStreamInfo Video(int width = 1920, int height = 1080, double fps = 60, int rotation = 0,
            string pixFmt = "yuv420p", string range = "tv", string transfer = "bt709", string primaries = "bt709",
            string field = "progressive", int index = 0)
        {
            return new VideoStreamInfo
            {
                Index = index,
                Codec = "h264",
                Width = width,
                Height = height,
                Fps = fps,
                Rotation = rotation,
                PixFmt = pixFmt,
                ColorRange = range,
                ColorTransfer = transfer,
                ColorPrimaries = primaries,
                FieldOrder = field,
            };
        }

        public static AudioStreamInfo Audio(int index = 1, string codec = "aac", int kbps = 192, int channels = 2)
        {
            return new AudioStreamInfo { Index = index, Codec = codec, BitrateBps = kbps * 1000L, Channels = channels };
        }

        public static MediaInfo Media(VideoStreamInfo video = null, double duration = 60, params AudioStreamInfo[] audio)
        {
            return new MediaInfo
            {
                Video = video ?? Video(),
                DurationSeconds = duration,
                Audio = audio.ToList(),
                TextSubtitleIndexes = new List<int>(),
            };
        }
    }
}
