using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    public class LevelTextTests
    {
        [Theory]
        [InlineData(LevelId.Light, "full resolution and frame rate")]
        [InlineData(LevelId.Balanced, "1080p")]
        [InlineData(LevelId.Maximum, "720p at 30 fps")]
        public void An_untouched_level_says_what_it_keeps(LevelId level, string expected)
        {
            Assert.Contains(expected, LevelText.Summary(level, Presets.Default(level)));
        }

        [Theory]
        [InlineData(LevelId.Light)]
        [InlineData(LevelId.Balanced)]
        [InlineData(LevelId.Maximum)]
        public void No_level_promises_it_will_look_the_same_any_more(LevelId level)
        {
            Assert.DoesNotContain("same as the original", LevelText.Summary(level, Presets.Default(level)));
        }

        [Fact]
        public void A_custom_preset_is_described_without_calling_it_changed()
        {
            var settings = Presets.Default(LevelId.Balanced);
            settings.TargetSizeMb = 10;

            string summary = LevelText.Describe(settings);

            Assert.DoesNotContain("Custom", summary);
            Assert.Contains("under 10 MB", summary);
            Assert.Contains("H.265", summary);
        }

        [Theory]
        [InlineData(10, 720, 30, "Under 10 MB")]
        [InlineData(0, 720, 30, "Up to 720p, 30 fps")]
        [InlineData(0, 1080, 0, "Up to 1080p")]
        [InlineData(0, 0, 30, "Full resolution, 30 fps")]
        [InlineData(0, 0, 0, "Full resolution")]
        public void A_custom_preset_card_says_what_it_does_in_a_few_words(int targetMb, int shortSide, int fps, string expected)
        {
            var settings = Presets.Default(LevelId.Balanced);
            settings.TargetSizeMb = targetMb;
            settings.MaxShortSide = shortSide;
            settings.MaxFps = fps;

            Assert.Equal(expected, LevelText.Caption(settings));
        }

        [Fact]
        public void A_changed_level_says_exactly_what_it_will_do()
        {
            var settings = Presets.Default(LevelId.Balanced);
            settings.Codec = VideoCodec.H264;
            settings.MaxShortSide = 480;
            settings.MaxFps = 24;
            settings.AudioKbps = LevelSettings.AudioRemove;

            string summary = LevelText.Summary(LevelId.Balanced, settings);

            Assert.StartsWith("Custom:", summary);
            Assert.Contains("H.264", summary);
            Assert.Contains("480p", summary);
            Assert.Contains("24 fps", summary);
            Assert.Contains("no audio", summary);
        }

        [Fact]
        public void A_changed_level_mentions_the_size_limit_and_processor()
        {
            var settings = Presets.Default(LevelId.Light);
            settings.TargetSizeMb = 10;
            settings.Encoder = EncoderChoice.Cpu;

            string summary = LevelText.Summary(LevelId.Light, settings);

            Assert.Contains("under 10 MB", summary);
            Assert.Contains("processor", summary);
        }

        [Fact]
        public void A_changed_level_keeping_everything_says_so()
        {
            var settings = Presets.Default(LevelId.Maximum);
            settings.MaxShortSide = 0;
            settings.MaxFps = 0;
            settings.AudioKbps = LevelSettings.AudioKeep;

            string summary = LevelText.Summary(LevelId.Maximum, settings);

            Assert.Contains("full resolution", summary);
            Assert.Contains("original audio", summary);
            Assert.DoesNotContain("fps", summary);
        }
    }
}
