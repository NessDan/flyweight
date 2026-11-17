using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// The window's height on a real screen, where it's Windows that does the resizing and says when
    /// a drag begins and ends. The window is really shown, so these run only on the hidden desktop.
    /// </summary>
    [Collection(WindowTests.Name)]
    public class WindowHeightTests : IDisposable
    {
        private const int DragBegins = 0x0231;
        private const int DragEnds = 0x0232;

        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        private string SettingsPath => _videos.PathOf("settings.json");

        private MainWindow Shown()
        {
            var window = new MainWindow(AppSettings.Load(SettingsPath), SettingsPath, TestVideos.Tools, compact: false, dark: false);
            window.ShowActivated = false;
            window.Show();
            Ui.Pump(() => window.ActualWidth > 0 && window.ActualHeight > 0);
            return window;
        }

        private static void Wait(int ms)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            Ui.Pump(() => DateTime.UtcNow > until);
        }

        private static bool FitsAgain(MainWindow window, double height)
        {
            return window.SizeToContent == SizeToContent.Height && Math.Abs(window.ActualHeight - height) < 2;
        }

        /// <summary>How tall the window really is on screen, in the screen's own dots, whatever WPF has laid out inside it.</summary>
        private static int DotsTall(MainWindow window)
        {
            GetWindowRect(new WindowInteropHelper(window).Handle, out var edges);
            return edges.Bottom - edges.Top;
        }

        /// <summary>Windows giving the window a size in one go, in the screen's own dots.</summary>
        private static void SizedByWindows(MainWindow window, int width, int height)
        {
            SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, 0, 0, width, height, 0x0002 | 0x0004 | 0x0010);
        }

        [OnHiddenDesktopFact]
        public void Maximized_it_really_fills_the_screen_rather_than_a_strip_across_the_top_of_it()
        {
            Ui.Run(() =>
            {
                var window = Shown();
                try
                {
                    int fitted = DotsTall(window);

                    window.WindowState = WindowState.Maximized;
                    Wait(400);

                    Assert.True(DotsTall(window) > fitted + 300, $"Maximized, it was {DotsTall(window)} dots tall on screen, against {fitted} before");
                }
                finally
                {
                    window.Close();
                }
            });
        }

        /// <summary>Half the screen across and all of it down, in its own dots: the size a window snapped to one side is given.</summary>
        private static (int Across, int Tall) HalfTheScreen(MainWindow window)
        {
            var toDots = PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;
            var area = window.WorkArea();
            return ((int)(area.Width * toDots.M11 / 2), (int)(area.Height * toDots.M22));
        }

        [OnHiddenDesktopFact]
        public void Snapped_to_a_side_it_takes_the_height_Windows_gives_it_and_keeps_it()
        {
            Ui.Run(() =>
            {
                var window = Shown();
                try
                {
                    double fitted = window.ActualHeight;
                    var half = HalfTheScreen(window);
                    // Nothing here can really snap a window, so Windows' word that it has is stood in for.
                    window.IsSnapped = () => true;
                    SizedByWindows(window, half.Across, half.Tall);
                    Wait(400);
                    Assert.Equal(half.Tall, DotsTall(window));

                    // Let go of from the keyboard, Windows puts it back to the size it had in one go.
                    window.IsSnapped = () => false;
                    SizedByWindows(window, 600, 700);

                    Ui.Pump(() => FitsAgain(window, fitted), 5000);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void Snapped_from_the_keyboard_it_takes_the_size_it_is_given_before_Windows_will_say_it_is_snapped()
        {
            Ui.Run(() =>
            {
                var window = Shown();
                try
                {
                    var half = HalfTheScreen(window);

                    // With no drag to go by, the new size arrives first. Only afterwards does Windows
                    // answer that the window is snapped.
                    SizedByWindows(window, half.Across, half.Tall);
                    window.IsSnapped = () => true;
                    Wait(400);

                    Assert.Equal(half.Across + " by " + half.Tall, DotsAcross(window) + " by " + DotsTall(window));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void Stretched_from_top_to_bottom_where_it_stands_it_takes_the_height_it_is_given()
        {
            Ui.Run(() =>
            {
                var window = Shown();
                try
                {
                    var half = HalfTheScreen(window);
                    int across = DotsAcross(window);
                    GetWindowRect(new WindowInteropHelper(window).Handle, out var edges);

                    // As when the top edge is double-clicked: the same width, moved to the top of the
                    // screen and made as tall as it. Windows says it's snapped only afterwards.
                    SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, edges.Left, 0, across, half.Tall, 0x0004 | 0x0010);
                    window.IsSnapped = () => true;
                    Wait(400);

                    Assert.Equal(across + " by " + half.Tall, DotsAcross(window) + " by " + DotsTall(window));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void Widened_by_the_app_itself_for_a_trim_it_never_stops_fitting_what_is_in_it()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                var window = Shown();
                int stopped = 0;
                bool asked = false;
                System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Window.SizeToContentProperty, typeof(Window))
                    .AddValueChanged(window, (s, e) => stopped += window.SizeToContent == SizeToContent.Manual ? 1 : 0);
                window.ShowTrim = async trim =>
                {
                    // Wider for the trim, wider again with Advanced options beside it, and back.
                    await System.Threading.Tasks.Task.Delay(300);
                    Ui.Named<ToggleButton>(window, "AdvancedToggle").IsChecked = true;
                    await System.Threading.Tasks.Task.Delay(300);
                    Ui.Named<ToggleButton>(window, "AdvancedToggle").IsChecked = false;
                    await System.Threading.Tasks.Task.Delay(300);
                    asked = true;
                    TrimFlowTests.Cancel(trim);
                };
                try
                {
                    window.TrimFirst(new[] { clip });
                    Ui.Pump(() => asked && !Look.At(window).IsTrimming);
                    Wait(300);

                    // A width the app asks for isn't Windows giving it a size.
                    Assert.Equal(0, stopped);
                    Assert.Equal(SizeToContent.Height, window.SizeToContent);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void Put_back_from_maximized_with_a_long_queue_it_is_never_for_a_moment_sized_to_all_of_it()
        {
            Ui.Run(() =>
            {
                var window = Shown();
                try
                {
                    for (int i = 0; i < 30; i++) window.Jobs.Add(new JobViewModel($@"C:\v\clip {i}.mp4", Presets.Default(LevelId.Light)));
                    window.RefreshQueueArea();
                    Wait(300);
                    var page = Ui.Named<System.Windows.Controls.ScrollViewer>(window, "Root");
                    var queue = Ui.Named<System.Windows.Controls.ScrollViewer>(window, "QueueScroller");
                    string limitsAsItFitted = null;
                    System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Window.SizeToContentProperty, typeof(Window))
                        .AddValueChanged(window, (s, e) =>
                        {
                            if (window.SizeToContent == SizeToContent.Height) limitsAsItFitted = $"page {page.MaxHeight}, queue {queue.MaxHeight}";
                        });

                    window.WindowState = WindowState.Maximized;
                    Wait(400);
                    Assert.True(double.IsInfinity(page.MaxHeight), "Maximized, the page may be as tall as the screen makes it");
                    window.WindowState = WindowState.Normal;
                    Ui.Pump(() => limitsAsItFitted != null, 5000);

                    // The limits that keep it on the screen are back before it's told to fit what's in it.
                    Assert.Equal($"page {window.WorkArea().Height - 64}, queue 290", limitsAsItFitted);
                }
                finally
                {
                    window.Jobs.Clear();
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void A_size_Windows_gives_it_while_it_is_being_dragged_is_taken_until_the_drag_is_over()
        {
            Ui.Run(() =>
            {
                var window = Shown();
                try
                {
                    double fitted = window.ActualHeight;
                    IntPtr handle = new WindowInteropHelper(window).Handle;

                    // As when a window is dragged to the side of the screen and dropped there.
                    var half = HalfTheScreen(window);
                    SendMessage(handle, DragBegins, IntPtr.Zero, IntPtr.Zero);
                    SizedByWindows(window, half.Across, half.Tall);
                    Wait(400);
                    Assert.Equal(half.Tall, DotsTall(window));

                    SendMessage(handle, DragEnds, IntPtr.Zero, IntPtr.Zero);

                    Ui.Pump(() => window.SizeToContent == SizeToContent.Height && window.ActualHeight < fitted + 100, 5000);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void Snapped_to_a_side_it_stays_the_size_Windows_gave_it_through_a_trim_and_after_it()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                var window = Shown();
                string during = null;
                window.ShowTrim = async trim =>
                {
                    await System.Threading.Tasks.Task.Delay(400);
                    during = DotsAcross(window) + " by " + DotsTall(window);
                    TrimFlowTests.Cancel(trim);
                };
                try
                {
                    var half = HalfTheScreen(window);
                    window.IsSnapped = () => true;
                    SizedByWindows(window, half.Across, half.Tall);
                    Wait(300);

                    window.TrimFirst(new[] { clip });
                    Ui.Pump(() => during != null && !Look.At(window).IsTrimming);
                    Wait(300);

                    Assert.Equal(half.Across + " by " + half.Tall, during);
                    Assert.Equal(half.Across + " by " + half.Tall, DotsAcross(window) + " by " + DotsTall(window));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        private static int DotsAcross(MainWindow window)
        {
            GetWindowRect(new WindowInteropHelper(window).Handle, out var edges);
            return edges.Right - edges.Left;
        }

        [OnHiddenDesktopFact]
        public void The_corner_window_dragged_somewhere_else_goes_on_fitting_what_is_in_it()
        {
            Ui.Run(() =>
            {
                var window = new MainWindow(AppSettings.Load(SettingsPath), SettingsPath, TestVideos.Tools, compact: true, dark: false);
                window.Show();
                try
                {
                    Ui.Pump(() => window.ActualHeight > 0);
                    IntPtr handle = new WindowInteropHelper(window).Handle;

                    SendMessage(handle, DragBegins, IntPtr.Zero, IntPtr.Zero);
                    // Moved, and not resized.
                    SetWindowPos(handle, IntPtr.Zero, 200, 150, 0, 0, 0x0001 | 0x0004 | 0x0010);
                    SendMessage(handle, DragEnds, IntPtr.Zero, IntPtr.Zero);
                    Wait(300);

                    // It has a row for every video, and has to go on growing and shrinking with them.
                    Assert.Equal(SizeToContent.Height, window.SizeToContent);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void Maximized_while_trimming_it_fills_the_screen_and_fits_again_once_it_is_put_back()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                var window = Shown();
                int whileTrimming = 0;
                window.ShowTrim = async trim =>
                {
                    await System.Threading.Tasks.Task.Delay(300);
                    window.WindowState = WindowState.Maximized;
                    await System.Threading.Tasks.Task.Delay(400);
                    whileTrimming = DotsTall(window);
                    TrimFlowTests.Cancel(trim);
                };
                try
                {
                    double fitted = window.ActualHeight;
                    int fittedDots = DotsTall(window);

                    window.TrimFirst(new[] { clip });
                    Ui.Pump(() => whileTrimming > 0 && !Look.At(window).IsTrimming);
                    Wait(300);

                    Assert.True(whileTrimming > fittedDots + 300, $"Maximized while trimming, it was {whileTrimming} dots tall on screen");
                    Assert.True(DotsTall(window) > fittedDots + 300, $"Still maximized after the trim, it was {DotsTall(window)} dots tall on screen");

                    window.WindowState = WindowState.Normal;
                    Ui.Pump(() => FitsAgain(window, fitted) && Math.Abs(window.ActualWidth - 480) < 1, 5000);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void Maximized_it_fills_the_screen_and_put_back_it_fits_what_is_in_it_again()
        {
            Ui.Run(() =>
            {
                var window = Shown();
                try
                {
                    double fitted = window.ActualHeight;

                    window.WindowState = WindowState.Maximized;
                    Ui.Pump(() => window.ActualHeight > fitted + 100, 5000);
                    double filling = window.ActualHeight;
                    Wait(400);
                    Assert.Equal(filling, window.ActualHeight);

                    window.WindowState = WindowState.Normal;
                    Ui.Pump(() => FitsAgain(window, fitted), 5000);

                    // And it goes on following what's in it, rather than being stuck at that height.
                    Ui.Named<ToggleButton>(window, "AdvancedToggle").IsChecked = true;
                    Ui.Pump(() => window.ActualHeight > fitted + 100, 5000);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void Resized_by_Windows_in_one_go_it_keeps_the_width_and_fits_its_height_again()
        {
            Ui.Run(() =>
            {
                var window = Shown();
                try
                {
                    double fitted = window.ActualHeight;
                    var toDots = PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;

                    // As when a snapped window is let go of from the keyboard: one resize, and no dragging.
                    SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, 0, 0,
                        (int)Math.Round(700 * toDots.M11), (int)Math.Round(900 * toDots.M22), 0x0002 | 0x0004 | 0x0010);
                    Ui.Pump(() => Math.Abs(window.ActualWidth - 700) < 1, 5000);

                    Ui.Pump(() => FitsAgain(window, fitted), 5000);
                    Assert.InRange(window.ActualWidth, 699, 701);

                    window.RememberSize();
                    Assert.InRange(AppSettings.Load(SettingsPath).WindowWidth, 699, 701);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void An_edge_dragged_down_stays_where_it_is_held_and_fits_again_when_it_is_let_go()
        {
            Ui.Run(() =>
            {
                var window = Shown();
                try
                {
                    double fitted = window.ActualHeight;
                    IntPtr handle = new WindowInteropHelper(window).Handle;

                    SendMessage(handle, DragBegins, IntPtr.Zero, IntPtr.Zero);
                    // What WPF does as soon as an edge moves under the hand.
                    window.SizeToContent = SizeToContent.Manual;
                    window.Height = fitted + 200;
                    Ui.Pump(() => Math.Abs(window.ActualHeight - (fitted + 200)) < 2, 5000);
                    Wait(400);
                    Assert.Equal(fitted + 200, window.ActualHeight, 0);

                    SendMessage(handle, DragEnds, IntPtr.Zero, IntPtr.Zero);

                    Ui.Pump(() => FitsAgain(window, fitted), 5000);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, int flags);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out Edges edges);

        private struct Edges
        {
            public int Left, Top, Right, Bottom;
        }
    }
}
