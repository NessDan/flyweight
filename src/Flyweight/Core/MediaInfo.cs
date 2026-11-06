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

        private bool IsSideways => Math.Abs(Rotation) % 180 == 90;

        public int DisplayWidth => IsSideways ? Height : Width;
        public int DisplayHeight => IsSideways ? Width : Height;

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

        public VideoStreamInfo Video { get; set; }
        public double DurationSeconds { get; set; }
        public List<AudioStreamInfo> Audio { get; set; } = new List<AudioStreamInfo>();
        public List<int> TextSubtitleIndexes { get; set; } = new List<int>();

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
                Video = video == null ? null : ToVideo(video),
                Audio = streams.Where(s => s.CodecType == "audio").Select(ToAudio).ToList(),
                TextSubtitleIndexes = streams
                    .Where(s => s.CodecType == "subtitle" && s.CodecName != null && TextSubtitleCodecs.Contains(s.CodecName))
                    .Select(s => s.Index)
                    .ToList(),
            };
        }

        private static VideoStreamInfo ToVideo(ProbeStream s)
        {
            double fps = ParseRate(s.AvgFrameRate);
            if (fps <= 0) fps = ParseRate(s.RFrameRate);

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
            [DataMember(Name = "avg_frame_rate")] public string AvgFrameRate { get; set; }
            [DataMember(Name = "r_frame_rate")] public string RFrameRate { get; set; }
            [DataMember(Name = "bit_rate")] public string BitRate { get; set; }
            [DataMember(Name = "channels")] public int Channels { get; set; }
            [DataMember(Name = "disposition")] public ProbeDisposition Disposition { get; set; }
            [DataMember(Name = "side_data_list")] public List<ProbeSideData> SideData { get; set; }
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
