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
    /// <summary>Trimming against real ffmpeg, on a clip where every frame can be told apart.</summary>
    public class TrimEngineTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();
        private readonly Compressor _compressor = new Compressor(TestVideos.Tools);

        public void Dispose() => _videos.Dispose();

        private sealed class Percents : IProgress<CompressionProgress>
        {
            private readonly List<double> _seen = new List<double>();
            public double Highest { get { lock (_seen) return _seen.Count == 0 ? -1 : _seen.Max(); } }
            public void Report(CompressionProgress value) { lock (_seen) _seen.Add(value.Percent); }
        }

        private static LevelSettings Level(EncoderChoice encoder = EncoderChoice.Cpu)
        {
            return new LevelSettings { Codec = VideoCodec.Hevc, Encoder = encoder, Quality = 24, AudioKbps = 128, ExtraArgs = "" };
        }

        private CompressionResult Run(string input, LevelSettings settings, TrimRange trim, IProgress<CompressionProgress> progress = null)
        {
            return _compressor.Run(input, settings, null, progress, CancellationToken.None, trim);
        }

        private static string Hash(string path)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(path))
            {
                return Convert.ToBase64String(md5.ComputeHash(stream));
            }
        }

        [Theory]
        [InlineData(EncoderChoice.Cpu)]
        [InlineData(EncoderChoice.Auto)]
        public void Keeps_exactly_the_chosen_part_even_between_keyframes(EncoderChoice encoder)
        {
            if (encoder == EncoderChoice.Auto && !TestVideos.HasNvenc) return;
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");

            var result = Run(clip, Level(encoder), new TrimRange(3.5, 5.5));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal(60, TestVideos.FrameCount(result.OutputPath));
            // Frame 105 of the original is the one at 3.5 seconds.
            double linedUp = TestVideos.Likeness(result.OutputPath, clip, 105);
            Assert.True(linedUp > TestVideos.Likeness(result.OutputPath, clip, 104) + 6, "does not start on frame 105");
            Assert.True(linedUp > TestVideos.Likeness(result.OutputPath, clip, 106) + 6, "does not start on frame 105");
        }

        [Fact]
        public void Sound_stays_in_step_with_the_picture()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");

            var result = Run(clip, Level(), new TrimRange(3.5, 7));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            // The tone starts 5 seconds into the original, so 1.5 seconds into this part.
            Assert.InRange(TestVideos.FirstSoundAt(result.OutputPath), 1.45, 1.55);
        }

        [Fact]
        public void A_trimmed_result_gets_its_own_name_and_the_original_is_left_alone()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");
            string before = Hash(clip);

            var result = Run(clip, Level(), new TrimRange(1, 2));

            Assert.Equal(_videos.PathOf("clip_trimmed.mp4"), result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));
            Assert.False(File.Exists(_videos.PathOf("clip_compressed.mp4")));
            Assert.False(File.Exists(result.OutputPath + ".part"));
            Assert.Equal(before, Hash(clip));
            Assert.Equal(1.0, result.KeptSeconds.Value, 3);
            Assert.Equal(new FileInfo(clip).Length, result.InputBytes);
            Assert.Equal(new FileInfo(result.OutputPath).Length, result.OutputBytes);
        }

        [Fact]
        public void Progress_runs_to_the_end_of_the_kept_part()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");
            var progress = new Percents();

            var result = Run(clip, Level(), new TrimRange(2, 4), progress);

            Assert.InRange(TestVideos.Probe(result.OutputPath).DurationSeconds, 1.9, 2.1);
            Assert.True(progress.Highest > 90, "only reached " + progress.Highest);
        }

        [Fact]
        public void A_part_that_runs_past_the_end_stops_where_the_video_does()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");

            var result = Run(clip, Level(), new TrimRange(8, 30));

            Assert.Equal(JobOutcome.Done, result.Outcome);
            Assert.Equal(2.0, result.KeptSeconds.Value, 1);
            Assert.InRange(TestVideos.Probe(result.OutputPath).DurationSeconds, 1.9, 2.1);
        }

        [Fact]
        public void A_part_that_starts_after_the_video_ends_fails_with_a_readable_message()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");

            var result = Run(clip, Level(), new TrimRange(20, 30));

            Assert.Equal(JobOutcome.Failed, result.Outcome);
            Assert.Contains("past the end", result.Message);
            Assert.False(File.Exists(_videos.PathOf("clip_trimmed.mp4")));
        }

        [Fact]
        public void An_untrimmed_run_reports_no_kept_length()
        {
            string clip = _videos.MakeBulkyClip("bulky.mov", seconds: 1);
            Assert.Null(Run(clip, Level(), null).KeptSeconds);
        }
    }
}
