using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Flyweight.Core;

namespace Flyweight.UI
{
    /// <summary>
    /// The controls for choosing which part of one video to keep, and which part of its picture. They
    /// sit in the main window where the drop zones usually are, so the levels and Advanced options
    /// underneath stay in reach meanwhile. Nothing is compressed from here: the answer goes back to
    /// the main window, which queues the video like any other.
    /// </summary>
    public partial class TrimPanel : UserControl
    {
        private readonly CancellationTokenSource _closing = new CancellationTokenSource();
        private readonly double _fileSeconds;
        private Rect _screen;

        // Whoever is waiting carries on after the click that answered has finished, not in the middle of it.
        private readonly TaskCompletionSource<bool> _answered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public TrimPanel(string path, MediaInfo media, FFmpegTools tools)
        {
            InitializeComponent();

            FileName = Path.GetFileName(path);
            Selection = new TrimSelection(media.PictureSeconds, media.Video.Fps);
            _fileSeconds = media.DurationSeconds;
            _grabber = new FrameGrabber(tools, path, media);

            // A phone video in a box made for wide ones would be a sliver, so it gets a taller box.
            _isTall = media.Video.DisplayHeight >= media.Video.DisplayWidth * TallFromRatio;
            Screen = SystemParameters.WorkArea;

            Player = new PartPlayer(tools, path, media);

            AutomationProperties.SetName(this, "Trim or crop " + FileName);
            // Getting the keyboard asks for the whole of these controls to be scrolled into view, as it
            // does for anything. On a page scrolled to their buttons, that would move the button out
            // from under the click that gave them the keyboard.
            RequestBringIntoView += (s, e) => e.Handled |= ReferenceEquals(e.TargetObject, this);

            SetUpCropping(media.Video);
            Strip.Attach(Selection);
            Strip.Moved += OnMoved;
            Strip.Picked += PlayFrom;
            ShowTimes();
            ShowLabel(TrimHandle.Start);
            Park(Selection.Start);
            ShowPlayState();

            // No pictures are fetched for controls that aren't on screen, and nothing plays to them.
            Loaded += (s, e) => Start();
            Unloaded += (s, e) => StopPlaying();
        }

        /// <summary>The video's name, so whatever shows these controls can say which one they're for.</summary>
        public string FileName { get; }

        /// <summary>Done once there's an answer: the main button, Cancel, Skip them all, or being closed.</summary>
        public Task Answered => _answered.Task;

        /// <summary>True once the main button was pressed, whatever it said. False if it ended any other way.</summary>
        public bool Confirmed { get; private set; }

        /// <summary>The part to keep, or null to keep the whole video.</summary>
        public TrimRange Range { get; private set; }

        /// <summary>The part of the picture to keep, or null to keep all of it.</summary>
        public CropArea Crop { get; private set; }

        /// <summary>True when the videos still waiting to be asked about should be dropped as well.</summary>
        public bool SkipRest { get; private set; }

        internal TrimSelection Selection { get; }

        /// <summary>
        /// The part of the screen there is to work in. Its height decides how tall a tall video's box
        /// can be, since the levels are underneath it and should still be on screen.
        /// </summary>
        internal Rect Screen
        {
            get => _screen;
            set
            {
                _screen = value;
                if (_isTall) PreviewBox.Height = TallBoxFor(value.Height);
            }
        }

        /// <summary>
        /// Says how many more videos will be asked about after this one, and offers a way out of them
        /// all, for when a whole selection was sent here by mistake.
        /// </summary>
        internal void ShowWaiting(int count)
        {
            WaitingText.Text = count == 1 ? "1 more video after this one." : $"{count} more videos after this one.";
            WaitingRow.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            // With others waiting, plain "Cancel" wouldn't say whether it means this one or all of them.
            CancelButton.Content = count > 0 ? "Skip this one" : "Cancel";
        }

        /// <summary>Takes the controls out of use. If nothing had been answered by now, the answer is no.</summary>
        public void Close()
        {
            StopPlaying();
            // Stops any ffmpeg still fetching a picture nobody will see.
            _closing.Cancel();
            _answered.TrySetResult(Confirmed);
        }

        /// <summary>
        /// Puts the keyboard on these controls, so Enter starts the trim and Escape calls it off, as
        /// they would in a window of its own. On the controls as a whole rather than on "Trim and
        /// compress", where a press of Space meant for whatever was in front a moment ago would count too.
        /// </summary>
        internal void TakeFocus()
        {
            if (IsVisible && !Answered.IsCompleted) Focus();
        }

        private void OnMoved(TrimHandle handle, bool settled)
        {
            // Whatever was playing was a part that's no longer the one being picked.
            StopPlaying();
            // However it was moved, by dragging, typing or stepping.
            ParkFor(handle);
            ShowTimes();
            ShowLabel(handle);
            ShowPlayState();
            Want(handle, exact: settled);
        }

        private void ShowTimes()
        {
            StartBox.Text = Format.Timecode(Selection.Start);
            EndBox.Text = Format.Timecode(Selection.End);

            ShowKeeps();
            ShowWhatConfirmDoes();
            Strip.Refresh();
        }

        private void ShowLabel(TrimHandle handle)
        {
            PreviewLabel.Text = (handle == TrimHandle.Start ? "Start" : "End") + " · " + Format.Timecode(Selection[handle]);
        }

        /// <summary>Moves a handle one frame, or one second, earlier or later.</summary>
        internal void Step(TrimHandle handle, int direction, bool bySecond)
        {
            if (bySecond) Selection.Move(handle, Selection[handle] + direction);
            else Selection.Nudge(handle, direction);
            OnMoved(handle, settled: true);
        }

        private TrimHandle HandleOf(TextBox box) => ReferenceEquals(box, StartBox) ? TrimHandle.Start : TrimHandle.End;

        /// <summary>Uses a typed-in time, or puts back the one that was there if it makes no sense.</summary>
        private void TakeIn(TextBox box)
        {
            var handle = HandleOf(box);
            string current = Format.Timecode(Selection[handle]);
            if (box.Text == current) return;

            if (Format.TryParseTimecode(box.Text, out double seconds))
            {
                Selection.Move(handle, seconds);
                OnMoved(handle, settled: true);
            }
            else
            {
                box.Text = current;
            }
        }

        private void TimeBox_LostFocus(object sender, RoutedEventArgs e)
        {
            TakeIn((TextBox)sender);
        }

        private void TimeBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var box = (TextBox)sender;
            // Asked of the keyboard the press came from, which is the one whose Shift counts.
            bool bySecond = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0;
            switch (e.Key)
            {
                case Key.Enter:
                    // In a time box, Enter means "use this time", not "start compressing".
                    TakeIn(box);
                    box.SelectAll();
                    break;
                case Key.Escape:
                    // And Escape first means "never mind what I typed". Only with nothing typed
                    // is it left to mean the trim as a whole.
                    if (box.Text == Format.Timecode(Selection[HandleOf(box)])) return;
                    box.Text = Format.Timecode(Selection[HandleOf(box)]);
                    box.SelectAll();
                    break;
                case Key.Up:
                    TakeIn(box);
                    Step(HandleOf(box), 1, bySecond);
                    break;
                case Key.Down:
                    TakeIn(box);
                    Step(HandleOf(box), -1, bySecond);
                    break;
                case Key.Space:
                    // A time has no spaces in it, and this is where the keyboard is while a handle
                    // is being stepped into place. A box would otherwise keep the key as typing.
                    if (!e.IsRepeat) PlayOrPause();
                    break;
                case Key.I:
                case Key.O:
                    // Nor letters.
                    if (!e.IsRepeat) MoveHere(e.Key == Key.I ? TrimHandle.Start : TrimHandle.End);
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);

            // A press on a handle leaves the keyboard in a time box, so a time being typed there
            // would never be taken in, and the handle's first move would write the old one over it.
            // Not the buttons on the picture, though: taking a time in moves the line and stops any
            // playing, which is what they go by. They take times in themselves, as their keys do.
            if (!IsInside(PictureButtons, e.OriginalSource))
            {
                foreach (var box in new[] { StartBox, EndBox })
                {
                    if (!IsInside(box, e.OriginalSource)) TakeIn(box);
                }
            }

            // After a visit to the levels or Advanced options, a click back here, on a handle say,
            // gives these controls the keyboard again. A box or button clicked then takes it for itself.
            if (!IsKeyboardFocusWithin) Focus();
        }

        private static bool IsInside(Visual box, object clicked)
        {
            return ReferenceEquals(clicked, box) || (clicked is Visual part && box.IsAncestorOf(part));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            // A key still held down from the last video's answer isn't an answer to this one.
            if (e.Handled || e.IsRepeat) return;

            // Only for keys pressed among these controls. Enter in a box under Advanced options,
            // further down the same window, has nothing to do with starting a trim.
            if (e.Key == Key.Enter) Confirm();
            else if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Space) PlayOrPause();
            else if (e.Key == Key.I) MoveHere(TrimHandle.Start);
            else if (e.Key == Key.O) MoveHere(TrimHandle.End);
            else return;
            e.Handled = true;
        }


        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Confirm();
        }

        private void Confirm()
        {
            if (Answered.IsCompleted) return;

            TakeIn(StartBox);
            TakeIn(EndBox);
            Range = Selection.ToRange(_fileSeconds);
            Crop = IsCropped ? Cropper.Area : null;
            Confirmed = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SkipAll_Click(object sender, RoutedEventArgs e)
        {
            if (!Answered.IsCompleted) SkipRest = true;
            Close();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            if (info.WidthChanged) FitPreviewBox(info.NewSize.Width);
        }

        /// <summary>
        /// A wide video's box is the shape of the video. So with less room across, beside Advanced
        /// options say, it gets shorter rather than putting bars above and below the picture. With
        /// more room it stops at its usual height, and a tall video's box is always the same.
        /// </summary>
        private void FitPreviewBox(double width)
        {
            if (_isTall || width <= 0) return;
            PreviewBox.Height = Math.Min(WidePreviewBox, Math.Round(width * 9 / 16));
        }
    }
}
