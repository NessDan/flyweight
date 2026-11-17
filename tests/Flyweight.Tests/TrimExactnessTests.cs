using System;
using System.IO;
using System.Text;
using System.Threading;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// The promises trimming makes: the cut is on the frame chosen at both ends, and nothing from
    /// outside the kept part is left in the file. Checked with real ffmpeg, at times that fall between
    /// frames, since whole seconds hide the mistakes.
    /// </summary>
    public class TrimExactnessTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();
        private readonly Compressor _compressor = new Compressor(TestVideos.Tools);

        public void Dispose() => _videos.Dispose();

        private static LevelSettings Level(EncoderChoice encoder = EncoderChoice.Cpu)
        {
            return new LevelSettings { Codec = VideoCodec.Hevc, Encoder = encoder, Quality = 24, AudioKbps = 128, ExtraArgs = "" };
        }

        private CompressionResult Trim(string input, double start, double end, EncoderChoice encoder = EncoderChoice.Cpu)
        {
            var result = _compressor.Run(input, Level(encoder), null, null, CancellationToken.None, new TrimRange(start, end));
            Assert.True(result.Outcome == JobOutcome.Done, result.Message + " " + result.Details);
            return result;
        }

        private static void AssertStartsOnFrame(string result, string original, int frame)
        {
            double linedUp = TestVideos.Likeness(result, original, frame);
            if (frame > 0) Assert.True(linedUp > TestVideos.Likeness(result, original, frame - 1) + 6, "does not start on frame " + frame);
            Assert.True(linedUp > TestVideos.Likeness(result, original, frame + 1) + 6, "does not start on frame " + frame);
        }

        // At 30 frames a second, frame n is at n/30 s. The times are what stepping in a time box
        // gives: the millisecond at or just before a frame.
        [Theory]
        [InlineData(1.066, 1.533, 32, 14, EncoderChoice.Cpu)]
        [InlineData(0.066, 0.2, 2, 4, EncoderChoice.Cpu)]
        [InlineData(1.033, 1.533, 31, 15, EncoderChoice.Cpu)]
        [InlineData(2.05, 2.59, 62, 16, EncoderChoice.Cpu)]      // dragged: neither end is on a frame
        [InlineData(1.066, 1.533, 32, 14, EncoderChoice.Auto)]
        public void Both_ends_of_the_cut_land_on_the_frame_chosen(double start, double end, int firstFrame, int frames, EncoderChoice encoder)
        {
            if (encoder == EncoderChoice.Auto && !TestVideos.HasNvenc) return;
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");

            var result = Trim(clip, start, end, encoder);

            Assert.Equal(frames, TestVideos.FrameCount(result.OutputPath));
            AssertStartsOnFrame(result.OutputPath, clip, firstFrame);
        }

        [Fact]
        public void A_cut_that_only_trims_the_end_starts_on_the_very_first_frame()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");

            var result = Trim(clip, 0, 1.533);

            Assert.Equal(46, TestVideos.FrameCount(result.OutputPath));
            AssertStartsOnFrame(result.OutputPath, clip, 0);
        }

        [Fact]
        public void A_file_whose_clock_does_not_start_at_zero_is_cut_just_as_exactly()
        {
            // Recordings off a broadcast, and some cameras, start their clocks at some large number.
            string clip = _videos.Make("late.ts", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=6",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "16", "-pix_fmt", "yuv420p",
                "-output_ts_offset", "100", "-f", "mpegts");
            Assert.True(TestVideos.Probe(clip).StartSeconds > 50, "the test clip should start its clock late");

            Assert.Equal(14, TestVideos.FrameCount(Trim(clip, 1.066, 1.533).OutputPath));

            var fromTheStart = Trim(clip, 0, 1.533);
            Assert.Equal(46, TestVideos.FrameCount(fromTheStart.OutputPath));
            AssertStartsOnFrame(fromTheStart.OutputPath, clip, 0);
        }

        [Theory]
        [InlineData("stream.ts", "libx264", "aac")]
        [InlineData("disc.mpg", "mpeg2video", "mp2")]
        public void A_broadcast_or_camcorder_file_is_cut_on_the_frame_chosen_not_the_next_keyframe(string name, string videoCodec, string soundCodec)
        {
            // Keyframes every second, and a tone that starts two seconds in.
            string clip = _videos.Make(name, "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=6",
                "-f", "lavfi", "-i", "aevalsrc='if(gte(t,2),0.5*sin(2*PI*1000*t),0)':d=6:s=48000",
                "-c:v", videoCodec, "-g", "30", "-q:v", "3", "-pix_fmt", "yuv420p", "-c:a", soundCodec);
            var media = TestVideos.Probe(clip);
            Assert.False(media.HasKeyframeIndex);

            // Where the frames really are, since in these files the picture can start a moment after the sound.
            double firstFrame = media.Video.StartSeconds - media.StartSeconds;
            int startsOn = (int)Math.Ceiling((1.566 - firstFrame) * 30 - 1e-6);
            int endsBefore = (int)Math.Ceiling((2.533 - firstFrame) * 30 - 1e-6);

            var result = Trim(clip, 1.566, 2.533);

            Assert.Equal(endsBefore - startsOn, TestVideos.FrameCount(result.OutputPath));
            AssertStartsOnFrame(result.OutputPath, clip, startsOn);
            Assert.InRange(TestVideos.FirstSoundAt(result.OutputPath) - (TestVideos.FirstSoundAt(clip) - 1.566), -0.02, 0.02);
            Assert.InRange(TestVideos.SoundPackets(result.OutputPath), 43, 50);
        }

        [Fact]
        public void No_sound_from_before_the_cut_is_left_inside_the_file()
        {
            // This clip's sound is the kind that would otherwise be copied across untouched.
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");

            var result = Trim(clip, 3.5, 7);

            // 3.5 seconds of sound is 164 packets. Copied, it came to 329: everything back to the keyframe.
            Assert.InRange(TestVideos.SoundPackets(result.OutputPath), 160, 170);
            Assert.InRange(TestVideos.FirstSoundAt(result.OutputPath), 1.45, 1.55);
        }

        [Fact]
        public void No_words_from_outside_the_kept_part_end_up_in_the_file()
        {
            string plain = _videos.MakeOneKeyframeClip("plain.mp4");
            string subtitles = _videos.PathOf("words.srt");
            File.WriteAllText(subtitles,
                "1\n00:00:01,000 --> 00:00:02,500\nsaid before the cut\n\n" +
                "2\n00:00:05,000 --> 00:00:06,000\nsaid inside it\n\n" +
                "3\n00:00:08,000 --> 00:00:09,000\nsaid after the cut\n");
            string clip = _videos.Make("clip.mp4", "-i", plain, "-i", subtitles, "-map", "0", "-map", "1", "-c", "copy", "-c:s", "mov_text");
            Assert.Single(TestVideos.Probe(clip).TextSubtitleIndexes);

            var result = Trim(clip, 4, 6);

            Assert.Empty(TestVideos.Probe(result.OutputPath).TextSubtitleIndexes);
            string bytes = Encoding.ASCII.GetString(File.ReadAllBytes(result.OutputPath));
            Assert.DoesNotContain("said before the cut", bytes);
            Assert.DoesNotContain("said after the cut", bytes);
        }
    }
}
