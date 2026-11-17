using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Flyweight.Core;

namespace Flyweight.UI
{
    /// <summary>
    /// The box with four corners drawn over the picture among the trim controls, for choosing the part
    /// of the picture to keep. It starts around the whole picture. Dragging a corner in crops from that
    /// corner, dragging inside the box moves it, and what's left out is dimmed. What it holds is a part
    /// of the picture as fractions of it, so it means the same whatever size the picture is shown.
    /// </summary>
    internal sealed class CropBox : FrameworkElement
    {
        /// <summary>The least the box can be across or down, in points, so its corners can still be taken hold of.</summary>
        public const double LeastSide = 32;

        /// <summary>
        /// Nor can it keep fewer than this many of the video's own pixels across and down. Graphics cards
        /// won't encode a picture much smaller: on an RTX 5080, 146 by 50 for H.264, 130 by 34 for H.265
        /// and 130 by 66 for AV1. Anything smaller would quietly go to the processor instead.
        /// </summary>
        private const int LeastPixelsAcross = 160;
        private const int LeastPixelsDown = 80;

        /// <summary>How near a corner a press has to be to take hold of it.</summary>
        private const double CornerReach = 14;

        /// <summary>How near an edge of the picture a side is put onto it, since a hand can't hit an edge exactly.</summary>
        private const double SnapReach = 6;

        private const double Arm = 16;
        private const double Thickness = 3;

        /// <summary>
        /// How much nearer the picture comes while a corner is held still, so it can be put exactly
        /// where it should be: a hand's movement then moves it a quarter as far.
        /// </summary>
        private const double Nearer = 4;

        /// <summary>How long a corner has to be held still for that, and how far a hand can drift meanwhile.</summary>
        private static readonly TimeSpan StillFor = TimeSpan.FromSeconds(0.5);
        private const double Drift = 3;

        private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)));
        private static readonly Brush Edge = Frozen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)));
        private static readonly Pen Frame = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255))), 1));

        private double _aspect = 16.0 / 9;

        // What's being held: the sides a corner moves, or the whole box.
        private bool _holdsLeft, _holdsTop, _holdsRight, _holdsBottom, _holdsBox;
        private Vector _grip;
        private DispatcherTimer _still;
        private Point _stillAt;

        /// <summary>Raised as the box changes. The flag is true once it has come to rest.</summary>
        public event Action<bool> Changed;

        public CropArea Area { get; private set; } = CropArea.Whole;

        /// <summary>Raised when the picture zooms in around a corner held still, and when it zooms out again.</summary>
        public event Action ZoomChanged;

        /// <summary>How many times nearer the picture is to be shown: 1, or more while a corner is held still.</summary>
        public double Zoom { get; private set; } = 1;

        /// <summary>The point to zoom in around: the corner being held.</summary>
        public Point ZoomedAt { get; private set; }

        /// <summary>How wide the picture is shown for its height.</summary>
        public double Aspect
        {
            get => _aspect;
            set
            {
                _aspect = value > 0 ? value : 16.0 / 9;
                InvalidateVisual();
            }
        }

        /// <summary>The picture's size in the video's own pixels, the right way up.</summary>
        public int VideoWidth { get; set; }

        public int VideoHeight { get; set; }

        private bool IsHolding => _holdsLeft || _holdsTop || _holdsRight || _holdsBottom || _holdsBox;

        private bool HoldsCorner => _holdsLeft || _holdsRight;

        /// <summary>Where the picture is: as big as fits in this space, in its own shape, in the middle.</summary>
        internal Rect Picture
        {
            get
            {
                double width = ActualWidth, height = ActualHeight;
                if (width <= 0 || height <= 0) return Rect.Empty;
                if (width / height > _aspect)
                {
                    double across = height * _aspect;
                    return new Rect((width - across) / 2, 0, across, height);
                }
                double down = width / _aspect;
                return new Rect(0, (height - down) / 2, width, down);
            }
        }

        /// <summary>Where the box is, over the picture.</summary>
        internal Rect Box
        {
            get
            {
                var picture = Picture;
                if (picture.IsEmpty) return Rect.Empty;
                return new Rect(
                    picture.Left + Area.Left * picture.Width, picture.Top + Area.Top * picture.Height,
                    (Area.Right - Area.Left) * picture.Width, (Area.Bottom - Area.Top) * picture.Height);
            }
        }

        /// <summary>Puts back the whole picture.</summary>
        public void Reset()
        {
            LetGo();
            Area = CropArea.Whole;
            InvalidateVisual();
            Changed?.Invoke(true);
        }

        /// <summary>
        /// Takes hold of a corner if the point is on one, and says whether it did. For a press that
        /// would otherwise go to something over the box, as Play can be over a corner of a narrow picture.
        /// </summary>
        internal bool TakeHold(Point point)
        {
            var box = Box;
            if (box.IsEmpty || CornerNear(box, point) < 0) return false;
            Press(point);
            CaptureMouse();
            return true;
        }

        internal void Press(Point point)
        {
            var box = Box;
            if (box.IsEmpty) return;

            int corner = CornerNear(box, point);
            if (corner >= 0)
            {
                _holdsLeft = corner == 0 || corner == 2;
                _holdsRight = !_holdsLeft;
                _holdsTop = corner < 2;
                _holdsBottom = !_holdsTop;
                StillFrom(point);
            }
            else if (!Area.IsWhole && box.Contains(point))
            {
                _holdsBox = true;
                _grip = point - box.TopLeft;
            }
        }

        internal void MoveTo(Point point)
        {
            if (!IsHolding)
            {
                Cursor = CursorFor(point);
                return;
            }

            // All in fractions of the picture, and only the sides being moved are worked out again.
            // Turned into points and back, a side left where it was would creep a little at a time.
            // Only a hand that has really moved starts the wait for it to hold still over again.
            if (HoldsCorner && Zoom == 1 && (point - _stillAt).Length > Drift) StillFrom(point);

            // Zoomed in, the reach onto an edge is as far on screen as ever, which is less of the
            // picture, so a thin strip along an edge can be cropped away too.
            var picture = Picture;
            double x = (point.X - picture.Left) / picture.Width;
            double y = (point.Y - picture.Top) / picture.Height;
            double snapAcross = SnapReach / Zoom / picture.Width, snapDown = SnapReach / Zoom / picture.Height;
            double left = Area.Left, top = Area.Top, right = Area.Right, bottom = Area.Bottom;

            if (_holdsBox)
            {
                // The same size, anywhere it fits, and onto an edge when it comes near one.
                (left, right) = Place(x - _grip.X / picture.Width, right - left, snapAcross);
                (top, bottom) = Place(y - _grip.Y / picture.Height, bottom - top, snapDown);
            }
            else
            {
                double across = Least(picture.Width, VideoWidth, LeastPixelsAcross) / picture.Width;
                double down = Least(picture.Height, VideoHeight, LeastPixelsDown) / picture.Height;
                if (_holdsLeft) left = OnEdge(Math.Max(0, Math.Min(x, right - across)), 0, snapAcross);
                if (_holdsRight) right = OnEdge(Math.Min(1, Math.Max(x, left + across)), 1, snapAcross);
                if (_holdsTop) top = OnEdge(Math.Max(0, Math.Min(y, bottom - down)), 0, snapDown);
                if (_holdsBottom) bottom = OnEdge(Math.Min(1, Math.Max(y, top + down)), 1, snapDown);
            }

            Area = new CropArea(left, top, right, bottom);
            InvalidateVisual();
            Changed?.Invoke(false);
        }

        /// <summary>A corner held still for long enough: the picture zooms in around it.</summary>
        internal void Rest()
        {
            _still?.Stop();
            if (!HoldsCorner || Zoom > 1) return;

            var box = Box;
            ZoomedAt = new Point(_holdsLeft ? box.Left : box.Right, _holdsTop ? box.Top : box.Bottom);
            Zoom = Nearer;
            InvalidateVisual();
            ZoomChanged?.Invoke();
        }

        private void StillFrom(Point point)
        {
            _stillAt = point;
            if (_still == null)
            {
                _still = new DispatcherTimer { Interval = StillFor };
                _still.Tick += (s, e) => Rest();
            }
            _still.Stop();
            _still.Start();
        }

        internal void Release(Point point)
        {
            if (!IsHolding) return;
            LetGo();
            Changed?.Invoke(true);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Press(e.GetPosition(this));
            if (!IsHolding) return;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            MoveTo(e.GetPosition(this));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!IsHolding) return;

            // Finished before the mouse is let go of, so that letting go finds nothing left to cut short.
            Release(e.GetPosition(this));
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            // Something else took the mouse part way. Wherever the box had got to is where it stays.
            if (!IsHolding) return;
            LetGo();
            Changed?.Invoke(true);
        }

        protected override void OnRender(DrawingContext dc)
        {
            // So a press anywhere over the picture lands here.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            var picture = Picture;
            if (picture.IsEmpty) return;
            var box = Box;

            // Zoomed in, the lines are drawn thinner by as much, to look the same on screen.
            double scale = 1 / Zoom;
            if (!Area.IsWhole)
            {
                dc.DrawGeometry(Dim, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(picture), new RectangleGeometry(box)));
                dc.DrawRectangle(null, Zoom > 1 ? new Pen(Frame.Brush, scale) : Frame, box);
            }

            // A mark at each corner: light with a dark edge, so it shows over any picture.
            foreach (bool edge in new[] { true, false })
            {
                foreach (var arm in Arms(box, Arm * scale, Thickness * scale))
                {
                    var rect = arm;
                    if (edge) rect.Inflate(scale, scale);
                    dc.DrawRectangle(edge ? Edge : Brushes.White, null, rect);
                }
            }
        }

        private static Rect[] Arms(Rect box, double arm, double thick)
        {
            return new[]
            {
                new Rect(box.Left, box.Top, arm, thick), new Rect(box.Left, box.Top, thick, arm),
                new Rect(box.Right - arm, box.Top, arm, thick), new Rect(box.Right - thick, box.Top, thick, arm),
                new Rect(box.Left, box.Bottom - thick, arm, thick), new Rect(box.Left, box.Bottom - arm, thick, arm),
                new Rect(box.Right - arm, box.Bottom - thick, arm, thick), new Rect(box.Right - thick, box.Bottom - arm, thick, arm),
            };
        }

        /// <summary>Which corner a point is near enough to take hold of: top left, top right, bottom left, bottom right, or none.</summary>
        private static int CornerNear(Rect box, Point point)
        {
            Point[] corners = { box.TopLeft, box.TopRight, box.BottomLeft, box.BottomRight };
            int nearest = -1;
            double distance = CornerReach;
            for (int i = 0; i < corners.Length; i++)
            {
                double here = (corners[i] - point).Length;
                if (here > distance) continue;
                nearest = i;
                distance = here;
            }
            return nearest;
        }

        private Cursor CursorFor(Point point)
        {
            var box = Box;
            if (box.IsEmpty) return null;

            int corner = CornerNear(box, point);
            if (corner == 0 || corner == 3) return Cursors.SizeNWSE;
            if (corner == 1 || corner == 2) return Cursors.SizeNESW;
            return !Area.IsWhole && box.Contains(point) ? Cursors.SizeAll : null;
        }

        /// <summary>The least the box can be in one direction: big enough to take hold of, and enough of the video's own pixels to encode.</summary>
        private static double Least(double shown, int pixels, int leastPixels)
        {
            double least = pixels > 0 ? Math.Max(LeastSide, leastPixels * shown / pixels) : LeastSide;
            return Math.Min(least, shown);
        }

        /// <summary>
        /// Where a box this long goes, as fractions of the picture: inside it, and onto an edge when it
        /// comes near one, exactly, with its length kept.
        /// </summary>
        private static (double Start, double End) Place(double start, double length, double reach)
        {
            start = Math.Max(0, Math.Min(start, 1 - length));
            if (start <= reach) return (0, length);
            if (1 - length - start <= reach) return (1 - length, 1);
            return (start, start + length);
        }

        private static double OnEdge(double side, double edge, double reach) => Math.Abs(side - edge) <= reach ? edge : side;

        private void LetGo()
        {
            _still?.Stop();
            _holdsLeft = _holdsTop = _holdsRight = _holdsBottom = _holdsBox = false;
            if (Zoom == 1) return;

            Zoom = 1;
            InvalidateVisual();
            ZoomChanged?.Invoke();
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
