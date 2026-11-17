using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Flyweight.Core
{
    /// <summary>Turns a probed video and a level's settings into ffmpeg arguments. Has no side effects.</summary>
    public static class CommandBuilder
    {
        internal const string ToneMapToSdr =
            "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv";

        private static readonly HashSet<string> CopyWithinCap = new HashSet<string> { "aac", "mp3" };
        private static readonly HashSet<string> CopyWhenKeeping = new HashSet<string> { "aac", "mp3", "ac3", "eac3" };

        private const int KeepFallbackKbps = 256;
        private const double CopyTolerance = 1.1;

        public static List<string> Build(string input, string output, MediaInfo media, LevelSettings settings,
            EncoderKind encoder, int cpuThreads, double targetScale = 1.0, TrimRange trim = null, CropArea crop = null)
        {
            var video = media.Video;
            if (video == null) throw new CompressionException("This file doesn't contain a video.");

            // A crop is the first thing done to the picture, so everything after it, the size cap and
            // what a size limit can afford included, goes by what's left of the picture. That's done on
            // copies, since the same facts about the video are used again if the compress is retried.
            var keep = crop?.In(video.DisplayWidth, video.DisplayHeight);
            if (keep.HasValue)
            {
                video = video.CroppedTo(keep.Value.Width, keep.Value.Height);
                media = media.WithVideo(video);
            }

            var plan = TargetPlan.Compute(media, settings, targetScale, trim?.LengthSeconds ?? media.DurationSeconds);

            var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-progress", "pipe:1", "-nostats" };
            if (encoder == EncoderKind.Nvenc) args.AddRange(new[] { "-hwaccel", "cuda" });
            double runUp = trim == null ? 0 : Seeking.RunUp(media, trim.StartSeconds);
            if (trim != null)
            {
                // Ahead of the input, -ss makes ffmpeg decode up to the exact frame instead of starting at
                // the nearest keyframe. It can afford to, because the video is re-encoded anyway. It's
                // given even for a cut from the very start, because it's also what makes every later
                // time count from the start of the file rather than from the file's own clock.
                args.AddRange(new[] { "-ss", TrimRange.Seconds(trim.StartSeconds - runUp) });
                // Only a limit on how much is read. Where the cut really ends is set in the filters.
                args.AddRange(new[] { "-t", TrimRange.Seconds(runUp + trim.LengthSeconds) });
            }
            args.AddRange(new[] { "-i", input });

            // After the input, -ss decodes and then drops that much: the lead-in a stream file needs.
            if (runUp > 0) args.AddRange(new[] { "-ss", TrimRange.Seconds(runUp) });

            var audioTracks = plan.IsActive ? media.Audio.Take(1).ToList() : media.Audio;
            bool removeAudio = settings.AudioKbps == LevelSettings.AudioRemove;

            args.AddRange(new[] { "-map", "0:" + video.Index });
            if (!removeAudio)
            {
                foreach (var track in audioTracks) args.AddRange(new[] { "-map", "0:" + track.Index });
            }
            // Subtitles and chapters keep the times they had in the whole video, and subtitles would
            // carry the words from the parts cut away, so a trimmed copy goes without both.
            var subtitles = trim == null ? media.TextSubtitleIndexes : new List<int>();
            foreach (int index in subtitles) args.AddRange(new[] { "-map", "0:" + index });
            args.AddRange(new[] { "-map_metadata", "0", "-map_chapters", trim == null ? "0" : "-1" });

            int maxShortSide = plan.IsActive ? plan.MaxShortSide : settings.MaxShortSide;
            int maxFps = plan.IsActive ? plan.MaxFps : settings.MaxFps;
            bool toneMap = video.IsHdr && settings.Codec == VideoCodec.H264;
            bool tenBit = video.IsHighBitDepth && settings.Codec != VideoCodec.H264 && !toneMap;

            string cropFilter = keep.HasValue ? $"crop={keep.Value.Width}:{keep.Value.Height}:{keep.Value.X}:{keep.Value.Y}" : null;
            string filters = BuildFilters(video, cropFilter, maxShortSide, maxFps, toneMap, tenBit, encoder);
            if (trim != null)
            {
                // ffmpeg counts -t from the first frame it keeps rather than from where it was told to
                // start, which lets one frame too many through whenever the start falls between
                // frames. Here the end is a fixed point on the same clock as the start.
                filters = $"trim=end={TrimRange.Seconds(runUp + trim.LengthSeconds)}," + filters;
            }
            args.AddRange(new[] { "-vf", filters });
            AddVideoEncoder(args, settings, encoder, plan, cpuThreads);

            if (removeAudio) args.Add("-an");
            else AddAudio(args, audioTracks, plan.IsActive ? plan.AudioKbps : settings.AudioKbps, mustEncode: trim != null);

            if (subtitles.Count > 0) args.AddRange(new[] { "-c:s", "mov_text" });

            args.AddRange(new[] { "-movflags", "+faststart", "-max_muxing_queue_size", "4096" });
            args.AddRange(ProcessArgs.Split(settings.ExtraArgs));
            args.AddRange(new[] { "-f", "mp4", output });
            return args;
        }

        /// <summary>The frame size after applying a limit to the shorter side. Never upscales; always even.</summary>
        public static (int Width, int Height) OutputSize(int width, int height, int maxShortSide)
        {
            int shortSide = Math.Min(width, height);
            if (maxShortSide > 0 && shortSide > maxShortSide)
            {
                double factor = maxShortSide / (double)shortSide;
                return (NearestEven(width * factor), NearestEven(height * factor));
            }
            return (width & ~1, height & ~1);
        }

        private static int NearestEven(double value)
        {
            return (int)Math.Round(value / 2) * 2;
        }

        /// <param name="cropFilter">A crop to make, or null. The other sizes are already those of what it leaves.</param>
        private static string BuildFilters(VideoStreamInfo video, string cropFilter, int maxShortSide, int maxFps, bool toneMap, bool tenBit, EncoderKind encoder)
        {
            var filters = new List<string>();

            if (video.IsInterlaced) filters.Add("bwdif=mode=send_frame:parity=auto:deint=all");
            // On whole frames, once the two fields are one picture, so an odd row can't swap them.
            if (cropFilter != null) filters.Add(cropFilter);
            if (maxFps > 0 && video.Fps > maxFps + 0.5) filters.Add("fps=" + maxFps);
            if (toneMap) filters.Add(ToneMapToSdr);

            var (width, height) = OutputSize(video.DisplayWidth, video.DisplayHeight, maxShortSide);
            bool shrinks = maxShortSide > 0 && Math.Min(video.DisplayWidth, video.DisplayHeight) > maxShortSide;
            bool convertRange = video.IsFullRange && !toneMap;

            if (shrinks)
            {
                filters.Add($"scale={width}:{height}:flags=lanczos" + (convertRange ? ":out_range=tv" : ""));
            }
            else
            {
                // A pixel of crop is invisible; rescaling for it would soften the whole picture.
                if (width != video.DisplayWidth || height != video.DisplayHeight) filters.Add($"crop={width}:{height}:0:0");
                if (convertRange) filters.Add("scale=out_range=tv");
            }

            filters.Add("format=" + (tenBit ? (encoder == EncoderKind.Nvenc ? "p010le" : "yuv420p10le") : "yuv420p"));
            return string.Join(",", filters);
        }

        private static void AddVideoEncoder(List<string> args, LevelSettings settings, EncoderKind encoder, TargetPlan plan, int cpuThreads)
        {
            if (encoder == EncoderKind.Nvenc) AddNvenc(args, settings, plan);
            else AddCpuEncoder(args, settings, plan, cpuThreads);

            if (settings.Codec == VideoCodec.Hevc) args.AddRange(new[] { "-tag:v", "hvc1" });
        }

        private static void AddNvenc(List<string> args, LevelSettings settings, TargetPlan plan)
        {
            string name = settings.Codec == VideoCodec.H264 ? "h264_nvenc" : settings.Codec == VideoCodec.Hevc ? "hevc_nvenc" : "av1_nvenc";
            // p5 measured 2.4-3x faster than the slowest setting, p7, for about 1% more size.
            args.AddRange(new[] { "-c:v", name, "-preset", "p5", "-tune", "hq", "-rc", "vbr" });

            if (plan.IsActive)
            {
                AddBitrate(args, plan.VideoKbps);
                args.AddRange(new[] { "-multipass", "fullres" });
            }
            else
            {
                args.AddRange(new[] { "-cq", settings.Quality.ToString(CultureInfo.InvariantCulture), "-b:v", "0" });
            }

            args.AddRange(new[] { "-rc-lookahead", "32", "-spatial-aq", "1", "-temporal-aq", "1", "-bf", "3", "-b_ref_mode", "middle" });

            // Both of a card's encoder chips: measured 46-70% faster for about 1% more size. Cards
            // with one chip ignore this, and H.264 doesn't have it.
            if (settings.Codec != VideoCodec.H264) args.AddRange(new[] { "-split_encode_mode", "2" });
        }

        private static void AddCpuEncoder(List<string> args, LevelSettings settings, TargetPlan plan, int threads)
        {
            string count = threads.ToString(CultureInfo.InvariantCulture);
            switch (settings.Codec)
            {
                case VideoCodec.H264:
                    args.AddRange(new[] { "-c:v", "libx264", "-preset", "medium", "-threads", count });
                    break;
                case VideoCodec.Hevc:
                    args.AddRange(new[] { "-c:v", "libx265", "-preset", "medium", "-x265-params", "log-level=error:pools=" + count });
                    break;
                default:
                    args.AddRange(new[] { "-c:v", "libsvtav1", "-preset", "6", "-svtav1-params", "lp=" + count });
                    break;
            }

            if (plan.IsActive)
            {
                if (settings.Codec == VideoCodec.Av1) args.AddRange(new[] { "-b:v", Kbps(plan.VideoKbps) });
                else AddBitrate(args, plan.VideoKbps);
            }
            else
            {
                args.AddRange(new[] { "-crf", Presets.CpuCrf(settings.Codec, settings.Quality).ToString(CultureInfo.InvariantCulture) });
            }
        }

        private static void AddBitrate(List<string> args, int videoKbps)
        {
            args.AddRange(new[] { "-b:v", Kbps(videoKbps), "-maxrate", Kbps(videoKbps * 3 / 2), "-bufsize", Kbps(videoKbps * 2) });
        }

        /// <param name="mustEncode">
        /// True for a trim. Copied sound can only be cut in whole packets, and ffmpeg copies it from the
        /// keyframe before the cut, so what was said before the cut would still be inside the file.
        /// </param>
        private static void AddAudio(List<string> args, List<AudioStreamInfo> tracks, int stereoKbps, bool mustEncode)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                int? bitrate = stereoKbps == LevelSettings.AudioKeep ? KeepBitrate(track, mustEncode) : CappedBitrate(track, stereoKbps, mustEncode);
                if (bitrate == null)
                {
                    args.AddRange(new[] { "-c:a:" + i, "copy" });
                }
                else
                {
                    args.AddRange(new[] { "-c:a:" + i, "aac", "-b:a:" + i, Kbps(bitrate.Value) });
                }
            }
        }

        /// <summary>Null means copy the track untouched.</summary>
        private static int? KeepBitrate(AudioStreamInfo track, bool mustEncode)
        {
            int generous = ForChannels(KeepFallbackKbps, track.Channels);
            if (!CopyWhenKeeping.Contains(track.Codec ?? "")) return generous;
            if (!mustEncode) return null;

            // Being redone only because it can't be copied, so nothing is skimped on.
            return Math.Max(generous, (int)(track.BitrateBps / 1000));
        }

        /// <summary>Null means copy the track untouched.</summary>
        private static int? CappedBitrate(AudioStreamInfo track, int stereoKbps, bool mustEncode)
        {
            int cap = ForChannels(stereoKbps, track.Channels);
            int sourceKbps = (int)(track.BitrateBps / 1000);
            bool couldCopy = CopyWithinCap.Contains(track.Codec ?? "") && sourceKbps > 0 && sourceKbps <= cap * CopyTolerance;

            if (couldCopy) return mustEncode ? cap : (int?)null;
            return sourceKbps > 0 ? Math.Min(cap, sourceKbps) : cap;
        }

        private static int ForChannels(int stereoKbps, int channels)
        {
            return channels > 2 ? stereoKbps * channels / 2 : stereoKbps;
        }

        private static string Kbps(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture) + "k";
        }

        /// <summary>The bitrate budget for fitting a video into a chosen file size.</summary>
        private sealed class TargetPlan
        {
            private const double SafetyMargin = 0.95; // Room for the container and for encoder overshoot.
            private const double MaxAudioShare = 0.2;
            private const int MinAudioKbps = 32;
            private const int MinVideoKbps = 30;
            private static readonly int[] ShortSideSteps = { 1080, 720, 540, 480, 360, 240 };

            public bool IsActive { get; private set; }
            public int VideoKbps { get; private set; }
            public int AudioKbps { get; private set; }
            public int MaxShortSide { get; private set; }
            public int MaxFps { get; private set; }

            /// <param name="lengthSeconds">How much video the budget has to cover: all of it, or the part being kept.</param>
            public static TargetPlan Compute(MediaInfo media, LevelSettings settings, double scale, double lengthSeconds)
            {
                if (settings.TargetSizeMb <= 0) return new TargetPlan();

                var video = media.Video;
                double seconds = Math.Max(lengthSeconds, 1);
                double totalKbps = settings.TargetSizeMb * 8000.0 / seconds * SafetyMargin * scale;

                bool hasAudio = media.Audio.Count > 0 && settings.AudioKbps != LevelSettings.AudioRemove;
                int wantedAudio = settings.AudioKbps == LevelSettings.AudioKeep ? 128 : settings.AudioKbps;
                int audioKbps = hasAudio ? Math.Min(wantedAudio, Math.Max(MinAudioKbps, (int)(totalKbps * MaxAudioShare))) : 0;

                int videoKbps = (int)Math.Round(totalKbps - audioKbps);
                if (videoKbps < MinVideoKbps)
                {
                    throw new CompressionException($"This video is too long to fit in {settings.TargetSizeMb} MB. Try a larger size.");
                }

                var plan = new TargetPlan
                {
                    IsActive = true,
                    VideoKbps = videoKbps,
                    AudioKbps = settings.AudioKbps == LevelSettings.AudioRemove ? LevelSettings.AudioRemove : audioKbps,
                    MaxShortSide = settings.MaxShortSide,
                    MaxFps = settings.MaxFps,
                };
                plan.FitPictureToBitrate(video, MinBitsPerPixel(settings.Codec));
                return plan;
            }

            /// <summary>Below a certain bits-per-pixel video turns to mush, so trade frames, then pixels.</summary>
            private void FitPictureToBitrate(VideoStreamInfo video, double minBitsPerPixel)
            {
                double fps = MaxFps > 0 ? Math.Min(video.Fps, MaxFps) : video.Fps;
                if (fps <= 0) fps = 30;

                if (fps > 30.5 && BitsPerPixel(video, MaxShortSide, fps) < minBitsPerPixel)
                {
                    fps = 30;
                    MaxFps = 30;
                }

                if (BitsPerPixel(video, MaxShortSide, fps) >= minBitsPerPixel) return;

                int sourceShort = Math.Min(video.DisplayWidth, video.DisplayHeight);
                int current = MaxShortSide > 0 ? Math.Min(MaxShortSide, sourceShort) : sourceShort;
                foreach (int step in ShortSideSteps.Where(s => s < current))
                {
                    MaxShortSide = step;
                    if (BitsPerPixel(video, step, fps) >= minBitsPerPixel) return;
                }
            }

            private double BitsPerPixel(VideoStreamInfo video, int maxShortSide, double fps)
            {
                var (width, height) = OutputSize(video.DisplayWidth, video.DisplayHeight, maxShortSide);
                return VideoKbps * 1000.0 / (width * (double)height * fps);
            }

            private static double MinBitsPerPixel(VideoCodec codec)
            {
                return codec == VideoCodec.H264 ? 0.04 : codec == VideoCodec.Hevc ? 0.025 : 0.02;
            }
        }
    }
}
