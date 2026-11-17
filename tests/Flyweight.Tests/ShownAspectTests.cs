using System;
using System.Threading;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// The shape worked out for a picture from what ffprobe says is the shape its pictures really come
    /// out. A crop box drawn over the picture has to line up with it.
    /// </summary>
    public class ShownAspectTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        /// <summary>Width over height of a still picture of the clip, as the trim controls get it.</summary>
        private static (double Still, double Worked) Aspects(string clip)
        {
            var media = TestVideos.Probe(clip);
            byte[] still = new FrameGrabber(TestVideos.Tools, clip, media).Grab(0.5, 360, exact: true, CancellationToken.None);
            Assert.NotNull(still);
            double width = BitConverter.ToInt32(still, 18);
            double height = Math.Abs(BitConverter.ToInt32(still, 22));
            return (width / height, media.Video.ShownAspect);
        }

        private string Stretched(string name) => _videos.Make(name, "-f", "lavfi", "-i", "testsrc2=s=480x360:r=30:d=1",
            "-vf", "setsar=4/3", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p");

        private string OnItsSide(string flat, string name) => _videos.Make(name, "-display_rotation", "90", "-i", flat, "-c", "copy");

        [Fact]
        public void For_a_video_with_stretched_pixels()
        {
            var aspects = Aspects(Stretched("stretched.mp4"));

            Assert.Equal(aspects.Still, aspects.Worked, 2);
            Assert.Equal(16.0 / 9, aspects.Worked, 3);
        }

        [Fact]
        public void For_a_sideways_video()
        {
            var aspects = Aspects(OnItsSide(_videos.MakeBulkyClip("flat.mp4", seconds: 1), "sideways.mp4"));

            Assert.Equal(aspects.Still, aspects.Worked, 2);
            Assert.True(aspects.Worked < 1);
        }

        [Fact]
        public void For_a_sideways_video_with_stretched_pixels()
        {
            var aspects = Aspects(OnItsSide(Stretched("stretched.mp4"), "both.mp4"));

            Assert.Equal(aspects.Still, aspects.Worked, 2);
            Assert.Equal(9.0 / 16, aspects.Worked, 3);
        }
    }
}
