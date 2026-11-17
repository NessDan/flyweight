using System.Collections.Generic;
using Flyweight.Core;
using Xunit;
using static Flyweight.Tests.Fixtures;

namespace Flyweight.Tests
{
    public class TrimCommandTests
    {
        private const string Input = @"C:\videos\in.mov";
        private const string Output = @"C:\videos\out.part";

        private static LevelSettings Settings(int targetMb = 0)
        {
            return new LevelSettings { Codec = VideoCodec.Hevc, Encoder = EncoderChoice.Auto, Quality = 30, AudioKbps = 96, TargetSizeMb = targetMb, ExtraArgs = "" };
        }

        private static List<string> Build(MediaInfo media, LevelSettings settings, TrimRange trim, EncoderKind encoder = EncoderKind.Nvenc)
        {
            return CommandBuilder.Build(Input, Output, media, settings, encoder, cpuThreads: 8, targetScale: 1.0, trim: trim);
        }

        private static string Value(List<string> args, string flag)
        {
            int i = args.IndexOf(flag);
            return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        }

        [Theory]
        [InlineData(EncoderKind.Nvenc)]
        [InlineData(EncoderKind.Cpu)]
        public void A_trim_seeks_and_limits_the_input_so_the_cut_is_exact(EncoderKind encoder)
        {
            var args = Build(Media(null, 600), Settings(), new TrimRange(12.5, 42.75), encoder);

            Assert.Equal("12.5", Value(args, "-ss"));
            Assert.Equal("30.25", Value(args, "-t"));
            // Ahead of the input, ffmpeg decodes up to the exact frame. After it they would mean something else.
            Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"));
            Assert.True(args.IndexOf("-t") < args.IndexOf("-i"));
        }

        [Fact]
        public void A_trim_from_the_very_start_still_seeks_so_that_times_count_from_where_the_file_starts()
        {
            // Without it ffmpeg keeps the file's own clock, which doesn't always start at zero.
            var args = Build(Media(null, 600), Settings(), new TrimRange(0, 20));
            Assert.Equal("0", Value(args, "-ss"));
            Assert.Equal("20", Value(args, "-t"));
            Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"));
        }

        [Fact]
        public void The_end_is_also_set_in_the_filters_because_only_there_is_it_exact()
        {
            // ffmpeg counts -t from the first frame it keeps, which can let one frame too many through.
            var args = Build(Media(null, 600), Settings(), new TrimRange(12.5, 42.75));
            Assert.StartsWith("trim=end=30.25,", Value(args, "-vf"));
            Assert.DoesNotContain("trim=", Value(Build(Media(null, 600), Settings(), null), "-vf"));
        }

        [Fact]
        public void A_stream_file_is_read_from_earlier_and_the_lead_in_thrown_away()
        {
            // With no list of keyframes to jump by, seeking straight there loses everything up to the next one.
            var media = Media(null, 600, Audio());
            media.FormatName = "mpegts";

            var args = Build(media, Settings(), new TrimRange(12.5, 42.75));

            int input = args.IndexOf("-i");
            Assert.Equal("2.5", args[args.IndexOf("-ss") + 1]);
            Assert.Equal("40.25", args[args.IndexOf("-t") + 1]);
            Assert.True(args.IndexOf("-t") < input);
            // The second -ss comes after the input, where it means "decode, then drop this much".
            Assert.Equal("10", args[args.LastIndexOf("-ss") + 1]);
            Assert.True(args.LastIndexOf("-ss") > input);
            Assert.StartsWith("trim=end=40.25,", Value(args, "-vf"));
        }

        [Fact]
        public void A_stream_file_cut_near_its_start_is_simply_read_from_the_start()
        {
            var media = Media(null, 600, Audio());
            media.FormatName = "mpeg";

            var args = Build(media, Settings(), new TrimRange(3, 5));

            Assert.Equal("0", args[args.IndexOf("-ss") + 1]);
            Assert.Equal("5", args[args.IndexOf("-t") + 1]);
            Assert.Equal("3", args[args.LastIndexOf("-ss") + 1]);
            Assert.StartsWith("trim=end=5,", Value(args, "-vf"));
        }

        [Fact]
        public void A_file_that_lists_its_keyframes_is_seeked_straight_to_the_start()
        {
            var args = Build(Media(null, 600, Audio()), Settings(), new TrimRange(12.5, 42.75));
            Assert.Equal(args.IndexOf("-ss"), args.LastIndexOf("-ss"));
        }

        [Theory]
        [InlineData(96)]
        [InlineData(LevelSettings.AudioKeep)]
        public void Sound_is_never_copied_into_a_trimmed_result(int audioKbps)
        {
            // Copied sound can only be cut in whole packets, and drags along what came before the cut.
            var media = Media(null, 600, Audio(kbps: 96));
            var settings = Settings();
            settings.AudioKbps = audioKbps;

            Assert.Equal("copy", Value(Build(media, settings, null), "-c:a:0"));

            var trimmed = Build(media, settings, new TrimRange(12.5, 42.75));
            Assert.Equal("aac", Value(trimmed, "-c:a:0"));
            Assert.DoesNotContain("copy", trimmed);
        }

        [Fact]
        public void Sound_kept_as_it_is_gets_a_generous_bitrate_when_a_trim_forces_it_to_be_redone()
        {
            var settings = Settings();
            settings.AudioKbps = LevelSettings.AudioKeep;

            var modest = Build(Media(null, 600, Audio(kbps: 96)), settings, new TrimRange(1, 2));
            var rich = Build(Media(null, 600, Audio(kbps: 384)), settings, new TrimRange(1, 2));

            Assert.Equal("256k", Value(modest, "-b:a:0"));
            Assert.Equal("384k", Value(rich, "-b:a:0"));
        }

        [Fact]
        public void Subtitles_and_chapters_are_left_out_of_a_trimmed_result()
        {
            // Their times belong to the whole video, and their text would include what was cut away.
            var media = Media(null, 600, Audio());
            media.TextSubtitleIndexes.Add(2);

            var whole = Build(media, Settings(), null);
            Assert.Contains("0:2", whole);
            Assert.Equal("mov_text", Value(whole, "-c:s"));
            Assert.Equal("0", Value(whole, "-map_chapters"));

            var trimmed = Build(media, Settings(), new TrimRange(12.5, 42.75));
            Assert.DoesNotContain("0:2", trimmed);
            Assert.DoesNotContain("-c:s", trimmed);
            Assert.Equal("-1", Value(trimmed, "-map_chapters"));
        }

        [Fact]
        public void Without_a_trim_the_whole_video_is_read()
        {
            var args = Build(Media(null, 600), Settings(), null);
            Assert.DoesNotContain("-ss", args);
            Assert.DoesNotContain("-t", args);
        }

        [Fact]
        public void A_size_limit_is_spent_on_the_kept_part_alone()
        {
            var trimmed = Build(Media(null, 600, Audio()), Settings(targetMb: 10), new TrimRange(100, 160));
            var sameLength = Build(Media(null, 60, Audio()), Settings(targetMb: 10), null);
            var whole = Build(Media(null, 600, Audio()), Settings(targetMb: 10), null);

            // 10 MB over the 60 s kept, as in CommandBuilderTests: 1171 kbps for the picture.
            Assert.Equal("1171k", Value(trimmed, "-b:v"));
            Assert.Equal(Value(sameLength, "-b:v"), Value(trimmed, "-b:v"));
            Assert.NotEqual(Value(whole, "-b:v"), Value(trimmed, "-b:v"));
        }
    }
}
