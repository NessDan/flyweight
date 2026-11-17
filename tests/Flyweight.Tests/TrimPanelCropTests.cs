using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>Cropping among the trim controls: the box over the picture, and what the controls make of it.</summary>
    [Collection(WindowTests.Name)]
    public class TrimPanelCropTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        /// <summary>The controls for a clip, laid out 560 across as in the window.</summary>
        private TrimPanel For(string clip)
        {
            var panel = new TrimPanel(clip, TestVideos.Probe(clip), TestVideos.Tools);
            panel.Measure(new Size(560, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 560, panel.DesiredSize.Height));
            panel.UpdateLayout();
            return panel;
        }

        private string Clip(string size = "640x360") => _videos.Make("clip.mp4", "-f", "lavfi", "-i", $"testsrc2=s={size}:r=30:d=2",
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", "8", "-pix_fmt", "yuv420p");

        private static CropBox Cropper(TrimPanel panel) => Ui.Named<CropBox>(panel, "Cropper");

        private static string ConfirmSays(TrimPanel panel) => (string)Ui.Named<Button>(panel, "ConfirmButton").Content;

        private static UIElement SizeText(TrimPanel panel) => Ui.Named<TextBlock>(panel, "CropSizeText");

        private static Button Reset(TrimPanel panel) => Ui.Named<Button>(panel, "ResetCropButton");

        private static void Drag(CropBox box, Point from, Point to)
        {
            box.Press(from);
            box.MoveTo(to);
            box.Release(to);
        }

        private static void Type(TrimPanel panel, string box, string text)
        {
            var field = Ui.Named<TextBox>(panel, box);
            field.Text = text;
            field.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        }

        [Fact]
        public void The_box_sits_over_the_picture_knowing_its_shape_and_size()
        {
            Ui.Run(() =>
            {
                // Stored 480 by 360 and shown 640 by 360.
                string clip = _videos.Make("stretched.mp4", "-f", "lavfi", "-i", "testsrc2=s=480x360:r=30:d=1",
                    "-vf", "setsar=4/3", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p");
                var panel = For(clip);
                var box = Cropper(panel);

                Assert.Equal(16.0 / 9, box.Aspect, 3);
                Assert.Equal((480, 360), (box.VideoWidth, box.VideoHeight));
                Assert.True(Ui.Named<Border>(panel, "PreviewBox").IsAncestorOf(box));
            });
        }

        [Fact]
        public void Nothing_is_cropped_to_begin_with()
        {
            Ui.Run(() =>
            {
                var panel = For(Clip());

                Assert.True(Cropper(panel).Area.IsWhole);
                Assert.Equal(Visibility.Collapsed, SizeText(panel).Visibility);
                Assert.Equal(Visibility.Collapsed, Reset(panel).Visibility);
                Assert.Equal("Compress", ConfirmSays(panel));
            });
        }

        [Fact]
        public void A_crop_says_what_size_it_keeps_and_can_be_put_back()
        {
            Ui.Run(() =>
            {
                var panel = For(Clip());
                var box = Cropper(panel);

                // The picture fills the box, 560 by 315 for 640 by 360: the bottom right quarter.
                Drag(box, new Point(0, 0), new Point(280, 157.5));

                Assert.Equal(Visibility.Visible, SizeText(panel).Visibility);
                Assert.Equal("Cropped to 320 × 180", Ui.Named<TextBlock>(panel, "CropSizeText").Text);
                Assert.Equal(Visibility.Visible, Reset(panel).Visibility);
                Assert.Equal("Crop and compress", ConfirmSays(panel));

                Ui.Click(Reset(panel));

                Assert.True(box.Area.IsWhole);
                Assert.Equal(Visibility.Collapsed, SizeText(panel).Visibility);
                Assert.Equal(Visibility.Collapsed, Reset(panel).Visibility);
                Assert.Equal("Compress", ConfirmSays(panel));
            });
        }

        [Fact]
        public void Beside_a_crop_the_length_kept_is_not_called_the_whole_video()
        {
            Ui.Run(() =>
            {
                var panel = For(Clip());
                var keeps = Ui.Named<TextBlock>(panel, "KeepsText");
                Assert.Equal("Keeps the whole video (0:02.0)", keeps.Text);

                Drag(Cropper(panel), new Point(0, 0), new Point(140, 80));
                Assert.Equal("Keeps the whole length (0:02.0)", keeps.Text);

                Ui.Click(Reset(panel));
                Assert.Equal("Keeps the whole video (0:02.0)", keeps.Text);
            });
        }

        [Fact]
        public void The_button_says_what_it_will_do()
        {
            Ui.Run(() =>
            {
                var panel = For(Clip());
                var box = Cropper(panel);

                Type(panel, "StartBox", "0.5");
                Assert.Equal("Trim and compress", ConfirmSays(panel));

                Drag(box, new Point(0, 0), new Point(140, 80));
                Assert.Equal("Trim, crop and compress", ConfirmSays(panel));

                Type(panel, "StartBox", "0");
                Assert.Equal("Crop and compress", ConfirmSays(panel));
            });
        }

        [Fact]
        public void The_button_counts_a_time_still_being_typed()
        {
            Ui.Run(() =>
            {
                // Pressing it takes the typed time in, so that's what it's going to do.
                var panel = For(Clip());
                var start = Ui.Named<TextBox>(panel, "StartBox");

                start.Text = "0.5";
                Assert.Equal("Trim and compress", ConfirmSays(panel));

                start.Text = "not a time";
                Assert.Equal("Compress", ConfirmSays(panel));

                start.Text = "0:00.000";
                Assert.Equal("Compress", ConfirmSays(panel));
            });
        }

        [Fact]
        public void Reset_crop_never_takes_the_keyboard_away_from_the_trim_controls()
        {
            Ui.Run(() =>
            {
                // Hidden again by its own click, a link holding the keyboard would leave Enter and Escape with nowhere to go.
                Assert.False(Reset(For(Clip())).Focusable);
            });
        }

        [Fact]
        public void Choosing_hands_back_the_crop_and_cancelling_hands_back_nothing()
        {
            Ui.Run(() =>
            {
                var chosen = For(Clip());
                Drag(Cropper(chosen), new Point(0, 0), new Point(140, 80));
                Ui.Click(Ui.Named<Button>(chosen, "ConfirmButton"));

                Assert.True(chosen.Confirmed);
                Assert.Equal(0.25, chosen.Crop.Left, 3);
                Assert.Null(chosen.Range);

                var cancelled = For(Clip());
                Drag(Cropper(cancelled), new Point(0, 0), new Point(140, 80));
                Ui.Click(Ui.Named<Button>(cancelled, "CancelButton"));
                Assert.False(cancelled.Confirmed);
            });
        }

        [Fact]
        public void A_box_dragged_back_to_the_whole_picture_is_no_crop()
        {
            Ui.Run(() =>
            {
                var panel = For(Clip());
                var box = Cropper(panel);
                Drag(box, new Point(0, 0), new Point(140, 80));
                Drag(box, new Point(140, 80), new Point(3, 2));

                Ui.Click(Ui.Named<Button>(panel, "ConfirmButton"));

                Assert.Null(panel.Crop);
            });
        }

        private static void AssertPoint(Point expected, Point actual)
        {
            Assert.True((expected - actual).Length < 0.01, $"expected {expected}, was {actual}");
        }

        [Fact]
        public void Holding_a_corner_still_zooms_in_the_picture_and_the_box_together_but_not_the_buttons()
        {
            Ui.Run(() =>
            {
                var panel = For(Clip());
                var box = Cropper(panel);
                var preview = Ui.Named<Border>(panel, "PreviewBox");
                var image = Ui.Named<Image>(panel, "PreviewImage");
                var play = Ui.Named<Button>(panel, "PlayButton");
                var playWas = play.TranslatePoint(new Point(0, 0), preview);
                Drag(box, new Point(0, 0), new Point(140, 80));

                box.Press(new Point(140, 80));
                box.Rest();
                panel.UpdateLayout();

                // The corner stays where it was on screen, and what's around it is four times the size.
                AssertPoint(new Point(140, 80), box.TranslatePoint(new Point(140, 80), preview));
                AssertPoint(new Point(180, 120), box.TranslatePoint(new Point(150, 90), preview));
                // The picture under it too, around the same point.
                var cornerOnPicture = box.TranslatePoint(new Point(140, 80), image);
                AssertPoint(new Point(180, 120), image.TranslatePoint(cornerOnPicture + new Vector(10, 10), preview));
                // So a hand's movement moves the corner a quarter as far.
                AssertPoint(new Point(150, 90), preview.TranslatePoint(new Point(180, 120), box));
                // And Play stays as it was.
                AssertPoint(playWas, play.TranslatePoint(new Point(0, 0), preview));

                box.Release(new Point(140, 80));
                panel.UpdateLayout();
                AssertPoint(new Point(150, 90), box.TranslatePoint(new Point(150, 90), preview));
            });
        }

        [Fact]
        public void Zoomed_in_the_picture_is_fetched_again_at_its_full_size_so_it_stays_sharp()
        {
            Ui.Run(() =>
            {
                var panel = For(Clip("1280x720"));
                var image = Ui.Named<Image>(panel, "PreviewImage");
                panel.Start();
                Ui.Pump(() => image.Source is BitmapSource, 20000);
                Assert.Equal(540, ((BitmapSource)image.Source).PixelHeight);
                var box = Cropper(panel);

                box.Press(box.Picture.TopLeft);
                box.Rest();

                Ui.Pump(() => ((BitmapSource)image.Source).PixelHeight == 720, 20000);
                box.Release(box.Picture.TopLeft);
                panel.Close();
            });
        }

        [Fact]
        public void A_handle_moved_while_the_sharp_picture_is_on_its_way_is_what_shows()
        {
            Ui.Run(() =>
            {
                var panel = For(Clip("1280x720"));
                var image = Ui.Named<Image>(panel, "PreviewImage");
                panel.Start();
                Ui.Pump(() => image.Source is BitmapSource, 20000);
                var box = Cropper(panel);
                box.Press(box.Picture.TopLeft);
                box.Rest();

                // As Up does in a time box, with the corner still held.
                panel.Step(TrimHandle.Start, 1, bySecond: true);

                Ui.Pump(() => panel.ShownSeconds == 1, 20000);
                var shown = image.Source;
                var until = DateTime.UtcNow.AddMilliseconds(1000);
                Ui.Pump(() => DateTime.UtcNow > until);
                Assert.Same(shown, image.Source);
                Assert.Equal(1, panel.ShownSeconds);
                // Still zoomed in, so that picture is fetched sharp as well.
                Assert.Equal(720, ((BitmapSource)shown).PixelHeight);
                box.Release(box.Picture.TopLeft);
                panel.Close();
            });
        }

        [Theory]
        [InlineData("1080x1920")]
        [InlineData("1080x2340")]   // a phone of today, 9 by 19.5
        public void On_a_phone_video_the_buttons_on_the_picture_stay_out_of_reach_of_its_bottom_corners(string size)
        {
            Ui.Run(() =>
            {
                // On a 1080p screen at 125%, as here, the box for a tall video is 394 points high.
                string clip = _videos.Make("phone.mp4", "-f", "lavfi", "-i", $"color=c=gray:s={size}:r=30:d=1",
                    "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p");
                var panel = new TrimPanel(clip, TestVideos.Probe(clip), TestVideos.Tools) { Screen = new Rect(0, 0, 1536, 824) };
                panel.Measure(new Size(560, double.PositiveInfinity));
                panel.Arrange(new Rect(0, 0, 560, panel.DesiredSize.Height));
                panel.UpdateLayout();
                var box = Cropper(panel);
                var picture = box.Picture;

                // A press within 14 points of a corner takes hold of the corner instead.
                foreach (var name in new[] { "StartHereButton", "PlayButton", "EndHereButton" })
                {
                    var button = Ui.Named<Button>(panel, name);
                    var place = new Rect(button.TranslatePoint(new Point(0, 0), box), new Size(button.ActualWidth, button.ActualHeight));
                    foreach (var corner in new[] { picture.BottomLeft, picture.BottomRight })
                    {
                        Assert.True(Distance(place, corner) > 14, $"{name}, at {place}, is in reach of the corner at {corner}");
                    }
                }
            });
        }

        private static double Distance(Rect rect, Point point)
        {
            double across = Math.Max(Math.Max(rect.Left - point.X, 0), point.X - rect.Right);
            double down = Math.Max(Math.Max(rect.Top - point.Y, 0), point.Y - rect.Bottom);
            return Math.Sqrt(across * across + down * down);
        }

        [OnHiddenDesktopFact]
        public void The_play_button_is_over_the_box_and_the_time_beside_it_lets_presses_through()
        {
            Ui.Run(() =>
            {
                // Where a press goes is only worked out for what's really on a screen.
                var panel = new TrimPanel(Clip(), TestVideos.Probe(_videos.PathOf("clip.mp4")), TestVideos.Tools);
                var window = new Window
                {
                    Content = panel, Width = 592, SizeToContent = SizeToContent.Height, ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = 200, Top = 100,
                };
                window.Show();
                try
                {
                Ui.Pump(() => panel.ActualWidth > 0);
                var preview = Ui.Named<Border>(panel, "PreviewBox");
                var play = Ui.Named<Button>(panel, "PlayButton");
                var label = Ui.Named<TextBlock>(panel, "PreviewLabel");

                // Where a press there would go, worked out as the mouse works it out.
                DependencyObject HitAt(FrameworkElement element)
                {
                    var middle = element.TranslatePoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2), preview);
                    return preview.InputHitTest(middle) as DependencyObject;
                }

                Assert.True(IsWithin(HitAt(play), play), "A press on Play should be Play's");
                Assert.IsType<CropBox>(HitAt(label));
                // In the middle at the bottom, out of the corners' way, with Start here and End here either side.
                foreach (var button in new[] { play, Ui.Named<Button>(panel, "StartHereButton"), Ui.Named<Button>(panel, "EndHereButton") })
                {
                    Assert.True(IsWithin(HitAt(button), button), "A press on " + button.Name + " should be its own");
                    var at = button.TranslatePoint(new Point(0, 0), preview);
                    Assert.True(at.X > 100 && at.X + button.ActualWidth < preview.ActualWidth - 100, button.Name + " is at " + at);
                }
                }
                finally
                {
                    panel.Close();
                    window.Close();
                }
            });
        }

        private static bool IsWithin(DependencyObject hit, DependencyObject ancestor)
        {
            for (var at = hit; at != null; at = VisualTreeHelper.GetParent(at))
            {
                if (ReferenceEquals(at, ancestor)) return true;
            }
            return false;
        }

        [Theory]
        [InlineData("640x360", 70, 63, 280, 252, 80, 72)]       // the picture fills the box
        [InlineData("480x360", 175, 63, 385, 252, 120, 72)]     // a narrower picture, with bars either side
        public void What_is_kept_is_the_picture_that_was_under_the_box(string size, double fromX, double fromY, double toX, double toY, int x, int y)
        {
            Ui.Run(() =>
            {
                string clip = Clip(size);
                var panel = For(clip);
                var box = Cropper(panel);
                var picture = box.Picture;
                Drag(box, picture.TopLeft, new Point(fromX, fromY));
                Drag(box, picture.BottomRight, new Point(toX, toY));
                Ui.Click(Ui.Named<Button>(panel, "ConfirmButton"));

                var result = new Compressor(TestVideos.Tools).Run(clip, new LevelSettings
                {
                    Codec = VideoCodec.Hevc, Encoder = EncoderChoice.Cpu, Quality = 18, AudioKbps = 96, ExtraArgs = "",
                }, null, null, CancellationToken.None, panel.Range, panel.Crop);

                Assert.Equal(JobOutcome.Done, result.Outcome);
                var whole = TestVideos.Gray(clip);
                var part = TestVideos.Gray(result.OutputPath);
                double here = TestVideos.Difference(whole, x, y, part);
                Assert.True(here < 6 && here * 2 < TestVideos.Difference(whole, x + 2, y, part) && here * 2 < TestVideos.Difference(whole, x, y + 2, part),
                    $"The kept picture, {part.Width} by {part.Height}, isn't the part at ({x}, {y}): {here:0.0} off");
            });
        }
    }
}
