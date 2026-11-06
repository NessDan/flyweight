using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    public class MediaInfoTests
    {
        // Shape taken from ffprobe 8 output for a real Pixel phone recording: data stream first,
        // audio second, video third, rotated, full range.
        private const string PixelJson = @"{
          ""streams"": [
            { ""index"": 0, ""codec_type"": ""data"", ""r_frame_rate"": ""0/0"", ""avg_frame_rate"": ""0/0"", ""bit_rate"": ""109257"",
              ""disposition"": { ""default"": 1, ""attached_pic"": 0 } },
            { ""index"": 1, ""codec_name"": ""aac"", ""codec_type"": ""audio"", ""channels"": 2, ""bit_rate"": ""192007"",
              ""r_frame_rate"": ""0/0"", ""avg_frame_rate"": ""0/0"", ""disposition"": { ""default"": 1, ""attached_pic"": 0 } },
            { ""index"": 2, ""codec_name"": ""hevc"", ""codec_type"": ""video"", ""width"": 3840, ""height"": 2160, ""pix_fmt"": ""yuvj420p"",
              ""color_range"": ""pc"", ""color_space"": ""bt709"", ""color_transfer"": ""bt709"", ""color_primaries"": ""bt709"",
              ""r_frame_rate"": ""60/1"", ""avg_frame_rate"": ""254910000/4251011"", ""bit_rate"": ""64200472"",
              ""disposition"": { ""default"": 1, ""attached_pic"": 0 },
              ""side_data_list"": [ { ""side_data_type"": ""Display Matrix"", ""displaymatrix"": ""\n00000000: 0\n"", ""rotation"": 90 } ] }
          ],
          ""format"": { ""duration"": ""141.710084"", ""size"": ""1142866099"", ""bit_rate"": ""64518547"" }
        }";

        // Real OBS MKV: no per-stream bitrates, no field order on audio.
        private const string MkvJson = @"{
          ""streams"": [
            { ""index"": 0, ""codec_name"": ""h264"", ""codec_type"": ""video"", ""width"": 1920, ""height"": 1080, ""pix_fmt"": ""yuv420p"",
              ""color_range"": ""tv"", ""field_order"": ""progressive"", ""r_frame_rate"": ""30/1"", ""avg_frame_rate"": ""30/1"",
              ""disposition"": { ""default"": 0, ""attached_pic"": 0 } },
            { ""index"": 1, ""codec_name"": ""aac"", ""codec_type"": ""audio"", ""channels"": 2, ""r_frame_rate"": ""0/0"", ""avg_frame_rate"": ""0/0"",
              ""disposition"": { ""default"": 0, ""attached_pic"": 0 } }
          ],
          ""format"": { ""duration"": ""1929.633000"", ""size"": ""997031718"" }
        }";

        [Fact]
        public void Finds_the_video_when_it_is_not_the_first_stream()
        {
            var info = MediaInfo.Parse(PixelJson);
            Assert.Equal(2, info.Video.Index);
            Assert.Equal("hevc", info.Video.Codec);
        }

        [Fact]
        public void Ignores_data_streams()
        {
            var info = MediaInfo.Parse(PixelJson);
            Assert.Single(info.Audio);
            Assert.Equal(1, info.Audio[0].Index);
        }

        [Fact]
        public void Rotation_swaps_the_displayed_dimensions()
        {
            var video = MediaInfo.Parse(PixelJson).Video;
            Assert.Equal(90, video.Rotation);
            Assert.Equal(2160, video.DisplayWidth);
            Assert.Equal(3840, video.DisplayHeight);
        }

        [Fact]
        public void Reads_average_frame_rate_and_duration()
        {
            var info = MediaInfo.Parse(PixelJson);
            Assert.Equal(59.96, info.Video.Fps, 2);
            Assert.Equal(141.71, info.DurationSeconds, 2);
        }

        [Fact]
        public void Detects_full_range_from_either_the_flag_or_the_pixel_format()
        {
            Assert.True(MediaInfo.Parse(PixelJson).Video.IsFullRange);
            Assert.False(MediaInfo.Parse(MkvJson).Video.IsFullRange);
            Assert.True(new VideoStreamInfo { PixFmt = "yuvj420p" }.IsFullRange);
        }

        [Fact]
        public void Missing_stream_bitrates_read_as_zero()
        {
            Assert.Equal(0, MediaInfo.Parse(MkvJson).Audio[0].BitrateBps);
        }

        [Fact]
        public void Skips_cover_art_that_looks_like_a_video_stream()
        {
            var info = MediaInfo.Parse(@"{ ""streams"": [
                { ""index"": 0, ""codec_name"": ""mjpeg"", ""codec_type"": ""video"", ""width"": 600, ""height"": 600, ""disposition"": { ""attached_pic"": 1 } },
                { ""index"": 1, ""codec_name"": ""h264"", ""codec_type"": ""video"", ""width"": 1280, ""height"": 720, ""avg_frame_rate"": ""30/1"", ""disposition"": { ""attached_pic"": 0 } }
              ], ""format"": { ""duration"": ""10"" } }");
            Assert.Equal(1, info.Video.Index);
        }

        [Fact]
        public void A_file_with_only_audio_has_no_video()
        {
            var info = MediaInfo.Parse(@"{ ""streams"": [ { ""index"": 0, ""codec_name"": ""mp3"", ""codec_type"": ""audio"", ""channels"": 2 } ], ""format"": { ""duration"": ""10"" } }");
            Assert.Null(info.Video);
        }

        [Theory]
        [InlineData("smpte2084", true)]
        [InlineData("arib-std-b67", true)]
        [InlineData("bt709", false)]
        [InlineData(null, false)]
        public void Detects_hdr_from_the_transfer_function(string transfer, bool expected)
        {
            Assert.Equal(expected, new VideoStreamInfo { ColorTransfer = transfer }.IsHdr);
        }

        [Theory]
        [InlineData("yuv420p", false)]
        [InlineData("yuvj420p", false)]
        [InlineData("yuv420p10le", true)]
        [InlineData("p010le", true)]
        [InlineData("yuv422p12le", true)]
        [InlineData(null, false)]
        public void Detects_high_bit_depth_from_the_pixel_format(string pixFmt, bool expected)
        {
            Assert.Equal(expected, new VideoStreamInfo { PixFmt = pixFmt }.IsHighBitDepth);
        }

        [Theory]
        [InlineData("tt", true)]
        [InlineData("bt", true)]
        [InlineData("progressive", false)]
        [InlineData("unknown", false)]
        [InlineData(null, false)]
        public void Detects_interlacing_from_the_field_order(string field, bool expected)
        {
            Assert.Equal(expected, new VideoStreamInfo { FieldOrder = field }.IsInterlaced);
        }

        [Fact]
        public void Falls_back_to_the_nominal_frame_rate_when_the_average_is_unknown()
        {
            var info = MediaInfo.Parse(@"{ ""streams"": [
                { ""index"": 0, ""codec_name"": ""h264"", ""codec_type"": ""video"", ""width"": 1280, ""height"": 720, ""avg_frame_rate"": ""0/0"", ""r_frame_rate"": ""25/1"" }
              ], ""format"": { ""duration"": ""10"" } }");
            Assert.Equal(25, info.Video.Fps);
        }

        [Fact]
        public void Keeps_text_subtitles_but_not_image_subtitles()
        {
            var info = MediaInfo.Parse(@"{ ""streams"": [
                { ""index"": 0, ""codec_name"": ""h264"", ""codec_type"": ""video"", ""width"": 1280, ""height"": 720 },
                { ""index"": 1, ""codec_name"": ""subrip"", ""codec_type"": ""subtitle"" },
                { ""index"": 2, ""codec_name"": ""hdmv_pgs_subtitle"", ""codec_type"": ""subtitle"" },
                { ""index"": 3, ""codec_name"": ""ass"", ""codec_type"": ""subtitle"" }
              ], ""format"": { ""duration"": ""10"" } }");
            Assert.Equal(new[] { 1, 3 }, info.TextSubtitleIndexes);
        }

        [Fact]
        public void Numbers_parse_the_same_regardless_of_system_locale()
        {
            var original = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.Equal(141.71, MediaInfo.Parse(PixelJson).DurationSeconds, 2);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json at all")]
        public void Unreadable_probe_output_reports_a_readable_error(string json)
        {
            Assert.Throws<CompressionException>(() => MediaInfo.Parse(json));
        }
    }
}
