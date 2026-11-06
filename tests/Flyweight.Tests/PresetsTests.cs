using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    public class PresetsTests
    {
        [Fact]
        public void Light_keeps_the_full_picture()
        {
            var light = Presets.Default(LevelId.Light);
            Assert.Equal(0, light.MaxShortSide);
            Assert.Equal(0, light.MaxFps);
        }

        [Fact]
        public void Balanced_caps_at_1080p_and_keeps_frame_rate()
        {
            var balanced = Presets.Default(LevelId.Balanced);
            Assert.Equal(1080, balanced.MaxShortSide);
            Assert.Equal(0, balanced.MaxFps);
        }

        [Fact]
        public void Maximum_caps_at_720p_30fps()
        {
            var maximum = Presets.Default(LevelId.Maximum);
            Assert.Equal(720, maximum.MaxShortSide);
            Assert.Equal(30, maximum.MaxFps);
        }

        [Fact]
        public void Levels_differ_only_in_the_resolution_and_frame_rate_they_keep()
        {
            var light = Presets.Default(LevelId.Light);
            var balanced = Presets.Default(LevelId.Balanced);
            var maximum = Presets.Default(LevelId.Maximum);

            foreach (var level in new[] { balanced, maximum })
            {
                Assert.Equal(light.Codec, level.Codec);
                Assert.Equal(light.Quality, level.Quality);
                Assert.Equal(light.AudioKbps, level.AudioKbps);
            }
        }

        [Theory]
        [InlineData(LevelId.Light)]
        [InlineData(LevelId.Balanced)]
        [InlineData(LevelId.Maximum)]
        public void Every_level_starts_at_the_quality_chosen_for_its_format(LevelId level)
        {
            var settings = Presets.Default(level);
            Assert.Equal(Presets.DefaultQuality(settings.Codec), settings.Quality);
        }

        [Theory]
        [InlineData(VideoCodec.H264)]
        [InlineData(VideoCodec.Hevc)]
        [InlineData(VideoCodec.Av1)]
        public void Knows_its_own_quality_number_from_ones_a_user_picked(VideoCodec codec)
        {
            Assert.True(Presets.IsPresetQuality(codec, Presets.DefaultQuality(codec)));
            Assert.False(Presets.IsPresetQuality(codec, 11));
        }

        [Fact]
        public void The_first_versions_defaults_are_recognised_as_untouched_rather_than_chosen()
        {
            var light = new LevelSettings { Codec = VideoCodec.Hevc, Quality = 28, AudioKbps = 192, ExtraArgs = "" };
            var balanced = new LevelSettings { Codec = VideoCodec.Hevc, Quality = 31, MaxShortSide = 1080, AudioKbps = 128, ExtraArgs = "" };
            var maximum = new LevelSettings { Codec = VideoCodec.Hevc, Quality = 34, MaxShortSide = 720, MaxFps = 30, AudioKbps = 96, ExtraArgs = "" };

            Assert.True(Presets.IsRetiredDefault(LevelId.Light, light));
            Assert.True(Presets.IsRetiredDefault(LevelId.Balanced, balanced));
            Assert.True(Presets.IsRetiredDefault(LevelId.Maximum, maximum));

            balanced.MaxFps = 24;
            Assert.False(Presets.IsRetiredDefault(LevelId.Balanced, balanced));
            Assert.False(Presets.IsRetiredDefault(LevelId.Light, maximum));
        }

        [Fact]
        public void Defaults_use_the_automatic_encoder_with_no_size_limit_or_extras()
        {
            foreach (LevelId level in System.Enum.GetValues(typeof(LevelId)))
            {
                var s = Presets.Default(level);
                Assert.Equal(EncoderChoice.Auto, s.Encoder);
                Assert.Equal(0, s.TargetSizeMb);
                Assert.Equal("", s.ExtraArgs);
            }
        }

        [Fact]
        public void Default_returns_an_independent_copy_each_time()
        {
            var first = Presets.Default(LevelId.Balanced);
            first.Quality = 1;
            Assert.NotEqual(1, Presets.Default(LevelId.Balanced).Quality);
        }

        [Fact]
        public void Clone_is_independent_and_complete()
        {
            var a = new LevelSettings { Codec = VideoCodec.Av1, Encoder = EncoderChoice.Cpu, Quality = 7, MaxShortSide = 480, MaxFps = 24, AudioKbps = 64, TargetSizeMb = 9, ExtraArgs = "-x" };
            var b = a.Clone();
            a.Quality = 99;
            Assert.Equal(7, b.Quality);
            Assert.True(b.SameAs(new LevelSettings { Codec = VideoCodec.Av1, Encoder = EncoderChoice.Cpu, Quality = 7, MaxShortSide = 480, MaxFps = 24, AudioKbps = 64, TargetSizeMb = 9, ExtraArgs = "-x" }));
        }

        [Fact]
        public void SameAs_notices_any_change()
        {
            var a = Presets.Default(LevelId.Balanced);
            Assert.True(a.SameAs(Presets.Default(LevelId.Balanced)));
            var changed = Presets.Default(LevelId.Balanced);
            changed.MaxFps = 24;
            Assert.False(a.SameAs(changed));
        }
    }
}
