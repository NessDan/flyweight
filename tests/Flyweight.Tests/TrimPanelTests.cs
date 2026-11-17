using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>Drives the real trim controls without showing them, against a real clip.</summary>
    [Collection(WindowTests.Name)]
    public class TrimPanelTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        private TrimPanel NewPanel()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");
            return new TrimPanel(clip, TestVideos.Probe(clip), TestVideos.Tools);
        }

        private static TextBox Box(TrimPanel panel, string name) => Ui.Named<TextBox>(panel, name);

        private static string Keeps(TrimPanel panel) => Ui.Named<TextBlock>(panel, "KeepsText").Text;

        /// <summary>Types a time and moves on, which is when the panel takes it in.</summary>
        private static void Type(TextBox box, string text)
        {
            box.Text = text;
            box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        }

        [Fact]
        public void Opens_keeping_the_whole_video()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Assert.Equal("clip.mp4", panel.FileName);
                Assert.False(panel.Answered.IsCompleted);
                Assert.Equal("0:00.000", Box(panel, "StartBox").Text);
                Assert.Equal(Format.Timecode(panel.Selection.Duration), Box(panel, "EndBox").Text);
                Assert.Contains("whole video", Keeps(panel));
            });
        }

        [Fact]
        public void Typing_times_picks_the_part_to_keep()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();

                Type(Box(panel, "StartBox"), "3.5");
                Type(Box(panel, "EndBox"), "0:05.5");

                Assert.Equal(3.5, panel.Selection.Start, 3);
                Assert.Equal(5.5, panel.Selection.End, 3);
                Assert.Equal("0:03.500", Box(panel, "StartBox").Text);
                Assert.Equal("0:05.500", Box(panel, "EndBox").Text);
                Assert.Equal("Keeps 0:02.0 of " + Format.Timecode(panel.Selection.Duration, 1), Keeps(panel));
            });
        }

        [Fact]
        public void Nonsense_typed_as_a_time_is_put_back()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Type(Box(panel, "StartBox"), "soon");
                Assert.Equal("0:00.000", Box(panel, "StartBox").Text);
                Assert.Equal(0, panel.Selection.Start, 3);
            });
        }

        [Fact]
        public void A_start_typed_past_the_end_stops_just_short_of_it()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Type(Box(panel, "EndBox"), "2");
                Type(Box(panel, "StartBox"), "9");
                Assert.Equal("0:01.900", Box(panel, "StartBox").Text);
            });
        }

        [Fact]
        public void Confirming_hands_back_the_part_to_whoever_is_waiting_for_it()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Type(Box(panel, "StartBox"), "3.5");
                Type(Box(panel, "EndBox"), "5.5");
                Assert.False(panel.Answered.IsCompleted);

                Ui.Click(Ui.Named<Button>(panel, "ConfirmButton"));

                Assert.True(panel.Confirmed);
                Assert.Equal(3.5, panel.Range.StartSeconds, 3);
                Assert.Equal(5.5, panel.Range.EndSeconds, 3);
                Assert.True(panel.Answered.IsCompleted);
            });
        }

        [Fact]
        public void Confirming_takes_in_a_time_still_being_typed()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Box(panel, "StartBox").Text = "4";

                Ui.Click(Ui.Named<Button>(panel, "ConfirmButton"));

                Assert.Equal(4, panel.Range.StartSeconds, 3);
            });
        }

        private static MouseButtonEventArgs PressOfTheMouse() =>
            new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };

        [Fact]
        public void A_time_still_being_typed_is_taken_in_when_a_handle_is_pressed()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Box(panel, "StartBox").Text = "4";

                // Pressing on the strip leaves the keyboard in the box, so nothing else would take the time in,
                // and the handle's first move would write the old time back over it.
                Ui.Named<TrimStrip>(panel, "Strip").RaiseEvent(PressOfTheMouse());

                Assert.Equal(4, panel.Selection.Start, 3);
                Assert.Equal("0:04.000", Box(panel, "StartBox").Text);
            });
        }

        [Fact]
        public void A_click_in_a_time_box_leaves_what_is_being_typed_there_alone()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var start = Box(panel, "StartBox");
                start.Text = "4";

                start.RaiseEvent(PressOfTheMouse());

                Assert.Equal("4", start.Text);
                Assert.Equal(0, panel.Selection.Start, 3);
            });
        }

        [Fact]
        public void Says_what_it_is_to_a_screen_reader()
        {
            Ui.Run(() => Assert.Equal("Trim or crop clip.mp4", AutomationProperties.GetName(NewPanel())));
        }

        [Fact]
        public void Getting_the_keyboard_does_not_scroll_the_page_but_a_control_inside_that_is_out_of_sight_still_does()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var column = new StackPanel();
                column.Children.Add(new Border { Height = 400 });
                column.Children.Add(panel);
                column.Children.Add(new Border { Height = 400 });
                var page = new ScrollViewer { Content = column };
                page.Measure(new Size(560, 300));
                page.Arrange(new Rect(0, 0, 560, 300));
                page.UpdateLayout();

                // Anything that gets the keyboard asks to be scrolled into view. For the controls as a whole,
                // on a page scrolled to their buttons, that would move the button out from under the click.
                page.ScrollToVerticalOffset(700);
                page.UpdateLayout();
                double scrolledTo = page.VerticalOffset;
                panel.BringIntoView();
                page.UpdateLayout();
                Assert.True(scrolledTo > 600, "The page should have scrolled: " + scrolledTo);
                Assert.Equal(scrolledTo, page.VerticalOffset);

                page.ScrollToVerticalOffset(0);
                page.UpdateLayout();
                Ui.Named<Button>(panel, "ConfirmButton").BringIntoView();
                page.UpdateLayout();
                Assert.True(page.VerticalOffset > 0, "A button out of sight should still be scrolled to");
            });
        }

        [Fact]
        public void Confirming_without_moving_anything_keeps_the_whole_video()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Ui.Click(Ui.Named<Button>(panel, "ConfirmButton"));
                Assert.True(panel.Confirmed);
                Assert.Null(panel.Range);
            });
        }

        [Fact]
        public void Trimming_only_the_start_keeps_the_sound_that_outlasts_the_picture()
        {
            Ui.Run(() =>
            {
                // Two seconds of picture with four of sound.
                string clip = _videos.Make("longer-sound.mkv",
                    "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=2",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4",
                    "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac");
                var media = TestVideos.Probe(clip);
                var panel = new TrimPanel(clip, media, TestVideos.Tools);
                Assert.InRange(panel.Selection.Duration, 1.9, 2.1);

                Type(Box(panel, "StartBox"), "1");
                Ui.Click(Ui.Named<Button>(panel, "ConfirmButton"));

                Assert.Equal(1, panel.Range.StartSeconds, 3);
                Assert.Equal(media.DurationSeconds, panel.Range.EndSeconds, 3);
            });
        }

        [Fact]
        public void Cancelling_confirms_nothing()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Type(Box(panel, "StartBox"), "3.5");
                Ui.Click(Ui.Named<Button>(panel, "CancelButton"));
                Assert.False(panel.Confirmed);
                Assert.Null(panel.Range);
                Assert.True(panel.Answered.IsCompleted);
            });
        }

        [Fact]
        public void Closing_it_from_outside_counts_as_an_answer_of_no()
        {
            Ui.Run(() =>
            {
                // As when the whole window is closed with a trim still open in it.
                var panel = NewPanel();
                Type(Box(panel, "StartBox"), "3.5");

                panel.Close();

                Assert.True(panel.Answered.IsCompleted);
                Assert.False(panel.Confirmed);
                Assert.Null(panel.Range);
            });
        }

        [Fact]
        public void Offers_to_skip_them_all_only_when_other_videos_are_waiting()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var row = Ui.Named<FrameworkElement>(panel, "WaitingRow");
                var cancel = Ui.Named<Button>(panel, "CancelButton");
                Assert.Equal(Visibility.Collapsed, row.Visibility);
                Assert.Equal("Cancel", cancel.Content);

                panel.ShowWaiting(2);
                Assert.Equal(Visibility.Visible, row.Visibility);
                Assert.Contains("2 more", Ui.Named<TextBlock>(panel, "WaitingText").Text);
                Assert.Equal("Skip this one", cancel.Content);

                panel.ShowWaiting(0);
                Assert.Equal(Visibility.Collapsed, row.Visibility);
                Assert.Equal("Cancel", cancel.Content);
            });
        }

        [Fact]
        public void Skipping_them_all_confirms_nothing_and_says_the_rest_should_go_too()
        {
            Ui.Run(() =>
            {
                var skipped = NewPanel();
                skipped.ShowWaiting(1);
                Ui.Click(Ui.Named<Button>(skipped, "SkipAllButton"));
                Assert.False(skipped.Confirmed);
                Assert.True(skipped.SkipRest);

                var cancelled = NewPanel();
                cancelled.ShowWaiting(1);
                Ui.Click(Ui.Named<Button>(cancelled, "CancelButton"));
                Assert.False(cancelled.SkipRest);
            });
        }
    }
}
