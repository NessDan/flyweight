using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flyweight.Core;

namespace Flyweight.UI
{
    /// <summary>The picture side of the trim controls: the preview, and the strip of pictures under it.</summary>
    public partial class TrimPanel
    {
        private const int PreviewHeight = 540;
        private const int TallPreviewHeight = 720;
        private const double WidePreviewBox = 315;
        private const double TallPreviewBox = 440;
        private const double TallFromRatio = 1.2;

        /// <summary>
        /// What the rest of the window needs under a tall video's preview: the strip, the times, the
        /// buttons, and the levels with a row of presets of your own.
        /// </summary>
        private const double RoomUnderTallPreview = 430;
        private const int StripHeight = 72;
        private const int StripAtOnce = 4;
        private const int LooksBack = 5;

        /// <summary>Up to this length, the strip is redone with the exact frame for each spot.</summary>
        private const double ExactStripUpToSeconds = 120;

        private readonly FrameGrabber _grabber;
        private readonly bool _isTall;
        private bool _started;
        private bool _fetching;
        private Request _wanted;
        private CancellationTokenSource _exactFetch;

        // The newest picture asked for: the one on show, or soon to be.
        private Request _asked;

        private sealed class Request
        {
            public double Seconds;
            public bool Exact;

            /// <summary>For the end handle: the end itself, which the picture has to come from just before.</summary>
            public double? Before;

            /// <summary>How many rows to fetch it with, when that's more than usual.</summary>
            public int Height;

            /// <summary>Where the playing was held, for the picture it was held on. Only that holding wants it.</summary>
            public double? PausedAt;
        }

        private static double TallBoxFor(double screenHeight)
        {
            return Math.Max(WidePreviewBox, Math.Min(TallPreviewBox, screenHeight - RoomUnderTallPreview));
        }

        /// <summary>The moment in the video that the picture now on show was taken from.</summary>
        internal double? ShownSeconds { get; private set; }

        /// <summary>Starts fetching pictures. Until then the controls only keep track of times.</summary>
        internal void Start()
        {
            if (_started) return;
            _started = true;
            Want(TrimHandle.Start, exact: true);
            LoadStrip();
        }

        /// <summary>
        /// Asks for the picture a handle now stands for. While it's being dragged that's a quick one, the
        /// keyframe before, so the picture keeps up. Once it rests, it's the exact frame.
        /// </summary>
        private void Want(TrimHandle handle, bool exact)
        {
            Ask(new Request
            {
                Seconds = Selection.PreviewTime(handle),
                Exact = exact,
                Before = handle == TrimHandle.End ? Selection.End : (double?)null,
            });
        }

        private void Ask(Request request)
        {
            if (!_started || _closing.IsCancellationRequested) return;

            // Zoomed in on a corner of the crop box, as much of the picture as the screen can show there.
            if (request.Exact && Cropper.Zoom > 1) request.Height = Dots(Cropper.Picture.Height * Cropper.Zoom, ToDots.M22);

            // Only the newest request matters. An exact picture can take a second or two, which isn't
            // worth waiting out for somewhere the handle has already left.
            _wanted = _asked = request;
            _exactFetch?.Cancel();
            if (!_fetching) FetchPreviews();
        }

        /// <summary>
        /// Zoomed in on a corner, the picture on show is fetched again with more of the video's own
        /// pixels, so it stays sharp. Moving pictures are made the size they're shown, so not while playing.
        /// </summary>
        private void Sharpen()
        {
            if (Player.IsPlaying) return;
            if (_pausedAt.HasValue) Ask(new Request { Seconds = Selection.FrameShown(_pausedAt.Value, Player.FramesASecond), Exact = true, PausedAt = _pausedAt });
            else if (_asked != null) Ask(new Request { Seconds = _asked.Seconds, Exact = true, Before = _asked.Before });
        }

        private async void FetchPreviews()
        {
            _fetching = true;
            try
            {
                while (_wanted != null && !_closing.IsCancellationRequested)
                {
                    var request = _wanted;
                    _wanted = null;

                    using (var fetch = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token))
                    {
                        _exactFetch = request.Exact ? fetch : null;
                        try
                        {
                            var fetched = await Fetch(request, fetch.Token);
                            Show(fetched.Picture, fetched.Seconds, request.PausedAt);
                        }
                        catch (OperationCanceledException)
                        {
                            // Overtaken by a newer request, or the controls were closed.
                        }
                        catch (TimeoutException)
                        {
                            // Whatever is on show stays. Asking again would only take as long again.
                            Show(null, request.Seconds, request.PausedAt);
                        }
                        finally
                        {
                            _exactFetch = null;
                        }
                    }
                }
            }
            finally
            {
                _fetching = false;
            }
        }

        private async Task<(ImageSource Picture, double Seconds)> Fetch(Request request, CancellationToken token)
        {
            // Fetched at about the size it's shown, and at half that while dragging, when speed matters more.
            int full = _isTall ? TallPreviewHeight : PreviewHeight;
            int height = request.Exact ? Math.Max(full, request.Height) : full / 2;
            double seconds = request.Seconds;
            double step = Selection.FrameSeconds;

            if (request.Exact && request.Before.HasValue)
            {
                // The last frame kept is the last one before the end. Where that is comes from the file,
                // because going back by an average frame is wrong wherever the frames aren't evenly spaced.
                double end = request.Before.Value;
                double? last = await Task.Run(() => _grabber.LastFrameBefore(end, token), token);
                if (last.HasValue) seconds = last.Value;
            }

            for (int tries = 0; ; tries++)
            {
                double at = seconds;
                var picture = await Task.Run(() => Decode(_grabber.Grab(at, height, request.Exact, token)), token);
                if (picture != null || !request.Exact || tries == LooksBack || seconds <= 0) return (picture, at);

                // A video's pictures can stop a little before the file says it ends. With no frame
                // here, look further and further back for the last one there is.
                seconds = Math.Max(0, seconds - step);
                step *= 2;
            }
        }

        private void Show(ImageSource picture, double seconds, double? pausedAt)
        {
            // A still that turns up while the video is playing, or held part way, would cut in on the
            // moving picture. Unless it's the picture it's held on, fetched again to zoom in on.
            if (_closing.IsCancellationRequested || Player.IsPlaying || _pausedAt != pausedAt) return;

            if (picture != null)
            {
                PreviewImage.Source = picture;
                RenderOptions.SetBitmapScalingMode(PreviewImage, BitmapScalingMode.HighQuality);
                PreviewNote.Visibility = Visibility.Collapsed;
                ShownSeconds = seconds;
            }
            else if (PreviewImage.Source == null)
            {
                // Times still work without a picture, so the controls stay usable.
                PreviewNote.Text = "No picture to show for this video.";
            }
        }

        private async void LoadStrip()
        {
            try
            {
                // Quick pictures first, so the strip fills in about a second whatever the video.
                await FillStrip(exact: false);
                if (Selection.Duration <= ExactStripUpToSeconds) await FillStrip(exact: true);
            }
            catch (OperationCanceledException)
            {
                // The controls were closed.
            }
        }

        private async Task FillStrip(bool exact)
        {
            var token = _closing.Token;
            using (var gate = new SemaphoreSlim(StripAtOnce))
            {
                await Task.WhenAll(Enumerable.Range(0, TrimStrip.Cells).Select(async cell =>
                {
                    // Each cell shows the moment at its middle.
                    double seconds = (cell + 0.5) / TrimStrip.Cells * Selection.Duration;
                    ImageSource picture;

                    await gate.WaitAsync(token);
                    try
                    {
                        picture = await Task.Run(() => Decode(_grabber.Grab(seconds, StripHeight, exact, token)), token);
                    }
                    catch (TimeoutException)
                    {
                        // That spot on the strip stays as it is.
                        picture = null;
                    }
                    finally
                    {
                        gate.Release();
                    }

                    if (picture != null && !token.IsCancellationRequested) Strip.SetPicture(cell, picture);
                }));
            }
        }

        /// <summary>Turns the bytes of a picture file into something WPF can draw, from any thread.</summary>
        internal static ImageSource Decode(byte[] bytes)
        {
            if (bytes == null) return null;

            try
            {
                var picture = new BitmapImage();
                picture.BeginInit();
                picture.CacheOption = BitmapCacheOption.OnLoad;
                picture.StreamSource = new MemoryStream(bytes);
                picture.EndInit();
                picture.Freeze();
                return picture;
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is IOException || ex is ArgumentException)
            {
                // Not a picture after all. Treated the same as there being none.
                return null;
            }
        }
    }
}
