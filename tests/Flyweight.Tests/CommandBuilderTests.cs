using System.Collections.Generic;
using System.Linq;
using Flyweight.Core;
using Xunit;
using static Flyweight.Tests.Fixtures;

namespace Flyweight.Tests
{
    public class CommandBuilderTests
    {
        private const string Input = @"C:\videos\in.mov";
        private const string Output = @"C:\videos\out.part";

        private static LevelSettings Settings(VideoCodec codec = VideoCodec.Hevc, int quality = 30, int maxShortSide = 0,
            int maxFps = 0, int audioKbps = 128, int targetMb = 0, string extra = "")
        {
            return new LevelSettings
            {
                Codec = codec,
                Encoder = EncoderChoice.Auto,
                Quality = quality,
                MaxShortSide = maxShortSide,
                MaxFps = maxFps,
                AudioKbps = audioKbps,
                TargetSizeMb = targetMb,
                ExtraArgs = extra,
            };
        }

        private static List<string> Build(MediaInfo media, LevelSettings settings, EncoderKind encoder = EncoderKind.Nvenc, double targetScale = 1.0)
        {
            return CommandBuilder.Build(Input, Output, media, settings, encoder, cpuThreads: 8, targetScale: targetScale);
        }

        private static string Value(List<string> args, string flag)
        {
            int i = args.IndexOf(flag);
            return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        }

        private static string Filter(List<string> args) => Value(args, "-vf") ?? "";

        // ---- choosing the encoder ----

        [Theory]
        [InlineData(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc")]
        [InlineData(VideoCodec.Hevc, EncoderKind.Nvenc, "hevc_nvenc")]
        [InlineData(VideoCodec.Av1, EncoderKind.Nvenc, "av1_nvenc")]
        [InlineData(VideoCodec.H264, EncoderKind.Cpu, "libx264")]
        [InlineData(VideoCodec.Hevc, EncoderKind.Cpu, "libx265")]
        [InlineData(VideoCodec.Av1, EncoderKind.Cpu, "libsvtav1")]
        public void Picks_the_encoder_for_codec_and_hardware(VideoCodec codec, EncoderKind kind, string expected)
        {
            var args = Build(Media(), Settings(codec), kind);
            Assert.Equal(expected, Value(args, "-c:v"));
        }

        [Fact]
        public void Gpu_quality_is_constant_quality_with_no_bitrate_ceiling()
        {
            var args = Build(Media(), Settings(quality: 31));
            Assert.Equal("31", Value(args, "-cq"));
            Assert.Equal("0", Value(args, "-b:v"));
            Assert.Equal("vbr", Value(args, "-rc"));
        }

        [Theory]
        [InlineData(VideoCodec.H264)]
        [InlineData(VideoCodec.Hevc)]
        [InlineData(VideoCodec.Av1)]
        public void Cpu_quality_is_crf_mapped_from_the_gpu_scale(VideoCodec codec)
        {
            var args = Build(Media(), Settings(codec, quality: 30), EncoderKind.Cpu);
            Assert.Equal(Presets.CpuCrf(codec, 30).ToString(), Value(args, "-crf"));
            Assert.Null(Value(args, "-cq"));
        }

        [Fact]
        public void Hevc_is_tagged_hvc1_so_apple_devices_can_play_it()
        {
            Assert.Equal("hvc1", Value(Build(Media(), Settings(VideoCodec.Hevc)), "-tag:v"));
            Assert.Equal("hvc1", Value(Build(Media(), Settings(VideoCodec.Hevc), EncoderKind.Cpu), "-tag:v"));
            Assert.Null(Value(Build(Media(), Settings(VideoCodec.H264)), "-tag:v"));
        }

        [Fact]
        public void Gpu_encode_also_decodes_on_the_gpu_to_keep_the_cpu_free()
        {
            var args = Build(Media(), Settings());
            Assert.Equal("cuda", Value(args, "-hwaccel"));
            Assert.True(args.IndexOf("-hwaccel") < args.IndexOf("-i"));
        }

        [Theory]
        [InlineData(VideoCodec.Hevc)]
        [InlineData(VideoCodec.Av1)]
        public void Gpu_encode_uses_both_encoder_chips_where_the_format_allows_it(VideoCodec codec)
        {
            // Measured 46-70% faster for about 1% more size. Ignored on cards with one chip.
            Assert.Equal("2", Value(Build(Media(), Settings(codec)), "-split_encode_mode"));
        }

        [Fact]
        public void H264_and_the_processor_have_no_second_chip_to_use()
        {
            Assert.Null(Value(Build(Media(), Settings(VideoCodec.H264)), "-split_encode_mode"));
            Assert.Null(Value(Build(Media(), Settings(VideoCodec.Hevc), EncoderKind.Cpu), "-split_encode_mode"));
        }

        [Fact]
        public void Cpu_encode_does_not_ask_for_hardware_decoding()
        {
            Assert.Null(Value(Build(Media(), Settings(), EncoderKind.Cpu), "-hwaccel"));
        }

        [Fact]
        public void Cpu_encode_is_limited_to_the_given_thread_count()
        {
            Assert.Equal("8", Value(Build(Media(), Settings(VideoCodec.H264), EncoderKind.Cpu), "-threads"));
            Assert.Contains("pools=8", Value(Build(Media(), Settings(VideoCodec.Hevc), EncoderKind.Cpu), "-x265-params"));
            Assert.Contains("lp=8", Value(Build(Media(), Settings(VideoCodec.Av1), EncoderKind.Cpu), "-svtav1-params"));
        }

        // ---- stream selection ----

        [Fact]
        public void Maps_exactly_the_video_and_audio_streams_by_index()
        {
            // Pixel phones put a data stream first; it must not be mapped.
            var media = Media(Video(index: 2), 60, Audio(index: 1));
            var maps = Build(media, Settings()).Select((a, i) => (a, i)).Where(x => x.a == "-map").Select(x => Build(media, Settings())[x.i + 1]).ToList();
            Assert.Equal(new[] { "0:2", "0:1" }, maps);
        }

        [Fact]
        public void Text_subtitles_are_kept_in_mp4_format()
        {
            var media = Media();
            media.TextSubtitleIndexes = new List<int> { 3 };
            var args = Build(media, Settings());
            Assert.Contains("0:3", args);
            Assert.Equal("mov_text", Value(args, "-c:s"));
        }

        [Fact]
        public void No_subtitle_flags_when_there_are_no_subtitles()
        {
            Assert.Null(Value(Build(Media(), Settings()), "-c:s"));
        }

        [Fact]
        public void A_file_without_video_is_rejected_with_a_readable_error()
        {
            var media = Media();
            media.Video = null;
            var ex = Assert.Throws<CompressionException>(() => Build(media, Settings()));
            Assert.Contains("video", ex.Message);
        }

        // ---- resolution ----

        [Theory]
        [InlineData(3840, 2160, 0, 1080, 1920, 1080)]  // 4K landscape
        [InlineData(3840, 2160, 90, 1080, 1080, 1920)] // 4K phone video filmed upright
        [InlineData(3840, 2160, -90, 720, 720, 1280)]
        [InlineData(1920, 1080, 0, 720, 1280, 720)]
        [InlineData(1920, 816, 0, 720, 1694, 720)]     // ultrawide stays in proportion and even
        [InlineData(1280, 720, 90, 480, 480, 854)]     // 853.33 rounds to the nearest even number, not down
        public void Downscales_so_the_short_side_fits_the_cap(int w, int h, int rotation, int cap, int expectW, int expectH)
        {
            var args = Build(Media(Video(w, h, rotation: rotation)), Settings(maxShortSide: cap));
            Assert.Contains($"scale={expectW}:{expectH}:flags=lanczos", Filter(args));
        }

        [Theory]
        [InlineData(1280, 720, 1080)]
        [InlineData(1920, 1080, 1080)]
        [InlineData(1920, 1080, 0)]
        public void Never_upscales(int w, int h, int cap)
        {
            Assert.DoesNotContain("scale=", Filter(Build(Media(Video(w, h)), Settings(maxShortSide: cap))));
        }

        [Fact]
        public void Odd_dimensions_are_cropped_to_even_because_encoders_require_it()
        {
            var filter = Filter(Build(Media(Video(1919, 1079)), Settings()));
            Assert.Contains("crop=1918:1078:0:0", filter);
            Assert.DoesNotContain("scale=", filter);
        }

        // ---- frame rate ----

        [Theory]
        [InlineData(60, 30, true)]
        [InlineData(119.8, 60, true)]
        [InlineData(30, 30, false)]
        [InlineData(29.97, 30, false)]
        [InlineData(60, 0, false)]
        public void Frame_rate_is_only_ever_lowered(double source, int cap, bool expectFilter)
        {
            var filter = Filter(Build(Media(Video(fps: source)), Settings(maxFps: cap)));
            Assert.Equal(expectFilter, filter.Contains($"fps={cap}"));
        }

        // ---- colour ----

        [Fact]
        public void Full_range_video_is_converted_to_standard_range()
        {
            // Pixel phones record yuvj420p. Left alone, many players show it washed out or crushed.
            var args = Build(Media(Video(pixFmt: "yuvj420p", range: "pc")), Settings());
            Assert.Contains("scale=out_range=tv", Filter(args));
        }

        [Fact]
        public void Full_range_conversion_combines_with_downscaling_in_one_filter()
        {
            var filter = Filter(Build(Media(Video(3840, 2160, pixFmt: "yuvj420p", range: "pc")), Settings(maxShortSide: 1080)));
            Assert.Contains("scale=1920:1080:flags=lanczos:out_range=tv", filter);
        }

        [Fact]
        public void Standard_range_video_needs_no_range_conversion()
        {
            Assert.DoesNotContain("out_range", Filter(Build(Media(), Settings())));
        }

        [Theory]
        [InlineData("tt", true)]
        [InlineData("bb", true)]
        [InlineData("tb", true)]
        [InlineData("bt", true)]
        [InlineData("progressive", false)]
        [InlineData(null, false)]
        public void Interlaced_video_is_deinterlaced(string field, bool expected)
        {
            Assert.Equal(expected, Filter(Build(Media(Video(field: field)), Settings())).Contains("bwdif"));
        }

        [Fact]
        public void Hdr_going_to_h264_is_tone_mapped_so_it_does_not_look_washed_out()
        {
            var media = Media(Video(pixFmt: "yuv420p10le", transfer: "smpte2084", primaries: "bt2020"));
            var args = Build(media, Settings(VideoCodec.H264));
            Assert.Contains("tonemap=", Filter(args));
            Assert.EndsWith("format=yuv420p", Filter(args));
        }

        [Theory]
        [InlineData("smpte2084")]
        [InlineData("arib-std-b67")]
        public void Hdr_going_to_hevc_stays_hdr_in_10_bit(string transfer)
        {
            var media = Media(Video(pixFmt: "yuv420p10le", transfer: transfer, primaries: "bt2020"));
            var args = Build(media, Settings(VideoCodec.Hevc));
            Assert.DoesNotContain("tonemap", Filter(args));
            Assert.EndsWith("format=p010le", Filter(args));
        }

        [Fact]
        public void Ten_bit_on_the_cpu_uses_the_software_pixel_format()
        {
            var media = Media(Video(pixFmt: "yuv420p10le", transfer: "smpte2084", primaries: "bt2020"));
            Assert.EndsWith("format=yuv420p10le", Filter(Build(media, Settings(VideoCodec.Hevc), EncoderKind.Cpu)));
        }

        [Fact]
        public void Eight_bit_sources_stay_eight_bit()
        {
            Assert.EndsWith("format=yuv420p", Filter(Build(Media(), Settings(VideoCodec.Hevc))));
        }

        [Fact]
        public void Ten_bit_sdr_going_to_h264_becomes_8_bit_without_tone_mapping()
        {
            var filter = Filter(Build(Media(Video(pixFmt: "yuv420p10le")), Settings(VideoCodec.H264)));
            Assert.EndsWith("format=yuv420p", filter);
            Assert.DoesNotContain("tonemap", filter);
        }

        // ---- audio ----

        [Fact]
        public void Aac_at_or_near_the_cap_is_copied_without_quality_loss()
        {
            Assert.Equal("copy", Value(Build(Media(null, 60, Audio(kbps: 196)), Settings(audioKbps: 192)), "-c:a:0"));
        }

        [Fact]
        public void Aac_above_the_cap_is_reencoded_to_the_cap()
        {
            var args = Build(Media(null, 60, Audio(kbps: 320)), Settings(audioKbps: 128));
            Assert.Equal("aac", Value(args, "-c:a:0"));
            Assert.Equal("128k", Value(args, "-b:a:0"));
        }

        [Fact]
        public void Audio_with_unknown_bitrate_is_reencoded()
        {
            // MKV files don't report per-stream bitrates.
            var args = Build(Media(null, 60, Audio(kbps: 0)), Settings(audioKbps: 128));
            Assert.Equal("aac", Value(args, "-c:a:0"));
            Assert.Equal("128k", Value(args, "-b:a:0"));
        }

        [Fact]
        public void Reencoding_low_bitrate_audio_never_raises_its_bitrate()
        {
            var args = Build(Media(null, 60, Audio(codec: "wmav2", kbps: 96)), Settings(audioKbps: 128));
            Assert.Equal("aac", Value(args, "-c:a:0"));
            Assert.Equal("96k", Value(args, "-b:a:0"));
        }

        [Fact]
        public void Every_audio_track_is_kept_and_handled_on_its_own()
        {
            // Capture tools record game audio and microphone as separate tracks.
            var media = Media(null, 60, Audio(index: 1, kbps: 196), Audio(index: 2, kbps: 320));
            var args = Build(media, Settings(audioKbps: 192));
            Assert.Contains("0:1", args);
            Assert.Contains("0:2", args);
            Assert.Equal("copy", Value(args, "-c:a:0"));
            Assert.Equal("aac", Value(args, "-c:a:1"));
            Assert.Equal("192k", Value(args, "-b:a:1"));
        }

        [Fact]
        public void Surround_audio_gets_proportionally_more_bitrate()
        {
            var args = Build(Media(null, 60, Audio(codec: "eac3", kbps: 640, channels: 6)), Settings(audioKbps: 128));
            Assert.Equal("384k", Value(args, "-b:a:0"));
        }

        [Theory]
        [InlineData("aac")]
        [InlineData("mp3")]
        [InlineData("ac3")]
        public void Keep_original_copies_audio_that_mp4_can_hold(string codec)
        {
            var args = Build(Media(null, 60, Audio(codec: codec, kbps: 640)), Settings(audioKbps: LevelSettings.AudioKeep));
            Assert.Equal("copy", Value(args, "-c:a:0"));
        }

        [Theory]
        [InlineData("pcm_s16le")]
        [InlineData("flac")]
        [InlineData("opus")]
        public void Keep_original_converts_audio_mp4_cannot_reliably_hold(string codec)
        {
            var args = Build(Media(null, 60, Audio(codec: codec, kbps: 0)), Settings(audioKbps: LevelSettings.AudioKeep));
            Assert.Equal("aac", Value(args, "-c:a:0"));
            Assert.Equal("256k", Value(args, "-b:a:0"));
        }

        [Fact]
        public void Audio_can_be_removed()
        {
            var args = Build(Media(null, 60, Audio()), Settings(audioKbps: LevelSettings.AudioRemove));
            Assert.Contains("-an", args);
            Assert.DoesNotContain("0:1", args);
        }

        // ---- output file ----

        [Fact]
        public void Keeps_metadata_and_writes_a_web_ready_mp4_with_progress_reporting()
        {
            var args = Build(Media(), Settings());
            Assert.Equal("0", Value(args, "-map_metadata"));
            Assert.Equal("0", Value(args, "-map_chapters"));
            Assert.Equal("+faststart", Value(args, "-movflags"));
            Assert.Equal("pipe:1", Value(args, "-progress"));
            Assert.Equal("mp4", Value(args, "-f"));
            Assert.Equal(Input, Value(args, "-i"));
            Assert.Equal(Output, args.Last());
        }

        [Fact]
        public void Extra_options_are_split_like_a_command_line_and_placed_before_the_output()
        {
            var args = Build(Media(), Settings(extra: "-metadata title=\"My Clip\" -g 120"));
            int title = args.IndexOf("title=My Clip");
            Assert.True(title > 0);
            Assert.Equal("-metadata", args[title - 1]);
            Assert.Equal("120", Value(args, "-g"));
            Assert.True(args.IndexOf("-g") < args.IndexOf("-f"));
            Assert.Equal(Output, args.Last());
        }

        // ---- fit under a size ----

        [Fact]
        public void Target_size_sets_a_bitrate_that_leaves_room_for_audio_and_container()
        {
            // 10 MB over 60 s = 1333 kbps; 5% safety margin = 1267; minus 96 kbps audio = 1171.
            var args = Build(Media(null, 60, Audio(kbps: 192)), Settings(audioKbps: 96, targetMb: 10));
            Assert.Equal("1171k", Value(args, "-b:v"));
            Assert.Null(Value(args, "-cq"));
            Assert.Equal("fullres", Value(args, "-multipass"));
            Assert.Equal("96k", Value(args, "-b:a:0"));
        }

        [Fact]
        public void Target_size_retry_scales_the_bitrate_down()
        {
            var args = Build(Media(null, 60, Audio(kbps: 192)), Settings(audioKbps: 96, targetMb: 10), targetScale: 0.9);
            // 1266.67 * 0.9 = 1140; minus 96 = 1044.
            Assert.Equal("1044k", Value(args, "-b:v"));
        }

        [Fact]
        public void Target_size_on_the_cpu_uses_a_bitrate_rather_than_crf()
        {
            var args = Build(Media(null, 60, Audio(kbps: 192)), Settings(audioKbps: 96, targetMb: 10), EncoderKind.Cpu);
            Assert.Equal("1171k", Value(args, "-b:v"));
            Assert.Null(Value(args, "-crf"));
        }

        [Fact]
        public void Target_size_keeps_only_the_first_audio_track_to_save_space()
        {
            var media = Media(null, 60, Audio(index: 1), Audio(index: 2));
            var args = Build(media, Settings(audioKbps: 96, targetMb: 10));
            Assert.Contains("0:1", args);
            Assert.DoesNotContain("0:2", args);
        }

        [Fact]
        public void Target_size_limits_audio_when_the_budget_is_tight()
        {
            // 10 MB over 300 s = 253 kbps after margin. Audio gets at most a fifth of that.
            var args = Build(Media(null, 300, Audio(kbps: 192)), Settings(audioKbps: 128, targetMb: 10));
            Assert.Equal("50k", Value(args, "-b:a:0"));
        }

        [Fact]
        public void Target_size_lowers_frame_rate_then_resolution_when_the_bitrate_would_look_terrible()
        {
            var args = Build(Media(Video(1920, 1080, fps: 60), 300, Audio(kbps: 192)), Settings(VideoCodec.Hevc, audioKbps: 128, targetMb: 10));
            Assert.Contains("fps=30", Filter(args));
            Assert.Contains("scale=640:360:flags=lanczos", Filter(args));
        }

        [Fact]
        public void Target_size_leaves_resolution_alone_when_there_is_plenty_of_bitrate()
        {
            var args = Build(Media(Video(1920, 1080, fps: 30), 30, Audio(kbps: 192)), Settings(VideoCodec.Hevc, audioKbps: 128, targetMb: 50));
            Assert.DoesNotContain("scale=", Filter(args));
            Assert.DoesNotContain("fps=", Filter(args));
        }

        [Fact]
        public void Target_size_that_cannot_fit_reports_a_readable_error()
        {
            var ex = Assert.Throws<CompressionException>(() =>
                Build(Media(null, 3600, Audio()), Settings(audioKbps: 128, targetMb: 1)));
            Assert.Contains("1 MB", ex.Message);
        }
    }
}
