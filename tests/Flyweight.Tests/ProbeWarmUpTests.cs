using System;
using System.Diagnostics;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// The first time in a run that ffprobe's answer is read, .NET spends about a quarter of a second
    /// working out how to read it. That's done as the app starts, so the first video looked at,
    /// usually one to trim, doesn't wait for it.
    /// </summary>
    public class ProbeWarmUpTests
    {
        // Shaped like ffprobe's answer for a phone video: sound first, then the picture, turned on its side.
        private const string PhoneJson = @"{
          ""streams"": [
            { ""index"": 0, ""codec_name"": ""aac"", ""codec_type"": ""audio"", ""channels"": 2, ""bit_rate"": ""66277"",
              ""r_frame_rate"": ""0/0"", ""avg_frame_rate"": ""0/0"", ""disposition"": { ""default"": 1, ""attached_pic"": 0 },
              ""tags"": { ""language"": ""und"" } },
            { ""index"": 1, ""codec_name"": ""h264"", ""codec_type"": ""video"", ""width"": 1280, ""height"": 720, ""pix_fmt"": ""yuvj420p"",
              ""color_range"": ""pc"", ""color_transfer"": ""bt709"", ""color_primaries"": ""bt709"",
              ""r_frame_rate"": ""30/1"", ""avg_frame_rate"": ""30/1"", ""bit_rate"": ""2298732"",
              ""disposition"": { ""default"": 1, ""attached_pic"": 0 }, ""tags"": { ""language"": ""und"" },
              ""side_data_list"": [ { ""side_data_type"": ""Display Matrix"", ""rotation"": -90 } ] }
          ],
          ""format"": { ""format_name"": ""mov,mp4,m4a,3gp,3g2,mj2"", ""start_time"": ""0.000000"", ""duration"": ""6.135000"" }
        }";

        /// <summary>Runs in an app domain of its own, where nothing has read a probe yet, as at the start of a run.</summary>
        public sealed class FreshStart : MarshalByRefObject
        {
            public double FirstReadMs(string json, bool warmedUp)
            {
                if (warmedUp) MediaInfo.WarmUp();
                var watch = Stopwatch.StartNew();
                var media = MediaInfo.Parse(json);
                double ms = watch.Elapsed.TotalMilliseconds;
                // And what was read is right, warmed up or not.
                if (media.Video?.DisplayWidth != 720 || media.Audio.Count != 1) throw new InvalidOperationException("Misread");
                return ms;
            }
        }

        private static double FirstReadMs(bool warmedUp)
        {
            var domain = AppDomain.CreateDomain("fresh start", null, AppDomain.CurrentDomain.SetupInformation);
            try
            {
                var fresh = (FreshStart)domain.CreateInstanceAndUnwrap(typeof(FreshStart).Assembly.FullName, typeof(FreshStart).FullName);
                return fresh.FirstReadMs(PhoneJson, warmedUp);
            }
            finally
            {
                AppDomain.Unload(domain);
            }
        }

        [Fact]
        public void Warmed_up_as_the_app_starts_the_first_video_looked_at_is_read_at_once()
        {
            // About a millisecond. Not warmed up, or warmed up on an answer with no streams in it, the
            // first takes about 260 or 30.
            Assert.InRange(FirstReadMs(warmedUp: true), 0, 10);
        }

        [Fact]
        public void Warming_up_never_fails_and_can_be_done_more_than_once()
        {
            MediaInfo.WarmUp();
            MediaInfo.WarmUp();
            Assert.Equal(720, MediaInfo.Parse(PhoneJson).Video.DisplayWidth);
        }
    }
}
