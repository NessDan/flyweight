using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Flyweight.Core;

namespace Flyweight.UI
{
    /// <summary>
    /// "Trim or crop first": asking which part of a video to keep, and of its picture, before it joins
    /// the queue. The controls for that take the place of the drop zones, so the levels and Advanced
    /// options stay in reach meanwhile, and the video is compressed with whichever card is selected.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The window's width while trimming, which makes the picture about 545 across.</summary>
        private const double TrimWidth = 592;

        /// <summary>
        /// How far past the edge of the screen the window may reach. Its border for resizing is
        /// wider than it looks, so a window that's flush with an edge is already a little over it.
        /// </summary>
        private const double EdgeAllowance = 8;

        /// <summary>The same, with Advanced options beside the trim controls instead of under them.</summary>
        private const double TrimWithAdvancedWidth = TrimWidth + SidePanelWidth;

        private readonly Queue<string> _toTrim = new Queue<string>();
        private FFmpegTools _tools;
        private TrimPanel _openTrim;
        private BeforeTrim _beforeTrim;
        private bool _isSettingWidth;
        private bool _isAsking;
        private bool _isClosed;

        /// <summary>How the window was before the trim controls went up, to go back to when they come down.</summary>
        private sealed class BeforeTrim
        {
            public string Title;
            public bool Compact;

            /// <summary>Whether its size is the app's to change: not for a window that's maximized or snapped, which has its size from Windows.</summary>
            public bool Resized;
            public double Width, Height;

            /// <summary>The least width it goes back to while trimming: what it had, or what it has been dragged to since.</summary>
            public double Floor;

            /// <summary>Whether it was a window in a place of its own, rather than the corner one or not yet shown.</summary>
            public bool HadPlace;

            /// <summary>Where it was before it had to be moved to stay on screen, and where that put it.</summary>
            public Point? CameFrom;
            public Point PutAt;
        }

        /// <summary>
        /// Waits for whoever is at the trim controls to answer. Nothing else is held up meanwhile:
        /// the queue keeps going underneath. Tests answer for themselves instead.
        /// </summary>
        internal Func<TrimPanel, Task> ShowTrim = trim => trim.Answered;

        /// <summary>How a video is looked at before its trim controls go up. Tests slow it down or stand in for it.</summary>
        internal Func<string, MediaInfo> ProbeForTrim;

        /// <summary>How long a video can take to open before the app says that it's working on it.</summary>
        internal int SlowProbeMs = 1500;

        /// <summary>
        /// Set when the app was started only to trim something, with this window not yet on screen:
        /// until the video has been looked at there's nothing for it to show. Called once there is.
        /// </summary>
        public Action ShowWhenNeeded { get; set; }

        /// <summary>
        /// The part of the screen this window has to work in: the screen it's on, less the taskbar.
        /// Tests give it a screen of their own, so that nothing depends on the one they happen to run on.
        /// </summary>
        internal Func<Rect> WorkArea;

        private bool IsTrimming => _beforeTrim != null;


        /// <summary>Asks which part of each video to keep, one video at a time, then queues it.</summary>
        public void TrimFirst(IEnumerable<string> paths)
        {
            var files = VideoFiles.Expand(paths);
            if (files.Count == 0)
            {
                ShowNothingAdded();
                return;
            }

            DropHint.Visibility = Visibility.Collapsed;
            foreach (string file in files) _toTrim.Enqueue(file);
            StopAutoClose();
            ShowHowManyWait();

            // Without ffmpeg there are no trim controls to show yet, only the offer to install it.
            if (_tools == null) Reveal();

            // Not from inside the drop itself, which Explorer waits on.
            Dispatcher.BeginInvoke(new Action(AskAboutTrims));
        }

        internal void TakeDrop(IEnumerable<string> paths, bool onTrimZone)
        {
            // While a video is being trimmed there are no drop zones to choose between, and trimming
            // is what's going on. So anything dropped then waits its turn for the same.
            if (onTrimZone || IsTrimming) TrimFirst(paths);
            else AddPaths(paths, null);
        }

        private async void AskAboutTrims()
        {
            if (_isAsking || _tools == null) return;

            _isAsking = true;
            try
            {
                while (_toTrim.Count > 0 && !_isClosed)
                {
                    string file = _toTrim.Dequeue();
                    try
                    {
                        await AskAbout(file);
                    }
                    catch (CompressionException ex)
                    {
                        AddFailed(file, ex.Message, ex.Details);
                    }
                    catch (Exception ex)
                    {
                        // Whatever went wrong with this video, the others should still be asked about.
                        AddFailed(file, "Something went wrong with this video.", ex.ToString());
                    }
                }
            }
            finally
            {
                // Here rather than after the loop, so that even if something above went badly wrong
                // the window can't be left showing trim controls for nothing, or running unseen.
                _isAsking = false;
                AfterTrimsAnswered();
            }
        }

        private async Task AskAbout(string file)
        {
            var probe = ProbeForTrim ?? _compressor.ProbeVideo;
            var looking = Task.Run(() => probe(file));
            if (await Task.WhenAny(looking, Task.Delay(SlowProbeMs)) != looking)
            {
                // A drive that has to wake up, or a file still in the cloud, can take a while. Without
                // this it would look as though nothing had happened.
                ShowTrimNote($"Opening {Path.GetFileName(file)}…");
                Reveal();
            }

            MediaInfo media;
            try
            {
                media = await looking;
            }
            finally
            {
                ShowTrimNote(null);
            }
            if (_isClosed) return;

            if (media.PictureSeconds <= 0)
            {
                throw new CompressionException("This video doesn't say how long it is, so it can't be trimmed or cropped here. Dropped on the main area, it can still be compressed.");
            }

            var trim = new TrimPanel(file, media, _tools) { Screen = WorkArea() };
            PutUp(trim);
            try
            {
                await ShowTrim(trim);
            }
            finally
            {
                // Answered, it stays where it is until the next video's controls replace it or the
                // drop zones come back, so the window doesn't jump about between videos.
                trim.Close();
                trim.IsEnabled = false;
                _openTrim = null;
            }
            if (_isClosed) return;

            if (trim.SkipRest) _toTrim.Clear();
            if (!trim.Confirmed) return;

            // Whichever card is selected now, with anything that was changed on it while trimming.
            Jobs.Add(new JobViewModel(file, _settings.Selected.Clone(), trim.Range, trim.Crop));
            RefreshQueueArea();
            RunQueue();
        }

        /// <summary>Puts a video's trim controls where the drop zones are, and brings the window to whoever asked.</summary>
        private void PutUp(TrimPanel trim)
        {
            // In place before the window is given room or shown, so that it opens, or grows, once,
            // around the controls it's making room for.
            _openTrim = trim;
            TrimSlot.Child = trim;
            if (!IsTrimming) MakeRoomForTrim();

            ShowHowManyWait();
            // Wherever the page had been scrolled to, for the last video's levels say.
            Root.ScrollToTop();
            Reveal();
            ComeForward();
            Dispatcher.BeginInvoke(new Action(trim.TakeFocus), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Swaps the drop zones for a place to put trim controls, in a window with room for them: the
        /// whole window rather than the corner one, wide enough for the picture, and as tall as the
        /// controls need. All of that goes back afterwards. A window that's maximized or snapped to a
        /// side has the room already, and its size is Windows' to give, so it's left as it is.
        /// </summary>
        private void MakeRoomForTrim()
        {
            bool wasCompact = _isCompact;
            if (wasCompact)
            {
                // Unlike a right-click compress, this was asked for a moment ago and needs an answer,
                // so the window takes the keyboard when it first appears.
                ShowActivated = true;
                if (!IsVisible) WindowStartupLocation = WindowStartupLocation.CenterScreen;
                SetCompact(false);
            }
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

            _beforeTrim = new BeforeTrim
            {
                Title = Title,
                Compact = wasCompact,
                Resized = WindowState == WindowState.Normal && !IsSnapped(),
                Width = Width,
                Floor = Width,
                // Fitting its contents, the height it has is whatever they came to.
                Height = SizeToContent != SizeToContent.Manual && ActualHeight > 0 ? ActualHeight : Height,
                HadPlace = IsVisible && !wasCompact,
            };

            DropZones.Visibility = Visibility.Collapsed;
            TrimSlot.Visibility = Visibility.Visible;
            if (_beforeTrim.Resized)
            {
                SizeToContent = SizeToContent.Height;
                FitTrimWidth();
                // Only once it's that wide. Raised first, the least width would widen the window by
                // itself, which looks no different from Windows giving it a size.
                MinWidth = TrimWidth;
            }
            UpdateHeightLimit();
            MakeRoomForQueue();
        }

        /// <summary>
        /// How wide the window should be while trimming. Under the trim controls, Advanced options
        /// would be off the bottom of most screens, with the picture scrolled away above them. So
        /// while they're open the window is wide enough to put them beside it, if the screen is.
        /// </summary>
        private double WidthForTrim()
        {
            bool beside = AdvancedToggle.IsChecked == true && WorkArea().Width >= TrimWithAdvancedWidth;
            return Math.Max(_beforeTrim.Floor, beside ? TrimWithAdvancedWidth : TrimWidth);
        }

        /// <summary>Gives the window the width trimming needs at the moment, unless its size is in somebody's hands just now.</summary>
        private void FitTrimWidth()
        {
            if (!IsTrimming || !_beforeTrim.Resized || WindowState != WindowState.Normal) return;
            if (SizeToContent == SizeToContent.Manual) return;

            SetOwnWidth(WidthForTrim());
            ArrangeForWidth(Width);
        }

        /// <summary>Puts the drop zones back, and the window back to how it was before trimming.</summary>
        private void TakeDownTrim()
        {
            var before = _beforeTrim;
            if (before == null) return;
            _beforeTrim = null;

            TrimSlot.Child = null;
            TrimSlot.Visibility = Visibility.Collapsed;
            DropZones.Visibility = Visibility.Visible;
            Title = before.Title;

            if (before.Compact)
            {
                // Started from the right-click menu, so from here on it's a right-click job like any
                // other: progress in the corner, or nothing at all if no part was chosen.
                SetCompact(true);
                ArrangeForWidth(Width);
                ShowWithoutTakingFocus();
                return;
            }

            MinWidth = FullWindowMinWidth;
            if (before.Resized)
            {
                // Maximized or minimized since, these still say what it comes back to when it's
                // restored. Left alone, that would be the size it borrowed, kept from then on.
                // Maximized, it isn't told to fit its contents: that would leave it a strip across
                // the top of the screen. It's fitted when it's put back.
                bool away = WindowState != WindowState.Normal;
                if (!away) SizeToContent = SizeToContent.Height;
                SetOwnWidth(before.Width);
                if (away && !double.IsNaN(before.Height)) Height = before.Height;

                // Back where it was, if it had to be moved to stay on screen and is still where that put it.
                if (!away && before.CameFrom.HasValue && IsAt(before.PutAt))
                {
                    Left = before.CameFrom.Value.X;
                    Top = before.CameFrom.Value.Y;
                }
            }
            // Maximized, it's as wide on screen as it was a moment ago, whatever it goes back to.
            if (WindowState == WindowState.Normal) ArrangeForWidth(Width);
            UpdateHeightLimit();
            MakeRoomForQueue();
        }

        /// <summary>
        /// Changes the window's width without that being taken for Windows giving it a size, which
        /// would stop it fitting its contents until Windows had finished with it.
        /// </summary>
        private void SetOwnWidth(double width)
        {
            if (Math.Abs(Width - width) < 0.5) return;

            _isSettingWidth = true;
            try
            {
                Width = width;
            }
            finally
            {
                _isSettingWidth = false;
            }
        }

        /// <summary>
        /// Trimming makes the window bigger where it stands, and it goes on growing: a row in the queue
        /// for each video chosen, a taller picture for a phone video. Any of that can push its far
        /// edges off the screen, so each time its size changes it's moved back on if need be.
        /// </summary>
        private void KeepOnScreen()
        {
            if (!IsTrimming || !IsVisible || WindowState != WindowState.Normal) return;
            // At a size somebody is dragging it to, it isn't growing by itself, and moving it would
            // pull it about under the hand.
            if (SizeToContent == SizeToContent.Manual) return;

            var before = _beforeTrim;
            // Moved by hand since it was last put somewhere: that's now where it belongs.
            if (before.CameFrom.HasValue && !IsAt(before.PutAt)) before.CameFrom = null;

            var area = WorkArea();
            area.Inflate(EdgeAllowance, EdgeAllowance);
            var onScreen = new Point(
                Math.Max(area.Left, Math.Min(Left, area.Right - ActualWidth)),
                Math.Max(area.Top, Math.Min(Top, area.Bottom - ActualHeight)));
            if (IsAt(onScreen)) return;

            // The corner window has no place of its own to go back to, and nor has one that was just opened.
            if (before.HadPlace && !before.CameFrom.HasValue) before.CameFrom = new Point(Left, Top);
            before.PutAt = onScreen;
            Left = onScreen.X;
            Top = onScreen.Y;
        }

        private bool IsAt(Point place) => Math.Abs(Left - place.X) < 1 && Math.Abs(Top - place.Y) < 1;

        /// <summary>
        /// Trimming was asked for a moment ago, so unlike a right-click compress the window comes to
        /// the front. Windows may not hand the keyboard to an app that was in the background, but
        /// going on top and straight off again at least leaves it above whatever is there.
        /// </summary>
        private void ComeForward()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            StopStayingOnTop();
            Topmost = true;
            Topmost = false;
            Activate();
        }

        /// <summary>Says in the title which video the trim controls are for, and how many more are waiting.</summary>
        private void ShowHowManyWait()
        {
            if (_openTrim == null) return;

            int waiting = _toTrim.Count;
            _openTrim.ShowWaiting(waiting);
            string more = waiting > 0 ? $" · {waiting} more after this" : "";
            Title = $"{_beforeTrim.Title} · trimming {_openTrim.FileName}{more}";
        }

        /// <summary>A row for a video that couldn't even be looked at, so it isn't silently dropped.</summary>
        private void AddFailed(string file, string message, string details)
        {
            var job = new JobViewModel(file, _settings.Selected.Clone());
            job.Apply(new CompressionResult { Outcome = JobOutcome.Failed, Message = message, Details = details });
            Jobs.Add(job);
            RefreshQueueArea();
            Reveal();
        }

        private void ShowTrimNote(string text)
        {
            TrimNote.Text = text ?? "";
            TrimNote.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void AfterTrimsAnswered()
        {
            if (_isClosed) return;

            TakeDownTrim();
            if (_isCompact && Jobs.Count == 0)
            {
                // The corner window is only there to show progress. Started just to trim, with nothing
                // chosen, there's none to show and nothing to stay open for.
                Close();
            }
            else if (!_isQueueRunning)
            {
                OnQueueStopped();
            }
        }

        /// <summary>Puts the window on screen if it has been waiting for a reason to be there.</summary>
        private void Reveal()
        {
            var show = ShowWhenNeeded;
            ShowWhenNeeded = null;
            show?.Invoke();
        }

        private void ShowNothingAdded()
        {
            if (Jobs.Count == 0) SetCompact(false);
            DropHint.Visibility = Visibility.Visible;
            Reveal();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Nothing more to ask once the app is going away, and no more pictures to fetch.
            _isClosed = true;
            _toTrim.Clear();
            _openTrim?.Close();
            base.OnClosed(e);
        }
    }
}
