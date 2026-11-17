using Flyweight.Core;
using Xunit;
using static Flyweight.Tests.Fixtures;

namespace Flyweight.Tests
{
    public class SeekingTests
    {
        [Theory]
        [InlineData("mov,mp4,m4a,3gp,3g2,mj2", true)]
        [InlineData("matroska,webm", true)]
        [InlineData("avi", true)]
        [InlineData("flv", true)]
        [InlineData("asf", true)]
        [InlineData("mpegts", false)]
        [InlineData("mpeg", false)]
        [InlineData("mpegvideo", false)]
        [InlineData("something new", false)]
        [InlineData(null, false)]
        public void Knows_which_kinds_of_file_list_their_keyframes(string format, bool expected)
        {
            var media = Media();
            media.FormatName = format;
            Assert.Equal(expected, media.HasKeyframeIndex);
        }

        [Fact]
        public void The_kind_of_file_is_read_from_what_ffprobe_says()
        {
            var media = MediaInfo.Parse(@"{ ""format"": { ""format_name"": ""mpegts"", ""duration"": ""6.0"", ""start_time"": ""1.400000"" }, ""streams"": [ {
                ""index"": 0, ""codec_type"": ""video"", ""codec_name"": ""h264"", ""width"": 640, ""height"": 360, ""avg_frame_rate"": ""30/1"" } ] }");
            Assert.Equal("mpegts", media.FormatName);
            Assert.False(media.HasKeyframeIndex);
            Assert.Equal(1.4, media.StartSeconds, 3);
        }

        [Fact]
        public void A_file_that_lists_its_keyframes_needs_no_run_up()
        {
            Assert.Equal(0, Seeking.RunUp(Media(), 300));
        }

        [Theory]
        [InlineData(300, 10)]
        [InlineData(10, 10)]
        [InlineData(3.25, 3.25)]
        [InlineData(0, 0)]
        public void A_stream_file_is_read_from_up_to_ten_seconds_before_but_never_before_its_start(double seconds, double expected)
        {
            var media = Media();
            media.FormatName = "mpegts";
            Assert.Equal(expected, Seeking.RunUp(media, seconds), 3);
        }
    }
}
