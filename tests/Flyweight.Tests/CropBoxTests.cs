using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>The box with four corners drawn over the picture, for choosing the part of it to keep.</summary>
    public class CropBoxTests
    {
        private const double Width = 560;
        private const double Height = 315;

        private static CropBox NewBox(double aspect = 16.0 / 9, int videoWidth = 1920, int videoHeight = 1080, double width = Width, double height = Height)
        {
            var box = new CropBox { Aspect = aspect, VideoWidth = videoWidth, VideoHeight = videoHeight };
            LayOut(box, width, height);
            return box;
        }

        private static void LayOut(CropBox box, double width, double height)
        {
            box.Measure(new Size(width, height));
            box.Arrange(new Rect(0, 0, width, height));
            box.UpdateLayout();
        }

        /// <summary>A press, a move and a release, as a hand dragging from one point to another.</summary>
        private static void Drag(CropBox box, Point from, Point to)
        {
            box.Press(from);
            box.MoveTo(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
            box.MoveTo(to);
            box.Release(to);
        }

        private static Point At(CropBox box, double x, double y) => new Point(box.Picture.Left + x * box.Picture.Width, box.Picture.Top + y * box.Picture.Height);

        private static void AssertRect(Rect expected, Rect actual)
        {
            Assert.True(Math.Abs(expected.X - actual.X) < 0.01 && Math.Abs(expected.Y - actual.Y) < 0.01
                && Math.Abs(expected.Width - actual.Width) < 0.01 && Math.Abs(expected.Height - actual.Height) < 0.01,
                $"expected {expected}, was {actual}");
        }

        private static void AssertArea(CropBox box, double left, double top, double right, double bottom)
        {
            var area = box.Area;
            string seen = $"{area.Left:0.000}, {area.Top:0.000}, {area.Right:0.000}, {area.Bottom:0.000}";
            Assert.True(Math.Abs(area.Left - left) < 0.002 && Math.Abs(area.Top - top) < 0.002
                && Math.Abs(area.Right - right) < 0.002 && Math.Abs(area.Bottom - bottom) < 0.002, seen);
        }

        [Fact]
        public void It_starts_as_the_whole_picture()
        {
            Ui.Run(() => Assert.True(NewBox().Area.IsWhole));
        }

        [Fact]
        public void Dragging_a_corner_in_crops_from_that_corner_and_says_so()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                var heard = new List<bool>();
                box.Changed += heard.Add;

                Drag(box, At(box, 0, 0), At(box, 0.25, 0.2));

                AssertArea(box, 0.25, 0.2, 1, 1);
                Assert.Equal(new[] { false, false, true }, heard);
            });
        }

        [Fact]
        public void Each_corner_moves_its_own_two_sides()
        {
            Ui.Run(() =>
            {
                var box = NewBox();

                Drag(box, At(box, 1, 1), At(box, 0.8, 0.7));
                Drag(box, At(box, 0.8, 0), At(box, 0.6, 0.1));
                Drag(box, At(box, 0, 0.7), At(box, 0.1, 0.5));

                AssertArea(box, 0.1, 0.1, 0.6, 0.5);
            });
        }

        [Fact]
        public void When_the_video_is_another_shape_than_the_space_the_corners_are_the_pictures_own()
        {
            Ui.Run(() =>
            {
                // Four by three in a space of sixteen by nine: 420 wide, with dark bars either side.
                var box = NewBox(aspect: 4.0 / 3);
                AssertRect(new Rect(70, 0, 420, 315), box.Picture);

                // A press out in the bar takes hold of nothing.
                Drag(box, new Point(2, 2), new Point(150, 80));
                Assert.True(box.Area.IsWhole);

                Drag(box, new Point(72, 2), new Point(70 + 105, 63));
                AssertArea(box, 0.25, 0.2, 1, 1);
            });
        }

        [Fact]
        public void Dragging_inside_moves_the_box_without_changing_its_size_and_it_stops_at_the_edges()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                Drag(box, At(box, 0, 0), At(box, 0.25, 0.25));
                Drag(box, At(box, 1, 1), At(box, 0.75, 0.75));

                Drag(box, At(box, 0.5, 0.5), At(box, 0.6, 0.4));
                AssertArea(box, 0.35, 0.15, 0.85, 0.65);

                // Pulled well past the edge, it goes as far as the edge and no further.
                Drag(box, At(box, 0.5, 0.5), At(box, 2, -1));
                AssertArea(box, 0.5, 0, 1, 0.5);
            });
        }

        [Fact]
        public void The_whole_picture_cannot_be_moved()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                var heard = new List<bool>();
                box.Changed += heard.Add;

                Drag(box, At(box, 0.5, 0.5), At(box, 0.7, 0.6));

                Assert.True(box.Area.IsWhole);
                Assert.Empty(heard);
            });
        }

        [Fact]
        public void A_corner_cannot_be_dragged_past_the_one_across_from_it()
        {
            Ui.Run(() =>
            {
                var box = NewBox();

                Drag(box, At(box, 0, 0), At(box, 1.2, 1.3));

                // It stops a short way from the far corner, so the box is still a box, and big enough to
                // take hold of. Here that's 160 of the video's 1920 pixels across, and 32 points down.
                AssertRect(new Rect(Width - 160 * Width / 1920, Height - CropBox.LeastSide, 160 * Width / 1920, CropBox.LeastSide), box.Box);
            });
        }

        [Theory]
        [InlineData(1920, 1080)]
        [InlineData(3840, 2160)]
        [InlineData(640, 360)]
        public void The_least_box_keeps_enough_of_the_video_for_a_graphics_card_to_encode(int videoWidth, int videoHeight)
        {
            Ui.Run(() =>
            {
                var box = NewBox(videoWidth: videoWidth, videoHeight: videoHeight);

                Drag(box, At(box, 0, 0), At(box, 1, 1));

                var keep = box.Area.In(videoWidth, videoHeight).Value;
                Assert.True(keep.Width >= 160 && keep.Height >= 80, $"{keep.Width} by {keep.Height}");
                Assert.True(box.Box.Width >= CropBox.LeastSide - 0.01 && box.Box.Height >= CropBox.LeastSide - 0.01, box.Box.ToString());
            });
        }

        [Fact]
        public void In_a_video_smaller_than_that_the_box_cannot_be_made_any_smaller_than_the_picture()
        {
            Ui.Run(() =>
            {
                var box = NewBox(videoWidth: 120, videoHeight: 68);

                Drag(box, At(box, 0, 0), At(box, 1, 1));

                Assert.True(box.Area.IsWhole);
            });
        }

        [Fact]
        public void A_side_let_go_of_near_the_edge_of_the_picture_goes_onto_it()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                Drag(box, At(box, 0, 0), At(box, 0.25, 0.2));

                // Back to within a few points of the corner, which a hand can't hit exactly.
                Drag(box, At(box, 0.25, 0.2), new Point(box.Picture.Left + 4, box.Picture.Top + 3));

                Assert.True(box.Area.IsWhole);
            });
        }

        [Fact]
        public void At_a_size_that_does_not_divide_evenly_the_sides_not_held_stay_exactly_where_they_were()
        {
            Ui.Run(() =>
            {
                // As at 125%, where sizes on screen come out in fractions of a point.
                var box = NewBox(width: 544.8, height: 306.45);

                box.Press(At(box, 0, 0));
                for (int i = 1; i <= 50; i++) box.MoveTo(At(box, 0.3 * i / 50, 0.2 * i / 50));
                box.Release(At(box, 0.3, 0.2));

                Assert.Equal(1, box.Area.Right);
                Assert.Equal(1, box.Area.Bottom);
                var keep = box.Area.In(1920, 1080).Value;
                Assert.Equal((1920, 1080), (keep.X + keep.Width, keep.Y + keep.Height));

                // And dragged back onto its corner, it's the whole picture again.
                box.Press(At(box, 0.3, 0.2));
                for (int i = 49; i >= 0; i--) box.MoveTo(At(box, 0.3 * i / 50, 0.2 * i / 50));
                box.Release(At(box, 0, 0));
                Assert.True(box.Area.IsWhole, $"{box.Area.Left}, {box.Area.Top}, {box.Area.Right}, {box.Area.Bottom}");
            });
        }

        [Fact]
        public void A_box_moved_onto_the_far_edges_is_exactly_on_them_and_the_same_size()
        {
            Ui.Run(() =>
            {
                var box = NewBox(width: 544.8, height: 306.45);
                Drag(box, At(box, 0, 0), At(box, 0.3, 0.3));
                Drag(box, At(box, 1, 1), At(box, 0.7, 0.7));

                box.Press(At(box, 0.5, 0.5));
                for (int i = 1; i <= 40; i++) box.MoveTo(At(box, 0.5 + i / 40.0, 0.5 + i / 40.0));
                box.Release(At(box, 1.5, 1.5));

                Assert.Equal(1, box.Area.Right);
                Assert.Equal(1, box.Area.Bottom);
                Assert.Equal(0.4, box.Area.Right - box.Area.Left, 9);
            });
        }

        [Fact]
        public void A_press_on_a_corner_can_be_taken_for_the_box_before_anything_over_it_sees_it()
        {
            Ui.Run(() =>
            {
                // On a narrow picture Play can sit over a bottom corner, and the corner still has to be draggable.
                var box = NewBox();

                Assert.False(box.TakeHold(At(box, 0.5, 0.5)));
                Assert.True(box.TakeHold(At(box, 0, 1)));
                box.MoveTo(At(box, 0.2, 0.8));
                box.Release(At(box, 0.2, 0.8));

                AssertArea(box, 0.2, 0, 1, 0.8);
            });
        }

        [Fact]
        public void Reset_puts_back_the_whole_picture_and_says_so()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                Drag(box, At(box, 0, 0), At(box, 0.25, 0.2));
                var heard = new List<bool>();
                box.Changed += heard.Add;

                box.Reset();

                Assert.True(box.Area.IsWhole);
                Assert.Equal(new[] { true }, heard);
            });
        }

        [Fact]
        public void The_box_keeps_to_the_same_part_of_the_picture_when_the_space_changes_size()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                Drag(box, At(box, 0, 0), At(box, 0.25, 0.2));

                // As when Advanced options open beside the picture, or a taller box is made for a phone video.
                LayOut(box, 400, 225);

                AssertRect(new Rect(100, 45, 300, 180), box.Box);
                AssertArea(box, 0.25, 0.2, 1, 1);
            });
        }

        [Fact]
        public void The_pointer_shows_what_a_press_would_do()
        {
            Ui.Run(() =>
            {
                var box = NewBox();

                box.MoveTo(At(box, 0, 0));
                Assert.Equal(Cursors.SizeNWSE, box.Cursor);
                box.MoveTo(At(box, 1, 0));
                Assert.Equal(Cursors.SizeNESW, box.Cursor);
                // Inside the whole picture there's nothing to move.
                box.MoveTo(At(box, 0.5, 0.5));
                Assert.Null(box.Cursor);

                Drag(box, At(box, 0, 0), At(box, 0.25, 0.2));
                box.MoveTo(At(box, 0.5, 0.5));
                Assert.Equal(Cursors.SizeAll, box.Cursor);
            });
        }

        [Fact]
        public void A_drag_that_something_else_interrupts_leaves_the_box_where_it_got_to()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                var heard = new List<bool>();
                box.Changed += heard.Add;

                box.Press(At(box, 0, 0));
                box.MoveTo(At(box, 0.25, 0.2));
                box.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.LostMouseCaptureEvent });
                box.MoveTo(At(box, 0.5, 0.5));

                AssertArea(box, 0.25, 0.2, 1, 1);
                Assert.Equal(new[] { false, true }, heard);
            });
        }

        [Fact]
        public void Holding_a_corner_still_zooms_in_around_it_and_letting_go_zooms_out()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                int heard = 0;
                box.ZoomChanged += () => heard++;
                box.Press(At(box, 0, 0));
                box.MoveTo(At(box, 0.25, 0.2));
                Assert.Equal(1, box.Zoom);

                box.Rest();   // what half a second without moving does

                Assert.Equal(4, box.Zoom);
                AssertPoint(At(box, 0.25, 0.2), box.ZoomedAt);
                Assert.Equal(1, heard);

                box.Release(At(box, 0.25, 0.2));
                Assert.Equal(1, box.Zoom);
                Assert.Equal(2, heard);
            });
        }

        [Fact]
        public void The_zoom_is_around_the_corner_itself_not_where_the_hand_took_hold_near_it()
        {
            Ui.Run(() =>
            {
                // So the corner stays where it was on screen, rather than being thrown out of sight.
                var box = NewBox();
                box.Press(At(box, 0, 0) + new Vector(8, 6));

                box.Rest();

                AssertPoint(At(box, 0, 0), box.ZoomedAt);
                box.Release(At(box, 0, 0));
            });
        }

        [Fact]
        public void It_takes_about_half_a_second_of_holding_still()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                var pressed = DateTime.UtcNow;
                box.Press(At(box, 1, 1));
                // A hand never holds quite still.
                box.MoveTo(At(box, 1, 1) + new Vector(-2, -1));

                Ui.Pump(() => box.Zoom > 1, 5000);

                Assert.InRange((DateTime.UtcNow - pressed).TotalSeconds, 0.45, 2);
                // Around the corner, which stayed on the edge of the picture.
                AssertPoint(At(box, 1, 1), box.ZoomedAt);
                box.Release(At(box, 1, 1));
            });
        }

        [Fact]
        public void A_corner_on_the_move_or_let_go_of_quickly_does_not_zoom()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                box.Press(At(box, 0, 0));
                var until = DateTime.UtcNow.AddMilliseconds(900);
                for (double x = 0.01; DateTime.UtcNow < until; x += 0.01)
                {
                    box.MoveTo(At(box, x, x / 2));
                    Wait(60);
                }
                Assert.Equal(1, box.Zoom);

                box.Release(At(box, 0.3, 0.15));
                Wait(700);
                Assert.Equal(1, box.Zoom);
            });
        }

        [Fact]
        public void Moving_the_whole_box_does_not_zoom()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                Drag(box, At(box, 0, 0), At(box, 0.5, 0.5));

                box.Press(At(box, 0.75, 0.75));
                box.Rest();
                Wait(700);

                Assert.Equal(1, box.Zoom);
                box.Release(At(box, 0.75, 0.75));
            });
        }

        [Fact]
        public void Zoomed_in_a_side_can_be_put_a_few_pixels_in_from_the_edge()
        {
            Ui.Run(() =>
            {
                // 1080 rows shown 315 points high: a point is about 3.4 of them, so six points of
                // reach onto the edge would leave nothing thinner than about 20 to crop away.
                var box = NewBox();
                box.Press(At(box, 0, 0));
                box.Rest();

                box.MoveTo(At(box, 0, 0) + new Vector(1.6, 1.6));
                box.Release(At(box, 0, 0) + new Vector(1.6, 1.6));

                var kept = box.Area.In(1920, 1080).Value;
                Assert.Equal((4, 4), (kept.X, kept.Y));
            });
        }

        [Fact]
        public void A_drag_cut_short_or_a_reset_zooms_out_as_well()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                box.Press(At(box, 1, 0));
                box.Rest();
                Assert.Equal(4, box.Zoom);
                box.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.LostMouseCaptureEvent });
                Assert.Equal(1, box.Zoom);

                box.Press(At(box, 1, 0));
                box.Rest();
                Assert.Equal(4, box.Zoom);
                box.Reset();
                Assert.Equal(1, box.Zoom);
            });
        }

        [Fact]
        public void Zoomed_in_the_corners_are_drawn_no_bigger_on_screen()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                var onAnArm = At(box, 0, 0) + new Vector(10, 1);
                Assert.True(AlphaAt(Drawn(box), onAnArm) > 200);

                box.Press(At(box, 0, 0));
                box.Rest();
                box.UpdateLayout();

                // Drawn a quarter the size here, it's the usual size once the picture is four times nearer.
                Assert.Equal(0, AlphaAt(Drawn(box), onAnArm));
                Assert.True(AlphaAt(Drawn(box), At(box, 0, 0) + new Vector(2.5, 0.25)) > 200);
                box.Release(At(box, 0, 0));
            });
        }

        private static void AssertPoint(Point expected, Point actual)
        {
            Assert.True((expected - actual).Length < 0.01, $"expected {expected}, was {actual}");
        }

        /// <summary>Lets the box's own clock run, as it would while a hand rests.</summary>
        private static void Wait(int ms)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            Ui.Pump(() => DateTime.UtcNow > until);
        }

        private static byte[] Drawn(CropBox box)
        {
            var bitmap = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(box);
            var dots = new byte[(int)Width * (int)Height * 4];
            bitmap.CopyPixels(dots, (int)Width * 4, 0);
            return dots;
        }

        private static byte AlphaAt(byte[] dots, Point point) => dots[((int)point.Y * (int)Width + (int)point.X) * 4 + 3];

        [Fact]
        public void What_is_left_out_is_dimmed_and_what_is_kept_is_not()
        {
            Ui.Run(() =>
            {
                var box = NewBox();
                byte[] whole = Drawn(box);
                Drag(box, At(box, 0, 0), At(box, 0.5, 0.5));
                box.UpdateLayout();
                byte[] cropped = Drawn(box);

                Assert.Equal(0, AlphaAt(whole, At(box, 0.25, 0.25)));
                Assert.True(AlphaAt(cropped, At(box, 0.25, 0.25)) > 100, "Left out, so it should be dimmed");
                Assert.Equal(0, AlphaAt(cropped, At(box, 0.75, 0.75)));
                // The corners are always there to take hold of.
                Assert.True(AlphaAt(whole, At(box, 0, 0) + new Vector(1, 1)) > 200);
            });
        }
    }
}
