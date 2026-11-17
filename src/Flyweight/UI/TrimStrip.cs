using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Flyweight.Core;

namespace Flyweight.UI
{
    /// <summary>
    /// The strip of pictures among the trim controls, with a start and an end handle. Drawn by hand, because
    /// two handles on one track with pictures behind them isn't something a stock slider does.
    /// </summary>
    internal sealed class TrimStrip : FrameworkElement
    {
        public const int Cells = 12;

        /// <summary>Room at each end, so a handle at the very start or end is still fully on show.</summary>
        public const double Inset = 8;

        private const double PictureMargin = 5;
        private const double HandleWidth = 8;
        private const double CellGap = 2;

        /// <summary>How near a handle a press has to be to take hold of it, rather than land between the two.</summary>
        private const double Grab = 12;

        private readonly ImageSource[] _pictures = new ImageSource[Cells];
        private TrimSelection _selection;
        private bool _dragging;
        private double? _pressedAt;
        private double? _position;

        /// <summary>Raised as a handle moves. The flag is true once it has come to rest.</summary>
        public event Action<TrimHandle, bool> Moved;

        /// <summary>The handle touched last.</summary>
        public TrimHandle Active { get; private set; }

        /// <summary>Raised by a click between the handles, with the moment clicked: somewhere to play from.</summary>
        public event Action<double> Picked;

        /// <summary>Where playing has got to, which is marked with a line, or null when nothing is playing.</summary>
        public double? Position
        {
            get => _position;
            set
            {
                if (_position == value) return;
                _position = value;
                InvalidateVisual();
            }
        }

        public int PictureCount => _pictures.Count(picture => picture != null);

        private double TrackWidth => Math.Max(1, ActualWidth - 2 * Inset);

        public void Attach(TrimSelection selection)
        {
            _selection = selection;
            InvalidateVisual();
        }

        public void SetPicture(int cell, ImageSource picture)
        {
            if (cell < 0 || cell >= Cells) return;
            _pictures[cell] = picture;
            InvalidateVisual();
        }

        public void Refresh() => InvalidateVisual();

        internal double XOf(double seconds)
        {
            if (_selection == null || _selection.Duration <= 0) return Inset;
            return Inset + seconds / _selection.Duration * TrackWidth;
        }

        internal double SecondsAt(double x) => _selection == null ? 0 : (x - Inset) / TrackWidth * _selection.Duration;

        internal TrimHandle HandleNear(double x)
        {
            return Math.Abs(x - XOf(_selection.Start)) <= Math.Abs(x - XOf(_selection.End)) ? TrimHandle.Start : TrimHandle.End;
        }

        internal void DragTo(TrimHandle handle, double x, bool settled)
        {
            if (_selection == null) return;
            Active = handle;
            _selection.Move(handle, SecondsAt(x));
            InvalidateVisual();
            Moved?.Invoke(handle, settled);
        }

        private bool IsBetweenHandles(double x)
        {
            return x > XOf(_selection.Start) + Grab && x < XOf(_selection.End) - Grab;
        }

        internal void Press(double x)
        {
            if (_selection == null) return;

            if (IsBetweenHandles(x))
            {
                // A click, to play from here, wherever it's let go. A handle that has been set with
                // care doesn't jump to the pointer because a hand slid a little while clicking.
                _pressedAt = x;
                return;
            }

            // On a handle, or out in what's being left off, the nearer handle comes to the press at
            // once, so there's no small target to hit.
            _dragging = true;
            DragTo(HandleNear(x), x, settled: false);
        }

        internal void MoveTo(double x)
        {
            if (_pressedAt.HasValue) return;

            if (_dragging) DragTo(Active, x, settled: false);
            else if (_selection != null) Cursor = IsBetweenHandles(x) ? Cursors.Hand : Cursors.SizeWE;
        }

        internal void Release(double x)
        {
            if (_pressedAt.HasValue)
            {
                double at = _pressedAt.Value;
                _pressedAt = null;
                Picked?.Invoke(SecondsAt(at));
            }
            else if (_dragging)
            {
                _dragging = false;
                DragTo(Active, x, settled: true);
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (_selection == null) return;

            CaptureMouse();
            Press(e.GetPosition(this).X);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            MoveTo(e.GetPosition(this).X);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!_dragging && !_pressedAt.HasValue) return;

            // Finished before the mouse is let go of, so that letting go finds nothing left to cut short.
            Release(e.GetPosition(this).X);
            ReleaseMouseCapture();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            // Something else took the mouse part way. A click that never finished isn't one, and
            // wherever a handle had got to is where it stays.
            _pressedAt = null;
            if (!_dragging) return;
            _dragging = false;
            Moved?.Invoke(Active, true);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var bounds = new Rect(RenderSize);
            dc.DrawRectangle(Brushes.Transparent, null, bounds);   // so a press between two pictures still lands
            if (_selection == null || bounds.Width <= 2 * Inset || bounds.Height <= 2 * PictureMargin) return;

            var track = new Rect(Inset, PictureMargin, TrackWidth, bounds.Height - 2 * PictureMargin);
            Brush empty = Themed("Track", Brushes.Gray);
            Brush accent = Themed("Accent", Brushes.Teal);

            double cellWidth = (track.Width - CellGap * (Cells - 1)) / Cells;
            for (int i = 0; i < Cells; i++)
            {
                var cell = new Rect(track.Left + i * (cellWidth + CellGap), track.Top, cellWidth, track.Height);
                dc.DrawRectangle(empty, null, cell);
                if (_pictures[i] != null) DrawFilling(dc, _pictures[i], cell);
            }

            // What's being left out is dimmed. What's being kept is framed, with a handle at each end.
            double start = XOf(_selection.Start);
            double end = XOf(_selection.End);
            Brush veil = Veil();
            if (start > track.Left) dc.DrawRectangle(veil, null, new Rect(track.Left, track.Top, start - track.Left, track.Height));
            if (end < track.Right) dc.DrawRectangle(veil, null, new Rect(end, track.Top, track.Right - end, track.Height));

            var frame = new Pen(accent, 2);
            dc.DrawLine(frame, new Point(start, track.Top - 1), new Point(end, track.Top - 1));
            dc.DrawLine(frame, new Point(start, track.Bottom + 1), new Point(end, track.Bottom + 1));

            if (_position.HasValue)
            {
                // Where playing has got to: light with a dark edge, so it shows over any picture.
                double at = XOf(_position.Value);
                dc.DrawRectangle(Brushes.Black, null, new Rect(at - 2, track.Top, 4, track.Height));
                dc.DrawRectangle(Brushes.White, null, new Rect(at - 1, track.Top, 2, track.Height));
            }

            Brush grip = Themed("OnAccent", Brushes.White);
            foreach (double x in new[] { start, end })
            {
                dc.DrawRoundedRectangle(accent, null, new Rect(x - HandleWidth / 2, 0, HandleWidth, bounds.Height), 3, 3);
                dc.DrawRectangle(grip, null, new Rect(x - 1, bounds.Height / 2 - 7, 2, 14));
            }
        }

        /// <summary>Fills a cell with a picture, cropping whatever spills over, so any shape of video lines up.</summary>
        private static void DrawFilling(DrawingContext dc, ImageSource picture, Rect cell)
        {
            if (picture.Width <= 0 || picture.Height <= 0) return;

            double scale = Math.Max(cell.Width / picture.Width, cell.Height / picture.Height);
            double width = picture.Width * scale;
            double height = picture.Height * scale;
            dc.PushClip(new RectangleGeometry(cell));
            dc.DrawImage(picture, new Rect(cell.Left + (cell.Width - width) / 2, cell.Top + (cell.Height - height) / 2, width, height));
            dc.Pop();
        }

        private Brush Themed(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

        private Brush Veil()
        {
            Color background = (TryFindResource("Bg") as SolidColorBrush)?.Color ?? Colors.White;
            var veil = new SolidColorBrush(Color.FromArgb(200, background.R, background.G, background.B));
            veil.Freeze();
            return veil;
        }
    }
}
