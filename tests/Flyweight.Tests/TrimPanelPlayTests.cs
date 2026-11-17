using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// Playing the video in place among the trim controls. ffmpeg really reads a clip whose every
    /// frame says which one it is, and the pictures really reach the picture box. Its sound, where
    /// it has any, goes to a stand-in, so nothing here can be heard.
    /// </summary>
    [Collection(WindowTests.Name)]
    public class TrimPanelPlayTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        // What the sound of the latest go at playing went to. Each go gets one of its own.
        private volatile PretendSound _sound;

        public void Dispose() => _videos.Dispose();

        /// <summary>The controls for a clip of three and a half seconds at 30 frames a second, laid out as in a window.</summary>
        private TrimPanel NewPanel(bool withSound = false)
        {
            string clip = _videos.MakeNumberedClip("clip.mp4", withSound: withSound);
            return For(clip, TestVideos.Probe(clip));
        }

        private TrimPanel For(string clip, MediaInfo media)
        {
            var panel = new TrimPanel(clip, media, TestVideos.Tools);
            panel.Player.NewSound = () => _sound = new PretendSound();
            panel.Measure(new Size(560, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 560, panel.DesiredSize.Height));
            panel.UpdateLayout();
            return panel;
        }

        private static Button Play(TrimPanel panel) => Ui.Named<Button>(panel, "PlayButton");

        private static string PlaySays(TrimPanel panel) => Ui.Named<TextBlock>(panel, "PlayText").Text;

        private static string Label(TrimPanel panel) => Ui.Named<TextBlock>(panel, "PreviewLabel").Text;

        private static Image Picture(TrimPanel panel) => Ui.Named<Image>(panel, "PreviewImage");

        private static bool IsMoving(TrimPanel panel) => Picture(panel).Source is WriteableBitmap;

        private static void Type(TrimPanel panel, string box, string text)
        {
            var field = Ui.Named<TextBox>(panel, box);
            field.Text = text;
            field.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        }

        /// <summary>Which frame of the clip is in the picture box.</summary>
        private static int NumberOn(TrimPanel panel)
        {
            var picture = (BitmapSource)Picture(panel).Source;
            var dots = new byte[picture.PixelWidth * picture.PixelHeight * 4];
            picture.CopyPixels(dots, picture.PixelWidth * 4, 0);
            return TestVideos.FrameNumber(dots, picture.PixelWidth, picture.PixelHeight);
        }

        /// <summary>Waits for the first picture of a go at playing, and says which frame it was, read as it went up.</summary>
        private static int FirstFrameShown(TrimPanel panel)
        {
            int? first = null;
            var source = DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image));
            EventHandler wentUp = (s, e) =>
            {
                if (first == null && IsMoving(panel)) first = NumberOn(panel);
            };
            source.AddValueChanged(Picture(panel), wentUp);
            try
            {
                Ui.Pump(() => first.HasValue, 20000);
            }
            finally
            {
                source.RemoveValueChanged(Picture(panel), wentUp);
            }
            return first.Value;
        }

        [Fact]
        public void Play_shows_the_video_moving_in_the_picture_box_from_the_start_handle()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Type(panel, "StartBox", "1");
                Assert.Equal("Play", PlaySays(panel));

                Ui.Click(Play(panel));

                Assert.Equal("Pause", PlaySays(panel));
                Assert.Equal(30, FirstFrameShown(panel));
                Assert.StartsWith("Playing · 0:01.", Label(panel));
                Assert.False(panel.Answered.IsCompleted, "Watching a part isn't choosing it");
                // Already the size it's shown at, so it isn't put through the slow resizing a still gets.
                Assert.Equal(BitmapScalingMode.Linear, RenderOptions.GetBitmapScalingMode(Picture(panel)));
                panel.Close();
            });
        }

        [Fact]
        public void A_narrower_video_is_played_in_its_own_shape_so_it_stays_under_the_crop_box_at_any_size()
        {
            Ui.Run(() =>
            {
                // Four by three, in a box made for sixteen by nine: 420 of the 560 across. Played the
                // size of the box, with bars, it would no longer sit under the crop box's corners once
                // the box changed shape, as it does when Advanced options open beside it.
                string clip = _videos.Make("square.mp4", "-f", "lavfi", "-i", "testsrc2=s=480x360:r=30:d=2",
                    "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p");
                var panel = For(clip, TestVideos.Probe(clip));

                Ui.Click(Play(panel));
                Ui.Pump(() => IsMoving(panel), 20000);

                var picture = (BitmapSource)Picture(panel).Source;
                Assert.Equal((420, 314), (picture.PixelWidth, picture.PixelHeight));
                panel.Close();
            });
        }

        [Theory]
        [InlineData(560, 1, 560)]
        [InlineData(315, 1, 314)]       // an even number, which video needs
        [InlineData(315, 1.25, 392)]    // text at 125%
        [InlineData(560, 1.5, 840)]
        [InlineData(560, 3, 1120)]      // finer than a moving picture would show
        [InlineData(0, 1, 2)]           // before the controls have a size
        public void The_moving_picture_is_sized_in_the_screens_own_dots(double points, double dotsToAPoint, int expected)
        {
            Assert.Equal(expected, TrimPanel.Dots(points, dotsToAPoint));
        }

        [Fact]
        public void The_moving_picture_is_made_the_size_of_the_box_it_is_shown_in()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();   // 560 across, so the box is 315 high

                Ui.Click(Play(panel));
                FirstFrameShown(panel);

                var picture = (BitmapSource)Picture(panel).Source;
                Assert.Equal(560, picture.PixelWidth);
                Assert.Equal(314, picture.PixelHeight);
                panel.Close();
            });
        }

        [Fact]
        public void The_line_on_the_strip_starts_on_the_start_handle_and_moves_with_it()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                Assert.Equal(0, strip.Position.Value, 3);

                Type(panel, "StartBox", "1");

                Assert.Equal(1, strip.Position.Value, 3);
                Assert.Equal("Play", PlaySays(panel));
                Ui.Click(Play(panel));
                Assert.Equal(30, FirstFrameShown(panel));
                panel.Close();
            });
        }

        [Fact]
        public void Moving_the_end_handle_puts_the_line_three_seconds_before_it_and_Play_starts_from_the_line()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");

                Type(panel, "EndBox", "3.4");

                // Nothing else changes: the button still just says Play, and the picture is the cut.
                Assert.Equal(0.4, strip.Position.Value, 3);
                Assert.Equal("Play", PlaySays(panel));
                Assert.StartsWith("End · 0:03.400", Label(panel));

                Ui.Click(Play(panel));

                // From 0.4 s, which is frame 12.
                Assert.Equal(12, FirstFrameShown(panel));
                panel.Close();
            });
        }

        [Fact]
        public void The_line_is_never_put_before_the_start_handle()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                Type(panel, "StartBox", "2");
                Type(panel, "EndBox", "3.4");

                Assert.Equal(2, strip.Position.Value, 3);
                Ui.Click(Play(panel));
                Assert.Equal(60, FirstFrameShown(panel));
                panel.Close();
            });
        }

        /// <summary>
        /// The controls for a clip whose sound, and so its pictures, go by at several times their
        /// usual speed, for the tests that have to play a part right through.
        /// </summary>
        private TrimPanel NewQuickPanel()
        {
            var panel = NewPanel(withSound: true);
            panel.Player.NewSound = () => _sound = new PretendSound { Pace = 4 };
            return panel;
        }

        [Fact]
        public void Once_it_has_played_up_to_the_end_Play_goes_back_to_the_start_of_the_part()
        {
            Ui.Run(() =>
            {
                var panel = NewQuickPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                Type(panel, "EndBox", "3.4");

                Ui.Click(Play(panel));
                Assert.Equal(12, FirstFrameShown(panel));
                Ui.Pump(() => !panel.Player.IsPlaying, 20000);

                // Like any player that has reached the end: the next Play is the whole part again.
                Assert.Equal("Play", PlaySays(panel));
                Assert.Equal(0, strip.Position.Value, 3);
                Ui.Click(Play(panel));
                Assert.Equal(0, FirstFrameShown(panel));
                panel.Close();
            });
        }

        [Fact]
        public void Moving_the_start_handle_after_the_end_means_the_part_from_its_start()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Type(panel, "EndBox", "3.4");
                Type(panel, "StartBox", "0.2");

                Assert.Equal(0.2, Ui.Named<TrimStrip>(panel, "Strip").Position.Value, 3);
                Ui.Click(Play(panel));

                // From 0.2 s, which is frame 6, not from three seconds before the end, which is frame 12.
                Assert.Equal(6, FirstFrameShown(panel));
                panel.Close();
            });
        }

        [Fact]
        public void Moving_the_end_again_puts_the_line_back_before_it()
        {
            Ui.Run(() =>
            {
                var panel = NewQuickPanel();
                Type(panel, "EndBox", "3.4");
                Ui.Click(Play(panel));
                Ui.Pump(() => IsMoving(panel), 20000);
                Ui.Pump(() => !panel.Player.IsPlaying, 20000);

                Type(panel, "EndBox", "3.3");

                Assert.Equal(0.3, Ui.Named<TrimStrip>(panel, "Strip").Position.Value, 3);
                Ui.Click(Play(panel));
                // From 0.3 s, which is frame 9.
                Assert.Equal(9, FirstFrameShown(panel));
                panel.Close();
            });
        }

        [Fact]
        public void Played_to_the_end_from_a_click_on_the_strip_it_starts_over_from_the_start_as_well()
        {
            Ui.Run(() =>
            {
                var panel = NewQuickPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                Type(panel, "EndBox", "3.4");

                strip.Press(strip.XOf(3));
                strip.Release(strip.XOf(3));
                Ui.Pump(() => IsMoving(panel), 20000);
                Ui.Pump(() => !panel.Player.IsPlaying, 20000);

                // However it got to the end, that's where it is, and Play starts the part over.
                Assert.Equal("Play", PlaySays(panel));
                Ui.Click(Play(panel));
                Assert.Equal(0, FirstFrameShown(panel));
                panel.Close();
            });
        }

        [Fact]
        public void A_time_still_being_typed_counts_when_Play_is_pressed()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Ui.Named<TextBox>(panel, "StartBox").Text = "2";   // typed, and Play pressed straight after

                Ui.Click(Play(panel));

                Assert.Equal(2, panel.Selection.Start, 3);
                Assert.Equal(60, FirstFrameShown(panel));
                panel.Close();
            });
        }

        [Fact]
        public void At_the_end_of_the_part_it_stops_on_the_cut_with_the_last_frame_kept_on_show()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                panel.Start();
                Type(panel, "StartBox", "1");
                Type(panel, "EndBox", "1.5");

                Ui.Click(Play(panel));
                FirstFrameShown(panel);
                Ui.Pump(() => !panel.Player.IsPlaying, 20000);

                Assert.Equal("End · 0:01.500", Label(panel));
                // Having played up to the end, the next Play is the part from its start.
                Assert.Equal("Play", PlaySays(panel));
                Assert.Equal(1, strip.Position.Value, 3);
                // The frame a cut ending at 1.5 s keeps last is the one before it: frame 44.
                Ui.Pump(() => !IsMoving(panel), 20000);
                Assert.Equal(44, NumberOn(panel));
                Assert.Equal(BitmapScalingMode.HighQuality, RenderOptions.GetBitmapScalingMode(Picture(panel)));
                panel.Close();
            });
        }

        [Fact]
        public void Pausing_holds_the_picture_and_says_where_it_is_and_Play_carries_on_from_that_frame()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel(withSound: true);
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                Ui.Click(Play(panel));
                Ui.Pump(() => panel.ShownSeconds >= 0.3, 20000);

                Ui.Click(Play(panel));

                // Nothing is left running behind a picture that's standing still.
                Assert.False(panel.Player.IsPlaying);
                Assert.True(_sound.IsClosed);
                Assert.Equal("Play", PlaySays(panel));
                Assert.StartsWith("Paused · 0:00.", Label(panel));
                int held = NumberOn(panel);
                Assert.Equal(held / 30.0, strip.Position.Value, 3);
                var until = DateTime.UtcNow.AddMilliseconds(400);
                Ui.Pump(() => DateTime.UtcNow > until);
                Assert.Equal(held, NumberOn(panel));
                Assert.True(IsMoving(panel), "The picture it was held on should still be the one on show");

                Ui.Click(Play(panel));

                Assert.Equal("Pause", PlaySays(panel));
                // From the frame it was held on, or the next: a time to the thousandth can't always tell them apart.
                Assert.InRange(FirstFrameShown(panel), held, held + 1);
                Assert.StartsWith("Playing · ", Label(panel));
                panel.Close();
            });
        }

        [Fact]
        public void Paused_before_the_first_picture_has_come_it_just_stops_and_the_line_stays_where_it_was()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                Type(panel, "EndBox", "3.4");

                Ui.Click(Play(panel));
                Ui.Click(Play(panel));

                Assert.False(panel.Player.IsPlaying);
                Assert.Equal("Play", PlaySays(panel));
                Assert.StartsWith("End · ", Label(panel));
                Assert.Equal(0.4, strip.Position.Value, 3);

                // So Play starts from the same place again.
                Ui.Click(Play(panel));
                Assert.Equal(12, FirstFrameShown(panel));
                panel.Close();
            });
        }

        [Fact]
        public void A_click_on_the_strip_puts_the_line_there_and_paused_before_anything_shows_it_stays_there()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                strip.Measure(new Size(560, 52));
                strip.Arrange(new Rect(0, 0, 560, 52));

                strip.Press(strip.XOf(2));
                strip.Release(strip.XOf(2));
                panel.PlayOrPause();

                Assert.False(panel.Player.IsPlaying);
                Assert.Equal(2, strip.Position.Value, 1);
                Ui.Click(Play(panel));
                Assert.InRange(FirstFrameShown(panel), 59, 61);
                panel.Close();
            });
        }

        [Fact]
        public void Moving_a_handle_lets_go_of_where_it_was_paused()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                Ui.Click(Play(panel));
                Ui.Pump(() => panel.ShownSeconds >= 0.3, 20000);
                Ui.Click(Play(panel));
                Assert.NotNull(strip.Position);

                Type(panel, "StartBox", "2");

                Assert.Equal(2, strip.Position.Value, 3);
                Assert.StartsWith("Start · 0:02.000", Label(panel));
                // And Play now starts from the handle, not from where it had been held.
                Ui.Click(Play(panel));
                Assert.Equal(60, FirstFrameShown(panel));
                panel.Close();
            });
        }

        [Fact]
        public void A_still_picture_that_turns_up_while_it_is_held_does_not_replace_the_picture_it_is_held_on()
        {
            Ui.Run(() =>
            {
                // Ten seconds with one keyframe, so the exact picture for near its end takes a while to get.
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                var panel = For(clip, TestVideos.Probe(clip));
                panel.Start();
                Type(panel, "EndBox", "9.9");

                // Meanwhile it's played from the start, which is quick, and held.
                panel.PlayFrom(0);
                FirstFrameShown(panel);
                panel.PlayOrPause();
                int stills = 0;
                DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image))
                    .AddValueChanged(Picture(panel), (s, e) => stills += IsMoving(panel) ? 0 : 1);
                var until = DateTime.UtcNow.AddMilliseconds(1500);
                Ui.Pump(() => DateTime.UtcNow > until);

                Assert.Equal(0, stills);
                Assert.StartsWith("Paused · ", Label(panel));
                panel.Close();
            });
        }

        [Fact]
        public void A_line_on_the_strip_follows_the_playing()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                Type(panel, "StartBox", "1");

                Ui.Click(Play(panel));
                Ui.Pump(() => panel.ShownSeconds >= 1.2, 20000);

                Assert.Equal(panel.ShownSeconds.Value, strip.Position.Value, 6);
                Assert.Equal(NumberOn(panel) / 30.0, strip.Position.Value, 3);
                panel.Close();
            });
        }

        [Fact]
        public void Moving_a_handle_stops_the_playing_and_shows_that_handles_picture_again()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                strip.Measure(new Size(560, 52));
                strip.Arrange(new Rect(0, 0, 560, 52));
                panel.Start();
                Ui.Click(Play(panel));
                FirstFrameShown(panel);

                strip.DragTo(TrimHandle.End, strip.XOf(2), settled: true);

                Assert.False(panel.Player.IsPlaying);
                // Three seconds before an end two seconds in would be before the start, so it's on the start.
                Assert.Equal(0, strip.Position.Value, 3);
                Assert.StartsWith("End · 0:02.0", Label(panel));
                Assert.Equal("Play", PlaySays(panel));
                Ui.Pump(() => !IsMoving(panel), 20000);
                Assert.Equal(59, NumberOn(panel));
                panel.Close();
            });
        }

        [Fact]
        public void Stepping_a_handle_or_typing_a_time_stops_the_playing_too()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Ui.Click(Play(panel));
                FirstFrameShown(panel);
                panel.Step(TrimHandle.Start, 1, bySecond: false);
                Assert.False(panel.Player.IsPlaying);

                Ui.Click(Play(panel));
                Ui.Pump(() => panel.Player.PicturesRead > 0, 20000);
                Type(panel, "EndBox", "3");
                Assert.False(panel.Player.IsPlaying);
                panel.Close();
            });
        }

        [Fact]
        public void A_click_on_the_strip_plays_from_there()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                strip.Measure(new Size(560, 52));
                strip.Arrange(new Rect(0, 0, 560, 52));

                strip.Press(strip.XOf(2));
                strip.Release(strip.XOf(2));

                Assert.True(panel.Player.IsPlaying);
                Assert.Equal("Pause", PlaySays(panel));
                Assert.Equal(60, FirstFrameShown(panel));
                Assert.Equal(0, panel.Selection.Start, 3);

                // And again while it plays: it starts over from the new spot.
                strip.Press(strip.XOf(1));
                strip.Release(strip.XOf(1));
                Ui.Pump(() => panel.ShownSeconds < 1.5 && panel.Player.Seconds > 0, 20000);
                Assert.InRange(NumberOn(panel), 30, 44);
                panel.Close();
            });
        }

        [Theory]
        [InlineData("ConfirmButton")]
        [InlineData("CancelButton")]
        public void Answering_stops_the_playing_and_the_sound(string button)
        {
            Ui.Run(() =>
            {
                var panel = NewPanel(withSound: true);
                Ui.Click(Play(panel));
                FirstFrameShown(panel);
                Assert.False(_sound.IsClosed);

                Ui.Click(Ui.Named<Button>(panel, button));

                Assert.False(panel.Player.IsPlaying, "Or it would go on playing a video that's no longer on show");
                Assert.True(_sound.IsClosed);
            });
        }

        [Fact]
        public void The_sound_is_played_along_with_the_pictures_and_let_go_of_when_it_is_paused()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel(withSound: true);
                Type(panel, "StartBox", "1");

                Ui.Click(Play(panel));
                Ui.Pump(() => panel.ShownSeconds >= 1.3, 20000);
                Assert.True(_sound.WasOpened);
                Assert.False(_sound.IsPaused);
                Assert.InRange(_sound.Seconds - (panel.ShownSeconds.Value - 1), -0.02, 0.3);

                Ui.Click(Play(panel));
                Assert.True(_sound.IsClosed);
                panel.Close();
            });
        }

        [Fact]
        public void A_video_that_cannot_be_played_says_so_and_can_still_be_trimmed()
        {
            Ui.Run(() =>
            {
                string fake = _videos.PathOf("notes.mp4");
                File.WriteAllText(fake, "not a video");
                var panel = For(fake, Fixtures.Media(null, 60));
                var note = Ui.Named<TextBlock>(panel, "PlayNote");
                Assert.Equal(Visibility.Collapsed, note.Visibility);

                Ui.Click(Play(panel));
                Ui.Pump(() => note.Visibility == Visibility.Visible, 20000);

                Assert.Contains("couldn't be played", note.Text);
                Assert.Equal("Play", PlaySays(panel));
                Assert.False(panel.Player.IsPlaying);
                Assert.StartsWith("Start · ", Label(panel));

                // Trying again takes the note away until there's something new to say.
                Ui.Click(Play(panel));
                Assert.Equal(Visibility.Collapsed, note.Visibility);
                Assert.False(panel.Answered.IsCompleted);
                panel.Close();
            });
        }

        [Fact]
        public void A_still_picture_that_turns_up_once_it_is_playing_does_not_cut_in_on_the_moving_one()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                panel.Start();
                // Dragging asks for quick pictures as it goes, and one of those is still on its way...
                strip.DragTo(TrimHandle.Start, strip.XOf(0.5), settled: false);

                // ...when Space is pressed.
                panel.PlayOrPause();
                int stills = 0;
                DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image))
                    .AddValueChanged(Picture(panel), (s, e) => stills += IsMoving(panel) ? 0 : 1);
                FirstFrameShown(panel);
                var until = DateTime.UtcNow.AddMilliseconds(700);
                Ui.Pump(() => DateTime.UtcNow > until);

                Assert.Equal(0, stills);
                Assert.True(IsMoving(panel));
                Assert.StartsWith("Playing · ", Label(panel));
                panel.Close();
            });
        }

        private static Button StartHere(TrimPanel panel) => Ui.Named<Button>(panel, "StartHereButton");

        private static Button EndHere(TrimPanel panel) => Ui.Named<Button>(panel, "EndHereButton");

        /// <summary>The first frame of the clip that a part starting where the start handle is keeps.</summary>
        private static int FirstKept(TrimPanel panel) => (int)Math.Ceiling(panel.Selection.Start * 30 - 1e-6);

        [Fact]
        public void Start_here_while_it_plays_starts_the_part_on_the_picture_on_show_and_it_plays_on()
        {
            Ui.Run(() =>
            {
                var panel = NewQuickPanel();
                Ui.Click(Play(panel));
                Ui.Pump(() => panel.ShownSeconds >= 0.5, 20000);
                int shown = NumberOn(panel);

                Ui.Click(StartHere(panel));

                Assert.Equal(shown, FirstKept(panel));
                Assert.Equal(Format.Timecode(panel.Selection.Start), Ui.Named<TextBox>(panel, "StartBox").Text);
                Assert.True(panel.Player.IsPlaying, "A start is picked while watching, and watching goes on");
                Assert.Equal("Pause", PlaySays(panel));

                // Played up to the end, it starts over from the new start.
                Ui.Pump(() => !panel.Player.IsPlaying, 20000);
                Ui.Click(Play(panel));
                Assert.Equal(shown, FirstFrameShown(panel));
                panel.Close();
            });
        }

        [Fact]
        public void Start_here_while_it_is_held_starts_the_part_on_the_picture_it_is_held_on()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                panel.Start();
                Ui.Click(Play(panel));
                Ui.Pump(() => panel.ShownSeconds >= 0.5, 20000);
                Ui.Click(Play(panel));
                int held = NumberOn(panel);

                Ui.Click(StartHere(panel));

                Assert.Equal(held, FirstKept(panel));
                Assert.False(panel.Player.IsPlaying);
                Assert.Equal("Start · " + Format.Timecode(panel.Selection.Start), Label(panel));
                Assert.Equal(panel.Selection.Start, strip.Position.Value, 3);
                // Then the exact picture for the new start, which is the one it was held on.
                Ui.Pump(() => !IsMoving(panel), 20000);
                Assert.Equal(held, NumberOn(panel));
                panel.Close();
            });
        }

        [Fact]
        public void End_here_while_it_plays_stops_on_the_new_end_with_the_picture_on_show_kept_last()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                panel.Start();
                panel.PlayFrom(3);
                Ui.Pump(() => panel.ShownSeconds >= 3.2, 20000);
                int shown = NumberOn(panel);

                Ui.Click(EndHere(panel));

                Assert.False(panel.Player.IsPlaying);
                Assert.Equal("Play", PlaySays(panel));
                Assert.Equal("End · " + Format.Timecode(panel.Selection.End), Label(panel));
                // The line goes three seconds before it, as for any move of the end.
                Assert.Equal(panel.Selection.End - 3, strip.Position.Value, 3);
                Ui.Pump(() => !IsMoving(panel), 20000);
                Assert.Equal(shown, NumberOn(panel));
                panel.Close();
            });
        }

        [Fact]
        public void With_nothing_playing_or_held_here_is_where_the_line_is()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                Type(panel, "EndBox", "3.4");   // which puts the line at 0.4

                Ui.Click(StartHere(panel));

                Assert.Equal(0.4, panel.Selection.Start, 3);
                Assert.Equal(3.4, panel.Selection.End, 3);
                Assert.Equal("Start · 0:00.400", Label(panel));
                Assert.Equal(0.4, strip.Position.Value, 3);
                Assert.False(panel.Player.IsPlaying);

                // The line is on the start now, and an end can't be closer to it than the shortest part.
                Ui.Click(EndHere(panel));
                Assert.Equal(0.5, panel.Selection.End, 3);
                panel.Close();
            });
        }

        /// <summary>A click as the mouse makes one: the button is pressed, and then it clicks.</summary>
        private static void ClickWithTheMouse(Button button)
        {
            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Ui.Click(button);
        }

        [Fact]
        public void Start_here_clicked_with_a_time_still_being_typed_starts_on_the_picture_it_is_held_on()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Ui.Click(Play(panel));
                Ui.Pump(() => panel.ShownSeconds >= 0.5, 20000);
                Ui.Click(Play(panel));
                int held = NumberOn(panel);
                Ui.Named<TextBox>(panel, "EndBox").Text = "3";

                ClickWithTheMouse(StartHere(panel));

                // The end typed counts as well, but taking it in doesn't move here before the click.
                Assert.Equal(held, FirstKept(panel));
                Assert.Equal(3, panel.Selection.End, 3);
                panel.Close();
            });
        }

        [Fact]
        public void Pause_clicked_with_a_time_still_being_typed_pauses()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Ui.Click(Play(panel));
                Ui.Pump(() => panel.ShownSeconds >= 0.5, 20000);
                Ui.Named<TextBox>(panel, "EndBox").Text = "3";

                ClickWithTheMouse(Play(panel));

                // As Space does. The time typed waits in its box, to be taken in as usual.
                Assert.False(panel.Player.IsPlaying);
                Assert.StartsWith("Paused · ", Label(panel));
                Assert.Equal("3", Ui.Named<TextBox>(panel, "EndBox").Text);
                panel.Close();
            });
        }

        [Fact]
        public void On_a_video_of_fewer_frames_a_second_than_it_is_played_at_Start_here_starts_on_the_frame_on_show()
        {
            Ui.Run(() =>
            {
                // Ten frames a second, played as thirty pictures a second: each frame is on show for three.
                string clip = _videos.MakeNumberedClip("slow.mp4", fps: 10);
                var panel = For(clip, TestVideos.Probe(clip));
                panel.Start();
                Ui.Click(Play(panel));
                // The last of a frame's three pictures, the nearest to the next frame.
                Func<bool> lastOfThree = () => Math.Round(panel.ShownSeconds.Value * 30) % 3 == 2;
                Ui.Pump(() => panel.ShownSeconds >= 0.5 && lastOfThree(), 20000);
                int shown = NumberOn(panel);

                Ui.Click(StartHere(panel));

                Assert.Equal(shown, (int)Math.Ceiling(panel.Selection.Start * 10 - 1e-6));

                // And held on one, End here keeps that frame last.
                Ui.Pump(() => panel.ShownSeconds >= 1.5 && lastOfThree(), 20000);
                Ui.Click(Play(panel));
                int held = NumberOn(panel);
                Ui.Click(EndHere(panel));
                Ui.Pump(() => !IsMoving(panel), 20000);
                Assert.Equal(held, NumberOn(panel));
                panel.Close();
            });
        }

        [Fact]
        public void Held_again_before_carrying_on_has_shown_anything_it_is_still_held_on_the_same_picture()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var cropper = Ui.Named<CropBox>(panel, "Cropper");
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                panel.Start();
                Ui.Click(Play(panel));
                Ui.Pump(() => panel.ShownSeconds >= 0.5, 20000);
                Ui.Click(Play(panel));
                int held = NumberOn(panel);
                double line = strip.Position.Value;

                Ui.Click(Play(panel));   // carries on...
                Ui.Click(Play(panel));   // ...and is held again before its first picture

                Assert.StartsWith("Paused · ", Label(panel));
                Assert.Equal(line, strip.Position.Value, 6);
                // So zooming in on a corner sharpens that picture, rather than swapping it for another.
                cropper.Press(cropper.Picture.TopLeft);
                cropper.Rest();
                Ui.Pump(() => !IsMoving(panel), 20000);
                Assert.Equal(held, NumberOn(panel));
                cropper.Release(cropper.Picture.TopLeft);
                panel.Close();
            });
        }

        [Fact]
        public void Played_from_a_click_on_the_strip_and_held_before_anything_shows_the_line_stays_where_it_was_clicked()
        {
            Ui.Run(() =>
            {
                // Even with the picture it was held on before still on show.
                var panel = NewPanel();
                var strip = Ui.Named<TrimStrip>(panel, "Strip");
                strip.Measure(new Size(560, 52));
                strip.Arrange(new Rect(0, 0, 560, 52));
                Ui.Click(Play(panel));
                Ui.Pump(() => panel.ShownSeconds >= 0.5, 20000);
                Ui.Click(Play(panel));

                strip.Press(strip.XOf(2));
                strip.Release(strip.XOf(2));
                panel.PlayOrPause();

                Assert.False(panel.Player.IsPlaying);
                Assert.Equal(2, strip.Position.Value, 1);
                panel.Close();
            });
        }

        [Fact]
        public void Paused_while_zoomed_in_on_a_crop_corner_the_picture_it_is_held_on_is_fetched_sharp()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var cropper = Ui.Named<CropBox>(panel, "Cropper");
                panel.Start();
                cropper.Press(cropper.Picture.TopLeft);
                cropper.Rest();
                Ui.Click(Play(panel));
                Ui.Pump(() => panel.ShownSeconds >= 0.5, 20000);

                Ui.Click(Play(panel));
                int held = NumberOn(panel);

                Ui.Pump(() => !IsMoving(panel), 20000);
                Assert.Equal(held, NumberOn(panel));
                Assert.StartsWith("Paused · ", Label(panel));
                cropper.Release(cropper.Picture.TopLeft);
                panel.Close();
            });
        }

        [Fact]
        public void Start_here_and_End_here_never_take_the_keyboard_and_say_what_they_are()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                // A button holding the keyboard would keep Space and Enter for itself.
                Assert.False(StartHere(panel).Focusable);
                Assert.False(EndHere(panel).Focusable);
                Assert.Equal("Start here", System.Windows.Automation.AutomationProperties.GetName(StartHere(panel)));
                Assert.Equal("End here", System.Windows.Automation.AutomationProperties.GetName(EndHere(panel)));
            });
        }

        [Fact]
        public void Zoomed_in_on_a_crop_corner_while_it_is_held_the_picture_it_is_held_on_is_fetched_sharp()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                var cropper = Ui.Named<CropBox>(panel, "Cropper");
                panel.Start();
                Ui.Click(Play(panel));
                // Held on a frame such as 17, at 0.5667 s. To the nearest thousandth that's 0.567,
                // where the frame on from there is 18.
                Ui.Pump(() => panel.ShownSeconds >= 0.3 && NumberOn(panel) % 3 == 2, 20000);
                Ui.Click(Play(panel));
                int held = NumberOn(panel);
                Assert.Equal(2, held % 3);

                cropper.Press(cropper.Picture.TopLeft);
                cropper.Rest();
                Ui.Pump(() => !IsMoving(panel), 20000);

                // The same frame, as a still picture from the file, and it's still held there.
                Assert.Equal(held, NumberOn(panel));
                Assert.StartsWith("Paused · ", Label(panel));
                cropper.Release(cropper.Picture.TopLeft);
                Ui.Click(Play(panel));
                Assert.InRange(FirstFrameShown(panel), held, held + 1);
                panel.Close();
            });
        }

        [Fact]
        public void Taken_off_the_screen_while_playing_it_stops()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Ui.Click(Play(panel));
                Assert.True(panel.Player.IsPlaying);

                panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

                Assert.False(panel.Player.IsPlaying);
            });
        }

        [Fact]
        public void Once_the_controls_are_closed_nothing_more_can_be_played()
        {
            Ui.Run(() =>
            {
                var panel = NewPanel();
                Ui.Click(Play(panel));
                panel.Close();
                Assert.False(panel.Player.IsPlaying);

                Ui.Click(Play(panel));

                Assert.False(panel.Player.IsPlaying);
            });
        }
    }
}
