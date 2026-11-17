using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flyweight.Core;

namespace Flyweight.UI
{
    /// <summary>
    /// Playing the video in place among the trim controls, to see and hear where a cut falls. The
    /// pictures are drawn into the same box the still ones are shown in, as each one's moment comes.
    /// </summary>
    public partial class TrimPanel
    {
        /// <summary>How far before the end handle the line goes when that handle is moved: enough to hear the way into the cut.</summary>
        private const double EndRunIn = 3;

        /// <summary>A screen with more dots than this to a point gains nothing a moving picture would show.</summary>
        private const double MostDotsToAPoint = 2;

        private static readonly Geometry PlayShape = Geometry.Parse("M0,0 L9,5.5 L0,11 Z");
        private static readonly Geometry PauseShape = Geometry.Parse("M0,0 H3 V11 H0 Z M6,0 H9 V11 H6 Z");

        private WriteableBitmap _moving;
        private double _playingFrom;
        private double? _pausedAt;

        // Where Play starts from, which the line on the strip marks while nothing is playing.
        private double _playhead;

        /// <summary>What does the playing.</summary>
        internal PartPlayer Player { get; }

        private void Play_Click(object sender, RoutedEventArgs e) => PlayOrPause();

        private void StartHere_Click(object sender, RoutedEventArgs e) => MoveHere(TrimHandle.Start);

        private void EndHere_Click(object sender, RoutedEventArgs e) => MoveHere(TrimHandle.End);

        /// <summary>
        /// Starts or ends the part here: on the picture on show while it plays or is held, or else at
        /// the line. What the buttons either side of Play do, and I and O.
        /// </summary>
        internal void MoveHere(TrimHandle handle)
        {
            // A moving picture's moment is known only to a picture, and a frame can be on show for
            // several. The line is a time, which stands for the first frame at or after it.
            double? shown = IsShowingItMove ? ShownSeconds : _pausedAt;
            double line = _playhead;
            // A time still being typed counts first, as it does for Play.
            TakeIn(StartBox);
            TakeIn(EndBox);
            if (shown.HasValue) Selection.MoveOntoShown(handle, shown.Value, Player.FramesASecond);
            else Selection.MoveOnto(handle, line);

            if (handle == TrimHandle.Start && Player.IsPlaying)
            {
                // It plays on, since a start is found while watching. It's the end that says where to stop.
                ShowTimes();
                return;
            }
            OnMoved(handle, settled: true);
        }

        /// <summary>What the button on the picture and the space bar do: play, hold it there, or carry on.</summary>
        internal void PlayOrPause()
        {
            if (Player.IsPlaying)
            {
                // Held by stopping and remembering where it had got to, so that nothing is left
                // running behind a picture that's standing still. Before the first picture has come,
                // a picture it was held on and carried on from is still on show, and it's held there
                // again. Otherwise it just stops.
                bool heldThere = PreviewImage.Source is WriteableBitmap && ShownSeconds == _playingFrom;
                double? at = IsShowingItMove || heldThere ? ShownSeconds : null;
                StopPlaying();
                if (at.HasValue)
                {
                    _pausedAt = at;
                    Park(at.Value);
                    if (Cropper.Zoom > 1) Sharpen();
                }
                ShowPlayState();
                return;
            }

            // A time taken in here counts as moving that handle, which puts the line where that handle wants it.
            TakeIn(StartBox);
            TakeIn(EndBox);
            PlayFrom(_playhead);
        }

        /// <summary>Puts the line on the strip, and so where Play will start from, at a moment in the video.</summary>
        private void Park(double seconds)
        {
            _playhead = seconds;
            Strip.Position = seconds;
        }

        /// <summary>
        /// Where the line goes when a handle is moved. Moving the start puts it on the start. Moving
        /// the end puts it a few seconds before the end, since checking where a part stops doesn't
        /// take watching all of it.
        /// </summary>
        private void ParkFor(TrimHandle handle)
        {
            Park(handle == TrimHandle.End ? Math.Max(Selection.Start, Selection.End - EndRunIn) : Selection.Start);
        }

        /// <summary>Plays from a moment in the video to the end of the part being kept.</summary>
        internal void PlayFrom(double seconds)
        {
            StopPlaying();
            if (_closing.IsCancellationRequested) return;

            PlayNote.Visibility = Visibility.Collapsed;

            // An end handle left at the far right means "to the end", and the sound can run on a
            // little past the last picture.
            double until = Selection.End >= Selection.Duration ? Math.Max(Selection.End, _fileSeconds) : Selection.End;
            _playingFrom = Math.Max(0, Math.Min(seconds, until - Selection.MinLength));
            // Stopped before anything has shown, it's from here that Play starts again.
            _playhead = _playingFrom;

            // Made the size they're shown at, in the screen's own dots, so nothing has to resize them.
            // That's the picture's own size within the box, as a still is, so that if the box changes
            // shape later the picture still sits under the crop box's corners.
            var toDots = ToDots;
            var picture = Cropper.Picture;
            int width = Dots(picture.IsEmpty ? PreviewBox.ActualWidth : picture.Width, toDots.M11);
            int height = Dots(picture.IsEmpty ? PreviewBox.ActualHeight : picture.Height, toDots.M22);

            // Whatever it took to get a still picture of this video, the moving ones need the same.
            Player.Play(_playingFrom, until - _playingFrom, width, height, _grabber.ToneMaps);
            _moving = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
            CompositionTarget.Rendering += ShowWhatIsDue;
            ShowPlayState();
        }

        /// <summary>How many of the screen's own dots there are to a point: M11 across, M22 down.</summary>
        private Matrix ToDots => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;

        /// <summary>A length on screen in the screen's own dots, as an even number, which video needs.</summary>
        internal static int Dots(double points, double dotsToAPoint)
        {
            return Math.Max(2, (int)(points * Math.Min(dotsToAPoint, MostDotsToAPoint) / 2) * 2);
        }

        /// <summary>Runs each time the window is about to draw, for as long as something is playing.</summary>
        private void ShowWhatIsDue(object sender, EventArgs e)
        {
            byte[] picture = Player.NextPicture();
            if (picture != null)
            {
                _moving.WritePixels(new Int32Rect(0, 0, Player.Width, Player.Height), picture, Player.Width * 4, 0);
                if (!ReferenceEquals(PreviewImage.Source, _moving))
                {
                    PreviewImage.Source = _moving;
                    PreviewNote.Visibility = Visibility.Collapsed;
                    // Already the right size, and the careful resizing a still gets would be done again for every one.
                    RenderOptions.SetBitmapScalingMode(PreviewImage, BitmapScalingMode.Linear);
                }
                ShownSeconds = _playingFrom + Player.Seconds;
                Strip.Position = ShownSeconds;
                ShowPlayState();
            }

            if (Player.HasFailed)
            {
                StopPlaying();
                PlayNote.Visibility = Visibility.Visible;
            }
            else if (Player.HasEnded)
            {
                // It stops on the cut: the last frame kept, exactly, as when the end handle is moved.
                // Like any player that has reached the end, the next Play starts the part over.
                _playhead = Selection.Start;
                StopPlaying();
                ShowLabel(TrimHandle.End);
                Want(TrimHandle.End, exact: true);
            }
        }

        /// <summary>
        /// Stops the playing, if there is any, and lets go of anywhere it was being held. Whatever
        /// picture it had got to stays until another is asked for, and the line goes back to where
        /// Play would start.
        /// </summary>
        private void StopPlaying()
        {
            _pausedAt = null;
            if (Player.IsPlaying)
            {
                CompositionTarget.Rendering -= ShowWhatIsDue;
                Player.Stop();
            }
            Strip.Position = _playhead;
            ShowPlayState();
        }

        private bool IsShowingItMove => Player.IsPlaying && ReferenceEquals(PreviewImage.Source, _moving);

        /// <summary>Makes the button on the picture, and the label beside it, say what's going on.</summary>
        private void ShowPlayState()
        {
            PlayIcon.Data = Player.IsPlaying ? PauseShape : PlayShape;
            PlayText.Text = Player.IsPlaying ? "Pause" : "Play";
            AutomationProperties.SetName(PlayButton, PlayText.Text);

            if (IsShowingItMove) PreviewLabel.Text = "Playing · " + Format.Timecode(ShownSeconds ?? 0);
            else if (_pausedAt.HasValue) PreviewLabel.Text = "Paused · " + Format.Timecode(_pausedAt.Value);
        }
    }
}
