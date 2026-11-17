using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// "Trim first" in the main window: a video dropped there is asked about before anything happens
    /// to it, with the trim controls standing where the drop zones were. The real controls are used,
    /// and the test stands in for whoever would be using them.
    /// </summary>
    [Collection(WindowTests.Name)]
    public class TrimFlowTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();
        private readonly List<string> _asked = new List<string>();

        public void Dispose() => _videos.Dispose();

        private string SettingsPath => _videos.PathOf("settings.json");

        private MainWindow NewWindow(Action<TrimPanel> answer, AppSettings settings = null)
        {
            MainWindow window = null;
            window = new MainWindow(settings ?? AppSettings.Load(SettingsPath), SettingsPath, TestVideos.Tools, compact: false, dark: false);
            window.ShowTrim = trim =>
            {
                _asked.Add(window.Title);
                answer(trim);
                return Task.CompletedTask;
            };
            return window;
        }

        internal static void Keep(TrimPanel trim, string start, string end)
        {
            foreach (var typed in new[] { ("StartBox", start), ("EndBox", end) })
            {
                var box = Ui.Named<TextBox>(trim, typed.Item1);
                box.Text = typed.Item2;
                box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            }
            Ui.Click(Ui.Named<Button>(trim, "ConfirmButton"));
        }

        internal static void Cancel(TrimPanel trim) => Ui.Click(Ui.Named<Button>(trim, "CancelButton"));

        [Fact]
        public void The_main_window_has_a_place_to_trim_first()
        {
            Ui.Run(() =>
            {
                var window = NewWindow(Cancel);
                Assert.Equal(Visibility.Visible, Ui.Named<Button>(window, "TrimZone").Visibility);
                Assert.False(Look.At(window).IsTrimming);
            });
        }

        [Fact]
        public void It_says_it_is_for_trimming_or_cropping_and_the_words_fit_in_it_at_the_narrowest()
        {
            Ui.Run(() =>
            {
                var window = NewWindow(Cancel);
                var zone = Ui.Named<Button>(window, "TrimZone");
                var title = Ui.Named<TextBlock>(window, "TrimZoneTitle");
                Ui.LayOut(window, 480);

                Assert.Equal("Trim or crop first", title.Text);
                Assert.Equal("Choose a video to trim or crop first", AutomationProperties.GetName(zone));
                Assert.True(title.DesiredSize.Width > 60 && title.DesiredSize.Width <= zone.ActualWidth - 8, $"{title.DesiredSize.Width:0} wide in a target {zone.ActualWidth:0} wide");
            });
        }

        [Fact]
        public void A_video_dropped_on_trim_first_is_asked_about_before_anything_is_queued()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                int queuedWhenAsked = -1;
                MainWindow window = null;
                window = NewWindow(trim =>
                {
                    queuedWhenAsked = window.Jobs.Count;
                    Cancel(trim);
                });

                window.TakeDrop(new[] { clip }, onTrimZone: true);
                Assert.Empty(window.Jobs);
                Ui.Pump(() => _asked.Count == 1);

                Assert.Contains("clip.mp4", _asked[0]);
                Assert.Equal(0, queuedWhenAsked);
                Assert.Empty(window.Jobs);
                Assert.Equal(new[] { clip }, Directory.GetFiles(_videos.Folder, "*.mp4"));
            });
        }

        [Fact]
        public void While_a_video_is_trimmed_its_controls_stand_where_the_drop_zones_were()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                MainWindow window = null;
                TrimPanel asked = null;
                Look during = null;
                window = NewWindow(trim =>
                {
                    asked = trim;
                    during = Look.At(window);
                    Cancel(trim);
                });

                window.TakeDrop(new[] { clip }, onTrimZone: true);
                Ui.Pump(() => during != null);

                Assert.Same(asked, during.InSlot);
                Assert.Equal(Visibility.Visible, during.Slot);
                Assert.Equal(Visibility.Collapsed, during.DropZones);
                // The rest of the main screen stays put, which is the point of trimming here.
                Assert.Equal(Visibility.Visible, during.Controls);
                Assert.Equal("Flyweight · trimming clip.mp4", during.Title);

                Ui.Pump(() => !Look.At(window).IsTrimming);
                var after = Look.At(window);
                Assert.Null(after.InSlot);
                Assert.Equal(Visibility.Visible, after.DropZones);
                Assert.Equal("Flyweight", after.Title);
            });
        }

        [Fact]
        public void A_crop_chosen_with_the_trim_controls_goes_with_the_video_and_comes_out_cropped()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");   // 640 by 360
                var window = NewWindow(trim =>
                {
                    // The top left quarter of the picture.
                    trim.Measure(new Size(560, double.PositiveInfinity));
                    trim.Arrange(new Rect(0, 0, 560, trim.DesiredSize.Height));
                    trim.UpdateLayout();
                    var box = Ui.Named<CropBox>(trim, "Cropper");
                    var corner = box.Picture.BottomRight;
                    var middle = new Point(box.Picture.Left + box.Picture.Width / 2, box.Picture.Top + box.Picture.Height / 2);
                    box.Press(corner);
                    box.MoveTo(middle);
                    box.Release(middle);
                    Ui.Click(Ui.Named<Button>(trim, "ConfirmButton"));
                });

                window.TakeDrop(new[] { clip }, onTrimZone: true);
                Ui.Pump(() => window.Jobs.Count == 1);
                var job = window.Jobs[0];
                Assert.Equal("clip.mp4 · cropped", job.Label);
                Ui.Pump(() => job.IsFinished);

                Assert.Equal(_videos.PathOf("clip_cropped.mp4"), job.ResultPath);
                var video = TestVideos.Probe(job.ResultPath).Video;
                Assert.Equal((320, 180), (video.Width, video.Height));
                Assert.StartsWith("Cropped · ", job.Status);
            });
        }

        [Fact]
        public void Confirming_makes_a_trimmed_copy_of_just_that_part_at_the_level_selected_on_the_cards()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                MainWindow window = null;
                window = NewWindow(trim =>
                {
                    // Picked while trimming, on the same cards as always.
                    Ui.Named<RadioButton>(window, "LevelMaximum").IsChecked = true;
                    Keep(trim, "1", "3");
                });

                window.TakeDrop(new[] { clip }, onTrimZone: true);
                Ui.Pump(() => window.Jobs.Count == 1);

                var job = window.Jobs.Single();
                Assert.Equal(1, job.Trim.StartSeconds, 3);
                Assert.Equal(3, job.Trim.EndSeconds, 3);
                Assert.Equal("clip.mp4 · 0:01 to 0:03", job.Label);
                Assert.True(job.Settings.SameAs(Presets.Default(LevelId.Maximum)));

                Ui.Pump(() => job.IsFinished);
                Assert.True(job.CanShowFile, job.Status);
                Assert.Equal(_videos.PathOf("clip_trimmed.mp4"), job.ResultPath);
                Assert.Equal(60, TestVideos.FrameCount(job.ResultPath));
                Assert.StartsWith("Trimmed to", job.Status);
                Assert.True(File.Exists(clip), "The original must be left alone");
            });
        }

        [Fact]
        public void A_level_changed_while_trimming_is_used_as_changed()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                MainWindow window = null;
                window = NewWindow(trim =>
                {
                    // Advanced options are the real ones, so this changes the level itself.
                    Ui.Named<TextBox>(window, "TargetSizeBox").Text = "25";
                    Keep(trim, "1", "3");
                });

                window.TakeDrop(new[] { clip }, onTrimZone: true);
                Ui.Pump(() => window.Jobs.Count == 1);

                var job = window.Jobs.Single();
                Assert.Equal(25, job.Settings.TargetSizeMb);
                Assert.Equal(25, AppSettings.Load(SettingsPath).Get(LevelId.Balanced).TargetSizeMb);
                Ui.Pump(() => job.IsFinished);
            });
        }

        [Fact]
        public void A_preset_of_your_own_picked_while_trimming_is_the_one_used()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                var settings = AppSettings.Load(SettingsPath);
                var discord = settings.AddCustom("Discord", Presets.Default(LevelId.Balanced));
                discord.Settings.TargetSizeMb = 10;
                MainWindow window = null;
                window = NewWindow(trim =>
                {
                    Ui.Named<UniformGrid>(window, "CustomCards").Children.Cast<RadioButton>().Single().IsChecked = true;
                    Keep(trim, "1", "3");
                }, settings);

                window.TakeDrop(new[] { clip }, onTrimZone: true);
                Ui.Pump(() => window.Jobs.Count == 1);

                var job = window.Jobs.Single();
                Assert.Equal(10, job.Settings.TargetSizeMb);
                // A copy, so a change made after this doesn't reach a video already in the queue.
                Assert.NotSame(discord.Settings, job.Settings);
                Ui.Pump(() => job.IsFinished);
            });
        }

        [Fact]
        public void Keeping_the_whole_video_compresses_it_as_usual()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeBulkyClip("clip.mov", seconds: 1);
                var window = NewWindow(trim => Ui.Click(Ui.Named<Button>(trim, "ConfirmButton")));

                window.TakeDrop(new[] { clip }, onTrimZone: true);
                Ui.Pump(() => window.Jobs.Count == 1);

                var job = window.Jobs.Single();
                Assert.Single(_asked);
                Assert.Null(job.Trim);
                Assert.Equal("clip.mov", job.Label);
                Ui.Pump(() => job.IsFinished);
                Assert.Equal(_videos.PathOf("clip_compressed.mp4"), job.ResultPath);
            });
        }

        [Fact]
        public void Several_videos_are_asked_about_one_at_a_time_without_the_drop_zones_flashing_back_between_them()
        {
            Ui.Run(() =>
            {
                string first = _videos.MakeBulkyClip("a.mov", seconds: 1);
                string second = _videos.MakeBulkyClip("b.mov", seconds: 2);
                var window = NewWindow(trim =>
                {
                    if (trim.FileName == "a.mov") Cancel(trim);
                    else Keep(trim, "0.5", "1.5");
                });
                var zones = Ui.Named<FrameworkElement>(window, "DropZones");
                int cameBack = 0;
                DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(FrameworkElement))
                    .AddValueChanged(zones, (s, e) => { if (zones.Visibility == Visibility.Visible) cameBack++; });

                window.TakeDrop(new[] { first, second }, onTrimZone: true);
                Ui.Pump(() => _asked.Count == 2 && !Look.At(window).IsTrimming);

                Assert.Equal("Flyweight · trimming a.mov · 1 more after this", _asked[0]);
                Assert.Equal("Flyweight · trimming b.mov", _asked[1]);
                Assert.Equal(1, cameBack);

                var job = window.Jobs.Single();
                Assert.Equal(second, job.InputPath);
                Ui.Pump(() => job.IsFinished);
            });
        }

        [Fact]
        public void A_video_dropped_on_the_window_while_trimming_waits_its_turn_to_be_trimmed_too()
        {
            Ui.Run(() =>
            {
                string first = _videos.MakeBulkyClip("a.mov", seconds: 1);
                string second = _videos.MakeBulkyClip("b.mov", seconds: 1);
                MainWindow window = null;
                string titleAfterDrop = null;
                Visibility? wayOut = null;
                window = NewWindow(trim =>
                {
                    if (trim.FileName == "a.mov")
                    {
                        // The drop zones aren't there to say otherwise, and trimming is what's going on.
                        window.TakeDrop(new[] { second }, onTrimZone: false);
                        titleAfterDrop = window.Title;
                        wayOut = Ui.Named<FrameworkElement>(trim, "WaitingRow").Visibility;
                    }
                    Cancel(trim);
                });

                window.TakeDrop(new[] { first }, onTrimZone: true);
                Ui.Pump(() => _asked.Count == 2 && !Look.At(window).IsTrimming);

                Assert.Equal("Flyweight · trimming a.mov · 1 more after this", titleAfterDrop);
                Assert.Equal(Visibility.Visible, wayOut);
                Assert.Equal("Flyweight · trimming b.mov", _asked[1]);
                Assert.Empty(window.Jobs);
            });
        }

        [Fact]
        public void A_video_sent_from_the_right_click_menu_to_compress_while_trimming_is_still_just_compressed()
        {
            Ui.Run(() =>
            {
                string first = _videos.MakeBulkyClip("a.mov", seconds: 1);
                string second = _videos.MakeBulkyClip("b.mov", seconds: 1);
                MainWindow window = null;
                window = NewWindow(trim =>
                {
                    // That menu entry names a level, so there's no doubt about what was asked for.
                    window.HandleRequest(new CliArgs { Level = LevelId.Light, Files = { second } });
                    Cancel(trim);
                });

                window.TakeDrop(new[] { first }, onTrimZone: true);
                Ui.Pump(() => _asked.Count == 1 && !Look.At(window).IsTrimming);

                var job = window.Jobs.Single();
                Assert.Equal(second, job.InputPath);
                Assert.Null(job.Trim);
                Ui.Pump(() => job.IsFinished);
                Assert.Single(_asked);
            });
        }

        [Fact]
        public void A_preset_made_while_trimming_is_the_one_selected_and_so_the_one_used()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                var settings = AppSettings.Load(SettingsPath);
                MainWindow window = null;
                window = NewWindow(trim =>
                {
                    Ui.Click(Ui.Named<Button>(window, "NewPresetButton"));
                    Ui.Named<TextBox>(window, "TargetSizeBox").Text = "8";
                    Keep(trim, "1", "3");
                }, settings);

                window.TakeDrop(new[] { clip }, onTrimZone: true);
                Ui.Pump(() => window.Jobs.Count == 1);

                var job = window.Jobs.Single();
                Assert.Equal(8, settings.CustomPresets.Single().Settings.TargetSizeMb);
                Assert.Equal(8, job.Settings.TargetSizeMb);
                // It started as a copy, so the level it was made from is as it was.
                Assert.NotEqual(8, settings.Get(LevelId.Balanced).TargetSizeMb);
                Ui.Pump(() => job.IsFinished);
            });
        }

        [Fact]
        public void On_a_short_screen_a_tall_videos_preview_leaves_room_for_the_levels_under_it()
        {
            Ui.Run(() =>
            {
                string tall = _videos.Make("tall.mp4", "-f", "lavfi", "-i", "testsrc2=s=360x640:r=30:d=1", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p");
                double box = 0;
                var window = NewWindow(trim =>
                {
                    box = Ui.Named<Border>(trim, "PreviewBox").Height;
                    Cancel(trim);
                });
                // A small laptop. On a 1080p screen the same video gets 440.
                window.WorkArea = () => new Rect(0, 0, 1366, 728);

                window.TakeDrop(new[] { tall }, onTrimZone: true);
                Ui.Pump(() => box > 0);

                Assert.Equal(315, box);
            });
        }

        [Fact]
        public void A_video_dropped_anywhere_else_is_compressed_straight_away()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeBulkyClip("clip.mov", seconds: 1);
                var window = NewWindow(Cancel);

                window.TakeDrop(new[] { clip }, onTrimZone: false);

                var job = window.Jobs.Single();
                Assert.Null(job.Trim);
                Ui.Pump(() => job.IsFinished);
                Assert.Empty(_asked);
            });
        }
    }
}
