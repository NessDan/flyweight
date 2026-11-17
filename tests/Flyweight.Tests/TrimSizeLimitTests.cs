using System;
using System.Threading;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>How trimming meets the rules about size limits and videos that are already small.</summary>
    public class TrimSizeLimitTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();
        private readonly Compressor _compressor = new Compressor(TestVideos.Tools);

        public void Dispose() => _videos.Dispose();

        private static LevelSettings Level(int quality = 24, int targetMb = 0)
        {
            return new LevelSettings
            {
                Codec = VideoCodec.Hevc, Encoder = EncoderChoice.Cpu, Quality = quality, AudioKbps = 96, TargetSizeMb = targetMb, ExtraArgs = "",
            };
        }

        private CompressionResult Run(string input, LevelSettings settings, TrimRange trim)
        {
            return _compressor.Run(input, settings, null, null, CancellationToken.None, trim);
        }

        private string MakeClip()
        {
            return _videos.Make("clip.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=10",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=10",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "16", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k");
        }

        [Fact]
        public void A_video_that_would_normally_be_left_alone_is_still_trimmed()
        {
            // Already starved of bits, so a full compress gives up and keeps the original (see CompressorTests).
            string clip = _videos.Make("starved.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=3",
                "-c:v", "libx265", "-preset", "ultrafast", "-b:v", "20k", "-x265-params", "log-level=error");

            var result = Run(clip, Level(quality: 10), new TrimRange(1, 2));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal(_videos.PathOf("starved_trimmed.mp4"), result.OutputPath);
            Assert.InRange(TestVideos.Probe(result.OutputPath).DurationSeconds, 0.9, 1.1);
        }

        [Fact]
        public void A_size_limit_the_whole_video_already_meets_does_not_stop_a_trim()
        {
            string clip = MakeClip();

            // Far more than the clip weighs, which makes a full compress leave it alone.
            var result = Run(clip, Level(targetMb: 500), new TrimRange(2, 4));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.InRange(TestVideos.Probe(result.OutputPath).DurationSeconds, 1.9, 2.1);
        }

        [Fact]
        public void A_size_limit_with_room_to_spare_does_not_pad_the_file_out()
        {
            string clip = MakeClip();

            long withLimit = Run(clip, Level(targetMb: 500), new TrimRange(2, 4)).OutputBytes;
            long without = Run(clip, Level(), new TrimRange(2, 4)).OutputBytes;

            // With nothing to squeeze, the limit shouldn't change how the part is compressed.
            Assert.True(without > 0);
            Assert.InRange((double)withLimit, without * 0.9, without * 1.1);
        }

        [Fact]
        public void A_tight_size_limit_is_spent_on_the_kept_part()
        {
            string clip = _videos.Make("busy.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=12,noise=alls=40:allf=t",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=12",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "10", "-c:a", "aac", "-b:a", "192k");

            var result = Run(clip, Level(targetMb: 2), new TrimRange(4, 8));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.InRange(TestVideos.Probe(result.OutputPath).DurationSeconds, 3.9, 4.1);
            Assert.True(result.OutputBytes <= 2000000, "came out at " + result.OutputBytes);
            Assert.True(result.OutputBytes > 1000000, "wasted budget: " + result.OutputBytes);
        }

        [Theory]
        [InlineData(1000L, 2.0, 10.0, 200L)]
        [InlineData(1000L, 10.0, 10.0, 1000L)]
        [InlineData(1000L, 30.0, 10.0, 1000L)]   // never more than the whole file
        [InlineData(1000L, 2.0, 0.0, 1000L)]     // unknown length, so assume all of it
        public void The_kept_part_is_weighed_against_its_share_of_the_original(long input, double kept, double duration, long expected)
        {
            Assert.Equal(expected, Compressor.ShareOf(input, kept, duration));
        }
    }
}
