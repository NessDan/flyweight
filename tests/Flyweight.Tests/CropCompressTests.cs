using System;
using System.IO;
using System.Threading;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// Real compresses of cropped videos, checked pixel by pixel against the part of the picture the
    /// box was over: that it's the very part, not one a pixel or two off, whatever the video.
    /// </summary>
    public class CropCompressTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();
        private readonly Compressor _compressor = new Compressor(TestVideos.Tools);

        public void Dispose() => _videos.Dispose();

        private static LevelSettings Level(EncoderChoice encoder = EncoderChoice.Cpu, int maxShortSide = 0, int targetMb = 0, int quality = 18)
        {
            return new LevelSettings
            {
                Codec = VideoCodec.Hevc, Encoder = encoder, Quality = quality, MaxShortSide = maxShortSide,
                AudioKbps = 96, TargetSizeMb = targetMb, ExtraArgs = "",
            };
        }

        private CompressionResult Run(string clip, LevelSettings settings, CropArea crop, TrimRange trim = null)
        {
            return _compressor.Run(clip, settings, null, null, CancellationToken.None, trim, crop);
        }

        private string Busy(string name, string size = "640x360", string extra = null)
        {
            var args = new System.Collections.Generic.List<string> { "-f", "lavfi", "-i", $"testsrc2=s={size}:r=30:d=1" };
            if (extra != null) args.AddRange(new[] { "-vf", extra });
            args.AddRange(new[] { "-c:v", "libx264", "-preset", "ultrafast", "-crf", "8", "-pix_fmt", "yuv420p" });
            return _videos.Make(name, args.ToArray());
        }

        private string OnItsSide(string flat, string name) => _videos.Make(name, "-display_rotation", "90", "-i", flat, "-c", "copy");

        /// <summary>
        /// Checks that the kept picture is the part of the original at (x, y): closer to it than to
        /// the same size of picture two pixels off in any direction there's room for, which a crop
        /// that was off would match.
        /// </summary>
        private static void AssertIsThePartAt(string original, string kept, int x, int y)
        {
            var whole = TestVideos.Gray(original);
            var part = TestVideos.Gray(kept);
            double here = TestVideos.Difference(whole, x, y, part);
            Assert.True(here < 6, $"The kept picture isn't that part of the original: {here:0.0} off at ({x}, {y})");

            int compared = 0;
            foreach (var (dx, dy) in new[] { (-2, 0), (2, 0), (0, -2), (0, 2) })
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx + part.Width > whole.Width || ny + part.Height > whole.Height) continue;
                double near = TestVideos.Difference(whole, nx, ny, part);
                Assert.True(here * 2 < near, $"It matches the part at ({nx}, {ny}) as well: {here:0.0} against {near:0.0}");
                compared++;
            }
            Assert.True(compared > 0, "There was no room to compare it with anything next to it");
        }

        private static void AssertUsedTheGraphicsCardIfAsked(EncoderChoice encoder, CompressionResult result)
        {
            // Otherwise a crop the graphics card refused would pass on the processor without anyone knowing.
            if (encoder == EncoderChoice.Auto && TestVideos.HasNvenc) Assert.Equal(EncoderKind.Nvenc, result.EncoderUsed);
        }

        private sealed class Notes : IProgress<CompressionProgress>
        {
            public readonly System.Collections.Generic.List<string> Seen = new System.Collections.Generic.List<string>();

            public void Report(CompressionProgress value)
            {
                lock (Seen) Seen.Add(value.Note);
            }
        }

        [Fact]
        public void A_crop_gets_no_second_harder_pass_for_falling_short_of_a_guess()
        {
            // A still picture with its sound: nearly all of the file is sound, which no crop makes
            // smaller. Judged against a quarter of the file, the copy would look as if the compress
            // had barely helped, and get a second, coarser pass for nothing.
            string clip = _videos.Make("talk.mp4", "-f", "lavfi", "-i", "color=c=gray:s=640x360:r=30:d=6",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=6",
                "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k");
            var settings = Presets.Default(LevelId.Balanced);
            settings.Encoder = EncoderChoice.Cpu;
            var notes = new Notes();

            var result = _compressor.Run(clip, settings, null, notes, CancellationToken.None, null, new CropArea(0.25, 0.25, 0.75, 0.75));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.NotEmpty(notes.Seen);
            Assert.DoesNotContain(notes.Seen, note => note != null && note.Contains("trying harder"));
        }

        [Theory]
        [InlineData(EncoderChoice.Cpu)]
        [InlineData(EncoderChoice.Auto)]
        public void An_off_centre_box_keeps_exactly_the_picture_under_it(EncoderChoice encoder)
        {
            string clip = Busy("clip.mp4");

            var result = Run(clip, Level(encoder), new CropArea(0.125, 0.1, 0.5, 0.6));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            AssertUsedTheGraphicsCardIfAsked(encoder, result);
            var video = TestVideos.Probe(result.OutputPath).Video;
            Assert.Equal((240, 180), (video.Width, video.Height));
            AssertIsThePartAt(clip, result.OutputPath, 80, 36);
        }

        [Theory]
        [InlineData(EncoderChoice.Cpu)]
        [InlineData(EncoderChoice.Auto)]
        public void A_sideways_video_is_cropped_as_it_is_shown_and_comes_out_upright(EncoderChoice encoder)
        {
            string clip = OnItsSide(Busy("flat.mp4"), "sideways.mp4");

            // Shown 360 wide and 640 tall.
            var result = Run(clip, Level(encoder), new CropArea(0.1, 0.2, 0.6, 0.5));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            AssertUsedTheGraphicsCardIfAsked(encoder, result);
            var video = TestVideos.Probe(result.OutputPath).Video;
            Assert.Equal((180, 192), (video.DisplayWidth, video.DisplayHeight));
            AssertIsThePartAt(clip, result.OutputPath, 36, 128);
        }

        [Theory]
        [InlineData(EncoderChoice.Cpu)]
        [InlineData(EncoderChoice.Auto)]
        public void A_video_with_stretched_pixels_keeps_its_shape(EncoderChoice encoder)
        {
            // Stored 480 by 360 and shown 640 by 360. Its right three quarters are shown 480 wide.
            string clip = Busy("stretched.mp4", "480x360", "setsar=4/3");

            var result = Run(clip, Level(encoder), new CropArea(0.25, 0, 1, 1));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            AssertUsedTheGraphicsCardIfAsked(encoder, result);
            var video = TestVideos.Probe(result.OutputPath).Video;
            Assert.Equal((360, 360), (video.Width, video.Height));
            Assert.Equal(4.0 / 3, video.PixelAspect, 3);
            AssertIsThePartAt(clip, result.OutputPath, 120, 0);
        }

        [Theory]
        [InlineData(EncoderChoice.Cpu)]
        [InlineData(EncoderChoice.Auto)]
        public void An_odd_sized_video_is_cropped_to_an_even_size(EncoderChoice encoder)
        {
            string clip = Busy("odd.mp4", "641x361");

            var result = Run(clip, Level(encoder), new CropArea(0.25, 0.25, 0.75, 0.75));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            AssertUsedTheGraphicsCardIfAsked(encoder, result);
            var video = TestVideos.Probe(result.OutputPath).Video;
            Assert.Equal((320, 180), (video.Width, video.Height));
            AssertIsThePartAt(clip, result.OutputPath, 160, 90);
        }

        [Theory]
        [InlineData(VideoCodec.Hevc)]
        [InlineData(VideoCodec.H264)]
        [InlineData(VideoCodec.Av1)]
        public void A_crop_as_small_as_the_box_allows_is_still_one_the_graphics_card_takes(VideoCodec codec)
        {
            // 160 by 80 pixels, the least the crop box lets through. Graphics cards won't encode much
            // less: on an RTX 5080, 146 by 50 for H.264, 130 by 34 for H.265 and 130 by 66 for AV1.
            string clip = Busy("big.mp4", "1920x1080");
            var settings = Level(EncoderChoice.Auto);
            settings.Codec = codec;
            settings.Quality = Presets.DefaultQuality(codec);

            var result = Run(clip, settings, new CropArea(0.5, 0.5, 0.5 + 160.0 / 1920, 0.5 + 80.0 / 1080));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal((160, 80), (TestVideos.Probe(result.OutputPath).Video.Width, TestVideos.Probe(result.OutputPath).Video.Height));
            AssertUsedTheGraphicsCardIfAsked(EncoderChoice.Auto, result);
        }

        [Fact]
        public void An_interlaced_video_is_cropped_once_its_fields_are_put_together()
        {
            string clip = _videos.Make("interlaced.mp4", "-f", "lavfi", "-i", "testsrc2=s=720x480:r=30:d=1",
                "-c:v", "libx264", "-preset", "ultrafast", "-flags", "+ilme+ildct", "-x264-params", "tff=1", "-pix_fmt", "yuv420p");
            Assert.True(TestVideos.Probe(clip).Video.IsInterlaced, "The test clip should say it's interlaced");

            var result = Run(clip, Level(), new CropArea(0.25, 0.25, 0.75, 0.75));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            var video = TestVideos.Probe(result.OutputPath).Video;
            Assert.Equal((360, 240), (video.Width, video.Height));
        }

        [Fact]
        public void A_part_bigger_than_the_level_allows_is_brought_down_in_its_own_shape()
        {
            string clip = Busy("big.mp4", "1280x720");

            // The left half is 640 wide and 720 tall, and comes down to 240 on its shorter side.
            var result = Run(clip, Level(maxShortSide: 240), new CropArea(0, 0, 0.5, 1));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            var video = TestVideos.Probe(result.OutputPath).Video;
            Assert.Equal((240, 270), (video.Width, video.Height));
        }

        [Fact]
        public void A_cropped_copy_is_named_for_it_and_a_trimmed_and_cropped_one_for_the_trim()
        {
            string clip = Busy("clip.mp4");

            var cropped = Run(clip, Level(), new CropArea(0, 0, 0.5, 0.5));
            var both = Run(clip, Level(), new CropArea(0, 0, 0.5, 0.5), new TrimRange(0.2, 0.7));

            Assert.Equal(_videos.PathOf("clip_cropped.mp4"), cropped.OutputPath);
            Assert.Equal(_videos.PathOf("clip_trimmed.mp4"), both.OutputPath);
            Assert.True(cropped.Cropped && both.Cropped);
            Assert.Null(cropped.KeptSeconds);
            Assert.Equal((320, 180), (TestVideos.Probe(both.OutputPath).Video.Width, TestVideos.Probe(both.OutputPath).Video.Height));
            Assert.InRange(TestVideos.Probe(both.OutputPath).DurationSeconds, 0.45, 0.55);
        }

        [Fact]
        public void A_box_around_the_whole_picture_is_an_ordinary_compress()
        {
            string clip = Busy("clip.mp4", "641x361");

            var result = Run(clip, Level(), CropArea.Whole);

            Assert.Equal(_videos.PathOf("clip_compressed.mp4"), result.OutputPath);
            Assert.False(result.Cropped);
        }

        [Fact]
        public void A_video_that_would_normally_be_left_alone_is_still_cropped()
        {
            // Already starved of bits, so a full compress gives up and keeps the original.
            string clip = _videos.Make("starved.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=2",
                "-c:v", "libx265", "-preset", "ultrafast", "-b:v", "20k", "-x265-params", "log-level=error");

            var result = Run(clip, Level(quality: 10), new CropArea(0, 0, 0.5, 0.5));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal((640, 360), (TestVideos.Probe(result.OutputPath).Video.Width, TestVideos.Probe(result.OutputPath).Video.Height));
        }

        [Fact]
        public void A_size_limit_the_whole_video_already_meets_does_not_stop_a_crop()
        {
            string clip = Busy("clip.mp4");

            var result = Run(clip, Level(targetMb: 500), new CropArea(0, 0, 0.5, 0.5));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal(320, TestVideos.Probe(result.OutputPath).Video.Width);
        }

        [Fact]
        public void A_size_limit_with_room_to_spare_for_the_part_kept_does_not_pad_it_out()
        {
            // The whole video is twice the limit, but a quarter of its picture is half of it.
            string clip = _videos.Make("busy.mp4", "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=4,noise=alls=30:allf=t",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "6", "-pix_fmt", "yuv420p");
            int limit = (int)(new FileInfo(clip).Length / 2 / 1000000);
            Assert.True(limit >= 2, "The test clip came out too small to test with");
            var quarter = new CropArea(0.25, 0.25, 0.75, 0.75);

            long withLimit = Run(clip, Level(targetMb: limit, quality: 28), quarter).OutputBytes;
            File.Delete(_videos.PathOf("busy_cropped.mp4"));
            long without = Run(clip, Level(quality: 28), quarter).OutputBytes;

            // With nothing to squeeze, the limit shouldn't change how the part kept is compressed.
            Assert.True(without > 0 && without < limit * 1000000L, $"{without} bytes without the limit");
            Assert.InRange((double)withLimit, without * 0.9, without * 1.1);
        }
    }
}
