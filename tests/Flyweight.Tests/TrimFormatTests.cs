using System.Globalization;
using System.Threading;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    public class TrimFormatTests
    {
        [Theory]
        [InlineData(0.0, "0:00.000")]
        [InlineData(12.4, "0:12.400")]
        [InlineData(72.433, "1:12.433")]
        [InlineData(3723.5, "1:02:03.500")]
        [InlineData(59.9996, "1:00.000")]
        public void Exact_times_show_minutes_seconds_and_thousandths(double seconds, string expected)
        {
            Assert.Equal(expected, Format.Timecode(seconds));
        }

        [Theory]
        [InlineData(41.63, "0:41.6")]
        [InlineData(59.96, "1:00.0")]
        [InlineData(190.0, "3:10.0")]
        public void Lengths_can_be_shown_to_the_tenth(double seconds, string expected)
        {
            Assert.Equal(expected, Format.Timecode(seconds, 1));
        }

        [Theory]
        [InlineData("12.4", 12.4)]
        [InlineData("0:12.4", 12.4)]
        [InlineData("1:12", 72.0)]
        [InlineData("1:02:03.5", 3723.5)]
        [InlineData("12,5", 12.5)]
        [InlineData(" 5 ", 5.0)]
        [InlineData("90", 90.0)]
        [InlineData("0:00.000", 0.0)]
        public void Typed_times_are_understood(string text, double expected)
        {
            Assert.True(Format.TryParseTimecode(text, out double seconds));
            Assert.Equal(expected, seconds, 6);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("soon")]
        [InlineData("-3")]
        [InlineData("1:99")]
        [InlineData("1:2:3:4")]
        [InlineData("1:")]
        public void Nonsense_is_not_a_time(string text)
        {
            Assert.False(Format.TryParseTimecode(text, out _));
        }

        [Fact]
        public void Times_read_and_write_the_same_whatever_the_language()
        {
            var before = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                Assert.Equal("0:12.400", Format.Timecode(12.4));
                Assert.True(Format.TryParseTimecode("0:12.4", out double seconds));
                Assert.Equal(12.4, seconds, 6);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }

        [Fact]
        public void A_trimmed_result_says_how_long_and_how_big_it_is()
        {
            Assert.Equal("Trimmed to 0:42 · 9.6 MB", Format.Trimmed(41.6, 10066330L));
        }
    }
}
