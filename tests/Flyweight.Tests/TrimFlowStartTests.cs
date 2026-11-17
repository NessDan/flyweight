using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// "Trim first" from the right-click menu, where the app starts with nothing on screen until the
    /// trim controls are ready, and the cases where a video can't be asked about at all.
    /// </summary>
    [Collection(WindowTests.Name)]
    public class TrimFlowStartTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();
        private readonly List<string> _asked = new List<string>();
        private int _shown;
        private bool _closed;
        private bool _trimWasUpWhenShown;

        public void Dispose() => _videos.Dispose();

        private MainWindow NewWindow(Action<TrimPanel> answer, bool compact = false, bool hidden = false, bool noTools = false)
        {
            return NewWindow(trim => { answer(trim); return Task.CompletedTask; }, compact, hidden, noTools);
        }

        /// <param name="answer">Stands in for someone using the trim controls. It's done when they've answered.</param>
        private MainWindow NewWindow(Func<TrimPanel, Task> answer, bool compact = false, bool hidden = false, bool noTools = false)
        {
            string settingsPath = _videos.PathOf("settings.json");
            MainWindow window = null;
            window = new MainWindow(AppSettings.Load(settingsPath), settingsPath, noTools ? null : TestVideos.Tools, compact, dark: false);
            window.ShowTrim = trim =>
            {
                _asked.Add(window.Title);
                return answer(trim);
            };
            if (hidden)
            {
                window.ShowWhenNeeded = () =>
                {
                    _shown++;
                    _trimWasUpWhenShown = Look.At(window).InSlot != null;
                };
            }
            window.Closed += (s, e) => _closed = true;
            return window;
        }

        private static void PumpBriefly()
        {
            int turns = 0;
            Ui.Pump(() => ++turns > 8);
        }

        [Fact]
        public void A_right_click_trim_request_asks_about_that_video()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                var window = NewWindow(TrimFlowTests.Cancel);

                window.HandleRequest(new CliArgs { Trim = true, Files = { clip } });
                Ui.Pump(() => _asked.Count == 1);

                Assert.Contains("clip.mp4", _asked[0]);
                Assert.Empty(window.Jobs);
                Assert.False(_closed, "A window that was already open stays open");
            });
        }

        [Fact]
        public void Started_for_a_trim_the_whole_window_opens_on_the_trim_controls_then_goes_to_the_corner()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                MainWindow window = null;
                Look whenAsked = null;
                int shownWhenAsked = -1;
                bool takesTheKeyboard = false;
                window = NewWindow(trim =>
                {
                    shownWhenAsked = _shown;
                    takesTheKeyboard = window.ShowActivated && !window.Topmost;
                    whenAsked = Look.At(window);
                    TrimFlowTests.Keep(trim, "1", "2");
                }, compact: true, hidden: true);

                window.TrimFirst(new[] { clip });
                Ui.Pump(() => window.Jobs.Count == 1);

                // The trim controls are in this window, so it has to be up, cards and Advanced options and all.
                Assert.Equal(1, shownWhenAsked);
                Assert.True(_trimWasUpWhenShown, "It should appear with the trim controls in it, not grow them a moment later");
                Assert.True(whenAsked.IsTrimming);
                Assert.Equal(Visibility.Visible, whenAsked.Controls);
                Assert.True(takesTheKeyboard, "It was asked for a moment ago, so unlike a right-click compress it comes forward");

                // With the part chosen it's a right-click job like any other: progress in the corner.
                Ui.Pump(() => !Look.At(window).IsTrimming);
                Assert.Equal(Visibility.Collapsed, Look.At(window).Controls);
                Assert.Equal(ResizeMode.CanMinimize, window.ResizeMode);
                Assert.Equal(480, window.Width);
                Assert.Equal(SizeToContent.Height, window.SizeToContent);
                Assert.Equal(1, _shown);
                Ui.Pump(() => window.Jobs.All(job => job.IsFinished));
                Assert.False(_closed);

                // And like any right-click job that went well, it then starts counting down to closing.
                var note = Ui.Named<TextBlock>(window, "ClosingNote");
                Ui.Pump(() => note.Visibility == Visibility.Visible, 5000);
            });
        }

        [Fact]
        public void Started_for_a_trim_and_cancelled_the_app_closes()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                var window = NewWindow(TrimFlowTests.Cancel, compact: true, hidden: true);

                window.TrimFirst(new[] { clip });
                Ui.Pump(() => _closed);

                Assert.Equal(1, _shown);
                Assert.Empty(window.Jobs);
            });
        }

        [Fact]
        public void A_corner_window_busy_with_other_videos_opens_out_for_a_trim_and_goes_back()
        {
            Ui.Run(() =>
            {
                string quick = _videos.MakeBulkyClip("quick.mov", seconds: 1);
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                MainWindow window = null;
                Look whenAsked = null;
                window = NewWindow(trim =>
                {
                    whenAsked = Look.At(window);
                    TrimFlowTests.Cancel(trim);
                }, compact: true);

                window.AddPaths(new[] { quick }, null);
                window.TrimFirst(new[] { clip });
                Ui.Pump(() => whenAsked != null && !Look.At(window).IsTrimming);

                Assert.Equal(Visibility.Visible, whenAsked.Controls);
                Assert.Equal(Visibility.Collapsed, Look.At(window).Controls);
                Assert.False(_closed, "There's still a queue to show");
                Ui.Pump(() => window.Jobs.All(job => job.IsFinished));
            });
        }

        [Fact]
        public void Opened_on_purpose_while_trimming_it_stays_the_full_window_afterwards()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                MainWindow window = null;
                double narrowestAllowed = 0;
                window = NewWindow(trim =>
                {
                    // As when the app's own shortcut is used while a right-click trim is open.
                    window.HandleRequest(new CliArgs());
                    narrowestAllowed = window.MinWidth;
                    TrimFlowTests.Cancel(trim);
                }, compact: true, hidden: true);

                window.TrimFirst(new[] { clip });
                Ui.Pump(() => _asked.Count == 1 && !Look.At(window).IsTrimming);
                PumpBriefly();

                Assert.Equal(592, narrowestAllowed);   // still trimming at that point, and still needing the room
                Assert.False(_closed, "It was opened on purpose, so it isn't the trim's to close");
                Assert.Equal(Visibility.Visible, Look.At(window).Controls);
            });
        }

        [Fact]
        public void Something_that_is_not_a_video_is_reported_rather_than_asked_about()
        {
            Ui.Run(() =>
            {
                string fake = _videos.PathOf("notes.mp4");
                File.WriteAllText(fake, "not a video");
                var window = NewWindow(TrimFlowTests.Cancel, compact: true, hidden: true);

                window.TrimFirst(new[] { fake });
                Ui.Pump(() => window.Jobs.Count == 1);

                var job = window.Jobs.Single();
                Assert.True(job.Failed);
                Assert.True(job.IsFinished);
                Assert.False(string.IsNullOrWhiteSpace(job.Status));
                Assert.Empty(_asked);
                Assert.Equal(1, _shown);
                Assert.False(_closed, "It has to stay up for the failure to be seen");
            });
        }

        [Fact]
        public void The_queue_carries_on_underneath_and_the_corner_window_does_not_start_closing_while_a_trim_is_being_chosen()
        {
            Ui.Run(() =>
            {
                string quick = _videos.MakeBulkyClip("quick.mov", seconds: 1);
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                MainWindow window = null;
                Visibility? noteWhileAsking = null;
                window = NewWindow(async trim =>
                {
                    // Trimming doesn't hold the rest of the app up, so the queue finishes meanwhile.
                    while (!window.Jobs.All(job => job.IsFinished)) await Task.Delay(30);
                    noteWhileAsking = Ui.Named<TextBlock>(window, "ClosingNote").Visibility;
                    TrimFlowTests.Cancel(trim);
                }, compact: true);

                window.AddPaths(new[] { quick }, null);
                window.TrimFirst(new[] { clip });
                Ui.Pump(() => noteWhileAsking.HasValue);

                Assert.Equal(Visibility.Collapsed, noteWhileAsking.Value);

                // With the trim answered and nothing left to do, it closes itself as it always has.
                var note = Ui.Named<TextBlock>(window, "ClosingNote");
                Ui.Pump(() => note.Visibility == Visibility.Visible, 5000);
            });
        }

        [Fact]
        public void Videos_sent_to_trim_before_ffmpeg_is_installed_are_asked_about_once_it_is()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                var window = NewWindow(TrimFlowTests.Cancel, compact: true, hidden: true, noTools: true);

                window.TrimFirst(new[] { clip });
                PumpBriefly();

                Assert.Empty(_asked);
                Assert.Equal(Visibility.Visible, Ui.Named<FrameworkElement>(window, "MissingToolsBanner").Visibility);
                Assert.Equal(1, _shown);   // or the app would sit there unseen, with no way to install ffmpeg

                window.SetTools(TestVideos.Tools);
                Ui.Pump(() => _asked.Count == 1);
                Assert.Contains("clip.mp4", _asked[0]);
                // Nothing was chosen, so the corner window has nothing to show and the app goes away.
                Ui.Pump(() => _closed, 10000);
            });
        }

        [Fact]
        public void A_drop_that_holds_no_videos_says_so()
        {
            Ui.Run(() =>
            {
                string notes = _videos.PathOf("notes.txt");
                File.WriteAllText(notes, "hello");
                var window = NewWindow(TrimFlowTests.Cancel);

                window.TrimFirst(new[] { notes });
                PumpBriefly();

                Assert.Empty(_asked);
                Assert.Equal(Visibility.Visible, Ui.Named<FrameworkElement>(window, "DropHint").Visibility);
            });
        }

        [Fact]
        public void A_video_that_trips_up_the_trim_controls_is_reported_and_the_rest_still_asked()
        {
            Ui.Run(() =>
            {
                string broken = _videos.MakeBulkyClip("a.mov", seconds: 1);
                string fine = _videos.MakeBulkyClip("b.mov", seconds: 1);
                var window = NewWindow(trim =>
                {
                    if (trim.FileName == "a.mov") throw new InvalidOperationException("boom");
                    TrimFlowTests.Cancel(trim);
                }, compact: true, hidden: true);

                window.TrimFirst(new[] { broken, fine });
                Ui.Pump(() => _asked.Count == 2 && !Look.At(window).IsTrimming);

                var job = window.Jobs.Single();
                Assert.Equal(broken, job.InputPath);
                Assert.True(job.Failed);
                Assert.Contains("boom", job.Details);
                Assert.Equal(1, _shown);
                Assert.False(_closed);
            });
        }

        [Fact]
        public void A_video_that_does_not_say_how_long_it_is_is_reported_rather_than_given_a_dead_timeline()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                var window = NewWindow(TrimFlowTests.Cancel, compact: true, hidden: true);
                // Recordings saved straight from a browser are like this.
                window.ProbeForTrim = file =>
                {
                    var media = TestVideos.Probe(file);
                    media.DurationSeconds = 0;
                    media.Video.EndSeconds = 0;
                    return media;
                };

                window.TrimFirst(new[] { clip });
                Ui.Pump(() => window.Jobs.Count == 1);

                var job = window.Jobs.Single();
                Assert.True(job.Failed);
                Assert.Contains("how long", job.Status);
                Assert.Empty(_asked);
            });
        }

        [Fact]
        public void A_video_that_is_slow_to_open_says_so_instead_of_showing_nothing()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                var opened = new ManualResetEventSlim();
                var window = NewWindow(TrimFlowTests.Cancel, compact: true, hidden: true);
                window.SlowProbeMs = 50;
                // As from a drive that has to wake up, or a file that's still in the cloud.
                window.ProbeForTrim = file =>
                {
                    opened.Wait();
                    return TestVideos.Probe(file);
                };
                var note = Ui.Named<TextBlock>(window, "TrimNote");

                window.TrimFirst(new[] { clip });
                Ui.Pump(() => _shown == 1, 10000);

                Assert.Equal(Visibility.Visible, note.Visibility);
                Assert.Contains("clip.mp4", note.Text);
                Assert.Empty(_asked);

                opened.Set();
                Ui.Pump(() => _asked.Count == 1);
                Assert.Equal(Visibility.Collapsed, note.Visibility);
                // Cancelled, and the corner window has nothing in it, so the app goes away.
                Ui.Pump(() => _closed, 10000);
            });
        }

        [Fact]
        public void Skipping_them_all_drops_the_videos_still_waiting()
        {
            Ui.Run(() =>
            {
                var clips = new[] { "a.mov", "b.mov", "c.mov" }.Select(name => _videos.MakeBulkyClip(name, seconds: 1)).ToArray();
                var window = NewWindow(trim => Ui.Click(Ui.Named<Button>(trim, "SkipAllButton")), compact: true, hidden: true);

                window.TrimFirst(clips);
                Ui.Pump(() => _closed, 20000);

                Assert.Single(_asked);
                Assert.Contains("2 more", _asked[0]);
                Assert.Empty(window.Jobs);
            });
        }

        [Fact]
        public void Closing_the_window_while_trimming_calls_the_trim_off()
        {
            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("clip.mp4");
                TrimPanel open = null;
                var window = NewWindow(trim =>
                {
                    open = trim;
                    // As the real thing does: it waits, and here nobody answers.
                    return trim.Answered;
                });

                window.TrimFirst(new[] { clip });
                Ui.Pump(() => open != null);
                window.Close();

                Assert.True(_closed, "The trim is part of this window, so its X closes both");
                Assert.True(open.Answered.IsCompleted, "Or its pictures would go on being fetched for nobody");
                Assert.False(open.Confirmed);
                PumpBriefly();
                Assert.Empty(window.Jobs);
            });
        }
    }
}
