using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// Drives the real window without showing it on screen. WPF needs an STA thread, which xUnit
    /// doesn't provide, so each test hops onto its own.
    /// </summary>
    public class MainWindowTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "fw-ui-" + Guid.NewGuid().ToString("N"));
        private string SettingsPath => Path.Combine(_folder, "settings.json");

        public MainWindowTests()
        {
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    Directory.Delete(_folder, true);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(200);
                }
            }
        }

        private static void OnUiThread(Action test)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // In the real app this is always in place; awaits rely on it to return to the UI thread.
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                    test();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private int _menuUpdates;

        private MainWindow NewWindow(AppSettings settings = null, FFmpegTools tools = null, bool compact = false, bool dark = false)
        {
            settings = settings ?? AppSettings.Load(SettingsPath);
            return new MainWindow(settings, SettingsPath, tools ?? TestVideos.Tools, compact, dark, _ => _menuUpdates++);
        }

        private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        private static RadioButton CardNamed(MainWindow window, string title)
        {
            return Named<Panel>(window, "CustomCards").Children.OfType<RadioButton>()
                .FirstOrDefault(card => ((TextBlock)((StackPanel)card.Content).Children[0]).Text == title);
        }

        // ---- custom presets ----

        [Fact]
        public void New_preset_copies_the_selected_level_and_gets_ready_to_be_named()
        {
            OnUiThread(() =>
            {
                var settings = AppSettings.Load(SettingsPath);
                var window = NewWindow(settings);
                Named<RadioButton>(window, "LevelMaximum").IsChecked = true;

                Click(Named<Button>(window, "NewPresetButton"));

                var preset = settings.CustomPresets.Single();
                Assert.True(preset.Settings.SameAs(Presets.Default(LevelId.Maximum)));
                Assert.Equal(preset.Id, settings.SelectedCustomId);
                Assert.True(CardNamed(window, preset.Name).IsChecked);
                Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "AdvancedPanel").Visibility);
                Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "PresetNameRow").Visibility);
                Assert.Equal(preset.Name, Named<TextBox>(window, "PresetNameBox").Text);
                Assert.Single(AppSettings.Load(SettingsPath).CustomPresets);
            });
        }

        [Fact]
        public void Editing_a_custom_preset_leaves_the_built_in_levels_alone()
        {
            OnUiThread(() =>
            {
                var settings = AppSettings.Load(SettingsPath);
                var window = NewWindow(settings);
                Click(Named<Button>(window, "NewPresetButton"));

                Named<TextBox>(window, "TargetSizeBox").Text = "10";
                Select(Named<ComboBox>(window, "FrameRateBox"), 24);

                var saved = AppSettings.Load(SettingsPath);
                Assert.Equal(10, saved.CustomPresets.Single().Settings.TargetSizeMb);
                Assert.Equal(24, saved.CustomPresets.Single().Settings.MaxFps);
                foreach (LevelId level in Enum.GetValues(typeof(LevelId))) Assert.True(saved.IsDefault(level), level + " changed");
            });
        }

        [Fact]
        public void A_custom_preset_card_keeps_up_with_its_settings_and_name()
        {
            OnUiThread(() =>
            {
                var settings = AppSettings.Load(SettingsPath);
                var window = NewWindow(settings);
                Click(Named<Button>(window, "NewPresetButton"));

                Named<TextBox>(window, "TargetSizeBox").Text = "10";
                Named<TextBox>(window, "PresetNameBox").Text = "Discord";

                var card = CardNamed(window, "Discord");
                Assert.NotNull(card);
                Assert.Equal("Under 10 MB", ((TextBlock)((StackPanel)card.Content).Children[1]).Text);
                Assert.Equal("Discord: Under 10 MB", System.Windows.Automation.AutomationProperties.GetName(card));
                Assert.Contains("under 10 MB", Named<TextBlock>(window, "LevelSummary").Text);
                Assert.Equal("Discord", AppSettings.Load(SettingsPath).CustomPresets.Single().Name);
            });
        }

        [Fact]
        public void Switching_between_levels_and_presets_shows_each_ones_settings()
        {
            OnUiThread(() =>
            {
                var settings = AppSettings.Load(SettingsPath);
                var window = NewWindow(settings);
                Click(Named<Button>(window, "NewPresetButton"));
                Named<TextBox>(window, "PresetNameBox").Text = "Discord";
                Named<TextBox>(window, "TargetSizeBox").Text = "10";

                Named<RadioButton>(window, "LevelBalanced").IsChecked = true;
                Assert.Equal("", Named<TextBox>(window, "TargetSizeBox").Text);
                Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(window, "PresetNameRow").Visibility);
                Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(window, "DeletePresetButton").Visibility);
                Assert.False(CardNamed(window, "Discord").IsChecked);
                Assert.Null(settings.SelectedCustomId);

                CardNamed(window, "Discord").IsChecked = true;
                Assert.Equal("10", Named<TextBox>(window, "TargetSizeBox").Text);
                Assert.Equal("Discord", Named<TextBox>(window, "PresetNameBox").Text);
                Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "DeletePresetButton").Visibility);
                Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(window, "ResetButton").Visibility);
                Assert.False(Named<RadioButton>(window, "LevelBalanced").IsChecked);

                foreach (LevelId level in Enum.GetValues(typeof(LevelId))) Assert.True(settings.IsDefault(level), level + " changed");
            });
        }

        [Fact]
        public void A_saved_preset_is_back_with_its_card_after_reopening()
        {
            OnUiThread(() =>
            {
                var first = NewWindow();
                Click(Named<Button>(first, "NewPresetButton"));
                Named<TextBox>(first, "PresetNameBox").Text = "Discord";

                var reopened = NewWindow();

                Assert.True(CardNamed(reopened, "Discord").IsChecked);
                Assert.Equal("Discord", Named<TextBox>(reopened, "PresetNameBox").Text);
            });
        }

        [Fact]
        public void Deleting_a_preset_takes_two_clicks()
        {
            OnUiThread(() =>
            {
                var settings = AppSettings.Load(SettingsPath);
                var window = NewWindow(settings);
                Named<RadioButton>(window, "LevelLight").IsChecked = true;
                Click(Named<Button>(window, "NewPresetButton"));
                var delete = Named<Button>(window, "DeletePresetButton");

                Click(delete);
                Assert.Single(settings.CustomPresets);

                Click(delete);
                Assert.Empty(settings.CustomPresets);
                Assert.Empty(AppSettings.Load(SettingsPath).CustomPresets);
                Assert.Empty(Named<Panel>(window, "CustomCards").Children);
                Assert.True(Named<RadioButton>(window, "LevelLight").IsChecked);
                Assert.Equal(Visibility.Collapsed, delete.Visibility);
            });
        }

        [Fact]
        public void Changing_your_mind_about_deleting_starts_over()
        {
            OnUiThread(() =>
            {
                var settings = AppSettings.Load(SettingsPath);
                var window = NewWindow(settings);
                Click(Named<Button>(window, "NewPresetButton"));
                var delete = Named<Button>(window, "DeletePresetButton");
                string name = settings.CustomPresets.Single().Name;

                Click(delete);
                Named<RadioButton>(window, "LevelBalanced").IsChecked = true;
                CardNamed(window, name).IsChecked = true;
                Click(delete);

                Assert.Single(settings.CustomPresets);
            });
        }

        [Fact]
        public void The_right_click_menu_is_only_rebuilt_when_presets_come_go_or_get_renamed()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();

                Click(Named<Button>(window, "NewPresetButton"));
                Assert.Equal(1, _menuUpdates);

                Named<TextBox>(window, "TargetSizeBox").Text = "10";
                Assert.Equal(1, _menuUpdates);

                Named<TextBox>(window, "PresetNameBox").Text = "Discord";
                Assert.True(_menuUpdates > 1);

                int beforeDelete = _menuUpdates;
                Click(Named<Button>(window, "DeletePresetButton"));
                Click(Named<Button>(window, "DeletePresetButton"));
                Assert.Equal(beforeDelete + 1, _menuUpdates);
            });
        }

        [Fact]
        public void A_blank_name_shows_as_the_fallback_on_the_card_and_menu_until_one_is_typed()
        {
            OnUiThread(() =>
            {
                var settings = AppSettings.Load(SettingsPath);
                var window = NewWindow(settings);
                Click(Named<Button>(window, "NewPresetButton"));

                Named<TextBox>(window, "PresetNameBox").Text = "   ";

                Assert.Equal("Untitled", settings.CustomPresets.Single().Name);
                Assert.NotNull(CardNamed(window, "Untitled"));
            });
        }

        [Fact]
        public void Right_clicking_a_custom_preset_uses_it_and_fits_under_its_size_limit()
        {
            OnUiThread(() =>
            {
                using (var videos = new TestVideos())
                {
                    var panel = NewWindow();
                    Click(Named<Button>(panel, "NewPresetButton"));
                    Named<TextBox>(panel, "PresetNameBox").Text = "Tiny";
                    Named<TextBox>(panel, "TargetSizeBox").Text = "1";
                    string id = AppSettings.Load(SettingsPath).CustomPresets.Single().Id;

                    string clip = videos.MakeBulkyClip("clip.mov", seconds: 5);
                    Assert.True(new FileInfo(clip).Length > 2000000);

                    // A right-click later starts a new copy of the app, which reads the saved settings.
                    var rightClick = NewWindow(compact: true);
                    rightClick.HandleRequest(new CliArgs { PresetId = id, Files = { clip } });
                    var job = rightClick.Jobs.Single();
                    Assert.Equal(1, job.Settings.TargetSizeMb);
                    Pump(() => job.IsFinished);

                    Assert.True(job.CanShowFile, job.Status);
                    Assert.True(new FileInfo(job.ResultPath).Length <= 1000000, "came out at " + new FileInfo(job.ResultPath).Length);
                }
            });
        }

        [Fact]
        public void A_right_click_on_a_preset_deleted_since_falls_back_to_the_selected_level()
        {
            OnUiThread(() =>
            {
                using (var videos = new TestVideos())
                {
                    string clip = videos.MakeBulkyClip("clip.mov", seconds: 1);
                    var window = NewWindow();

                    window.HandleRequest(new CliArgs { PresetId = "gone", Files = { clip } });

                    var job = window.Jobs.Single();
                    Assert.True(job.Settings.SameAs(Presets.Default(LevelId.Balanced)));
                    Pump(() => job.IsFinished);
                }
            });
        }

        [Fact]
        public void A_queued_video_keeps_its_preset_settings_even_if_the_preset_changes_later()
        {
            OnUiThread(() =>
            {
                using (var videos = new TestVideos())
                {
                    string clip = videos.MakeBulkyClip("clip.mov", seconds: 1);
                    var settings = AppSettings.Load(SettingsPath);
                    var window = NewWindow(settings);
                    Click(Named<Button>(window, "NewPresetButton"));

                    window.AddPaths(new[] { clip }, null);
                    var job = window.Jobs.Single();
                    Named<TextBox>(window, "TargetSizeBox").Text = "7";

                    Assert.Equal(0, job.Settings.TargetSizeMb);
                    Pump(() => job.IsFinished);
                }
            });
        }

        private static T Named<T>(MainWindow window, string name) where T : class
        {
            var found = window.FindName(name) as T;
            Assert.True(found != null, "No element named " + name);
            return found;
        }

        private static void Select(ComboBox box, int tagValue)
        {
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == tagValue);
        }

        private static int SelectedTag(ComboBox box) => (int)((ComboBoxItem)box.SelectedItem).Tag;

        private static void Pump(Func<bool> until, int timeoutMs = 60000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!until())
            {
                Assert.True(DateTime.UtcNow < deadline, "Timed out waiting");
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Background, (s, e) => frame.Continue = false, Dispatcher.CurrentDispatcher);
                Dispatcher.PushFrame(frame);
                timer.Stop();
            }
        }

        // ---- what's on screen ----

        [Fact]
        public void The_full_window_offers_drop_zone_levels_and_advanced()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "MainControls").Visibility);
                Assert.NotNull(Named<Button>(window, "DropZone"));
                Assert.True(window.AllowDrop);
            });
        }

        [Fact]
        public void The_right_click_window_is_just_the_queue()
        {
            OnUiThread(() =>
            {
                var window = NewWindow(compact: true);
                Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(window, "MainControls").Visibility);
            });
        }

        // ---- never taking focus from what the user is doing ----

        [Fact]
        public void The_right_click_window_appears_without_taking_focus()
        {
            OnUiThread(() =>
            {
                var window = NewWindow(compact: true);
                Assert.False(window.ShowActivated);
                Assert.True(window.Topmost, "Without focus it needs this to be seen at all");
            });
        }

        [Fact]
        public void The_full_window_opens_like_any_other_app()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                Assert.True(window.ShowActivated);
                Assert.False(window.Topmost);
            });
        }

        [Fact]
        public void A_video_added_from_elsewhere_stays_on_top_only_briefly()
        {
            OnUiThread(() =>
            {
                using (var videos = new TestVideos())
                {
                    string clip = videos.MakeBulkyClip("clip.mov", seconds: 1);
                    var window = NewWindow();

                    window.HandleRequest(new CliArgs { Level = LevelId.Light, Files = { clip } });

                    Assert.True(window.Topmost);
                    Pump(() => !window.Topmost, 10000);
                    Pump(() => window.Jobs.All(j => j.IsFinished));
                }
            });
        }

        [Fact]
        public void A_video_added_from_elsewhere_leaves_a_minimized_window_minimized()
        {
            OnUiThread(() =>
            {
                using (var videos = new TestVideos())
                {
                    string clip = videos.MakeBulkyClip("clip.mov", seconds: 1);
                    var window = NewWindow();
                    window.WindowState = WindowState.Minimized;

                    window.HandleRequest(new CliArgs { Level = LevelId.Light, Files = { clip } });

                    Assert.Equal(WindowState.Minimized, window.WindowState);
                    Assert.False(window.Topmost);
                    Pump(() => window.Jobs.All(j => j.IsFinished));
                }
            });
        }

        [Fact]
        public void Opening_the_app_on_purpose_restores_a_minimized_window()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                window.WindowState = WindowState.Minimized;
                window.HandleRequest(new CliArgs());
                Assert.Equal(WindowState.Normal, window.WindowState);
            });
        }

        [Fact]
        public void Switching_to_the_full_window_stops_staying_on_top()
        {
            OnUiThread(() =>
            {
                var window = NewWindow(compact: true);
                window.HandleRequest(new CliArgs());
                Assert.False(window.Topmost);
            });
        }

        [Fact]
        public void Opening_the_app_while_compact_switches_to_the_full_window()
        {
            OnUiThread(() =>
            {
                var window = NewWindow(compact: true);
                window.HandleRequest(new CliArgs());
                Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "MainControls").Visibility);
            });
        }

        [Fact]
        public void The_queue_stays_out_of_the_way_until_there_is_something_in_it()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(window, "QueueArea").Visibility);
            });
        }

        [Fact]
        public void Missing_ffmpeg_is_explained_with_a_way_to_fix_it()
        {
            OnUiThread(() =>
            {
                var withTools = NewWindow();
                Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(withTools, "MissingToolsBanner").Visibility);

                var without = new MainWindow(AppSettings.Load(SettingsPath), SettingsPath, null, false, false);
                Assert.Equal(Visibility.Visible, Named<FrameworkElement>(without, "MissingToolsBanner").Visibility);
                Assert.NotNull(Named<Button>(without, "InstallToolsButton"));
            });
        }

        // ---- choosing a level ----

        [Theory]
        [InlineData("LevelLight", "Light", "Full resolution")]
        [InlineData("LevelBalanced", "Balanced", "Up to 1080p")]
        [InlineData("LevelMaximum", "Maximum", "720p, 30 fps")]
        public void Each_level_card_says_what_it_keeps(string card, string title, string caption)
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                var button = Named<RadioButton>(window, card);
                var panel = (StackPanel)button.Content;
                Assert.Equal(new[] { title, caption }, panel.Children.Cast<TextBlock>().Select(t => t.Text));
                Assert.Equal($"{title}: {caption}", System.Windows.Automation.AutomationProperties.GetName(button));
            });
        }

        [Fact]
        public void Balanced_is_selected_on_first_run()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                Assert.True(Named<RadioButton>(window, "LevelBalanced").IsChecked);
                Assert.Contains("1080p", Named<TextBlock>(window, "LevelSummary").Text);
            });
        }

        [Fact]
        public void Choosing_a_level_is_remembered()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                Named<RadioButton>(window, "LevelMaximum").IsChecked = true;
                Assert.Contains("720p", Named<TextBlock>(window, "LevelSummary").Text);

                Assert.Equal(LevelId.Maximum, AppSettings.Load(SettingsPath).SelectedLevel);

                var reopened = NewWindow();
                Assert.True(Named<RadioButton>(reopened, "LevelMaximum").IsChecked);
            });
        }

        // ---- advanced ----

        [Fact]
        public void Advanced_starts_closed_and_opens_on_request()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                var panel = Named<FrameworkElement>(window, "AdvancedPanel");
                Assert.Equal(Visibility.Collapsed, panel.Visibility);

                Named<ToggleButton>(window, "AdvancedToggle").IsChecked = true;
                Assert.Equal(Visibility.Visible, panel.Visibility);
            });
        }

        [Fact]
        public void Advanced_shows_the_settings_of_the_selected_level()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                var resolution = Named<ComboBox>(window, "ResolutionBox");
                Assert.Equal(1080, SelectedTag(resolution));

                Named<RadioButton>(window, "LevelMaximum").IsChecked = true;
                Assert.Equal(720, SelectedTag(resolution));
                Assert.Equal(30, SelectedTag(Named<ComboBox>(window, "FrameRateBox")));
                Assert.Equal(Presets.Default(LevelId.Maximum).Quality, (int)Named<Slider>(window, "QualitySlider").Value);
            });
        }

        [Fact]
        public void Looking_at_other_levels_changes_nothing()
        {
            OnUiThread(() =>
            {
                var settings = AppSettings.Load(SettingsPath);
                var window = NewWindow(settings);
                Named<RadioButton>(window, "LevelMaximum").IsChecked = true;
                Named<RadioButton>(window, "LevelLight").IsChecked = true;
                Named<RadioButton>(window, "LevelBalanced").IsChecked = true;

                foreach (LevelId level in Enum.GetValues(typeof(LevelId)))
                {
                    Assert.True(settings.IsDefault(level), level + " changed");
                }
            });
        }

        [Fact]
        public void Advanced_changes_apply_to_that_level_and_are_saved()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                Select(Named<ComboBox>(window, "ResolutionBox"), 480);
                Select(Named<ComboBox>(window, "FrameRateBox"), 24);
                Select(Named<ComboBox>(window, "AudioBox"), LevelSettings.AudioRemove);
                Select(Named<ComboBox>(window, "EncoderBox"), (int)EncoderChoice.Cpu);
                Named<Slider>(window, "QualitySlider").Value = 33;
                Named<TextBox>(window, "ExtraArgsBox").Text = "-g 120";

                var saved = AppSettings.Load(SettingsPath).Get(LevelId.Balanced);
                Assert.Equal(480, saved.MaxShortSide);
                Assert.Equal(24, saved.MaxFps);
                Assert.Equal(LevelSettings.AudioRemove, saved.AudioKbps);
                Assert.Equal(EncoderChoice.Cpu, saved.Encoder);
                Assert.Equal(33, saved.Quality);
                Assert.Equal("-g 120", saved.ExtraArgs);
                Assert.True(AppSettings.Load(SettingsPath).IsDefault(LevelId.Light));
            });
        }

        [Fact]
        public void Changing_codec_moves_quality_to_the_matching_number_for_that_codec()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                Select(Named<ComboBox>(window, "CodecBox"), (int)VideoCodec.H264);

                var saved = AppSettings.Load(SettingsPath).Get(LevelId.Balanced);
                Assert.Equal(VideoCodec.H264, saved.Codec);
                Assert.Equal(Presets.DefaultQuality(VideoCodec.H264), saved.Quality);
                Assert.Equal(saved.Quality, (int)Named<Slider>(window, "QualitySlider").Value);
            });
        }

        [Theory]
        [InlineData("10", 10)]
        [InlineData("", 0)]
        [InlineData("abc", 0)]
        [InlineData(" 25 ", 25)]
        public void Fit_under_a_size_reads_what_was_typed(string typed, int expected)
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                Named<TextBox>(window, "TargetSizeBox").Text = typed;
                Assert.Equal(expected, AppSettings.Load(SettingsPath).Get(LevelId.Balanced).TargetSizeMb);
            });
        }

        [Fact]
        public void A_size_limit_disables_the_quality_slider_because_it_takes_over()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                var slider = Named<Slider>(window, "QualitySlider");
                Assert.True(slider.IsEnabled);
                Named<TextBox>(window, "TargetSizeBox").Text = "10";
                Assert.False(slider.IsEnabled);
                Named<TextBox>(window, "TargetSizeBox").Text = "";
                Assert.True(slider.IsEnabled);
            });
        }

        [Fact]
        public void Advanced_changes_reach_a_later_right_click_and_only_on_that_level()
        {
            OnUiThread(() =>
            {
                using (var videos = new TestVideos())
                {
                    // Change Balanced in the real panel, as a person would.
                    var panel = NewWindow();
                    Named<RadioButton>(panel, "LevelBalanced").IsChecked = true;
                    Select(Named<ComboBox>(panel, "CodecBox"), (int)VideoCodec.H264);
                    Select(Named<ComboBox>(panel, "ResolutionBox"), 480);
                    Select(Named<ComboBox>(panel, "FrameRateBox"), 24);
                    Select(Named<ComboBox>(panel, "AudioBox"), LevelSettings.AudioRemove);

                    // A right-click later starts a new copy of the app, which reads the saved settings.
                    string clip = videos.MakeBulkyClip("clip.mov", seconds: 2);
                    var rightClick = NewWindow(compact: true);
                    rightClick.HandleRequest(new CliArgs { Level = LevelId.Balanced, Files = { clip } });
                    var job = rightClick.Jobs.Single();
                    Pump(() => job.IsFinished);

                    var result = TestVideos.Probe(job.ResultPath);
                    Assert.Equal("h264", result.Video.Codec);
                    Assert.Equal(480, result.Video.Height);
                    Assert.Equal(854, result.Video.Width);
                    Assert.Equal(24, result.Video.Fps, 1);
                    Assert.Empty(result.Audio);

                    // The other levels never heard about it.
                    var other = NewWindow(compact: true);
                    other.HandleRequest(new CliArgs { Level = LevelId.Light, Files = { clip } });
                    var lightJob = other.Jobs.Single();
                    Assert.True(lightJob.Settings.SameAs(Presets.Default(LevelId.Light)));
                    Pump(() => lightJob.IsFinished);
                }
            });
        }

        [Fact]
        public void A_changed_level_can_be_put_back_to_its_defaults()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                var reset = Named<Button>(window, "ResetButton");
                Assert.Equal(Visibility.Collapsed, reset.Visibility);

                Select(Named<ComboBox>(window, "ResolutionBox"), 480);
                Assert.Equal(Visibility.Visible, reset.Visibility);
                Assert.StartsWith("Custom:", Named<TextBlock>(window, "LevelSummary").Text);

                reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(Visibility.Collapsed, reset.Visibility);
                Assert.Equal(1080, SelectedTag(Named<ComboBox>(window, "ResolutionBox")));
                Assert.True(AppSettings.Load(SettingsPath).IsDefault(LevelId.Balanced));
            });
        }

        [Fact]
        public void The_save_folder_can_be_cleared_back_to_next_to_the_original()
        {
            OnUiThread(() =>
            {
                var settings = AppSettings.Load(SettingsPath);
                settings.OutputFolder = @"D:\out";
                var window = NewWindow(settings);
                Assert.Contains(@"D:\out", Named<TextBlock>(window, "OutputFolderText").Text);

                Named<Button>(window, "OutputFolderClear").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Null(AppSettings.Load(SettingsPath).OutputFolder);
                Assert.Contains("Same folder", Named<TextBlock>(window, "OutputFolderText").Text);
            });
        }

        // ---- the queue ----

        [Fact]
        public void Adding_a_non_video_explains_why_nothing_happened()
        {
            OnUiThread(() =>
            {
                string text = Path.Combine(_folder, "notes.txt");
                File.WriteAllText(text, "x");
                var window = NewWindow();

                window.AddPaths(new[] { text }, null);

                Assert.Empty(window.Jobs);
                Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "DropHint").Visibility);
            });
        }

        [Fact]
        public void A_queued_video_uses_its_own_copy_of_the_settings()
        {
            OnUiThread(() =>
            {
                using (var videos = new TestVideos())
                {
                    string clip = videos.MakeBulkyClip("clip.mov", seconds: 1);
                    var settings = AppSettings.Load(SettingsPath);
                    var window = NewWindow(settings);

                    window.AddPaths(new[] { clip }, LevelId.Light);
                    var job = window.Jobs.Single();
                    Assert.True(job.Settings.SameAs(Presets.Default(LevelId.Light)));

                    settings.Get(LevelId.Light).Quality = 1;
                    Assert.NotEqual(1, job.Settings.Quality);

                    Pump(() => job.IsFinished);
                }
            });
        }

        [Fact]
        public void Videos_run_one_at_a_time_and_finish_with_their_savings_shown()
        {
            OnUiThread(() =>
            {
                using (var videos = new TestVideos())
                {
                    string a = videos.MakeBulkyClip("a.mov", seconds: 1);
                    string b = videos.MakeBulkyClip("b.mov", seconds: 1);
                    var window = NewWindow();

                    window.AddPaths(new[] { a, b }, LevelId.Balanced);

                    Assert.Equal(2, window.Jobs.Count);
                    Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "QueueArea").Visibility);

                    int mostRunning = 0;
                    Pump(() =>
                    {
                        mostRunning = Math.Max(mostRunning, window.Jobs.Count(j => j.IsRunning));
                        return window.Jobs.All(j => j.IsFinished);
                    });

                    Assert.Equal(1, mostRunning);
                    Assert.All(window.Jobs, j => Assert.Contains("smaller", j.Status));
                    Assert.True(File.Exists(videos.PathOf("a_compressed.mp4")));
                    Assert.True(File.Exists(videos.PathOf("b_compressed.mp4")));
                }
            });
        }

        [Fact]
        public void A_right_click_request_joins_the_queue_of_the_open_window()
        {
            OnUiThread(() =>
            {
                using (var videos = new TestVideos())
                {
                    string clip = videos.MakeBulkyClip("clip.mov", seconds: 1);
                    var window = NewWindow();

                    window.HandleRequest(new CliArgs { Level = LevelId.Maximum, Files = { clip } });

                    var job = window.Jobs.Single();
                    Assert.True(job.Settings.SameAs(Presets.Default(LevelId.Maximum)));
                    Pump(() => job.IsFinished);
                }
            });
        }

        // ---- resizing ----

        [Fact]
        public void The_full_window_can_be_resized_but_the_corner_one_stays_small()
        {
            OnUiThread(() =>
            {
                Assert.Equal(ResizeMode.CanResize, NewWindow().ResizeMode);
                Assert.Equal(ResizeMode.CanMinimize, NewWindow(compact: true).ResizeMode);
            });
        }

        [Fact]
        public void Until_someone_resizes_it_the_window_fits_its_contents()
        {
            OnUiThread(() => Assert.Equal(SizeToContent.Height, NewWindow().SizeToContent));
        }

        [Fact]
        public void A_remembered_size_comes_back()
        {
            OnUiThread(() =>
            {
                var window = NewWindow(SettingsWithWindowSize(900, 700));

                Assert.Equal(SizeToContent.Manual, window.SizeToContent);
                Assert.Equal(900, window.Width);
                Assert.Equal(700, window.Height);
            });
        }

        [Fact]
        public void The_corner_window_ignores_a_remembered_size_until_it_opens_fully()
        {
            OnUiThread(() =>
            {
                var window = NewWindow(SettingsWithWindowSize(900, 700), compact: true);
                Assert.Equal(SizeToContent.Height, window.SizeToContent);
                Assert.Equal(480, window.Width);

                window.HandleRequest(new CliArgs());

                Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
                Assert.Equal(900, window.Width);
                Assert.Equal(700, window.Height);
            });
        }

        [Fact]
        public void A_wide_window_moves_advanced_options_to_the_side_and_back()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                var panel = Named<FrameworkElement>(window, "AdvancedPanel");
                var side = Named<ColumnDefinition>(window, "SideColumn");
                Named<ToggleButton>(window, "AdvancedToggle").IsChecked = true;

                window.ArrangeForWidth(1000);
                Assert.Same(Named<Decorator>(window, "SideSlot"), panel.Parent);
                Assert.True(side.Width.Value > 0);

                window.ArrangeForWidth(600);
                Assert.Same(Named<Decorator>(window, "StackedSlot"), panel.Parent);
                Assert.Equal(0, side.Width.Value);
            });
        }

        [Fact]
        public void A_wide_window_only_makes_room_at_the_side_while_advanced_options_are_open()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                var side = Named<ColumnDefinition>(window, "SideColumn");
                var toggle = Named<ToggleButton>(window, "AdvancedToggle");

                window.ArrangeForWidth(1000);
                Assert.Equal(0, side.Width.Value);

                toggle.IsChecked = true;
                Assert.True(side.Width.Value > 0);

                toggle.IsChecked = false;
                Assert.Equal(0, side.Width.Value);
            });
        }

        [Fact]
        public void The_corner_window_never_goes_wide()
        {
            OnUiThread(() =>
            {
                var window = NewWindow(compact: true);
                window.ArrangeForWidth(1000);
                Assert.Same(Named<Decorator>(window, "StackedSlot"), Named<FrameworkElement>(window, "AdvancedPanel").Parent);
            });
        }

        [Fact]
        public void In_a_tall_window_the_queue_uses_the_spare_room()
        {
            OnUiThread(() =>
            {
                var window = NewWindow(SettingsWithWindowSize(480, 1000));
                for (int i = 0; i < 30; i++) window.Jobs.Add(new JobViewModel($@"C:\v\clip {i}.mp4", Presets.Default(LevelId.Light)));
                window.RefreshQueueArea();

                LayOut(window, 480, 1000);

                var queue = Named<ScrollViewer>(window, "QueueScroller");
                var page = Named<ScrollViewer>(window, "Root");
                Assert.True(queue.ActualHeight > 400, $"The queue only got {queue.ActualHeight}, limit {queue.MaxHeight}");
                Assert.True(page.ExtentHeight <= page.ViewportHeight, "The page shouldn't need to scroll when the queue can use the room");
            });
        }

        [Fact]
        public void A_window_fitting_its_contents_keeps_the_queue_at_its_usual_height()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                for (int i = 0; i < 30; i++) window.Jobs.Add(new JobViewModel($@"C:\v\clip {i}.mp4", Presets.Default(LevelId.Light)));
                window.RefreshQueueArea();

                LayOut(window, 480, 1000);

                Assert.Equal(290, Named<ScrollViewer>(window, "QueueScroller").MaxHeight);
            });
        }

        [Fact]
        public void Closing_remembers_a_size_someone_chose()
        {
            OnUiThread(() =>
            {
                var window = NewWindow();
                window.SizeToContent = SizeToContent.Manual;
                window.Width = 1000;
                window.Height = 720;

                window.RememberSize();

                var saved = AppSettings.Load(SettingsPath);
                Assert.Equal(1000, saved.WindowWidth);
                Assert.Equal(720, saved.WindowHeight);
            });
        }

        [Fact]
        public void A_window_left_fitting_its_contents_remembers_nothing()
        {
            OnUiThread(() =>
            {
                NewWindow().RememberSize();
                Assert.Equal(0, AppSettings.Load(SettingsPath).WindowWidth);
            });
        }

        [Fact]
        public void The_corner_window_remembers_nothing()
        {
            OnUiThread(() =>
            {
                var window = NewWindow(compact: true);
                window.SizeToContent = SizeToContent.Manual;

                window.RememberSize();

                Assert.Equal(0, AppSettings.Load(SettingsPath).WindowWidth);
            });
        }

        private AppSettings SettingsWithWindowSize(double width, double height)
        {
            var settings = AppSettings.Load(SettingsPath);
            settings.WindowWidth = width;
            settings.WindowHeight = height;
            return settings;
        }

        private static void LayOut(MainWindow window, double width, double height)
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
        }

        // ---- looks, checked by eye: set FLYWEIGHT_SNAPSHOT_DIR to write pictures of each state ----

        [Fact]
        public void Snapshots_when_asked()
        {
            string dir = Environment.GetEnvironmentVariable("FLYWEIGHT_SNAPSHOT_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);

            OnUiThread(() =>
            {
                foreach (bool dark in new[] { false, true })
                {
                    string theme = dark ? "dark" : "light";

                    var plain = NewWindow(dark: dark);
                    Snapshot(plain, Path.Combine(dir, theme + "-1-start.png"));

                    var advanced = NewWindow(dark: dark);
                    Named<ToggleButton>(advanced, "AdvancedToggle").IsChecked = true;
                    Snapshot(advanced, Path.Combine(dir, theme + "-2-advanced.png"));

                    var queue = NewWindow(dark: dark);
                    AddFakeJobs(queue);
                    Snapshot(queue, Path.Combine(dir, theme + "-3-queue.png"));

                    var compact = NewWindow(compact: true, dark: dark);
                    AddFakeJobs(compact);
                    Snapshot(compact, Path.Combine(dir, theme + "-4-compact.png"));

                    var missing = new MainWindow(AppSettings.Load(SettingsPath), SettingsPath, null, false, dark);
                    Snapshot(missing, Path.Combine(dir, theme + "-5-missing-ffmpeg.png"));

                    var presets = AppSettings.Load(SettingsPath);
                    var discord = presets.AddCustom("Discord", Presets.Default(LevelId.Balanced));
                    discord.Settings.TargetSizeMb = 10;
                    presets.AddCustom("X", Presets.Default(LevelId.Balanced)).Settings.MaxFps = 60;
                    presets.AddCustom("Slack clips", Presets.Default(LevelId.Maximum));
                    presets.AddCustom("Long name that needs trimming", Presets.Default(LevelId.Light));
                    presets.SelectCustom(discord.Id);
                    var withPresets = NewWindow(presets, dark: dark);
                    Named<ToggleButton>(withPresets, "AdvancedToggle").IsChecked = true;
                    Snapshot(withPresets, Path.Combine(dir, theme + "-7-presets.png"));

                    var wide = NewWindow(dark: dark);
                    AddFakeJobs(wide);
                    Named<ToggleButton>(wide, "AdvancedToggle").IsChecked = true;
                    wide.ArrangeForWidth(1000);
                    Snapshot(wide, Path.Combine(dir, theme + "-8-wide.png"), 1000);

                    // For the website. The results shown are real ones, from the table in the README.
                    var main = NewWindow(dark: dark);
                    AddWebsiteJobs(main);
                    Snapshot(main, Path.Combine(dir, "site-app-" + theme + ".png"));

                    var corner = NewWindow(compact: true, dark: dark);
                    AddWebsiteJobs(corner);
                    Snapshot(corner, Path.Combine(dir, "site-corner-" + theme + ".png"));

                    var yours = AppSettings.Load(SettingsPath);
                    var forDiscord = yours.AddCustom("Discord", Presets.Default(LevelId.Balanced));
                    forDiscord.Settings.TargetSizeMb = 10;
                    forDiscord.Settings.Codec = VideoCodec.H264;
                    forDiscord.Settings.Quality = Presets.DefaultQuality(VideoCodec.H264);
                    var forX = yours.AddCustom("X", Presets.Default(LevelId.Balanced));
                    forX.Settings.TargetSizeMb = 512;
                    forX.Settings.Codec = VideoCodec.H264;
                    forX.Settings.Quality = Presets.DefaultQuality(VideoCodec.H264);
                    yours.AddCustom("Slack clips", Presets.Default(LevelId.Maximum));
                    yours.SelectCustom(forDiscord.Id);
                    var presetsWindow = NewWindow(yours, dark: dark);
                    Named<ToggleButton>(presetsWindow, "AdvancedToggle").IsChecked = true;
                    Snapshot(presetsWindow, Path.Combine(dir, "site-presets-" + theme + ".png"));

                    Snapshot(new SetupWindow(@"C:\x.exe", toolsFound: true, dark), Path.Combine(dir, "site-setup-" + theme + ".png"), 440);

                    Snapshot(new SetupWindow(@"C:\x.exe", toolsFound: false, dark), Path.Combine(dir, theme + "-6-setup.png"), 440);
                }
            });
        }

        [Fact]
        public void Setup_warns_when_ffmpeg_is_missing()
        {
            OnUiThread(() =>
            {
                var withTools = new SetupWindow(@"C:\x.exe", toolsFound: true, dark: false);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)withTools.FindName("ToolsNote")).Visibility);

                var without = new SetupWindow(@"C:\x.exe", toolsFound: false, dark: false);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)without.FindName("ToolsNote")).Visibility);
            });
        }

        private static void AddFakeJobs(MainWindow window)
        {
            var settings = Presets.Default(LevelId.Balanced);

            var running = new JobViewModel(@"C:\v\Tournament grand finals.mp4", settings);
            running.MarkStarted();
            running.Apply(new CompressionProgress { Percent = 62, Remaining = TimeSpan.FromSeconds(74) });
            window.Jobs.Add(running);

            var done = new JobViewModel(@"C:\v\Phone video.mp4", settings);
            done.Apply(new CompressionResult { Outcome = JobOutcome.Done, InputBytes = 1142866099, OutputBytes = 214000000, OutputPath = @"C:\v\x.mp4" });
            window.Jobs.Add(done);

            var waiting = new JobViewModel(@"C:\v\Game capture.mp4", settings);
            window.Jobs.Add(waiting);

            var small = new JobViewModel(@"C:\v\already-tiny.mp4", settings);
            small.Apply(new CompressionResult { Outcome = JobOutcome.AlreadySmall, Message = "Already well compressed, so the original was left as is" });
            window.Jobs.Add(small);

            var failed = new JobViewModel(@"C:\v\broken.mkv", settings);
            failed.Apply(new CompressionResult { Outcome = JobOutcome.Failed, Message = "Couldn't compress this video.", Details = "ffmpeg output" });
            window.Jobs.Add(failed);

            window.RefreshQueueArea();
        }

        private static void AddWebsiteJobs(MainWindow window)
        {
            var settings = Presets.Default(LevelId.Balanced);

            var fromX = new JobViewModel(@"C:\v\Clip from X (4K).mp4", settings);
            fromX.Apply(new CompressionResult { Outcome = JobOutcome.Done, InputBytes = 105500000, OutputBytes = 13420000, OutputPath = @"C:\v\x.mp4" });
            window.Jobs.Add(fromX);

            var phone = new JobViewModel(@"C:\v\Phone video (4K60).mp4", settings);
            phone.Apply(new CompressionResult { Outcome = JobOutcome.Done, InputBytes = 1181116006, OutputBytes = 106300000, OutputPath = @"C:\v\x.mp4" });
            window.Jobs.Add(phone);

            var capture = new JobViewModel(@"C:\v\Game capture (1080p120).mp4", settings);
            capture.MarkStarted();
            capture.Apply(new CompressionProgress { Percent = 62, Remaining = TimeSpan.FromSeconds(74) });
            window.Jobs.Add(capture);

            window.RefreshQueueArea();
        }

        private static void Snapshot(Window window, string path, int width = 480)
        {
            var root = (FrameworkElement)window.Content;
            root.Width = width;
            root.Measure(new Size(width, double.PositiveInfinity));
            root.Arrange(new Rect(new Size(width, root.DesiredSize.Height)));
            root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            const double scale = 2;
            var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
        }
    }
}
