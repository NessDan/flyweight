using System.Collections.Generic;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>What ffmpeg is asked for, to play one part of a video: once for its pictures, once for its sound.</summary>
    public class PartPlayerArgsTests
    {
        private const string Clip = @"C:\v\clip.mkv";

        private static MediaInfo WithSound(VideoStreamInfo video = null) => Fixtures.Media(video, 60, Fixtures.Audio());

        private static string Value(List<string> args, string flag)
        {
            int i = args.IndexOf(flag);
            return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        }

        private static List<string> Pictures(MediaInfo media = null, double from = 0, double length = 60, double fps = 30, bool toneMap = false)
        {
            return PartPlayer.PictureArgs(Clip, media ?? WithSound(), from, length, 700, 394, fps, toneMap);
        }

        [Fact]
        public void Pictures_come_as_plain_frames_one_after_another_with_no_sound_among_them()
        {
            var args = Pictures(WithSound(Fixtures.Video(index: 2)));

            Assert.Equal(Clip, Value(args, "-i"));
            Assert.Contains("0:2", args);
            Assert.Contains("-an", args);
            Assert.Equal("rawvideo", Value(args, "-f"));
            Assert.Contains(",format=bgr0,", Value(args, "-vf"));
            Assert.Equal("-", args[args.Count - 1]);
        }

        [Fact]
        public void Every_picture_is_exactly_the_size_asked_for_with_the_video_fitted_inside_in_its_own_shape()
        {
            string filters = Value(Pictures(), "-vf");

            // Whichever way the video is the wrong shape for the space, it's fitted in whole...
            Assert.Contains("scale='if(gt(dar,700/394),700,max(2,round(394*dar/2)*2))':'if(gt(dar,700/394),max(2,round(700/dar/2)*2),394)',setsar=1", filters);
            // ...and the rest is filled in the colour of the box the picture sits in.
            Assert.EndsWith(",pad=700:394:(ow-iw)/2:(oh-ih)/2:color=0x101010", filters);
        }

        [Fact]
        public void Pictures_come_at_a_steady_rate_so_each_ones_moment_is_known()
        {
            // Counted from the part's first moment, wherever the first picture in it happens to fall.
            Assert.Contains(",fps=59.94:start_time=0,", "," + Value(Pictures(fps: 59.94), "-vf"));
            Assert.Contains(",fps=30:start_time=0,", "," + Value(Pictures(fps: 30), "-vf"));
        }

        [Theory]
        [InlineData(60, 60)]
        [InlineData(29.97, 29.97)]
        [InlineData(23.976, 23.976)]
        [InlineData(120, 60)]     // more than a screen shows
        [InlineData(240, 60)]
        [InlineData(7, 30)]       // an average over a recording that stood still: the pictures come unevenly
        [InlineData(0, 30)]       // unknown
        public void It_shows_as_many_pictures_a_second_as_the_video_has_within_reason(double videoFps, double shown)
        {
            Assert.Equal(shown, PartPlayer.RateFor(videoFps));
        }

        [Fact]
        public void A_part_is_cut_the_way_a_trim_cuts_it()
        {
            var args = Pictures(from: 2.066, length: 2.967);
            int input = args.IndexOf("-i");

            // Before the input, so ffmpeg jumps there instead of reading everything up to it.
            Assert.Equal("2.066", Value(args, "-ss"));
            Assert.Equal("2.967", Value(args, "-t"));
            Assert.True(args.IndexOf("-ss") < input && args.IndexOf("-t") < input);
            // -t alone lets one frame too many through.
            Assert.StartsWith("trim=end=2.967,", Value(args, "-vf"));
        }

        [Fact]
        public void A_stream_file_is_read_from_earlier_so_the_part_starts_on_the_frame_chosen()
        {
            var media = WithSound();
            media.FormatName = "mpegts";

            var args = Pictures(media, from: 12.5, length: 1.5);
            int input = args.IndexOf("-i");

            Assert.Equal("2.5", Value(args, "-ss"));
            Assert.Equal("11.5", Value(args, "-t"));
            Assert.Equal("10", args[args.LastIndexOf("-ss") + 1]);
            Assert.True(args.LastIndexOf("-ss") > input, "The lead-in is dropped after decoding");
            Assert.StartsWith("trim=end=11.5,", Value(args, "-vf"));
        }

        [Fact]
        public void HDR_video_is_brought_into_the_range_an_ordinary_window_can_show()
        {
            Assert.DoesNotContain("tonemap", Value(Pictures(), "-vf"));
            Assert.Contains("tonemap", Value(Pictures(toneMap: true), "-vf"));
        }

        [Fact]
        public void Sound_comes_as_plain_stereo_samples_with_no_pictures_among_them()
        {
            var args = PartPlayer.SoundArgs(Clip, WithSound(), 0, 60);

            Assert.Equal(Clip, Value(args, "-i"));
            Assert.Contains("-vn", args);
            Assert.Equal("s16le", Value(args, "-f"));
            Assert.Equal("48000", Value(args, "-ar"));
            Assert.Equal("2", Value(args, "-ac"));
            Assert.Equal("-", args[args.Count - 1]);
        }

        [Fact]
        public void Sound_that_starts_late_is_led_in_with_silence_so_it_stays_in_step_with_the_pictures()
        {
            // Plain samples carry no times, so a track that began a moment after the part did would
            // otherwise be played from the part's first instant.
            Assert.Equal("aresample=async=1:first_pts=0", Value(PartPlayer.SoundArgs(Clip, WithSound(), 0, 60), "-af"));
        }

        [Fact]
        public void Only_the_first_sound_track_is_played()
        {
            var media = Fixtures.Media(null, 60, Fixtures.Audio(index: 1, channels: 6), Fixtures.Audio(index: 2));

            var args = PartPlayer.SoundArgs(Clip, media, 0, 60);

            Assert.Contains("0:1", args);
            Assert.DoesNotContain("0:2", args);
        }

        [Fact]
        public void The_sound_is_cut_at_the_same_moments_as_the_pictures()
        {
            var media = WithSound();
            var args = PartPlayer.SoundArgs(Clip, media, 2.066, 2.967);
            Assert.Equal("2.066", Value(args, "-ss"));
            Assert.Equal("2.967", Value(args, "-t"));
            Assert.True(args.IndexOf("-t") < args.IndexOf("-i"));

            media.FormatName = "mpegts";
            args = PartPlayer.SoundArgs(Clip, media, 12.5, 1.5);
            Assert.Equal("2.5", Value(args, "-ss"));
            Assert.Equal("11.5", Value(args, "-t"));
            Assert.Equal("10", args[args.LastIndexOf("-ss") + 1]);
        }
    }
}
