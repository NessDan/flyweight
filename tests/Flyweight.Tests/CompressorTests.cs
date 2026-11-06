using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>End-to-end runs against real ffmpeg on generated clips.</summary>
    public class CompressorTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();
        private readonly Compressor _compressor = new Compressor(TestVideos.Tools);

        public void Dispose() => _videos.Dispose();

        private sealed class Recorder : IProgress<CompressionProgress>
        {
            private readonly object _lock = new object();
            private readonly List<CompressionProgress> _items = new List<CompressionProgress>();
            public Action<CompressionProgress> OnReport;

            public List<CompressionProgress> Items
            {
                get { lock (_lock) return _items.ToList(); }
            }

            public void Report(CompressionProgress value)
            {
                lock (_lock) _items.Add(value);
                OnReport?.Invoke(value);
            }
        }

        private static LevelSettings Cpu(int quality = 30, VideoCodec codec = VideoCodec.Hevc)
        {
            return new LevelSettings { Codec = codec, Encoder = EncoderChoice.Cpu, Quality = quality, AudioKbps = 128, ExtraArgs = "" };
        }

        private static LevelSettings Auto(int quality = 30, VideoCodec codec = VideoCodec.Hevc)
        {
            return new LevelSettings { Codec = codec, Encoder = EncoderChoice.Auto, Quality = quality, AudioKbps = 128, ExtraArgs = "" };
        }

        private CompressionResult Run(string input, LevelSettings settings, string folder = null, IProgress<CompressionProgress> progress = null)
        {
            return _compressor.Run(input, settings, folder, progress, CancellationToken.None);
        }

        private static string Hash(string path)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(path))
            {
                return Convert.ToBase64String(md5.ComputeHash(stream));
            }
        }

        // ---- the normal path ----

        [Fact]
        public void Makes_a_smaller_copy_next_to_the_original_and_leaves_the_original_alone()
        {
            string clip = _videos.MakeBulkyClip("clip.mov");
            string before = Hash(clip);

            var result = Run(clip, Cpu());

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal(_videos.PathOf("clip_compressed.mp4"), result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));
            Assert.True(result.OutputBytes < result.InputBytes);
            Assert.Equal(new FileInfo(result.OutputPath).Length, result.OutputBytes);
            Assert.Equal(new FileInfo(clip).Length, result.InputBytes);
            Assert.Equal(before, Hash(clip));
            Assert.False(File.Exists(result.OutputPath + ".part"));
        }

        [Fact]
        public void Uses_the_graphics_card_when_it_can()
        {
            if (!TestVideos.HasNvenc) return;
            string clip = _videos.MakeBulkyClip("clip.mov");

            var result = Run(clip, Auto());

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal(EncoderKind.Nvenc, result.EncoderUsed);
            Assert.True(result.OutputBytes < result.InputBytes);
        }

        [Theory]
        [InlineData(VideoCodec.H264, "h264")]
        [InlineData(VideoCodec.Hevc, "hevc")]
        [InlineData(VideoCodec.Av1, "av1")]
        public void Every_codec_produces_a_file_of_that_codec_on_the_cpu(VideoCodec codec, string expected)
        {
            string clip = _videos.MakeBulkyClip("clip.mov", seconds: 1);
            var result = Run(clip, Cpu(30, codec));
            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal(expected, TestVideos.Probe(result.OutputPath).Video.Codec);
        }

        [Theory]
        [InlineData(VideoCodec.H264, "h264")]
        [InlineData(VideoCodec.Hevc, "hevc")]
        [InlineData(VideoCodec.Av1, "av1")]
        public void Every_codec_produces_a_file_of_that_codec_on_the_gpu(VideoCodec codec, string expected)
        {
            if (!TestVideos.HasNvenc) return;
            string clip = _videos.MakeBulkyClip("clip.mov", seconds: 1);
            var result = Run(clip, Auto(30, codec));
            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal(EncoderKind.Nvenc, result.EncoderUsed);
            Assert.Equal(expected, TestVideos.Probe(result.OutputPath).Video.Codec);
        }

        [Fact]
        public void Falls_back_to_the_processor_when_the_graphics_card_cannot_do_it()
        {
            // NVENC refuses frames this small.
            string clip = _videos.Make("tiny.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=32x32:r=30:d=2", "-c:v", "mpeg4", "-q:v", "1");

            var result = Run(clip, Auto(20, VideoCodec.H264));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal(EncoderKind.Cpu, result.EncoderUsed);
        }

        [Fact]
        public void Reports_climbing_progress_ending_at_the_finish()
        {
            string clip = _videos.MakeBulkyClip("clip.mov");
            var progress = new Recorder();

            Run(clip, Cpu(), progress: progress);

            var percents = progress.Items.Select(p => p.Percent).ToList();
            Assert.NotEmpty(percents);
            Assert.Equal(percents.OrderBy(p => p), percents);
            Assert.True(percents.Last() > 90, "last was " + percents.Last());
        }

        [Fact]
        public void A_second_run_does_not_overwrite_the_first_result()
        {
            string clip = _videos.MakeBulkyClip("clip.mov", seconds: 1);
            var first = Run(clip, Cpu());
            var second = Run(clip, Cpu());
            Assert.Equal(_videos.PathOf("clip_compressed.mp4"), first.OutputPath);
            Assert.Equal(_videos.PathOf("clip_compressed (2).mp4"), second.OutputPath);
            Assert.True(File.Exists(first.OutputPath) && File.Exists(second.OutputPath));
        }

        [Fact]
        public void Can_save_into_a_folder_that_does_not_exist_yet()
        {
            string clip = _videos.MakeBulkyClip("clip.mov", seconds: 1);
            string folder = Path.Combine(_videos.Folder, "new", "folder");

            var result = Run(clip, Cpu(), folder);

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal(Path.Combine(folder, "clip_compressed.mp4"), result.OutputPath);
        }

        [Fact]
        public void Handles_awkward_file_names()
        {
            string clip = _videos.MakeBulkyClip("Chicago's 動画 (1) [final] 100%.mov", seconds: 1);
            var result = Run(clip, Cpu());
            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal(_videos.PathOf("Chicago's 動画 (1) [final] 100%_compressed.mp4"), result.OutputPath);
        }

        [Fact]
        public void Carries_over_the_file_dates()
        {
            string clip = _videos.MakeBulkyClip("clip.mov", seconds: 1);
            var when = new DateTime(2020, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(clip, when);
            File.SetCreationTimeUtc(clip, when);

            var result = Run(clip, Cpu());

            Assert.Equal(when, File.GetLastWriteTimeUtc(result.OutputPath));
            Assert.Equal(when, File.GetCreationTimeUtc(result.OutputPath));
        }

        // ---- when compressing isn't worth it ----

        [Fact]
        public void Keeps_the_original_when_the_result_would_not_be_smaller()
        {
            // Already starved of bits, so re-encoding at good quality can only grow it.
            string clip = _videos.Make("starved.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=3",
                "-c:v", "libx265", "-preset", "ultrafast", "-b:v", "20k", "-x265-params", "log-level=error");

            var result = Run(clip, Cpu(10));

            Assert.Equal(JobOutcome.AlreadySmall, result.Outcome);
            Assert.False(File.Exists(_videos.PathOf("starved_compressed.mp4")));
            Assert.False(File.Exists(_videos.PathOf("starved_compressed.mp4.part")));
            Assert.True(File.Exists(clip));
        }

        [Theory]
        [InlineData(1000, 1200, true)] // grew
        [InlineData(1000, 950, true)]  // barely helped
        [InlineData(1000, 900, true)]
        [InlineData(1000, 899, false)] // worth keeping
        public void A_level_that_barely_helps_tries_harder(long input, long output, bool expected)
        {
            Assert.Equal(expected, Compressor.ShouldTryHarder(Presets.Default(LevelId.Balanced), input, output));
        }

        [Fact]
        public void A_quality_the_user_picked_is_never_second_guessed()
        {
            var settings = Presets.Default(LevelId.Balanced);
            settings.Quality = 20;
            Assert.False(Compressor.ShouldTryHarder(settings, 1000, 1200));
        }

        [Fact]
        public void A_size_limit_handles_its_own_retries()
        {
            var settings = Presets.Default(LevelId.Balanced);
            settings.TargetSizeMb = 10;
            Assert.False(Compressor.ShouldTryHarder(settings, 1000, 1200));
        }

        [Fact]
        public void Tightly_compressed_video_gets_a_second_pass_and_says_so()
        {
            // Squeezed hard enough that the usual setting can't help, but the harder one still can.
            string clip = _videos.Make("downloaded.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=4",
                "-c:v", "libx264", "-preset", "medium", "-crf", "30");
            var progress = new Recorder();

            var result = Run(clip, Presets.Default(LevelId.Light), progress: progress);

            Assert.Contains(progress.Items, p => p.Note != null && p.Note.Contains("harder"));
            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.True(result.OutputBytes < result.InputBytes);
            Assert.False(File.Exists(_videos.PathOf("downloaded_compressed.mp4.part")));
        }

        [Fact]
        public void Compressing_well_on_the_first_pass_needs_no_second_one()
        {
            string clip = _videos.MakeBulkyClip("clip.mov");
            var progress = new Recorder();

            var result = Run(clip, Presets.Default(LevelId.Balanced), progress: progress);

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.DoesNotContain(progress.Items, p => p.Note != null && p.Note.Contains("harder"));
        }

        [Fact]
        public void A_file_already_under_the_size_limit_is_left_alone_without_encoding()
        {
            string clip = _videos.MakeBulkyClip("clip.mov", seconds: 1);
            var settings = Cpu();
            settings.TargetSizeMb = 500;

            var result = Run(clip, settings);

            Assert.Equal(JobOutcome.AlreadySmall, result.Outcome);
            Assert.Contains("500 MB", result.Message);
            Assert.False(File.Exists(_videos.PathOf("clip_compressed.mp4")));
        }

        // ---- cancelling ----

        [Theory]
        [InlineData(EncoderChoice.Cpu)]
        [InlineData(EncoderChoice.Auto)]
        public void Cancelling_stops_the_work_and_leaves_nothing_behind(EncoderChoice encoder)
        {
            string clip = _videos.Make("long.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=1920x1080:r=60:d=40", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "30");
            string part = _videos.PathOf("long_compressed.mp4.part");

            using (var cancel = new CancellationTokenSource())
            {
                var progress = new Recorder { OnReport = _ => cancel.Cancel() };
                var settings = Cpu();
                settings.Encoder = encoder;

                var result = _compressor.Run(clip, settings, null, progress, cancel.Token);

                Assert.Equal(JobOutcome.Canceled, result.Outcome);
                Assert.False(File.Exists(part));
                Assert.False(File.Exists(_videos.PathOf("long_compressed.mp4")));
            }
        }

        [Fact]
        public void Cancelling_before_starting_does_nothing()
        {
            string clip = _videos.MakeBulkyClip("clip.mov", seconds: 1);
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                var result = _compressor.Run(clip, Cpu(), null, null, cancel.Token);
                Assert.Equal(JobOutcome.Canceled, result.Outcome);
            }
        }

        // ---- problems ----

        [Fact]
        public void A_file_that_is_not_a_video_fails_with_a_readable_message()
        {
            string fake = _videos.PathOf("fake.mp4");
            File.WriteAllText(fake, "this is not a video");

            var result = Run(fake, Auto());

            Assert.Equal(JobOutcome.Failed, result.Outcome);
            Assert.Contains("couldn't be read", result.Message);
        }

        [Fact]
        public void A_missing_file_fails_with_a_readable_message()
        {
            var result = Run(_videos.PathOf("gone.mp4"), Auto());
            Assert.Equal(JobOutcome.Failed, result.Outcome);
            Assert.Contains("no longer", result.Message);
        }

        [Fact]
        public void An_audio_only_file_fails_with_a_readable_message()
        {
            string audio = _videos.Make("sound.mp4", "-f", "lavfi", "-i", "sine=duration=1", "-c:a", "aac");
            var result = Run(audio, Auto());
            Assert.Equal(JobOutcome.Failed, result.Outcome);
            Assert.Contains("video", result.Message);
        }

        [Fact]
        public void Bad_extra_options_fail_and_show_what_ffmpeg_said()
        {
            string clip = _videos.MakeBulkyClip("clip.mov", seconds: 1);
            var settings = Cpu();
            settings.ExtraArgs = "-this_is_not_an_option 1";

            var result = Run(clip, settings);

            Assert.Equal(JobOutcome.Failed, result.Outcome);
            Assert.Contains("this_is_not_an_option", result.Details);
            Assert.False(File.Exists(_videos.PathOf("clip_compressed.mp4.part")));
        }

        // ---- real-world quirks ----

        [Theory]
        [InlineData(EncoderChoice.Cpu)]
        [InlineData(EncoderChoice.Auto)]
        public void Sideways_phone_video_comes_out_upright_at_the_right_size(EncoderChoice encoder)
        {
            string flat = _videos.MakeBulkyClip("flat.mp4", seconds: 1);
            string rotated = _videos.PathOf("rotated.mp4");
            var made = _videos.Make("rotated.mp4", "-display_rotation", "90", "-i", flat, "-c", "copy");
            Assert.Equal(720, TestVideos.Probe(made).Video.DisplayWidth);

            var settings = Cpu();
            settings.Encoder = encoder;
            settings.MaxShortSide = 480;
            var result = Run(rotated, settings);

            Assert.Equal(JobOutcome.Done, result.Outcome);
            var video = TestVideos.Probe(result.OutputPath).Video;
            Assert.Equal(480, video.Width);
            Assert.Equal(854, video.Height);
            Assert.Equal(0, video.Rotation);
        }

        [Theory]
        [InlineData(EncoderChoice.Cpu)]
        [InlineData(EncoderChoice.Auto)]
        public void Full_range_video_is_really_converted_to_standard_range(EncoderChoice encoder)
        {
            // Busy enough to be worth compressing, with a block of pure white to measure.
            string clip = _videos.Make("fullrange.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=1",
                "-vf", "drawbox=x=0:y=0:w=200:h=200:color=white:t=fill,scale=out_range=pc,format=yuvj420p",
                "-c:v", "libx264", "-crf", "1", "-color_range", "pc");
            Assert.True(TestVideos.MaxLuma(clip) > 250);

            var settings = Cpu(30, VideoCodec.H264);
            settings.Encoder = encoder;
            var result = Run(clip, settings);

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.False(TestVideos.Probe(result.OutputPath).Video.IsFullRange);
            Assert.InRange(TestVideos.MaxLuma(result.OutputPath), 225, 245);
        }

        [Theory]
        [InlineData(EncoderChoice.Cpu)]
        [InlineData(EncoderChoice.Auto)]
        public void Odd_sized_video_is_handled(EncoderChoice encoder)
        {
            string clip = _videos.MakeBulkyClip("odd.mov", seconds: 1, size: "641x361");
            var settings = Cpu();
            settings.Encoder = encoder;

            var result = Run(clip, settings);

            Assert.Equal(JobOutcome.Done, result.Outcome);
            var video = TestVideos.Probe(result.OutputPath).Video;
            Assert.Equal(640, video.Width);
            Assert.Equal(360, video.Height);
        }

        [Fact]
        public void Every_audio_track_survives()
        {
            string clip = _videos.Make("tracks.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=2",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
                "-f", "lavfi", "-i", "sine=frequency=880:duration=2",
                "-map", "0", "-map", "1", "-map", "2", "-c:v", "mpeg4", "-q:v", "1", "-c:a", "aac", "-b:a", "256k");

            var result = Run(clip, Cpu());

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal(2, TestVideos.Probe(result.OutputPath).Audio.Count);
        }

        [Fact]
        public void Video_with_no_audio_is_fine()
        {
            string clip = _videos.Make("silent.mp4", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=1", "-c:v", "mpeg4", "-q:v", "1");
            var result = Run(clip, Cpu());
            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Empty(TestVideos.Probe(result.OutputPath).Audio);
        }

        [Fact]
        public void Subtitles_in_an_mkv_survive_the_move_to_mp4()
        {
            string srt = _videos.PathOf("subs.srt");
            File.WriteAllText(srt, "1\n00:00:00,000 --> 00:00:01,000\nHello\n");
            string clip = _videos.Make("subbed.mkv",
                "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=1", "-i", srt,
                "-map", "0", "-map", "1", "-c:v", "mpeg4", "-q:v", "1", "-c:s", "subrip");

            var result = Run(clip, Cpu());

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Single(TestVideos.Probe(result.OutputPath).TextSubtitleIndexes);
        }

        [Theory]
        [InlineData(EncoderChoice.Cpu)]
        [InlineData(EncoderChoice.Auto)]
        public void Hdr_stays_hdr_in_hevc(EncoderChoice encoder)
        {
            string clip = MakeHdrClip();
            var settings = Cpu(26);
            settings.Encoder = encoder;

            var result = Run(clip, settings);

            Assert.Equal(JobOutcome.Done, result.Outcome);
            var video = TestVideos.Probe(result.OutputPath).Video;
            Assert.Equal("smpte2084", video.ColorTransfer);
            Assert.Equal("bt2020", video.ColorPrimaries);
            Assert.True(video.IsHighBitDepth, video.PixFmt);
        }

        [Theory]
        [InlineData(EncoderChoice.Cpu)]
        [InlineData(EncoderChoice.Auto)]
        public void Hdr_is_tone_mapped_for_h264(EncoderChoice encoder)
        {
            string clip = MakeHdrClip();
            var settings = Cpu(23, VideoCodec.H264);
            settings.Encoder = encoder;

            var result = Run(clip, settings);

            Assert.Equal(JobOutcome.Done, result.Outcome);
            var video = TestVideos.Probe(result.OutputPath).Video;
            Assert.Equal("yuv420p", video.PixFmt);
            Assert.Equal("bt709", video.ColorTransfer);
            Assert.Equal("bt709", video.ColorPrimaries);
        }

        [Fact]
        public void Sources_the_gpu_decoder_cannot_read_still_encode_on_the_gpu()
        {
            if (!TestVideos.HasNvenc) return;
            string clip = _videos.Make("prores.mov",
                "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=1", "-c:v", "prores_ks", "-profile:v", "3", "-pix_fmt", "yuv422p10le");

            var result = Run(clip, Auto());

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal(EncoderKind.Nvenc, result.EncoderUsed);
        }

        // ---- fit under a size ----

        [Theory]
        [InlineData(EncoderChoice.Auto, VideoCodec.Hevc)]
        [InlineData(EncoderChoice.Auto, VideoCodec.H264)]
        [InlineData(EncoderChoice.Auto, VideoCodec.Av1)]
        [InlineData(EncoderChoice.Cpu, VideoCodec.Hevc)]
        [InlineData(EncoderChoice.Cpu, VideoCodec.H264)]
        [InlineData(EncoderChoice.Cpu, VideoCodec.Av1)]
        public void Fit_under_a_size_lands_under_it_without_wasting_much(EncoderChoice encoder, VideoCodec codec)
        {
            string clip = _videos.Make("busy.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=20,noise=alls=40:allf=t",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=20",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "10", "-c:a", "aac", "-b:a", "192k");
            var settings = Cpu(30, codec);
            settings.Encoder = encoder;
            settings.TargetSizeMb = 3;
            settings.AudioKbps = 96;

            var result = Run(clip, settings);

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.True(result.OutputBytes <= 3000000, "came out at " + result.OutputBytes);
            Assert.True(result.OutputBytes > 1500000, "wasted budget: " + result.OutputBytes);
        }

        private string MakeHdrClip()
        {
            // setparams tags the frames themselves; ffmpeg 8 ignores -color_trc on the output for filtered video.
            string clip = _videos.Make("hdr.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=1,format=yuv420p10le,setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc",
                "-c:v", "libx265", "-preset", "ultrafast", "-crf", "12", "-x265-params", "log-level=error");
            Assert.True(TestVideos.Probe(clip).Video.IsHdr);
            return clip;
        }
    }

    public class FFmpegLocatorTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "fw-loc-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private string Folder(string name, params string[] files)
        {
            string dir = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
            foreach (string file in files) File.WriteAllText(Path.Combine(dir, file), "x");
            return dir;
        }

        [Fact]
        public void Uses_the_first_folder_that_has_both_tools()
        {
            string partial = Folder("partial", "ffmpeg.exe");
            string complete = Folder("complete", "ffmpeg.exe", "ffprobe.exe");
            string later = Folder("later", "ffmpeg.exe", "ffprobe.exe");

            var tools = FFmpegLocator.FindIn(new[] { partial, complete, later });

            Assert.Equal(Path.Combine(complete, "ffmpeg.exe"), tools.FFmpeg);
            Assert.Equal(Path.Combine(complete, "ffprobe.exe"), tools.FFprobe);
        }

        [Fact]
        public void Reports_nothing_when_the_tools_are_missing()
        {
            Assert.Null(FFmpegLocator.FindIn(new[] { Folder("empty") }));
        }

        [Fact]
        public void Bad_folder_entries_are_ignored()
        {
            string good = Folder("good", "ffmpeg.exe", "ffprobe.exe");
            var tools = FFmpegLocator.FindIn(new[] { null, "", "  ", @"C:\does\not\exist", "bad|chars\"here", good });
            Assert.Equal(Path.Combine(good, "ffmpeg.exe"), tools.FFmpeg);
        }

        [Fact]
        public void Finds_the_ffmpeg_installed_on_this_machine()
        {
            var tools = FFmpegLocator.Find();
            Assert.NotNull(tools);
            Assert.True(File.Exists(tools.FFmpeg));
            Assert.True(File.Exists(tools.FFprobe));
        }

        [Fact]
        public void Looks_in_the_winget_install_location()
        {
            string packages = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\WinGet\Packages");
            if (!Directory.Exists(packages) || Directory.GetDirectories(packages, "*FFmpeg*").Length == 0) return;

            Assert.Contains(FFmpegLocator.SearchFolders(), d =>
                d.StartsWith(packages, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(d, "ffmpeg.exe")));
        }
    }
}
