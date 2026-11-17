using System;

namespace Flyweight.Core
{
    /// <summary>
    /// The part of the picture to keep, as fractions of the picture the way it's shown: upright, and
    /// in its own shape. Fractions mean the same thing whatever size the picture is drawn at, and in
    /// a video whose pixels aren't square, the same fractions of its stored pixels are the same part.
    /// </summary>
    public sealed class CropArea
    {
        public static readonly CropArea Whole = new CropArea(0, 0, 1, 1);

        public CropArea(double left, double top, double right, double bottom)
        {
            if (!IsFraction(left) || !IsFraction(top) || !IsFraction(right) || !IsFraction(bottom) || right <= left || bottom <= top)
            {
                throw new ArgumentException("A crop has to be a box inside the picture.");
            }
            Left = Settle(left);
            Top = Settle(top);
            Right = Settle(right);
            Bottom = Settle(bottom);
        }

        public double Left { get; }
        public double Top { get; }
        public double Right { get; }
        public double Bottom { get; }

        /// <summary>True for a box around the whole picture.</summary>
        public bool IsWhole => Left <= 0 && Top <= 0 && Right >= 1 && Bottom >= 1;

        /// <summary>
        /// The pixels it keeps of a frame this size: an even width and height at an even place, which
        /// is what video with half-size colour needs, and never past the frame's edges. Null when
        /// that's the whole frame, or as much of it as an even size allows, so there's nothing to crop.
        /// </summary>
        public (int X, int Y, int Width, int Height)? In(int width, int height)
        {
            if (width < 2 || height < 2) return null;
            int x = Even(Left * width);
            int y = Even(Top * height);
            // From the box's own size, so a box slid along keeps the same size. A box out to the far
            // edge keeps out to it, wherever its near side falls within a pixel.
            int w = Math.Max(2, Right >= 1 ? Even(width - x) : Even((Right - Left) * width));
            int h = Math.Max(2, Bottom >= 1 ? Even(height - y) : Even((Bottom - Top) * height));
            x = Math.Min(x, Even(width - w));
            y = Math.Min(y, Even(height - h));

            if (x == 0 && y == 0 && w >= Even(width) && h >= Even(height)) return null;
            return (x, y, w, h);
        }

        /// <summary>How much of a frame this size it keeps, from 0 to 1.</summary>
        public double ShareOf(int width, int height)
        {
            var keep = In(width, height);
            return keep == null || width <= 0 || height <= 0 ? 1 : keep.Value.Width * (double)keep.Value.Height / ((double)width * height);
        }

        // With a hair to spare, so that 1919.9999999999998 pixels, which is 1920 but for rounding, counts as 1920.
        private static int Even(double value) => (int)Math.Floor(value / 2 + 1e-6) * 2;

        /// <summary>A hair from an edge is on it: what adding up fractions of a picture on a screen at 125% can leave.</summary>
        private static double Settle(double fraction) => fraction < 1e-9 ? 0 : fraction > 1 - 1e-9 ? 1 : fraction;

        private static bool IsFraction(double value) => value >= 0 && value <= 1;
    }
}
