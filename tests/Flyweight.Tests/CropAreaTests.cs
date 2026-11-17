using System;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>A crop box, chosen as fractions of the picture, and the pixels that comes to.</summary>
    public class CropAreaTests
    {
        [Fact]
        public void An_off_centre_box_keeps_the_pixels_under_it()
        {
            var keep = new CropArea(0.125, 0.1, 0.5, 0.6).In(640, 360);

            Assert.Equal((80, 36, 240, 180), keep.Value);
        }

        [Fact]
        public void Every_side_and_place_comes_out_even_as_video_needs()
        {
            // Fractions that fall between pixels, in a frame of odd size.
            var keep = new CropArea(0.25, 0.25, 0.75, 0.75).In(641, 361);

            Assert.Equal((160, 90, 320, 180), keep.Value);
        }

        [Theory]
        [InlineData(641, 361)]
        [InlineData(640, 360)]
        public void A_tiny_box_in_the_far_corner_is_still_inside_the_frame(int width, int height)
        {
            var keep = new CropArea(0.999, 0.999, 1, 1).In(width, height).Value;

            Assert.Equal(2, keep.Width);
            Assert.Equal(2, keep.Height);
            Assert.True(keep.X + keep.Width <= width && keep.Y + keep.Height <= height, keep.ToString());
            Assert.True(keep.X % 2 == 0 && keep.Y % 2 == 0, keep.ToString());
        }

        [Theory]
        [InlineData(1920, 1080)]
        [InlineData(641, 361)]   // where no even size is the whole of it
        public void The_whole_picture_or_as_near_as_makes_no_pixel_of_difference_is_no_crop(int width, int height)
        {
            Assert.Null(CropArea.Whole.In(width, height));
            Assert.Null(new CropArea(0.0004, 0, 1, 1).In(width, height));
        }

        [Fact]
        public void A_side_on_the_edge_but_for_rounding_is_on_the_edge()
        {
            // What adding up fractions of a picture shown at 125% can leave in place of exactly 1.
            var keep = new CropArea(0.25, 0.25, 0.9999999999999999, 0.9999999999999998).In(1920, 1080).Value;

            Assert.Equal(1920, keep.X + keep.Width);
            Assert.Equal(1080, keep.Y + keep.Height);
        }

        [Fact]
        public void A_box_a_hair_from_the_edges_is_the_whole_picture()
        {
            // So it isn't drawn, or offered to be put back, as a crop.
            Assert.True(new CropArea(1e-12, 0, 0.9999999999999999, 1).IsWhole);
            Assert.False(new CropArea(0.001, 0, 1, 1).IsWhole);
        }

        [Fact]
        public void A_box_slid_along_keeps_its_size()
        {
            // Half the width of a picture 641 wide, at two places a pixel apart.
            double first = 64.9 / 641, second = 65.9 / 641;

            var here = new CropArea(first, 0, first + 0.5, 1).In(641, 361).Value;
            var there = new CropArea(second, 0, second + 0.5, 1).In(641, 361).Value;

            Assert.Equal(here.Width, there.Width);
        }

        [Fact]
        public void A_frame_of_no_known_size_has_nothing_to_crop()
        {
            Assert.Null(new CropArea(0.25, 0.25, 0.75, 0.75).In(0, 0));
        }

        [Fact]
        public void Says_how_much_of_the_picture_it_keeps()
        {
            Assert.Equal(0.25, new CropArea(0.25, 0.25, 0.75, 0.75).ShareOf(640, 360), 3);
            Assert.Equal(1, CropArea.Whole.ShareOf(640, 360));
        }

        [Theory]
        [InlineData(0.5, 0, 0.5, 1)]     // no width
        [InlineData(0, 0.6, 1, 0.4)]     // upside down
        [InlineData(-0.1, 0, 1, 1)]      // off the picture
        [InlineData(0, 0, 1.2, 1)]
        [InlineData(double.NaN, 0, 1, 1)]
        public void A_box_that_is_not_inside_the_picture_is_refused(double left, double top, double right, double bottom)
        {
            Assert.Throws<ArgumentException>(() => new CropArea(left, top, right, bottom));
        }
    }
}
