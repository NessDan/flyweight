using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// The trim controls need more room than the drop zones they replace, so the window makes room
    /// while they're up and goes back to how it was afterwards.
    /// </summary>
    [Collection(WindowTests.Name)]
    public class TrimRoomTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        private string SettingsPath => _videos.PathOf("settings.json");

        /// <summary>Trims one video in a window, noting how the window was before, during and after.</summary>
        private (Look Before, Look During, Look After) Trim(
            AppSettings settings, Action<MainWindow> whileTrimming = null, Action<MainWindow> first = null, Action<MainWindow> afterwards = null)
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");
            MainWindow window = null;
            Look during = null;
            window = new MainWindow(settings, SettingsPath, TestVideos.Tools, compact: false, dark: false);
            // A screen of the test's own, so nothing here depends on the one it happens to run on.
            window.WorkArea = () => new Rect(0, 0, 1536, 824);
            first?.Invoke(window);
            window.ShowTrim = trim =>
            {
                during = Look.At(window);
                whileTrimming?.Invoke(window);
                TrimFlowTests.Cancel(trim);
                return Task.CompletedTask;
            };
            var before = Look.At(window);

            window.TrimFirst(new[] { clip });
            Ui.Pump(() => during != null && !Look.At(window).IsTrimming);
            var after = Look.At(window);
            afterwards?.Invoke(window);
            return (before, during, after);
        }

        private AppSettings Widened(double width)
        {
            var settings = AppSettings.Load(SettingsPath);
            settings.WindowWidth = width;
            return settings;
        }

        [Fact]
        public void The_window_makes_room_for_the_trim_controls_then_goes_back_to_how_it_was()
        {
            Ui.Run(() =>
            {
                var seen = Trim(AppSettings.Load(SettingsPath));

                Assert.Equal(480, seen.Before.Width);
                // Wide enough for a picture about 545 across.
                Assert.Equal(592, seen.During.Width);
                Assert.Equal(592, seen.During.MinWidth);
                Assert.Equal(SizeToContent.Height, seen.During.Fit);

                Assert.Equal(480, seen.After.Width);
                Assert.Equal(480, seen.After.MinWidth);
                Assert.Equal(SizeToContent.Height, seen.After.Fit);
            });
        }

        [Fact]
        public void A_window_someone_widened_keeps_its_width_through_the_trim_and_after_it()
        {
            Ui.Run(() =>
            {
                var seen = Trim(Widened(700));

                Assert.Equal(SizeToContent.Height, seen.Before.Fit);
                // No narrower than it was, and as tall as the controls need.
                Assert.Equal(SizeToContent.Height, seen.During.Fit);
                Assert.Equal(700, seen.During.Width);

                Assert.Equal(SizeToContent.Height, seen.After.Fit);
                Assert.Equal(700, seen.After.Width);
            });
        }

        [Fact]
        public void A_window_snapped_to_a_side_of_the_screen_has_its_size_from_Windows_and_is_left_as_it_is()
        {
            Ui.Run(() =>
            {
                var seen = Trim(AppSettings.Load(SettingsPath),
                    first: window =>
                    {
                        window.IsSnapped = () => true;
                        window.SizeToContent = SizeToContent.Manual;
                        window.Width = 700;
                        window.Height = 800;
                    },
                    whileTrimming: window => Advanced(window).IsChecked = true);

                foreach (var look in new[] { seen.During, seen.After })
                {
                    Assert.Equal(700, look.Width);
                    Assert.Equal(800, look.Height);
                    Assert.Equal(seen.Before.MinWidth, look.MinWidth);
                    Assert.Equal(seen.Before.MinHeight, look.MinHeight);
                    Assert.Equal(SizeToContent.Manual, look.Fit);
                }
            });
        }

        [Fact]
        public void A_width_dragged_to_while_trimming_is_kept_as_Advanced_options_are_opened_and_shut()
        {
            Ui.Run(() =>
            {
                double widthOpen = 0, widthShut = 0;

                var seen = Trim(AppSettings.Load(SettingsPath), window =>
                {
                    // Dragged wider for a better look, and let go.
                    window.WindowMessage(0x0231);
                    window.Width = 800;
                    window.WindowMessage(0x0232);

                    Advanced(window).IsChecked = true;
                    widthOpen = window.Width;
                    Advanced(window).IsChecked = false;
                    widthShut = window.Width;
                });

                Assert.Equal(1052, widthOpen);
                // Back to what it was dragged to, not to the least the trim controls need.
                Assert.Equal(800, widthShut);
                // And when the trim is over, to what it was before any of it.
                Assert.Equal(480, seen.After.Width);
            });
        }

        [Fact]
        public void A_window_that_fits_its_contents_is_as_tall_as_the_trim_needs_and_no_taller()
        {
            Ui.Run(() =>
            {
                var seen = Trim(AppSettings.Load(SettingsPath));

                Assert.Equal(seen.Before.MinHeight, seen.During.MinHeight);
            });
        }

        [Fact]
        public void A_size_dragged_to_while_trimming_lasts_only_as_long_as_the_trim()
        {
            Ui.Run(() =>
            {
                var settings = Widened(700);
                double remembered = 0;

                var seen = Trim(settings, window =>
                {
                    // As when someone drags the window bigger for a better look, then closes the app.
                    window.SizeToContent = SizeToContent.Manual;
                    window.Width = 1000;
                    window.Height = 900;
                    window.RememberSize();
                    remembered = settings.WindowWidth;
                });

                Assert.Equal(700, remembered);
                Assert.Equal(700, seen.After.Width);
                Assert.Equal(SizeToContent.Height, seen.After.Fit);
            });
        }

        private static ToggleButton Advanced(MainWindow window) => Ui.Named<ToggleButton>(window, "AdvancedToggle");

        private static bool AdvancedIsBeside(MainWindow window) => Ui.Named<Decorator>(window, "SideSlot").Child != null;

        [Fact]
        public void Opening_Advanced_options_while_trimming_widens_the_window_to_put_them_beside_the_picture()
        {
            Ui.Run(() =>
            {
                double widthOpen = 0, widthShut = 0;
                bool besideWhenOpen = false;

                var seen = Trim(AppSettings.Load(SettingsPath), window =>
                {
                    // Underneath, they'd be off the bottom of most screens, with the picture scrolled away.
                    Advanced(window).IsChecked = true;
                    widthOpen = window.Width;
                    besideWhenOpen = AdvancedIsBeside(window);

                    Advanced(window).IsChecked = false;
                    widthShut = window.Width;
                });

                Assert.Equal(1052, widthOpen);
                Assert.True(besideWhenOpen);
                Assert.Equal(592, widthShut);
                Assert.Equal(480, seen.After.Width);
            });
        }

        [Fact]
        public void A_trim_started_with_Advanced_options_open_starts_out_wide_enough_for_both()
        {
            Ui.Run(() =>
            {
                bool beside = false;

                var seen = Trim(AppSettings.Load(SettingsPath),
                    whileTrimming: window => beside = AdvancedIsBeside(window),
                    first: window => Advanced(window).IsChecked = true);

                Assert.Equal(1052, seen.During.Width);
                Assert.True(beside);
                Assert.Equal(480, seen.After.Width);
            });
        }

        [Fact]
        public void On_a_screen_too_narrow_for_both_Advanced_options_stay_underneath()
        {
            Ui.Run(() =>
            {
                double widthOpen = 0;
                bool besideWhenOpen = true;

                Trim(AppSettings.Load(SettingsPath),
                    first: window => window.WorkArea = () => new Rect(0, 0, 1024, 728),
                    whileTrimming: window =>
                    {
                        Advanced(window).IsChecked = true;
                        widthOpen = window.Width;
                        besideWhenOpen = AdvancedIsBeside(window);
                    });

                Assert.Equal(592, widthOpen);
                Assert.False(besideWhenOpen);
            });
        }

        [Fact]
        public void A_window_already_wide_enough_is_left_at_its_width()
        {
            Ui.Run(() =>
            {
                double widthOpen = 0;

                var seen = Trim(AppSettings.Load(SettingsPath), first: window =>
                {
                    // As someone would have widened it.
                    window.Width = 1200;
                },
                whileTrimming: window =>
                {
                    Advanced(window).IsChecked = true;
                    widthOpen = window.Width;
                });

                Assert.Equal(1200, seen.During.Width);
                Assert.Equal(1200, widthOpen);
                Assert.Equal(1200, seen.After.Width);
                Assert.Equal(SizeToContent.Height, seen.After.Fit);
            });
        }

        [Fact]
        public void Maximized_while_trimming_a_window_that_fits_its_contents_still_goes_back_to_fitting_them()
        {
            Ui.Run(() =>
            {
                var settings = AppSettings.Load(SettingsPath);

                var seen = Trim(settings,
                    whileTrimming: window => window.WindowState = WindowState.Maximized,
                    afterwards: window => window.RememberSize());

                // Still maximized, so these are what it comes back to when it's restored.
                Assert.Equal(480, seen.After.Width);
                Assert.Equal(480, seen.After.MinWidth);
                Assert.Equal(SizeToContent.Height, seen.After.Fit);
                // And the size it borrowed for the trim isn't taken for one somebody chose.
                Assert.Equal(0, settings.WindowWidth);
            });
        }

        [Fact]
        public void Maximized_while_trimming_a_window_someone_widened_still_comes_back_to_that_width()
        {
            Ui.Run(() =>
            {
                var settings = Widened(700);

                var seen = Trim(settings,
                    whileTrimming: window => window.WindowState = WindowState.Maximized,
                    afterwards: window => window.RememberSize());

                Assert.Equal(SizeToContent.Height, seen.After.Fit);
                Assert.Equal(700, seen.After.Width);
                Assert.Equal(700, settings.WindowWidth);
            });
        }

        [Fact]
        public void Still_maximized_when_the_trim_ends_it_is_not_told_to_fit_its_contents_until_it_is_put_back()
        {
            Ui.Run(() =>
            {
                MainWindow shown = null;
                var seen = Trim(AppSettings.Load(SettingsPath), whileTrimming: window =>
                {
                    shown = window;
                    // What happens on screen: maximizing stops it fitting its contents, so that it can fill the screen.
                    window.WindowState = WindowState.Maximized;
                    window.SizeToContent = SizeToContent.Manual;
                });

                // Told to fit them while maximized, it would shrink to a strip across the top of the screen.
                Assert.Equal(SizeToContent.Manual, seen.After.Fit);

                shown.WindowState = WindowState.Normal;
                shown.FitHeight();
                Assert.Equal(SizeToContent.Height, shown.SizeToContent);
                Assert.Equal(480, shown.Width);
            });
        }

        [Fact]
        public void A_window_caught_at_a_size_it_was_only_passing_through_fits_its_contents_once_the_trim_is_over()
        {
            Ui.Run(() =>
            {
                MainWindow shown = null;
                // As when a trim is asked for in the moment between the window being put back from
                // maximized and its going back to fitting what's in it.
                var seen = Trim(AppSettings.Load(SettingsPath), first: window =>
                {
                    shown = window;
                    window.SizeToContent = SizeToContent.Manual;
                    window.Height = 800;
                });

                Assert.Equal(SizeToContent.Height, seen.After.Fit);
                Assert.Equal(SizeToContent.Height, shown.SizeToContent);
            });
        }

        [Fact]
        public void A_maximized_window_has_the_room_already_and_is_left_as_it_is()
        {
            Ui.Run(() =>
            {
                var seen = Trim(AppSettings.Load(SettingsPath),
                    first: window => window.WindowState = WindowState.Maximized,
                    whileTrimming: window => Advanced(window).IsChecked = true);

                foreach (var look in new[] { seen.During, seen.After })
                {
                    Assert.Equal(seen.Before.Width, look.Width);
                    Assert.Equal(seen.Before.MinWidth, look.MinWidth);
                    Assert.Equal(seen.Before.Fit, look.Fit);
                }
            });
        }

        [Fact]
        public void A_window_that_fits_its_contents_is_not_taken_for_one_someone_sized()
        {
            Ui.Run(() =>
            {
                var settings = AppSettings.Load(SettingsPath);

                Trim(settings, window =>
                {
                    window.SizeToContent = SizeToContent.Manual;
                    window.RememberSize();
                });

                Assert.Equal(0, settings.WindowWidth);
            });
        }
    }
}
