using System;
using System.Globalization;
using System.Threading;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    public class TrimRangeTests
    {
        [Fact]
        public void Times_are_kept_to_the_millisecond()
        {
            var range = new TrimRange(1.23456, 5.00049);
            Assert.Equal(1.235, range.StartSeconds, 6);
            Assert.Equal(5.0, range.EndSeconds, 6);
            Assert.Equal(3.765, range.LengthSeconds, 6);
        }

        [Theory]
        [InlineData(-1.0, 5.0)]
        [InlineData(5.0, 5.0)]
        [InlineData(5.0, 4.0)]
        [InlineData(double.NaN, 4.0)]
        [InlineData(1.0, double.PositiveInfinity)]
        public void A_range_has_to_run_forwards_from_the_start_or_later(double start, double end)
        {
            Assert.ThrowsAny<ArgumentException>(() => new TrimRange(start, end));
        }

        [Fact]
        public void A_range_past_the_end_of_the_video_is_cut_back_to_it()
        {
            var clamped = new TrimRange(2, 100).ClampTo(10);
            Assert.Equal(2, clamped.StartSeconds, 6);
            Assert.Equal(10, clamped.EndSeconds, 6);
            Assert.Equal(8, clamped.LengthSeconds, 6);
        }

        [Fact]
        public void A_range_inside_the_video_is_left_alone()
        {
            var clamped = new TrimRange(2, 5).ClampTo(10);
            Assert.Equal(2, clamped.StartSeconds, 6);
            Assert.Equal(5, clamped.EndSeconds, 6);
            Assert.Equal(3, clamped.LengthSeconds, 6);
        }

        [Fact]
        public void A_range_that_starts_after_the_video_ends_leaves_nothing()
        {
            Assert.Null(new TrimRange(12, 20).ClampTo(10));
        }

        [Fact]
        public void A_video_of_unknown_length_cannot_limit_a_range()
        {
            var clamped = new TrimRange(2, 5).ClampTo(0);
            Assert.Equal(5, clamped.EndSeconds, 6);
        }

        [Fact]
        public void Times_for_ffmpeg_use_a_dot_whatever_the_language()
        {
            var before = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                Assert.Equal("12.5", TrimRange.Seconds(12.5));
                Assert.Equal("3", TrimRange.Seconds(3));
                Assert.Equal("0.017", TrimRange.Seconds(0.0166));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }
    }
}
