using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// Where a drop lands, and how the window sizes and places itself, are things only a window that
    /// has really been laid out can answer. So these show the window, and for that reason run only on
    /// the hidden desktop the tests are given.
    /// </summary>
    [Collection(WindowTests.Name)]
    public class TrimDropTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        [OnHiddenDesktopFact]
        public void A_drop_goes_to_whichever_target_it_lands_on()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeBulkyClip("clip.mov", seconds: 1);
                string settingsPath = _videos.PathOf("settings.json");
                var asked = new List<string>();
                var window = new MainWindow(AppSettings.Load(settingsPath), settingsPath, TestVideos.Tools, compact: false, dark: false);
                window.ShowTrim = trim =>
                {
                    asked.Add(trim.FileName);
                    TrimFlowTests.Cancel(trim);
                    return Task.CompletedTask;
                };
                window.ShowActivated = false;
                window.Show();
                try
                {
                    var trimZone = Ui.Named<Button>(window, "TrimZone");
                    var dropZone = Ui.Named<Button>(window, "DropZone");
                    Ui.Pump(() => trimZone.IsVisible && trimZone.ActualWidth > 0);

                    // Hovering marks the target the drop would go to, and only that one.
                    trimZone.RaiseEvent(DragOver(DragDrop.PreviewDragOverEvent, trimZone, clip));
                    Assert.Equal("over", trimZone.Tag);
                    Assert.Null(dropZone.Tag);
                    dropZone.RaiseEvent(DragOver(DragDrop.PreviewDragOverEvent, dropZone, clip));
                    Assert.Equal("over", dropZone.Tag);
                    Assert.Null(trimZone.Tag);

                    trimZone.RaiseEvent(DragOver(DragDrop.PreviewDropEvent, trimZone, clip));
                    Ui.Pump(() => asked.Count == 1);
                    Assert.Empty(window.Jobs);
                    Assert.Null(trimZone.Tag);

                    // Anywhere else in the window compresses, as it always has.
                    var card = Ui.Named<RadioButton>(window, "LevelLight");
                    card.RaiseEvent(DragOver(DragDrop.PreviewDropEvent, card, clip));
                    Assert.Single(window.Jobs);
                    Assert.Null(window.Jobs[0].Trim);
                    Assert.Single(asked);
                }
                finally
                {
                    // Closing with a job still running would stop to ask about it.
                    Ui.Pump(() => window.Jobs.All(job => job.IsFinished));
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void On_screen_the_window_grows_to_show_all_of_the_trim_controls_then_shrinks_back()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                string settingsPath = _videos.PathOf("settings.json");
                var window = new MainWindow(AppSettings.Load(settingsPath), settingsPath, TestVideos.Tools, compact: false, dark: false);
                double widthDuring = 0, heightDuring = 0, bottomOfControls = 0, widthWithAdvanced = 0;
                var fitDuring = SizeToContent.Manual;
                var advancedAt = new Point();
                TrimPanel asked = null;
                object hadTheKeyboard = null, hadItAfterAClick = null, hadItMeanwhile = null;
                window.ShowTrim = async trim =>
                {
                    // Long enough for the window to have rearranged itself on screen.
                    await Task.Delay(400);
                    asked = trim;
                    hadTheKeyboard = FocusManager.GetFocusedElement(window);

                    // A visit to the rest of the window takes the keyboard there. A click back among the
                    // trim controls, on a handle say, brings it back, so Enter means the trim again.
                    var elsewhere = Ui.Named<ToggleButton>(window, "AdvancedToggle");
                    elsewhere.Focus();
                    hadItMeanwhile = FocusManager.GetFocusedElement(window);
                    Ui.Named<TrimStrip>(trim, "Strip").RaiseEvent(
                        new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                    hadItAfterAClick = FocusManager.GetFocusedElement(window);
                    widthDuring = window.ActualWidth;
                    heightDuring = window.ActualHeight;
                    fitDuring = window.SizeToContent;
                    var last = Ui.Named<Button>(trim, "ConfirmButton");
                    bottomOfControls = last.TranslatePoint(new Point(0, last.ActualHeight), window).Y;

                    var toggle = Ui.Named<ToggleButton>(window, "AdvancedToggle");
                    toggle.IsChecked = true;
                    await Task.Delay(400);
                    widthWithAdvanced = window.ActualWidth;
                    advancedAt = Ui.Named<FrameworkElement>(window, "AdvancedPanel").TranslatePoint(new Point(0, 0), window);
                    toggle.IsChecked = false;
                    await Task.Delay(400);

                    TrimFlowTests.Cancel(trim);
                };
                window.ShowActivated = false;
                window.Show();
                try
                {
                    Ui.Pump(() => window.ActualWidth > 0 && window.ActualHeight > 0);
                    double widthBefore = window.ActualWidth, heightBefore = window.ActualHeight;

                    window.TrimFirst(new[] { clip });
                    Ui.Pump(() => widthDuring > 0 && !Look.At(window).IsTrimming);

                    Assert.InRange(widthBefore, 479, 481);
                    Assert.InRange(widthDuring, 591, 593);
                    Assert.Equal(SizeToContent.Height, fitDuring);
                    Assert.True(heightDuring > heightBefore + 200, $"Room for the picture and the strip: {heightBefore} then {heightDuring}");
                    Assert.True(bottomOfControls > 300 && bottomOfControls < heightDuring,
                        $"Trim and compress has to be in view: its bottom is at {bottomOfControls} in a window {heightDuring} tall");

                    // Enter starts the trim and Escape calls it off, as in a window of its own. That's the
                    // controls as a whole, not a button, where a stray press of Space would count as well.
                    Assert.Same(asked, hadTheKeyboard);
                    Assert.IsType<ToggleButton>(hadItMeanwhile);
                    Assert.Same(asked, hadItAfterAClick);

                    // Advanced options went beside the picture, level with it, not off the bottom of the screen.
                    Assert.InRange(widthWithAdvanced, 1051, 1053);
                    Assert.True(advancedAt.X > 500 && advancedAt.Y < 120, $"Advanced options were at {advancedAt}");

                    // Back to what it was, and still fitting its contents rather than stuck at a size nobody chose.
                    Ui.Pump(() => Math.Abs(window.ActualWidth - widthBefore) < 1 && Math.Abs(window.ActualHeight - heightBefore) < 1, 5000);
                    Assert.Equal(SizeToContent.Height, window.SizeToContent);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        /// <summary>A window showing where the test put it, on a screen whose edges the test chose.</summary>
        private MainWindow ShownAt(double left, double top, Rect screen)
        {
            string settingsPath = _videos.PathOf("settings.json");
            var window = new MainWindow(AppSettings.Load(settingsPath), settingsPath, TestVideos.Tools, compact: false, dark: false);
            window.WorkArea = () => screen;
            window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = top;
            window.Show();
            Ui.Pump(() => window.ActualWidth > 0 && window.ActualHeight > 0);
            return window;
        }

        [OnHiddenDesktopFact]
        public void Growing_for_a_trim_the_window_stays_on_its_screen_then_goes_back_where_it_was()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                // Smaller than any real screen and away from its corner, so the edges are the test's own.
                var screen = new Rect(100, 50, 1200, 720);
                var window = ShownAt(700, 380, screen);
                Rect during = Rect.Empty, withAdvanced = Rect.Empty;
                window.ShowTrim = async trim =>
                {
                    await Task.Delay(400);
                    during = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
                    Ui.Named<ToggleButton>(window, "AdvancedToggle").IsChecked = true;
                    await Task.Delay(400);
                    withAdvanced = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
                    Ui.Named<ToggleButton>(window, "AdvancedToggle").IsChecked = false;
                    await Task.Delay(400);
                    TrimFlowTests.Cancel(trim);
                };
                try
                {
                    window.TrimFirst(new[] { clip });
                    Ui.Pump(() => !withAdvanced.IsEmpty && !Look.At(window).IsTrimming);

                    // Where it stood, it would have reached 300 past the bottom of this screen.
                    Assert.True(during.Height > 600, "It should have grown: " + during);
                    Assert.True(during.Bottom <= screen.Bottom + 9 && during.Top >= screen.Top - 9, "Off the screen top or bottom: " + during);
                    Assert.Equal(700, during.Left, 0);

                    // Wider for Advanced options, it would have reached 450 past the right edge.
                    Assert.True(withAdvanced.Width > 1000, "It should have widened: " + withAdvanced);
                    Assert.True(withAdvanced.Right <= screen.Right + 9 && withAdvanced.Left >= screen.Left - 9, "Off the screen's sides: " + withAdvanced);

                    Ui.Pump(() => Math.Abs(window.Left - 700) < 1 && Math.Abs(window.Top - 380) < 1, 5000);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void Each_videos_trim_controls_start_with_the_picture_in_view()
        {
            Ui.Run(() =>
            {
                string first = _videos.MakeBulkyClip("a.mov", seconds: 1);
                string second = _videos.MakeBulkyClip("b.mov", seconds: 1);
                // Too short for the trim controls and the levels together, so the page scrolls.
                var window = ShownAt(120, 60, new Rect(100, 50, 1200, 600));
                var page = Ui.Named<ScrollViewer>(window, "Root");
                double scrolledDuringTheFirst = -1, offsetForTheSecond = -1;
                int asked = 0;
                window.ShowTrim = async trim =>
                {
                    asked++;
                    await Task.Delay(400);
                    if (asked == 1)
                    {
                        // As someone would to reach the levels.
                        page.ScrollToEnd();
                        await Task.Delay(200);
                        scrolledDuringTheFirst = page.VerticalOffset;
                    }
                    else
                    {
                        offsetForTheSecond = page.VerticalOffset;
                    }
                    TrimFlowTests.Cancel(trim);
                };
                try
                {
                    window.TrimFirst(new[] { first, second });
                    Ui.Pump(() => asked == 2 && offsetForTheSecond >= 0 && !Look.At(window).IsTrimming);

                    Assert.True(scrolledDuringTheFirst > 0, "The page should have had somewhere to scroll to");
                    Assert.Equal(0, offsetForTheSecond);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void A_window_sized_by_hand_while_trimming_is_not_moved_about_under_the_hand()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                var screen = new Rect(100, 50, 1200, 720);
                var window = ShownAt(700, 380, screen);
                double topBefore = -1, topAfter = -1, bottomAfter = -1;
                window.ShowTrim = async trim =>
                {
                    await Task.Delay(400);
                    topBefore = window.Top;
                    // As when its bottom edge is dragged down past the end of the screen, and held there.
                    window.WindowMessage(0x0231);
                    window.SizeToContent = SizeToContent.Manual;
                    window.Height = window.ActualHeight + 80;
                    await Task.Delay(300);
                    topAfter = window.Top;
                    bottomAfter = window.Top + window.ActualHeight;
                    window.WindowMessage(0x0232);
                    TrimFlowTests.Cancel(trim);
                };
                try
                {
                    window.TrimFirst(new[] { clip });
                    Ui.Pump(() => topAfter >= 0 && !Look.At(window).IsTrimming);

                    Assert.True(bottomAfter > screen.Bottom + 20, "It should have been dragged past the bottom: " + bottomAfter);
                    Assert.Equal(topBefore, topAfter, 0);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [OnHiddenDesktopFact]
        public void A_window_moved_by_hand_while_trimming_stays_where_it_was_put()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                var window = ShownAt(700, 380, new Rect(100, 50, 1200, 720));
                bool moved = false;
                var seen = new List<string>();
                void Note(string when) => seen.Add($"{when}: {window.Left:0.#},{window.Top:0.#} {window.ActualWidth:0.#}x{window.ActualHeight:0.#}");
                Note("shown");
                window.ShowTrim = async trim =>
                {
                    await Task.Delay(400);
                    Note("trimming");
                    // As when someone drags it somewhere they like better.
                    window.Left = 300;
                    Note("left set");
                    window.Top = 60;
                    Note("top set");
                    moved = true;
                    await Task.Delay(200);
                    Note("a moment later");
                    TrimFlowTests.Cancel(trim);
                };
                try
                {
                    window.TrimFirst(new[] { clip });
                    Ui.Pump(() => moved && !Look.At(window).IsTrimming);
                    Note("trim down");
                    int turns = 0;
                    Ui.Pump(() => ++turns > 10);
                    Note("settled");

                    Assert.True(Math.Abs(window.Left - 300) < 1 && Math.Abs(window.Top - 60) < 1, string.Join(" | ", seen));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        /// <summary>What WPF hands a window when files are dragged over the middle of an element.</summary>
        private static DragEventArgs DragOver(RoutedEvent routedEvent, FrameworkElement over, params string[] files)
        {
            var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var middle = new Point(over.ActualWidth / 2, over.ActualHeight / 2);
            var args = (DragEventArgs)constructor.Invoke(new object[]
            {
                new DataObject(DataFormats.FileDrop, files), DragDropKeyStates.None, DragDropEffects.Copy, over, middle,
            });
            args.RoutedEvent = routedEvent;
            return args;
        }
    }
}
