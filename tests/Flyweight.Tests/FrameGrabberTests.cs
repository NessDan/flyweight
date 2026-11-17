using System;
using System.IO;
using System.Threading;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>Pulling preview pictures out of real clips with real ffmpeg.</summary>
    public class FrameGrabberTests : IDisposable
    {
        private const int Height = 180;
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        private static FrameGrabber GrabberFor(string clip)
        {
            return new FrameGrabber(TestVideos.Tools, clip, TestVideos.Probe(clip));
        }

        /// <summary>One numbered frame of a clip, pulled out a different way, to compare against.</summary>
        private byte[] Frame(string clip, int number)
        {
            string path = _videos.Make($"frame{number}.bmp", "-i", clip, "-vf", $@"select=eq(n\,{number}),scale=-2:{Height}", "-frames:v", "1");
            return File.ReadAllBytes(path);
        }

        /// <summary>The average difference between two pictures of the same size: 0 when identical.</summary>
        private static double Difference(byte[] a, byte[] b)
        {
            const int header = 54;
            Assert.Equal(a.Length, b.Length);
            long total = 0;
            for (int i = header; i < a.Length; i++) total += Math.Abs(a[i] - b[i]);
            return total / (double)(a.Length - header);
        }

        private static (int Width, int Height) SizeOf(byte[] bmp)
        {
            return (BitConverter.ToInt32(bmp, 18), Math.Abs(BitConverter.ToInt32(bmp, 22)));
        }

        [Fact]
        public void An_exact_picture_is_the_first_frame_at_that_moment()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");

            byte[] picture = GrabberFor(clip).Grab(3.5, Height, exact: true, CancellationToken.None);

            Assert.NotNull(picture);
            Assert.Equal((320, 180), SizeOf(picture));
            // Frame 105 is the one at 3.5 seconds. A frame either side is clearly a different picture.
            double right = Difference(picture, Frame(clip, 105));
            Assert.True(right < 1, "differs from frame 105 by " + right);
            Assert.True(Difference(picture, Frame(clip, 104)) > right + 1);
            Assert.True(Difference(picture, Frame(clip, 106)) > right + 1);
        }

        [Fact]
        public void A_quick_picture_is_the_keyframe_before_that_moment()
        {
            // The clip's only keyframe is its first frame, so that is the one that comes back.
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");

            byte[] picture = GrabberFor(clip).Grab(3.5, Height, exact: false, CancellationToken.None);

            Assert.NotNull(picture);
            Assert.True(Difference(picture, Frame(clip, 0)) < 1);
        }

        [Fact]
        public void A_moment_past_the_end_gives_no_picture_and_does_no_harm()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");
            var grabber = GrabberFor(clip);

            Assert.Null(grabber.Grab(60, Height, exact: true, CancellationToken.None));
            Assert.NotNull(grabber.Grab(1, Height, exact: true, CancellationToken.None));
            Assert.NotNull(grabber.Grab(1, Height, exact: false, CancellationToken.None));
        }

        [Fact]
        public void The_end_handle_of_an_untouched_video_shows_its_very_last_frame()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");
            var media = TestVideos.Probe(clip);
            var selection = new TrimSelection(media.PictureSeconds, media.Video.Fps);

            byte[] picture = GrabberFor(clip).Grab(selection.PreviewTime(TrimHandle.End), Height, exact: true, CancellationToken.None);

            Assert.NotNull(picture);
            Assert.True(Difference(picture, Frame(clip, 299)) < 1);
        }

        [Fact]
        public void On_a_stream_file_an_exact_picture_is_the_frame_at_that_moment_not_the_next_keyframe()
        {
            // Keyframes every second. 1.566 s is just before frame 47; the next keyframe is frame 60.
            string clip = _videos.Make("stream.ts", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=4",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "16", "-g", "30", "-pix_fmt", "yuv420p");

            byte[] picture = GrabberFor(clip).Grab(1.566, Height, exact: true, CancellationToken.None);

            Assert.NotNull(picture);
            Assert.True(Difference(picture, Frame(clip, 47)) < 1, "differs from frame 47 by " + Difference(picture, Frame(clip, 47)));
            Assert.True(Difference(picture, Frame(clip, 60)) > 2);
        }

        [Fact]
        public void A_grab_that_takes_too_long_says_so_instead_of_pretending_there_is_no_frame()
        {
            var grabber = GrabberFor(_videos.MakeOneKeyframeClip("clip.mp4"));
            grabber.TimeLimitMs = 1;

            Assert.Throws<TimeoutException>(() => grabber.Grab(3.5, Height, exact: true, CancellationToken.None));
        }

        [Fact]
        public void A_small_video_is_not_blown_up()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");
            byte[] picture = GrabberFor(clip).Grab(1, 1080, exact: true, CancellationToken.None);
            Assert.NotNull(picture);
            Assert.Equal((640, 360), SizeOf(picture));
        }

        [Fact]
        public void A_video_with_stretched_pixels_is_shown_at_its_proper_shape()
        {
            // Camcorders and DVDs store a narrower picture than the one they show.
            string clip = _videos.Make("wide-pixels.mp4", "-f", "lavfi", "-i", "testsrc2=s=480x360:r=30:d=1",
                "-vf", "setsar=4/3", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p");

            byte[] picture = GrabberFor(clip).Grab(0.5, 360, exact: true, CancellationToken.None);

            Assert.NotNull(picture);
            Assert.Equal((640, 360), SizeOf(picture));
        }

        [Fact]
        public void Sideways_phone_video_comes_out_upright()
        {
            string flat = _videos.MakeBulkyClip("flat.mp4", seconds: 1);
            string rotated = _videos.Make("rotated.mp4", "-display_rotation", "90", "-i", flat, "-c", "copy");

            byte[] picture = GrabberFor(rotated).Grab(0.5, Height, exact: true, CancellationToken.None);

            Assert.NotNull(picture);
            var size = SizeOf(picture);
            Assert.Equal(180, size.Height);
            Assert.True(size.Width < size.Height, $"came out {size.Width} wide");
        }

        [Fact]
        public void Hdr_video_still_gives_a_picture()
        {
            string clip = _videos.Make("hdr.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=1,format=yuv420p10le,setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc",
                "-c:v", "libx265", "-preset", "ultrafast", "-crf", "12", "-x265-params", "log-level=error");
            Assert.True(TestVideos.Probe(clip).Video.IsHdr);

            byte[] picture = GrabberFor(clip).Grab(0.5, Height, exact: true, CancellationToken.None);

            Assert.NotNull(picture);
            Assert.Equal((320, 180), SizeOf(picture));
        }

        [Fact]
        public void A_file_that_is_not_a_video_gives_no_picture()
        {
            string fake = _videos.PathOf("notes.mp4");
            File.WriteAllText(fake, "this is not a video");
            var grabber = new FrameGrabber(TestVideos.Tools, fake, new MediaInfo { Video = new VideoStreamInfo { Index = 0 } });

            Assert.Null(grabber.Grab(0, Height, exact: true, CancellationToken.None));
        }

        [Fact]
        public void A_cancelled_grab_stops()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");
            var grabber = GrabberFor(clip);
            var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            Assert.ThrowsAny<OperationCanceledException>(() => grabber.Grab(3.5, Height, exact: true, cancelled.Token));
        }
    }
}
