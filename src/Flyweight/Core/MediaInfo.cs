using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace Flyweight.Core
{
    public sealed class VideoStreamInfo
    {
        public int Index { get; set; }
        public string Codec { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Rotation { get; set; }
        public double Fps { get; set; }
        public string PixFmt { get; set; }
        public string ColorRange { get; set; }
        public string ColorTransfer { get; set; }
        public string ColorPrimaries { get; set; }
        public string FieldOrder { get; set; }

        /// <summary>Where the picture ends, in seconds from the start of the file. 0 when the file doesn't say.</summary>
        public double EndSeconds { get; set; }

        /// <summary>The time of the first frame on the file's own clock, as <see cref="MediaInfo.StartSeconds"/> is.</summary>
        public double StartSeconds { get; set; }

        /// <summary>
        /// How wide each stored pixel is shown, for each unit of its height. 1 for most video. Widescreen
        /// DVDs and some camcorders store a narrower picture than they show, and say so here.
        /// </summary>
        public double PixelAspect { get; set; } = 1;

        private bool IsSideways => Math.Abs(Rotation) % 180 == 90;

        /// <summary>The size of the picture in stored pixels, the right way up.</summary>
        public int DisplayWidth => IsSideways ? Height : Width;
        public int DisplayHeight => IsSideways ? Width : Height;

        /// <summary>How wide the picture is shown for its height: upright, with its pixels the shape they're shown.</summary>
        public double ShownAspect
        {
            get
            {
                if (DisplayWidth <= 0 || DisplayHeight <= 0) return 16.0 / 9;
                // On its side, a pixel's width is shown as height.
                return IsSideways ? DisplayWidth / (DisplayHeight * PixelAspect) : DisplayWidth * PixelAspect / DisplayHeight;
            }
        }

        /// <summary>The same video, as if its upright picture were only this big: what's left of it after a crop.</summary>
        internal VideoStreamInfo CroppedTo(int displayWidth, int displayHeight)
        {
            var cropped = (VideoStreamInfo)MemberwiseClone();
            cropped.Width = IsSideways ? displayHeight : displayWidth;
            cropped.Height = IsSideways ? displayWidth : displayHeight;
            return cropped;
        }

        public bool IsFullRange =>
            ColorRange == "pc" || (PixFmt != null && PixFmt.StartsWith("yuvj", StringComparison.Ordinal));

        public bool IsHdr => ColorTransfer == "smpte2084" || ColorTransfer == "arib-std-b67";

        public bool IsHighBitDepth => PixFmt != null && Regex.IsMatch(PixFmt, @"(9|1[0-6]|48|64)(le|be)$");

        public bool IsInterlaced =>
            FieldOrder == "tt" || FieldOrder == "bb" || FieldOrder == "tb" || FieldOrder == "bt";
    }

    public sealed class AudioStreamInfo
    {
        public int Index { get; set; }
        public string Codec { get; set; }
        public int Channels { get; set; }

        /// <summary>0 when the container doesn't say, which is normal for MKV.</summary>
        public long BitrateBps { get; set; }
    }

    /// <summary>What ffprobe found in a file.</summary>
    public sealed class MediaInfo
    {
        private static readonly HashSet<string> TextSubtitleCodecs = new HashSet<string>
        {
            "subrip", "srt", "ass", "ssa", "mov_text", "webvtt", "text",
        };

        // Checked on this app's test clips: a cut starting between keyframes comes out whole from these.
        private static readonly HashSet<string> IndexedFormats = new HashSet<string>
        {
            "mov", "mp4", "matroska", "webm", "avi", "flv", "asf",
        };

        public VideoStreamInfo Video { get; set; }
        public double DurationSeconds { get; set; }

        /// <summary>
        /// The time on the file's own clock at which it starts, which isn't always zero. Every other
        /// time in the app counts from the start of the file, as ffmpeg's -ss does.
        /// </summary>
        public double StartSeconds { get; set; }

        /// <summary>What kind of file ffmpeg says this is, such as "mov,mp4,m4a,3gp,3g2,mj2" or "mpegts".</summary>
        public string FormatName { get; set; }

        /// <summary>
        /// True for the kinds of file that list where their keyframes are, which is what lets ffmpeg
        /// jump straight to a moment and still land on the exact frame. See <see cref="Seeking"/>.
        /// Anything not known to is treated as if it doesn't, since that only costs a little time.
        /// </summary>
        public bool HasKeyframeIndex => (FormatName ?? "").Split(',').Any(IndexedFormats.Contains);
        public List<AudioStreamInfo> Audio { get; set; } = new List<AudioStreamInfo>();
        public List<int> TextSubtitleIndexes { get; set; } = new List<int>();

        /// <summary>The same file, with these facts about its video in place of its own.</summary>
        internal MediaInfo WithVideo(VideoStreamInfo video)
        {
            var copy = (MediaInfo)MemberwiseClone();
            copy.Video = video;
            return copy;
        }

        /// <summary>
        /// How long the picture lasts. A file's length is often a touch longer, because its sound runs
        /// on for a moment after the last frame, and there's nothing to show or cut there.
        /// </summary>
        public double PictureSeconds
        {
            get
            {
                if (Video == null || Video.EndSeconds <= 0) return DurationSeconds;
                return DurationSeconds > 0 ? Math.Min(DurationSeconds, Video.EndSeconds) : Video.EndSeconds;
            }
        }

        /// <summary>
        /// Reads a made-up answer from ffprobe with every part a real one can have. The first time in
        /// a run, .NET takes about a quarter of a second to work out how to read them, and this way
        /// that's done before the first real one comes, alongside whatever else is starting up.
        /// </summary>
        public static void WarmUp()
        {
            try
            {
                Parse(@"{ ""streams"": [ { ""index"": 0, ""codec_type"": ""video"", ""tags"": { ""DURATION"": ""1"" },
                    ""disposition"": { ""attached_pic"": 0 }, ""side_data_list"": [ { ""rotation"": 0 } ] } ],
                    ""format"": { ""duration"": ""1"" } }");
            }
            catch (Exception)
            {
                // Nothing is lost. The first real answer just takes as long as it always did.
            }
        }

        public static MediaInfo Parse(string json)
        {
            ProbeRoot root;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(ProbeRoot));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                {
                    root = (ProbeRoot)serializer.ReadObject(stream);
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is ArgumentException || ex is InvalidOperationException)
            {
                throw new CompressionException("This file couldn't be read as a video.");
            }

            var streams = root?.Streams ?? new List<ProbeStream>();
            var video = streams.FirstOrDefault(s => s.CodecType == "video" && s.Disposition?.AttachedPic != 1);

            return new MediaInfo
            {
                DurationSeconds = ParseDouble(root?.Format?.Duration),
                StartSeconds = ParseDouble(root?.Format?.StartTime),
                FormatName = root?.Format?.FormatName,
                Video = video == null ? null : ToVideo(video, ParseDouble(root?.Format?.StartTime)),
                Audio = streams.Where(s => s.CodecType == "audio").Select(ToAudio).ToList(),
                TextSubtitleIndexes = streams
                    .Where(s => s.CodecType == "subtitle" && s.CodecName != null && TextSubtitleCodecs.Contains(s.CodecName))
                    .Select(s => s.Index)
                    .ToList(),
            };
        }

        /// <param name="fileStart">The time the file's own clock starts at, which isn't always zero.</param>
        private static VideoStreamInfo ToVideo(ProbeStream s, double fileStart)
        {
            double fps = ParseRate(s.AvgFrameRate);
            if (fps <= 0) fps = ParseRate(s.RFrameRate);
            double length = ParseDouble(s.Duration);
            // MKV keeps each stream's length in a tag rather than in the usual place. Programs differ
            // on what it holds for a picture that starts late: its length, or the time it ends at.
            // Reading it as a length can only put the end too late, never too early, and the file's
            // own length then caps it, which is the harmless way to be wrong. Too early would hide
            // the last part of the video from the trim controls.
            if (length <= 0 && Format.TryParseTimecode(s.Tags?.Duration, out double tagged)) length = tagged;

            var rotation = s.SideData?.FirstOrDefault(d => d.Rotation.HasValue)?.Rotation ?? 0;

            return new VideoStreamInfo
            {
                Index = s.Index,
                Codec = s.CodecName,
                Width = s.Width,
                Height = s.Height,
                Rotation = (int)Math.Round(rotation),
                Fps = fps,
                PixFmt = s.PixFmt,
                ColorRange = s.ColorRange,
                ColorTransfer = s.ColorTransfer,
                ColorPrimaries = s.ColorPrimaries,
                FieldOrder = s.FieldOrder,
                PixelAspect = ParseAspect(s.SampleAspectRatio),
                StartSeconds = ParseDouble(s.StartTime),
                EndSeconds = length > 0 ? Math.Max(0, ParseDouble(s.StartTime) - fileStart) + length : 0,
            };
        }

        private static AudioStreamInfo ToAudio(ProbeStream s)
        {
            return new AudioStreamInfo
            {
                Index = s.Index,
                Codec = s.CodecName,
                Channels = s.Channels,
                BitrateBps = (long)ParseDouble(s.BitRate),
            };
        }

        private static double ParseDouble(string text)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        /// <summary>A ratio such as "32:27". Most files say "1:1", some "0:1" for "not known", which means square too.</summary>
        private static double ParseAspect(string ratio)
        {
            var parts = (ratio ?? "").Split(':');
            double width = parts.Length == 2 ? ParseDouble(parts[0]) : 0;
            double height = parts.Length == 2 ? ParseDouble(parts[1]) : 0;
            return width > 0 && height > 0 ? width / height : 1;
        }

        private static double ParseRate(string rate)
        {
            if (string.IsNullOrEmpty(rate)) return 0;
            var parts = rate.Split('/');
            double numerator = ParseDouble(parts[0]);
            double denominator = parts.Length > 1 ? ParseDouble(parts[1]) : 1;
            return denominator > 0 ? numerator / denominator : 0;
        }

        // The subset of ffprobe's JSON this app reads. Numbers ffprobe writes as strings stay strings here.
        [DataContract]
        private sealed class ProbeRoot
        {
            [DataMember(Name = "streams")] public List<ProbeStream> Streams { get; set; }
            [DataMember(Name = "format")] public ProbeFormat Format { get; set; }
        }

        [DataContract]
        private sealed class ProbeFormat
        {
            [DataMember(Name = "duration")] public string Duration { get; set; }
            [DataMember(Name = "start_time")] public string StartTime { get; set; }
            [DataMember(Name = "format_name")] public string FormatName { get; set; }
        }

        [DataContract]
        private sealed class ProbeStream
        {
            [DataMember(Name = "index")] public int Index { get; set; }
            [DataMember(Name = "codec_type")] public string CodecType { get; set; }
            [DataMember(Name = "codec_name")] public string CodecName { get; set; }
            [DataMember(Name = "width")] public int Width { get; set; }
            [DataMember(Name = "height")] public int Height { get; set; }
            [DataMember(Name = "pix_fmt")] public string PixFmt { get; set; }
            [DataMember(Name = "color_range")] public string ColorRange { get; set; }
            [DataMember(Name = "color_transfer")] public string ColorTransfer { get; set; }
            [DataMember(Name = "color_primaries")] public string ColorPrimaries { get; set; }
            [DataMember(Name = "field_order")] public string FieldOrder { get; set; }
            [DataMember(Name = "sample_aspect_ratio")] public string SampleAspectRatio { get; set; }
            [DataMember(Name = "avg_frame_rate")] public string AvgFrameRate { get; set; }
            [DataMember(Name = "r_frame_rate")] public string RFrameRate { get; set; }
            [DataMember(Name = "bit_rate")] public string BitRate { get; set; }
            [DataMember(Name = "start_time")] public string StartTime { get; set; }
            [DataMember(Name = "duration")] public string Duration { get; set; }
            [DataMember(Name = "tags")] public ProbeTags Tags { get; set; }
            [DataMember(Name = "channels")] public int Channels { get; set; }
            [DataMember(Name = "disposition")] public ProbeDisposition Disposition { get; set; }
            [DataMember(Name = "side_data_list")] public List<ProbeSideData> SideData { get; set; }
        }

        [DataContract]
        private sealed class ProbeTags
        {
            [DataMember(Name = "DURATION")] public string Duration { get; set; }
        }

        [DataContract]
        private sealed class ProbeDisposition
        {
            [DataMember(Name = "attached_pic")] public int AttachedPic { get; set; }
        }

        [DataContract]
        private sealed class ProbeSideData
        {
            [DataMember(Name = "rotation")] public double? Rotation { get; set; }
        }
    }
}
