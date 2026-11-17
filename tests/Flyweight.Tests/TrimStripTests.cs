using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    public class TrimStripTests
    {
        private const double Width = 516;

        private static TrimStrip NewStrip(TrimSelection selection)
        {
            var strip = new TrimStrip();
            strip.Attach(selection);
            strip.Measure(new Size(Width, 52));
            strip.Arrange(new Rect(0, 0, Width, 52));
            return strip;
        }

        [Fact]
        public void The_video_spans_the_strip_between_its_end_caps()
        {
            Ui.Run(() =>
            {
                var strip = NewStrip(new TrimSelection(10, 30));
                Assert.Equal(TrimStrip.Inset, strip.XOf(0), 3);
                Assert.Equal(Width - TrimStrip.Inset, strip.XOf(10), 3);
                Assert.Equal(2.5, strip.SecondsAt(strip.XOf(2.5)), 6);
            });
        }

        [Fact]
        public void Dragging_a_handle_moves_that_end_and_says_so()
        {
            Ui.Run(() =>
            {
                var selection = new TrimSelection(10, 30);
                var strip = NewStrip(selection);
                var heard = new List<string>();
                strip.Moved += (handle, settled) => heard.Add(handle + (settled ? " settled" : " moving"));

                strip.DragTo(TrimHandle.Start, strip.XOf(2.5), settled: false);
                strip.DragTo(TrimHandle.Start, strip.XOf(3), settled: true);
                strip.DragTo(TrimHandle.End, strip.XOf(8), settled: true);

                Assert.Equal(3, selection.Start, 2);
                Assert.Equal(8, selection.End, 2);
                Assert.Equal(new[] { "Start moving", "Start settled", "End settled" }, heard);
                Assert.Equal(TrimHandle.End, strip.Active);
            });
        }

        [Fact]
        public void Pressing_on_the_strip_takes_hold_of_the_nearer_handle()
        {
            Ui.Run(() =>
            {
                var selection = new TrimSelection(10, 30);
                selection.Move(TrimHandle.Start, 2);
                selection.Move(TrimHandle.End, 8);
                var strip = NewStrip(selection);

                Assert.Equal(TrimHandle.Start, strip.HandleNear(strip.XOf(1)));
                Assert.Equal(TrimHandle.Start, strip.HandleNear(strip.XOf(4)));
                Assert.Equal(TrimHandle.End, strip.HandleNear(strip.XOf(6.5)));
                Assert.Equal(TrimHandle.End, strip.HandleNear(strip.XOf(9.5)));
            });
        }

        // No real mouse is involved in these, so every event arrives at the strip's left edge,
        // which is nearest the start handle. What they check is the order of things.

        private static void Raise(TrimStrip strip, RoutedEvent mouseEvent)
        {
            var args = mouseEvent == UIElement.MouseMoveEvent || mouseEvent == UIElement.LostMouseCaptureEvent
                ? new MouseEventArgs(Mouse.PrimaryDevice, 0)
                : new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left);
            args.RoutedEvent = mouseEvent;
            strip.RaiseEvent(args);
        }

        private static (TrimStrip Strip, TrimSelection Selection, List<string> Heard) StripWithPartPicked()
        {
            var selection = new TrimSelection(10, 30);
            selection.Move(TrimHandle.Start, 2);
            selection.Move(TrimHandle.End, 8);
            var strip = NewStrip(selection);
            var heard = new List<string>();
            strip.Moved += (handle, settled) => heard.Add(handle + (settled ? " settled" : " moving"));
            return (strip, selection, heard);
        }

        [Fact]
        public void A_press_a_move_and_a_release_drag_a_handle_then_let_it_rest()
        {
            Ui.Run(() =>
            {
                var (strip, selection, heard) = StripWithPartPicked();

                Raise(strip, UIElement.MouseMoveEvent);
                Assert.Empty(heard);   // passing over it without pressing does nothing

                Raise(strip, UIElement.MouseLeftButtonDownEvent);
                Raise(strip, UIElement.MouseMoveEvent);
                Raise(strip, UIElement.MouseLeftButtonUpEvent);
                Raise(strip, UIElement.MouseMoveEvent);

                Assert.Equal(new[] { "Start moving", "Start moving", "Start settled" }, heard);
                Assert.Equal(0, selection.Start, 3);
                Assert.Equal(8, selection.End, 3);
            });
        }

        [Fact]
        public void A_drag_cut_short_leaves_the_handle_where_it_got_to()
        {
            Ui.Run(() =>
            {
                var (strip, _, heard) = StripWithPartPicked();

                Raise(strip, UIElement.MouseLeftButtonDownEvent);
                Raise(strip, UIElement.LostMouseCaptureEvent);
                Raise(strip, UIElement.MouseLeftButtonUpEvent);
                Raise(strip, UIElement.MouseMoveEvent);

                Assert.Equal(new[] { "Start moving", "Start settled" }, heard);
            });
        }

        // These say where the pointer is, which the events above can't.

        [Fact]
        public void A_click_between_the_handles_picks_a_moment_to_play_from_and_moves_nothing()
        {
            Ui.Run(() =>
            {
                var (strip, selection, heard) = StripWithPartPicked();   // keeps 2 s to 8 s
                var picked = new List<double>();
                strip.Picked += picked.Add;

                strip.Press(strip.XOf(5));
                Assert.Empty(picked);   // not until it's let go: it might yet be a drag
                strip.Release(strip.XOf(5));

                Assert.Equal(5, Assert.Single(picked), 2);
                Assert.Empty(heard);
                Assert.Equal(2, selection.Start, 3);
                Assert.Equal(8, selection.End, 3);
            });
        }

        [Fact]
        public void A_hand_that_wobbles_a_little_while_clicking_has_still_clicked()
        {
            Ui.Run(() =>
            {
                var (strip, _, heard) = StripWithPartPicked();
                var picked = new List<double>();
                strip.Picked += picked.Add;

                strip.Press(strip.XOf(5));
                strip.MoveTo(strip.XOf(5) + 2);
                strip.Release(strip.XOf(5) + 2);

                Assert.Equal(5, Assert.Single(picked), 2);
                Assert.Empty(heard);
            });
        }

        [Fact]
        public void A_press_between_the_handles_never_moves_one_however_far_it_wanders_before_it_is_let_go()
        {
            Ui.Run(() =>
            {
                var (strip, selection, heard) = StripWithPartPicked();
                var picked = new List<double>();
                strip.Picked += picked.Add;

                // A handle set with care shouldn't jump to the pointer because a hand slid while clicking.
                strip.Press(strip.XOf(4));
                strip.MoveTo(strip.XOf(4.5));
                strip.MoveTo(strip.XOf(7));
                strip.Release(strip.XOf(7));

                Assert.Empty(heard);
                Assert.Equal(2, selection.Start, 3);
                Assert.Equal(8, selection.End, 3);
                // It plays from where it was pressed.
                Assert.Equal(4, Assert.Single(picked), 2);
            });
        }

        [Theory]
        [InlineData(2, 6, "Start")]      // just inside the start handle
        [InlineData(8, -6, "End")]       // just inside the end handle
        [InlineData(1, 0, "Start")]      // out in what's being left off, at either end
        [InlineData(9.5, 0, "End")]
        public void A_press_on_a_handle_or_outside_the_part_kept_takes_hold_of_the_handle_at_once(double seconds, double nudge, string handle)
        {
            Ui.Run(() =>
            {
                var (strip, _, heard) = StripWithPartPicked();
                var picked = new List<double>();
                strip.Picked += picked.Add;

                strip.Press(strip.XOf(seconds) + nudge);
                strip.Release(strip.XOf(seconds) + nudge);

                Assert.Equal(new[] { handle + " moving", handle + " settled" }, heard);
                Assert.Empty(picked);
            });
        }

        [Fact]
        public void With_the_handles_close_together_any_press_between_them_is_on_a_handle()
        {
            Ui.Run(() =>
            {
                var selection = new TrimSelection(10, 30);
                selection.Move(TrimHandle.Start, 5);
                selection.Move(TrimHandle.End, 5.3);   // 15 dots apart
                var strip = NewStrip(selection);
                var picked = new List<double>();
                strip.Picked += picked.Add;

                strip.Press(strip.XOf(5.15));
                strip.Release(strip.XOf(5.15));

                Assert.Empty(picked);
            });
        }

        [Fact]
        public void A_click_that_something_else_interrupts_is_not_a_click()
        {
            Ui.Run(() =>
            {
                var (strip, _, heard) = StripWithPartPicked();
                var picked = new List<double>();
                strip.Picked += picked.Add;

                strip.Press(strip.XOf(5));
                Raise(strip, UIElement.LostMouseCaptureEvent);
                strip.Release(strip.XOf(5));

                Assert.Empty(picked);
                Assert.Empty(heard);
            });
        }

        [Fact]
        public void The_pointer_shows_whether_a_press_would_drag_or_pick_a_moment()
        {
            Ui.Run(() =>
            {
                var (strip, _, _) = StripWithPartPicked();

                strip.MoveTo(strip.XOf(5));
                Assert.Equal(Cursors.Hand, strip.Cursor);

                strip.MoveTo(strip.XOf(2) + 3);
                Assert.Equal(Cursors.SizeWE, strip.Cursor);

                strip.MoveTo(strip.XOf(5));
                strip.MoveTo(strip.XOf(9));
                Assert.Equal(Cursors.SizeWE, strip.Cursor);

                // With a click held down it stays as it was, since nothing will be dragged wherever it goes.
                strip.MoveTo(strip.XOf(5));
                strip.Press(strip.XOf(5));
                strip.MoveTo(strip.XOf(2) + 3);
                Assert.Equal(Cursors.Hand, strip.Cursor);
                strip.Release(strip.XOf(2) + 3);
            });
        }

        private static byte[] Drawn(TrimStrip strip)
        {
            // What a window does before it next draws: anything that asked to be redrawn, is.
            strip.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Width, 52, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(strip);
            var pixels = new byte[(int)Width * 52 * 4];
            bitmap.CopyPixels(pixels, (int)Width * 4, 0);
            return pixels;
        }

        [Fact]
        public void A_line_marks_where_playing_has_got_to()
        {
            Ui.Run(() =>
            {
                var (strip, _, _) = StripWithPartPicked();
                int dot = (26 * (int)Width + (int)strip.XOf(5)) * 4;   // half way down, at five seconds
                int elsewhere = (26 * (int)Width + (int)strip.XOf(6)) * 4;

                byte[] without = Drawn(strip);
                strip.Position = 5;
                byte[] with = Drawn(strip);
                strip.Position = null;
                byte[] after = Drawn(strip);

                Assert.True(with[dot] > 240 && with[dot + 1] > 240 && with[dot + 2] > 240, "There should be a white line at five seconds");
                Assert.False(without[dot] > 240 && without[dot + 1] > 240, "And none before playing");
                Assert.Equal(without[elsewhere], with[elsewhere]);
                Assert.Equal(without, after);
            });
        }

        [Fact]
        public void Pictures_fill_the_strip_one_cell_at_a_time()
        {
            Ui.Run(() =>
            {
                var strip = NewStrip(new TrimSelection(10, 30));
                var picture = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgr24, null);
                Assert.Equal(0, strip.PictureCount);

                strip.SetPicture(0, picture);
                strip.SetPicture(TrimStrip.Cells - 1, picture);
                strip.SetPicture(TrimStrip.Cells + 5, picture);   // off the end: ignored

                Assert.Equal(2, strip.PictureCount);
            });
        }

        [Fact]
        public void Draws_itself_with_or_without_pictures_or_a_theme()
        {
            Ui.Run(() =>
            {
                var selection = new TrimSelection(10, 30);
                selection.Move(TrimHandle.Start, 2);
                selection.Move(TrimHandle.End, 8);
                var strip = NewStrip(selection);
                strip.SetPicture(3, new WriteableBitmap(64, 36, 96, 96, PixelFormats.Bgr24, null));

                var bitmap = new RenderTargetBitmap((int)Width, 52, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(strip);

                var pixels = new byte[(int)Width * 52 * 4];
                bitmap.CopyPixels(pixels, (int)Width * 4, 0);
                Assert.Contains(pixels, value => value != 0);
            });
        }
    }
}
