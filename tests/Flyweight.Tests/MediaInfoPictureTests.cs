using System;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    public class MediaInfoPictureTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        private static MediaInfo Parse(string formatFields, string videoFields)
        {
            return MediaInfo.Parse(@"{ ""format"": { " + formatFields + @" }, ""streams"": [ {
                ""index"": 0, ""codec_type"": ""video"", ""codec_name"": ""h264"", ""width"": 640, ""height"": 360, ""avg_frame_rate"": ""30/1""" + videoFields + " } ] }");
        }

        [Fact]
        public void The_picture_ends_where_the_video_stream_does_when_the_sound_runs_longer()
        {
            var media = Parse(@"""duration"": ""10.021000"", ""start_time"": ""0.000000""", @", ""start_time"": ""0.000000"", ""duration"": ""10.000000""");
            Assert.Equal(10.0, media.Video.EndSeconds, 3);
            Assert.Equal(10.0, media.PictureSeconds, 3);
            Assert.Equal(10.021, media.DurationSeconds, 3);
        }

        [Fact]
        public void A_file_that_does_not_say_where_the_picture_ends_goes_by_its_whole_length()
        {
            // Normal for MKV, which only records the length of the file as a whole.
            var media = Parse(@"""duration"": ""10.021000""", "");
            Assert.Equal(0, media.Video.EndSeconds, 3);
            Assert.Equal(10.021, media.PictureSeconds, 3);
        }

        [Fact]
        public void An_mkv_says_where_its_picture_ends_in_a_tag_instead()
        {
            var media = Parse(@"""duration"": ""4.021000"", ""start_time"": ""-0.021000""",
                @", ""start_time"": ""0.000000"", ""tags"": { ""DURATION"": ""00:00:02.000000000"" }");
            Assert.Equal(2.021, media.PictureSeconds, 3);
        }

        [Fact]
        public void A_tag_that_makes_no_sense_is_ignored()
        {
            var media = Parse(@"""duration"": ""4.000000""", @", ""tags"": { ""DURATION"": ""soon"" }");
            Assert.Equal(4.0, media.PictureSeconds, 3);
        }

        [Fact]
        public void A_real_mkv_whose_sound_runs_on_reports_where_the_picture_ends()
        {
            string clip = _videos.Make("longer-sound.mkv",
                "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=2",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4",
                "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac");

            var media = TestVideos.Probe(clip);

            Assert.InRange(media.DurationSeconds, 3.9, 4.1);
            Assert.InRange(media.PictureSeconds, 1.95, 2.05);
        }

        [Fact]
        public void A_picture_that_starts_late_ends_late_too()
        {
            var media = Parse(@"""duration"": ""12.000000"", ""start_time"": ""0.000000""", @", ""start_time"": ""0.500000"", ""duration"": ""9.000000""");
            Assert.Equal(9.5, media.PictureSeconds, 3);
        }

        [Fact]
        public void Times_are_counted_from_where_the_file_itself_starts()
        {
            // Recordings off a broadcast start their clocks at some large number.
            var media = Parse(@"""duration"": ""10.000000"", ""start_time"": ""100.000000""", @", ""start_time"": ""100.000000"", ""duration"": ""9.960000""");
            Assert.Equal(9.96, media.PictureSeconds, 3);
        }

        [Fact]
        public void The_picture_never_outlasts_the_file()
        {
            var media = Parse(@"""duration"": ""10.000000""", @", ""start_time"": ""0.000000"", ""duration"": ""10.400000""");
            Assert.Equal(10.0, media.PictureSeconds, 3);
        }

        [Fact]
        public void A_real_clip_reports_where_its_picture_ends()
        {
            var media = TestVideos.Probe(_videos.MakeOneKeyframeClip("clip.mp4"));
            Assert.InRange(media.PictureSeconds, 9.99, 10.01);
        }
    }
}
