using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>The trim controls' stepping and pictures, without showing them.</summary>
    [Collection(WindowTests.Name)]
    public class TrimPanelPreviewTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        private TrimPanel NewPanel()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");
            return new TrimPanel(clip, TestVideos.Probe(clip), TestVideos.Tools);
        }

        /// <summary>Lays the controls out in a space this wide, as the window they sit in would.</summary>
        private static void LayOut(TrimPanel panel, double width)
        {
            panel.Measure(new Size(width, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
            panel.UpdateLayout();
        }

        [Fact]
        public void Bytes_that_are_not_a_whole_picture_are_treated_as_no_picture()
        {
            // As when ffmpeg stops part way through writing one: the start of a picture file, then nothing.
            var cutShort = new byte[] { (byte)'B', (byte)'M', 0x36, 0x10, 0x0E, 0, 0, 0, 0, 0, 0x36, 0, 0, 0, 0x28, 0, 0, 0 };

            Assert.Null(TrimPanel.Decode(cutShort));
            Assert.Null(TrimPanel.Decode(new byte[] { 1, 2, 3 }));
            Assert.Null(TrimPanel.Decode(new byte[0]));
            Assert.Null(TrimPanel.Decode(null));
        }

        [Fact]
        public void Stepping_moves_a_handle_by_a_frame_or_by_a_second()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();   // 30 frames a second

                panel.Step(TrimHandle.Start, 1, bySecond: false);
                Assert.Equal(0.033, panel.Selection.Start, 3);

                panel.Step(TrimHandle.Start, 1, bySecond: true);
                Assert.Equal(1.033, panel.Selection.Start, 3);
                Assert.Equal("0:01.033", Ui.Named<TextBox>(panel, "StartBox").Text);

                panel.Step(TrimHandle.End, -1, bySecond: true);
                Assert.Equal(panel.Selection.Duration - 1, panel.Selection.End, 3);
            });
        }

        [Fact]
        public void Shows_the_first_frame_kept_then_follows_whichever_handle_moved()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var image = Ui.Named<Image>(panel, "PreviewImage");
                var label = Ui.Named<TextBlock>(panel, "PreviewLabel");

                panel.Start();
                Ui.Pump(() => image.Source != null);
                Assert.Equal("Start · 0:00.000", label.Text);
                Assert.Equal(Visibility.Collapsed, Ui.Named<TextBlock>(panel, "PreviewNote").Visibility);
                var first = image.Source;

                var end = Ui.Named<TextBox>(panel, "EndBox");
                end.Text = "5";
                end.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

                Assert.Equal("End · 0:05.000", label.Text);
                Ui.Pump(() => image.Source != first);
                panel.Close();
            });
        }

        [Fact]
        public void The_strip_fills_with_pictures_from_across_the_video()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");

                panel.Start();
                Ui.Pump(() => strip.PictureCount == TrimStrip.Cells);
                panel.Close();
            });
        }

        [Fact]
        public void Dragging_on_the_strip_updates_the_times()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                strip.Measure(new Size(560, 52));
                strip.Arrange(new Rect(0, 0, 560, 52));

                strip.DragTo(TrimHandle.Start, strip.XOf(2), settled: true);

                Assert.Equal(Format.Timecode(panel.Selection.Start), Ui.Named<TextBox>(panel, "StartBox").Text);
                Assert.Equal(2, panel.Selection.Start, 1);
                Assert.StartsWith("Start", Ui.Named<TextBlock>(panel, "PreviewLabel").Text);
                panel.Close();
            });
        }

        [Fact]
        public void A_tall_video_gets_a_taller_preview_than_a_wide_one()
        {
            Ui.Run(() =>
            {
                string tall = _videos.Make("tall.mp4", "-f", "lavfi", "-i", "testsrc2=s=360x640:r=30:d=1", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p");
                var wide = NewPanel();
                var phone = new TrimPanel(tall, TestVideos.Probe(tall), TestVideos.Tools) { Screen = new Rect(0, 0, 1536, 1040) };
                LayOut(wide, 560);
                LayOut(phone, 560);

                double wideBox = Ui.Named<Border>(wide, "PreviewBox").Height;
                double tallBox = Ui.Named<Border>(phone, "PreviewBox").Height;

                Assert.Equal(315, wideBox);
                Assert.True(tallBox >= wideBox + 60, "A phone video in a box made for wide ones is a sliver: " + tallBox);
            });
        }

        [Theory]
        [InlineData(1040, 440)]   // 1080p: as tall as it gets
        [InlineData(824, 394)]    // 1080p with text at 125%: shorter, so the levels under it stay on screen
        [InlineData(680, 315)]    // a small laptop: no shorter than a wide video's box
        public void A_tall_videos_preview_is_only_as_tall_as_the_screen_has_room_for(double screenHeight, double expected)
        {
            Ui.Run(() =>
            {
                string tall = _videos.Make("tall.mp4", "-f", "lavfi", "-i", "testsrc2=s=360x640:r=30:d=1", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p");
                var phone = new TrimPanel(tall, TestVideos.Probe(tall), TestVideos.Tools) { Screen = new Rect(0, 0, 1536, screenHeight) };
                LayOut(phone, 560);

                Assert.Equal(expected, Ui.Named<Border>(phone, "PreviewBox").Height);
            });
        }

        [Fact]
        public void A_wide_videos_preview_does_not_change_with_the_height_of_the_screen()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                panel.Screen = new Rect(0, 0, 1536, 680);
                LayOut(panel, 560);

                Assert.Equal(315, Ui.Named<Border>(panel, "PreviewBox").Height);
            });
        }

        [Fact]
        public void With_less_room_across_the_preview_gets_shorter_rather_than_growing_bars()
        {
            Ui.Run(() =>
            {
                // Beside Advanced options the controls get a narrower column than they'd have alone.
                var panel = NewPanel();
                var box = Ui.Named<Border>(panel, "PreviewBox");

                LayOut(panel, 400);
                Assert.Equal(225, box.Height);

                // With more room it stops at the size it has always been.
                LayOut(panel, 900);
                Assert.Equal(315, box.Height);
            });
        }

        [Fact]
        public void On_video_with_an_uneven_frame_rate_the_end_picture_is_still_the_last_frame_kept()
        {
            Ui.Run(() =>
            {
                // Two frames a second for eight seconds, then thirty a second: about seven on average.
                string clip = _videos.Make("uneven.mp4", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=10",
                    "-vf", "select='if(lt(t,8),not(mod(n,15)),1)'", "-fps_mode", "vfr",
                    "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p");
                var panel = new TrimPanel(clip, TestVideos.Probe(clip), TestVideos.Tools);
                panel.Start();

                var end = Ui.Named<TextBox>(panel, "EndBox");
                end.Text = "9.5";
                end.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

                // A cut ending at 9.5 s keeps up to the frame at 9.467 s. Going back by an average frame would show 9.4 s.
                Ui.Pump(() => panel.ShownSeconds.HasValue && Math.Abs(panel.ShownSeconds.Value - 9.466) < 0.002, 20000);
                panel.Close();
            });
        }
    }
}
