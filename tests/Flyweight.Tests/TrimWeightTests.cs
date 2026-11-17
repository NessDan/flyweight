using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// How much of a file the kept part really weighs. A video's bytes are rarely spread evenly: a
    /// minute of a still screen takes almost nothing and five busy seconds take nearly all of it. The
    /// rules that decide whether compressing helped have to go by the real figure.
    /// </summary>
    public class TrimWeightTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();
        private readonly Compressor _compressor = new Compressor(TestVideos.Tools);

        public void Dispose() => _videos.Dispose();

        private sealed class Notes : IProgress<CompressionProgress>
        {
            private readonly List<string> _seen = new List<string>();
            public List<string> Seen { get { lock (_seen) return _seen.Where(note => note != null).Distinct().ToList(); } }
            public void Report(CompressionProgress value) { lock (_seen) _seen.Add(value.Note); }
        }

        /// <summary>Nearly a minute of a plain grey screen, then three seconds of busy, noisy picture.</summary>
        private string MakeStillThenBusyClip()
        {
            return _videos.Make("lopsided.mp4",
                "-f", "lavfi", "-i", "color=c=gray:s=640x360:r=30:d=57",
                "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=3",
                "-filter_complex", "[1:v]noise=alls=20:allf=t[busy];[0:v][busy]concat=n=2:v=1:a=0[v]", "-map", "[v]",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "20", "-g", "30", "-pix_fmt", "yuv420p");
        }

        [Fact]
        public void The_kept_part_is_weighed_by_what_it_holds_not_by_how_long_it_is()
        {
            string clip = MakeStillThenBusyClip();
            var media = TestVideos.Probe(clip);
            long whole = new FileInfo(clip).Length;

            long busy = _compressor.KeptBytes(clip, media, new TrimRange(57, 60), whole, CancellationToken.None);
            long still = _compressor.KeptBytes(clip, media, new TrimRange(0, 57), whole, CancellationToken.None);

            // By length the busy part is a twentieth of the file. In fact it's nearly all of it.
            Assert.True(busy > whole * 0.8, $"busy part weighed {busy} of {whole}");
            Assert.True(still < whole * 0.2, $"still part weighed {still} of {whole}");
            Assert.InRange(busy + still, whole * 0.9, whole * 1.01);
        }

        [Fact]
        public void A_file_that_cannot_be_weighed_falls_back_to_its_share_by_length()
        {
            string missing = _videos.PathOf("gone.mp4");
            var media = new MediaInfo { DurationSeconds = 60 };

            long weighed = _compressor.KeptBytes(missing, media, new TrimRange(30, 60), 1000, CancellationToken.None);

            Assert.Equal(500, weighed);
        }

        [Fact]
        public void A_busy_part_of_a_quiet_video_is_not_mistaken_for_one_that_is_already_compressed()
        {
            string clip = MakeStillThenBusyClip();
            var settings = Presets.Default(LevelId.Balanced);
            settings.Encoder = EncoderChoice.Cpu;
            var notes = new Notes();

            var result = _compressor.Run(clip, settings, null, notes, CancellationToken.None, new TrimRange(57, 60));

            Assert.True(result.Outcome == JobOutcome.Done, result.Message + " " + result.Details);
            // Judged against a twentieth of the file, any result looks barely smaller, and the whole
            // part got done again at a lower quality for nothing.
            Assert.DoesNotContain(notes.Seen, note => note.Contains("trying harder"));
        }
    }
}
