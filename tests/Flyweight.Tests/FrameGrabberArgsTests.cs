using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    public class FrameGrabberArgsTests
    {
        private static List<string> Args(double seconds = 12.5, bool exact = true, bool toneMap = false, bool lean = true, double runUp = 0)
        {
            return FrameGrabber.BuildArgs(@"C:\v\clip.mkv", 2, seconds, 360, exact, toneMap, lean, runUp);
        }

        [Fact]
        public void A_stream_file_is_read_from_earlier_for_an_exact_picture()
        {
            // Jumping straight there would show the next keyframe instead, which is not what a cut there keeps.
            var args = Args(seconds: 12.5, runUp: 10);
            int input = args.IndexOf("-i");

            Assert.Equal("2.5", args[args.IndexOf("-ss") + 1]);
            Assert.True(args.IndexOf("-ss") < input);
            Assert.Equal("10", args[args.LastIndexOf("-ss") + 1]);
            Assert.True(args.LastIndexOf("-ss") > input);
        }

        [Fact]
        public void A_quick_picture_never_bothers_with_a_run_up()
        {
            var args = Args(seconds: 12.5, exact: false, runUp: 10);
            Assert.Equal("12.5", Value(args, "-ss"));
            Assert.Equal(args.IndexOf("-ss"), args.LastIndexOf("-ss"));
        }

        private static string Value(List<string> args, string flag)
        {
            int i = args.IndexOf(flag);
            return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        }

        [Fact]
        public void Asks_for_one_picture_of_the_right_stream_at_the_right_moment()
        {
            var args = Args();
            Assert.Equal("12.5", Value(args, "-ss"));
            Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"));
            Assert.Equal(@"C:\v\clip.mkv", Value(args, "-i"));
            Assert.Equal("0:2", Value(args, "-map"));
            Assert.Equal("1", Value(args, "-frames:v"));
            Assert.Contains("-an", args);
            Assert.Equal("bmp", Value(args, "-c:v"));
            Assert.Equal("-", args[args.Count - 1]);
        }

        [Fact]
        public void An_exact_picture_decodes_up_to_the_frame()
        {
            var args = Args(exact: true);
            Assert.DoesNotContain("-noaccurate_seek", args);
            Assert.DoesNotContain("-skip_frame", args);
        }

        [Fact]
        public void A_quick_picture_reads_only_the_keyframe()
        {
            var args = Args(exact: false);
            Assert.Contains("-noaccurate_seek", args);
            Assert.Equal("nokey", Value(args, "-skip_frame"));
            Assert.True(args.IndexOf("-skip_frame") < args.IndexOf("-i"));
            // Without this, ffmpeg throws the keyframe away for being earlier than the moment asked for.
            Assert.Equal("passthrough", Value(args, "-fps_mode"));
        }

        [Fact]
        public void Scales_down_to_the_height_asked_for_but_never_up()
        {
            Assert.Contains(":'min(360,ih)'", Value(Args(), "-vf"));
        }

        [Fact]
        public void The_picture_is_made_the_shape_the_video_is_shown_at()
        {
            string filters = Value(Args(), "-vf");
            Assert.StartsWith("scale='max(2,round(oh*dar/2)*2)':", filters);
            Assert.Contains("setsar=1", filters);
        }

        [Fact]
        public void Hdr_video_is_converted_for_an_ordinary_screen()
        {
            Assert.Contains("tonemap", Value(Args(toneMap: true), "-vf"));
            Assert.DoesNotContain("tonemap", Value(Args(toneMap: false), "-vf"));
        }

        [Fact]
        public void Skips_studying_the_file_again_unless_asked_to()
        {
            Assert.Contains("-probesize", Args(lean: true));
            Assert.DoesNotContain("-probesize", Args(lean: false));
        }

        [Fact]
        public void Times_use_a_dot_whatever_the_language()
        {
            var before = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                Assert.Equal("0.5", Value(Args(seconds: 0.5), "-ss"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }
    }
}
