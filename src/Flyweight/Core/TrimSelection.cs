using System;

namespace Flyweight.Core
{
    public enum TrimHandle { Start, End }

    /// <summary>
    /// The start and end being chosen with the trim controls. Keeps them inside the video, in order and a
    /// sensible distance apart, whatever the handles, the arrow keys or typed-in times ask for.
    /// </summary>
    public sealed class TrimSelection
    {
        private const double AssumedFps = 30;
        private const double ShortestSeconds = 0.1;

        // Absorbs floating-point error where a frame count should come out whole.
        private const double Slack = 1e-6;

        public TrimSelection(double durationSeconds, double fps)
        {
            Duration = Round(Math.Max(durationSeconds, 0));
            FrameSeconds = 1 / (fps > 0 ? fps : AssumedFps);
            MinLength = Math.Min(Duration, Round(Math.Max(FrameSeconds, ShortestSeconds)));
            End = Duration;
        }

        public double Duration { get; }
        public double FrameSeconds { get; }
        public double MinLength { get; }
        public double Start { get; private set; }
        public double End { get; private set; }
        public double Length => Round(End - Start);
        public bool IsWholeVideo => Start <= 0 && End >= Duration;

        public double this[TrimHandle handle] => handle == TrimHandle.Start ? Start : End;

        public void Move(TrimHandle handle, double seconds)
        {
            if (double.IsNaN(seconds)) return;

            if (handle == TrimHandle.Start)
            {
                Start = Round(Math.Max(0, Math.Min(seconds, End - MinLength)));
            }
            else
            {
                End = Round(Math.Min(Duration, Math.Max(seconds, Start + MinLength)));
            }
        }

        /// <summary>Steps a handle by whole frames, landing where the neighbouring frame begins.</summary>
        public void Nudge(TrimHandle handle, int frames)
        {
            // A time stands for the first frame at or after it.
            double frame = Math.Ceiling(this[handle] / FrameSeconds - Slack) + frames;
            Move(handle, JustBefore(frame * FrameSeconds));
        }

        /// <summary>
        /// Puts a handle on the frame a time stands for, the first at or after it, as with any time a
        /// handle is at: kept first for the start, or last for the end.
        /// </summary>
        public void MoveOnto(TrimHandle handle, double seconds)
        {
            if (handle == TrimHandle.Start) Move(handle, JustBefore(seconds));
            else Move(handle, JustBefore((Math.Ceiling(seconds / FrameSeconds - Slack) + 1) * FrameSeconds));
        }

        /// <summary>
        /// Where the frame a moving picture shows at a moment begins, to the thousandth at or just
        /// before it. That's the last frame due before the next picture takes over, half a picture
        /// later, with this many pictures a second: a frame can be on show for several, or for none.
        /// </summary>
        public double FrameShown(double seconds, double picturesASecond)
        {
            return JustBefore((Math.Ceiling(NextPicture(seconds, picturesASecond) / FrameSeconds - Slack) - 1) * FrameSeconds);
        }

        /// <summary>Puts a handle so the frame a moving picture shows at a moment is kept: first for the start, last for the end.</summary>
        public void MoveOntoShown(TrimHandle handle, double seconds, double picturesASecond)
        {
            // The end goes where the next picture takes over: the frame on show is due before then, and the next one isn't.
            Move(handle, handle == TrimHandle.Start ? FrameShown(seconds, picturesASecond) : JustBefore(NextPicture(seconds, picturesASecond)));
        }

        private static double NextPicture(double seconds, double picturesASecond) => seconds + 0.5 / picturesASecond;

        /// <summary>The time whose picture shows what a handle means: the first frame kept, or the last.</summary>
        public double PreviewTime(TrimHandle handle)
        {
            return handle == TrimHandle.Start ? Start : Math.Max(0, JustBefore(End - FrameSeconds));
        }

        /// <summary>Null when the whole video is kept, since that needs no trimming.</summary>
        /// <param name="fileSeconds">
        /// How long the file is as a whole. Its sound often runs on a little past its last frame, and
        /// an end handle left at the far right means "to the end", so that tail is kept too.
        /// </param>
        public TrimRange ToRange(double fileSeconds = 0)
        {
            if (IsWholeVideo) return null;
            return new TrimRange(Start, End >= Duration ? Math.Max(End, fileSeconds) : End);
        }

        /// <summary>Rounds down to the millisecond, so the time is at or just before a frame and never past it.</summary>
        private static double JustBefore(double seconds) => Math.Floor(seconds * 1000 + Slack) / 1000;

        private static double Round(double seconds) => Math.Round(seconds, 3, MidpointRounding.AwayFromZero);
    }
}
