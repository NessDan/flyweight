using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>The keys that work among the trim controls.</summary>
    [Collection(WindowTests.Name)]
    public class TrimPanelKeyTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        private TrimPanel NewPanel()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");   // 10 seconds at 30 frames a second
            var panel = new TrimPanel(clip, TestVideos.Probe(clip), TestVideos.Tools);
            // The clip has sound, and a key might set it playing.
            panel.Player.NewSound = () => new PretendSound();
            return panel;
        }

        /// <summary>Presses a key in a box, and says whether the box dealt with it.</summary>
        private static bool Press(TextBox box, Key key, bool shift = false, bool held = false) => Raise(box, key, Keyboard.PreviewKeyDownEvent, shift, held);

        /// <summary>Presses a key on something that has no use for it, so it's left for whatever is around it.</summary>
        private static bool PressOn(UIElement element, Key key, bool held = false) => Raise(element, key, Keyboard.KeyDownEvent, shift: false, held: held);

        private static bool Raise(UIElement on, Key key, RoutedEvent how, bool shift, bool held = false)
        {
            // A key press has to name the window it came through. This one is message-only, so it can never show.
            var hidden = new HwndSourceParameters("keys") { WindowStyle = 0, ParentWindow = new IntPtr(-3) };
            using (var source = new HwndSource(hidden))
            {
                var press = new KeyEventArgs(new PretendKeyboard { ShiftDown = shift }, source, 0, key) { RoutedEvent = how };
                // While a key is held down, Windows sends the press again and again, marked as a repeat.
                if (held) typeof(KeyEventArgs).GetMethod("SetRepeat", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(press, new object[] { true });
                on.RaiseEvent(press);
                return press.Handled;
            }
        }

        /// <summary>
        /// A keyboard whose Shift is down only when a test says so. The real one reports whatever is
        /// being held on this PC at that moment, and someone may well be typing.
        /// </summary>
        private sealed class PretendKeyboard : KeyboardDevice
        {
            public bool ShiftDown;

            public PretendKeyboard() : base(InputManager.Current)
            {
            }

            protected override KeyStates GetKeyStatesFromSystem(Key key)
            {
                return ShiftDown && (key == Key.LeftShift || key == Key.RightShift) ? KeyStates.Down : KeyStates.None;
            }
        }

        [Fact]
        public void With_Shift_held_Up_and_Down_step_a_whole_second()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var start = Ui.Named<TextBox>(panel, "StartBox");

                Assert.True(Press(start, Key.Up, shift: true));
                Assert.True(Press(start, Key.Up, shift: true));
                Assert.Equal(2, panel.Selection.Start, 3);

                Assert.True(Press(start, Key.Down, shift: true));
                Assert.Equal(1, panel.Selection.Start, 3);

                // And without it, a frame again, whatever went before.
                Assert.True(Press(start, Key.Up));
                Assert.Equal(1.033, panel.Selection.Start, 3);
            });
        }

        [Fact]
        public void Enter_among_the_trim_controls_starts_the_trim()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Ui.Named<TextBox>(panel, "StartBox").Text = "3.5";

                bool handled = PressOn(Ui.Named<TrimStrip>(panel, "Strip"), Key.Enter);

                Assert.True(handled);
                Assert.True(panel.Confirmed);
                Assert.Equal(3.5, panel.Range.StartSeconds, 3);
            });
        }

        [Fact]
        public void Escape_among_the_trim_controls_calls_the_trim_off()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();

                bool handled = PressOn(Ui.Named<TrimStrip>(panel, "Strip"), Key.Escape);

                Assert.True(handled);
                Assert.False(panel.Confirmed);
                Assert.True(panel.Answered.IsCompleted);
            });
        }

        [Fact]
        public void Space_plays_and_pauses_from_anywhere_among_the_trim_controls()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");

                Assert.True(PressOn(strip, Key.Space));
                Assert.True(panel.Player.IsPlaying);

                // In a time box as well, where a space has no other use. A box would otherwise keep it as typing.
                var start = Ui.Named<TextBox>(panel, "StartBox");
                Assert.True(Press(start, Key.Space));
                Assert.False(panel.Player.IsPlaying);
                Assert.True(Press(start, Key.Space, held: true), "A held Space shouldn't be typed into the box either");
                Assert.False(panel.Player.IsPlaying);

                Assert.True(PressOn(strip, Key.Space));
                Assert.True(panel.Player.IsPlaying);
                Assert.False(panel.Answered.IsCompleted);
                panel.Close();
            });
        }

        [Fact]
        public void Space_held_down_does_not_keep_starting_and_stopping()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");

                PressOn(strip, Key.Space);
                PressOn(strip, Key.Space, held: true);
                PressOn(strip, Key.Space, held: true);
                PressOn(strip, Key.Space, held: true);

                Assert.True(panel.Player.IsPlaying);
                panel.Close();
            });
        }

        [Fact]
        public void A_key_still_held_down_from_the_last_video_does_not_answer_for_this_one()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");

                PressOn(strip, Key.Enter, held: true);
                PressOn(strip, Key.Escape, held: true);

                Assert.False(panel.Answered.IsCompleted);
            });
        }

        [Fact]
        public void Escape_in_a_time_box_first_puts_back_the_time_that_was_there()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var start = Ui.Named<TextBox>(panel, "StartBox");
                start.Text = "3.5";

                Assert.True(Press(start, Key.Escape));
                Assert.Equal("0:00.000", start.Text);
                Assert.Equal(0, panel.Selection.Start, 3);
                Assert.False(panel.Answered.IsCompleted, "Calling off a slip of typing shouldn't call off the trim");

                // With nothing typed to put back, Escape is left to mean the trim as a whole.
                Assert.False(Press(start, Key.Escape));
            });
        }

        [Fact]
        public void A_key_the_trim_has_no_use_for_is_left_alone()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Assert.False(PressOn(Ui.Named<TrimStrip>(panel, "Strip"), Key.A));
                Assert.False(panel.Answered.IsCompleted);
            });
        }

        [Fact]
        public void Enter_in_a_time_box_uses_the_time_typed_without_starting_anything()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var start = Ui.Named<TextBox>(panel, "StartBox");
                start.Text = "3.5";

                bool handled = Press(start, Key.Enter);

                Assert.True(handled, "Otherwise Enter would also press Trim and compress");
                Assert.Equal(3.5, panel.Selection.Start, 3);
                Assert.Equal("0:03.500", start.Text);
                Assert.False(panel.Confirmed);
            });
        }

        [Fact]
        public void Up_and_Down_in_a_time_box_step_one_frame_at_a_time()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var start = Ui.Named<TextBox>(panel, "StartBox");
                var end = Ui.Named<TextBox>(panel, "EndBox");

                Assert.True(Press(start, Key.Up));
                Assert.True(Press(start, Key.Up));
                Assert.Equal(0.066, panel.Selection.Start, 3);
                Assert.Equal("0:00.066", start.Text);

                Assert.True(Press(start, Key.Down));
                Assert.Equal(0.033, panel.Selection.Start, 3);

                Assert.True(Press(end, Key.Down));
                Assert.Equal(9.966, panel.Selection.End, 3);
                Assert.StartsWith("End", Ui.Named<TextBlock>(panel, "PreviewLabel").Text);
            });
        }

        [Fact]
        public void A_step_starts_from_whatever_time_was_just_typed()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var start = Ui.Named<TextBox>(panel, "StartBox");
                start.Text = "2";

                Press(start, Key.Up);

                Assert.Equal(2.033, panel.Selection.Start, 3);
            });
        }

        [Fact]
        public void I_and_O_start_and_end_the_part_at_the_line_from_anywhere_among_the_trim_controls()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var end = Ui.Named<TextBox>(panel, "EndBox");
                end.Text = "9";
                Press(end, Key.Enter);   // which puts the line three seconds before the end, at 6

                Assert.True(PressOn(Ui.Named<TrimStrip>(panel, "Strip"), Key.O));
                // The frame at 6 s is kept last, so the end is where the next one begins.
                Assert.Equal(6.033, panel.Selection.End, 3);

                // In a time box as well, since a time has no letters in it. The line is three seconds before the new end.
                Assert.True(Press(Ui.Named<TextBox>(panel, "StartBox"), Key.I));
                Assert.Equal(3.033, panel.Selection.Start, 3);
                Assert.False(panel.Answered.IsCompleted);
            });
        }

        [Fact]
        public void A_time_still_being_typed_counts_and_here_is_where_the_line_was_when_the_key_went_down()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Ui.Named<TextBox>(panel, "EndBox").Text = "9";   // typed, and I pressed straight after

                Assert.True(PressOn(Ui.Named<TrimStrip>(panel, "Strip"), Key.I));

                Assert.Equal(9, panel.Selection.End, 3);
                // The line was on the start then, though taking in the end moves it.
                Assert.Equal(0, panel.Selection.Start, 3);
            });
        }

        [Fact]
        public void I_or_O_held_down_does_nothing()
        {
            Ui.Run(() =>
            {
                // As when it's still held from the last video's answer, which is nothing to do with this one.
                var panel = NewPanel();
                PressOn(Ui.Named<TrimStrip>(panel, "Strip"), Key.O, held: true);
                Assert.True(Press(Ui.Named<TextBox>(panel, "StartBox"), Key.O, held: true), "Nor should it be typed into the box");
                Assert.True(panel.Selection.IsWholeVideo);
            });
        }

        [Fact]
        public void Other_keys_are_left_for_typing()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Assert.False(Press(Ui.Named<TextBox>(panel, "StartBox"), Key.D5));
                Assert.False(Press(Ui.Named<TextBox>(panel, "StartBox"), Key.Left));
                Assert.Equal(0, panel.Selection.Start, 3);
            });
        }
    }
}
