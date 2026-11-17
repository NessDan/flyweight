using System;
using System.Threading;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// Finding the last frame before a moment, which is the last one a cut ending there keeps. It has
    /// to come from the file's real frame times: many recordings don't space their frames evenly.
    /// </summary>
    public class LastFrameTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        private static double? LastBefore(string clip, double seconds)
        {
            return new FrameGrabber(TestVideos.Tools, clip, TestVideos.Probe(clip)).LastFrameBefore(seconds, CancellationToken.None);
        }

        [Fact]
        public void On_evenly_spaced_video_it_is_the_frame_before()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");   // 30 frames a second

            Assert.Equal(7.233, LastBefore(clip, 7.25).Value, 3);    // frame 217
            Assert.Equal(7.2, LastBefore(clip, 7.233).Value, 3);     // a frame exactly at the moment is not before it
            Assert.Equal(9.966, LastBefore(clip, 10).Value, 3);      // the very last frame
        }

        [Fact]
        public void On_unevenly_spaced_video_it_goes_by_where_the_frames_really_are()
        {
            // Two frames a second for eight seconds, as when a screen sits still, then thirty a second.
            string clip = _videos.Make("uneven.mp4", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=10",
                "-vf", "select='if(lt(t,8),not(mod(n,15)),1)'", "-fps_mode", "vfr",
                "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p");
            Assert.InRange(TestVideos.Probe(clip).Video.Fps, 6, 9);   // which is all an average can say

            Assert.Equal(9.466, LastBefore(clip, 9.5).Value, 3);      // in the busy part: one thirtieth back, not one seventh
            Assert.Equal(7.5, LastBefore(clip, 8.0).Value, 3);        // in the still part: half a second back
            Assert.Equal(7.5, LastBefore(clip, 7.9).Value, 3);
            Assert.Equal(0, LastBefore(clip, 0.3).Value, 3);
        }

        [Fact]
        public void Nothing_comes_before_the_first_frame()
        {
            Assert.Null(LastBefore(_videos.MakeOneKeyframeClip("clip.mp4"), 0));
        }

        [Fact]
        public void Times_count_from_where_the_file_starts_even_when_its_clock_does_not()
        {
            string clip = _videos.Make("late.ts", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=4",
                "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-output_ts_offset", "100", "-f", "mpegts");

            Assert.Equal(1.5, LastBefore(clip, 1.533).Value, 3);      // frame 45
        }
    }
}
