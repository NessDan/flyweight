using System.Collections.Generic;
using Flyweight.Core;
using Xunit;
using static Flyweight.Tests.Fixtures;

namespace Flyweight.Tests
{
    /// <summary>What ffmpeg is asked to do with a crop.</summary>
    public class CropCommandTests
    {
        private static readonly CropArea Quarter = new CropArea(0.25, 0.25, 0.75, 0.75);

        private static LevelSettings Settings(int maxShortSide = 0, int maxFps = 0, int targetMb = 0, VideoCodec codec = VideoCodec.Hevc)
        {
            return new LevelSettings
            {
                Codec = codec, Encoder = EncoderChoice.Auto, Quality = 30, MaxShortSide = maxShortSide, MaxFps = maxFps,
                AudioKbps = 128, TargetSizeMb = targetMb, ExtraArgs = "",
            };
        }

        private static List<string> Build(MediaInfo media, LevelSettings settings, CropArea crop)
        {
            return CommandBuilder.Build(@"C:\v\in.mov", @"C:\v\out.part", media, settings, EncoderKind.Nvenc, 8, crop: crop);
        }

        private static string Value(List<string> args, string flag)
        {
            int i = args.IndexOf(flag);
            return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        }

        private static string Filters(List<string> args) => Value(args, "-vf") ?? "";

        private static List<string> Without(List<string> args, string flag)
        {
            var rest = new List<string>(args);
            rest.RemoveRange(rest.IndexOf(flag), 2);
            return rest;
        }

        [Fact]
        public void The_crop_is_the_first_thing_done_to_the_picture()
        {
            var args = Build(Media(Video(640, 360, fps: 30)), Settings(), new CropArea(0.125, 0.1, 0.5, 0.6));

            Assert.StartsWith("crop=240:180:80:36,", Filters(args));
        }

        [Fact]
        public void An_interlaced_picture_is_made_whole_before_it_is_cropped()
        {
            var args = Build(Media(Video(720, 480, fps: 30, field: "tt")), Settings(), Quarter);

            Assert.StartsWith("bwdif=", Filters(args));
            Assert.Contains(",crop=360:240:180:120,", Filters(args));
        }

        [Fact]
        public void It_comes_before_frames_are_dropped_and_before_HDR_is_brought_down()
        {
            var media = Media(Video(1920, 1080, fps: 60, transfer: "smpte2084"));

            string filters = Filters(Build(media, Settings(maxFps: 30, codec: VideoCodec.H264), Quarter));

            Assert.StartsWith("crop=960:540:480:270,", filters);
            Assert.True(filters.IndexOf("fps=30") < filters.IndexOf("tonemap"), filters);
        }

        [Fact]
        public void A_part_already_within_the_size_cap_is_not_shrunk()
        {
            // A 1080p piece of a 4K video, on a level that brings anything larger down to 1080p.
            string filters = Filters(Build(Media(Video(3840, 2160, fps: 30)), Settings(maxShortSide: 1080), Quarter));

            Assert.DoesNotContain("scale=", filters);
        }

        [Fact]
        public void A_part_larger_than_the_size_cap_is_shrunk_in_its_own_shape()
        {
            // The left half of a 4K picture is 1920 wide and 2160 tall. Brought down to 720 on its
            // shorter side, that's 810 tall. The whole picture would have been 1280 by 720.
            string filters = Filters(Build(Media(Video(3840, 2160, fps: 30)), Settings(maxShortSide: 720), new CropArea(0, 0, 0.5, 1)));

            Assert.Contains("scale=720:810", filters);
        }

        [Fact]
        public void A_sideways_video_is_cropped_the_way_it_is_shown()
        {
            // Stored on its side, shown upright at 1080 wide and 1920 tall, so its left half is 540 wide.
            var args = Build(Media(Video(1920, 1080, fps: 30, rotation: 90)), Settings(), new CropArea(0, 0, 0.5, 1));

            Assert.StartsWith("crop=540:1920:0:0,", Filters(args));
        }

        [Fact]
        public void The_whole_picture_is_no_crop_at_all()
        {
            var media = Media(Video(641, 361, fps: 30));

            Assert.Equal(Build(media, Settings(), null), Build(media, Settings(), CropArea.Whole));
        }

        [Fact]
        public void Cropping_alone_changes_nothing_but_the_picture()
        {
            // Subtitles and chapters keep their times, and the sound goes as it would for any video.
            var media = Media(Video(1920, 1080, fps: 30), 60, Audio(codec: "aac", kbps: 128));
            media.TextSubtitleIndexes = new List<int> { 2 };

            var plain = Build(media, Settings(), null);
            var cropped = Build(media, Settings(), Quarter);

            Assert.Equal(Without(plain, "-vf"), Without(cropped, "-vf"));
            Assert.Contains("0:2", cropped);
            Assert.Equal("0", Value(cropped, "-map_chapters"));
        }

        [Fact]
        public void A_tight_size_limit_steps_down_only_as_far_as_the_part_kept_needs()
        {
            // Six megabytes for a minute is too little for 1080p, so the whole picture comes down to
            // 720p. A quarter of it gets bits enough for every pixel at the size it is.
            var media = Media(Video(1920, 1080, fps: 30));

            Assert.Contains("scale=1280:720", Filters(Build(media, Settings(targetMb: 6), null)));
            Assert.DoesNotContain("scale=", Filters(Build(media, Settings(targetMb: 6), Quarter)));
        }

        [Fact]
        public void A_tight_size_limit_keeps_the_frame_rate_when_the_part_kept_can_afford_it()
        {
            // Eight megabytes for a minute of 1080p60: the whole picture has to drop to 30 frames a
            // second, but a quarter of it has bits enough for all 60.
            var media = Media(Video(1920, 1080, fps: 60));

            Assert.Contains("fps=30", Filters(Build(media, Settings(targetMb: 8), null)));
            Assert.DoesNotContain("fps=", Filters(Build(media, Settings(targetMb: 8), Quarter)));
        }

        [Fact]
        public void The_video_it_was_given_is_left_as_it_was()
        {
            // The same facts about a video are used again for every retry of a compress.
            var media = Media(Video(1920, 1080, fps: 30));

            Build(media, Settings(), Quarter);

            Assert.Equal(1920, media.Video.Width);
            Assert.Equal(1080, media.Video.Height);
        }
    }
}
