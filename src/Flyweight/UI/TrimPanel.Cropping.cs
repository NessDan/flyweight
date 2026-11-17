using System;
using System.Windows;
using System.Windows.Media;
using Flyweight.Core;

namespace Flyweight.UI
{
    /// <summary>Cropping among the trim controls: the box over the picture, and what the controls say about it.</summary>
    public partial class TrimPanel
    {
        private int _videoWidth;
        private int _videoHeight;

        private void SetUpCropping(VideoStreamInfo video)
        {
            _videoWidth = video.DisplayWidth;
            _videoHeight = video.DisplayHeight;
            Cropper.Aspect = video.ShownAspect;
            Cropper.VideoWidth = _videoWidth;
            Cropper.VideoHeight = _videoHeight;
            Cropper.Changed += _ => ShowCrop();
            Cropper.ZoomChanged += ShowZoom;
            // A time still being typed is taken in when the button is pressed, so it counts already.
            StartBox.TextChanged += (s, e) => ShowWhatConfirmDoes();
            EndBox.TextChanged += (s, e) => ShowWhatConfirmDoes();
            // On a narrow picture, Play and the time beside it can sit over a bottom corner. A press
            // on a corner is the corner's all the same.
            PreviewBox.PreviewMouseLeftButtonDown += (s, e) => e.Handled |= Cropper.TakeHold(e.GetPosition(Cropper));
        }

        /// <summary>The pixels the box keeps, or null when that's the whole picture. The same as a compress will keep.</summary>
        private (int X, int Y, int Width, int Height)? Kept => Cropper.Area.In(_videoWidth, _videoHeight);

        private bool IsCropped => Kept.HasValue;

        private void ShowCrop()
        {
            var kept = Kept;
            CropSizeText.Text = kept.HasValue ? $"Cropped to {kept.Value.Width} × {kept.Value.Height}" : "";
            CropSizeText.Visibility = kept.HasValue ? Visibility.Visible : Visibility.Collapsed;
            ResetCropButton.Visibility = CropSizeText.Visibility;
            ShowKeeps();
            ShowWhatConfirmDoes();
        }

        /// <summary>How much of the video's length is kept. Beside a crop, all of it isn't "the whole video".</summary>
        private void ShowKeeps()
        {
            string whole = Format.Timecode(Selection.Duration, 1);
            KeepsText.Text = !Selection.IsWholeVideo ? $"Keeps {Format.Timecode(Selection.Length, 1)} of {whole}"
                : IsCropped ? $"Keeps the whole length ({whole})"
                : $"Keeps the whole video ({whole})";
        }

        /// <summary>The main button says what it will do: trim, crop, both, or neither.</summary>
        private void ShowWhatConfirmDoes()
        {
            bool trimmed = WillTrim();
            ConfirmButton.Content = trimmed && IsCropped ? "Trim, crop and compress"
                : trimmed ? "Trim and compress"
                : IsCropped ? "Crop and compress"
                : "Compress";
        }

        /// <summary>Whether pressing the button now would trim, with a time still being typed taken as it will be.</summary>
        private bool WillTrim()
        {
            double start = Format.TryParseTimecode(StartBox.Text, out double typedStart) ? typedStart : Selection.Start;
            double end = Format.TryParseTimecode(EndBox.Text, out double typedEnd) ? typedEnd : Selection.End;
            // To the thousandth, as the times are kept.
            return Math.Round(start, 3) > 0 || Math.Round(end, 3) < Selection.Duration;
        }

        /// <summary>
        /// While a corner of the box is held still, the picture and the box come nearer together around
        /// it. Play and the rest stay as they are.
        /// </summary>
        private void ShowZoom()
        {
            double zoom = Cropper.Zoom;
            var at = Cropper.ZoomedAt;
            PictureLayer.RenderTransform = zoom > 1 ? new ScaleTransform(zoom, zoom, at.X, at.Y) : Transform.Identity;
            if (zoom > 1) Sharpen();
        }

        private void ResetCrop_Click(object sender, RoutedEventArgs e) => Cropper.Reset();
    }
}
