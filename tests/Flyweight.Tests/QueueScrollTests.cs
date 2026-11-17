using System;
using System.Windows;
using System.Windows.Controls;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// The queue scrolls inside the page, which scrolls too when the window can't show everything.
    /// The mouse wheel has to work for whichever of them has somewhere to go.
    /// </summary>
    [Collection(WindowTests.Name)]
    public class QueueScrollTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        /// <summary>A main window with this many finished videos, laid out in less height than it needs.</summary>
        private MainWindow ShortWindowWith(int videos, bool advancedOpen = false)
        {
            string settingsPath = _videos.PathOf("settings.json");
            var window = new MainWindow(AppSettings.Load(settingsPath), settingsPath, TestVideos.Tools, compact: false, dark: false);
            Ui.Named<System.Windows.Controls.Primitives.ToggleButton>(window, "AdvancedToggle").IsChecked = advancedOpen;
            for (int i = 0; i < videos; i++)
            {
                var job = new JobViewModel($@"C:\Videos\clip {i}.mp4", Presets.Default(LevelId.Balanced));
                job.Apply(new CompressionResult { Outcome = JobOutcome.Done, OutputPath = $@"C:\Videos\clip {i}_compressed.mp4", InputBytes = 40000000, OutputBytes = 9000000 });
                window.Jobs.Add(job);
            }
            window.RefreshQueueArea();

            var page = Page(window);
            page.Measure(new Size(480, 300));
            page.Arrange(new Rect(0, 0, 480, 300));
            page.UpdateLayout();
            Assert.True(page.ScrollableHeight > 0, "The page should have more than fits in view");
            return window;
        }

        private static ScrollViewer Page(MainWindow window) => Ui.Named<ScrollViewer>(window, "Root");

        private static ScrollViewer Queue(MainWindow window) => Ui.Named<ScrollViewer>(window, "QueueScroller");

        private static void WheelOverTheQueue(MainWindow window, bool down)
        {
            Ui.Wheel(Ui.Named<ItemsControl>(window, "QueueList"), down);
            Page(window).UpdateLayout();
        }

        [Fact]
        public void The_wheel_over_a_queue_with_nothing_to_scroll_scrolls_the_page()
        {
            Ui.Run(() =>
            {
                var window = ShortWindowWith(videos: 2);
                Assert.Equal(0, Queue(window).ScrollableHeight);

                WheelOverTheQueue(window, down: true);
                Assert.True(Page(window).VerticalOffset > 0, "The page stayed where it was");

                WheelOverTheQueue(window, down: false);
                Assert.Equal(0, Page(window).VerticalOffset);
            });
        }

        [Theory]
        [InlineData("DropZone")]
        [InlineData("LevelBalanced")]
        [InlineData("AdvancedToggle")]
        [InlineData("CodecBox")]
        [InlineData("QualitySlider")]
        [InlineData("TargetSizeBox")]
        [InlineData("ExtraArgsBox")]
        public void The_wheel_scrolls_the_page_whatever_else_the_pointer_is_over(string name)
        {
            Ui.Run(() =>
            {
                var window = ShortWindowWith(videos: 0, advancedOpen: true);

                Ui.Wheel(Ui.Named<UIElement>(window, name), down: true);
                Page(window).UpdateLayout();

                Assert.True(Page(window).VerticalOffset > 0, "The page stayed where it was");
            });
        }

        [Fact]
        public void The_wheel_over_a_long_queue_scrolls_the_queue_first_then_the_page()
        {
            Ui.Run(() =>
            {
                var window = ShortWindowWith(videos: 30);
                var page = Page(window);
                var queue = Queue(window);
                Assert.True(queue.ScrollableHeight > 0, "The queue should have more rows than it shows");

                WheelOverTheQueue(window, down: true);
                Assert.True(queue.VerticalOffset > 0, "The queue should have scrolled");
                Assert.Equal(0, page.VerticalOffset);

                // At the end of the queue, the wheel carries on with the page.
                queue.ScrollToEnd();
                page.UpdateLayout();
                WheelOverTheQueue(window, down: true);
                Assert.True(page.VerticalOffset > 0, "The page stayed where it was");

                // And back up: the queue first, then the page once the queue is at its top.
                queue.ScrollToTop();
                page.UpdateLayout();
                WheelOverTheQueue(window, down: false);
                Assert.Equal(0, page.VerticalOffset);
            });
        }
    }
}
