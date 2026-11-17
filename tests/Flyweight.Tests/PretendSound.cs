using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Flyweight.Core;

namespace Flyweight.Tests
{
    /// <summary>
    /// Stands in for sound going out through Windows, so a video with sound can be played in a test
    /// without any of it reaching the speakers. It takes sound in the way the real thing does, a
    /// little ahead of what has been "heard" so far, and keeps all of it to be looked at afterwards.
    /// It can also be made to misbehave in the ways real outputs do.
    /// </summary>
    internal sealed class PretendSound : SoundOut
    {
        private const double RoomSeconds = 0.4;

        private readonly Stopwatch _playing = new Stopwatch();
        private readonly MemoryStream _taken = new MemoryStream();
        private readonly object _gate = new object();
        private bool _hasWaited;

        /// <summary>False for a PC with nothing to play sound through.</summary>
        public bool CanOpen { get; set; } = true;

        /// <summary>How long opening takes, as an output that has gone to sleep can.</summary>
        public int OpenTakesMs { get; set; }

        /// <summary>How long the first of the sound is kept waiting before it's taken, as when it's slow to come.</summary>
        public int FirstWriteWaitsMs { get; set; }

        /// <summary>
        /// How fast it plays, where 1 is as fast as the clock on the wall. Anything else shows whether
        /// pictures are following the sound or only the clock.
        /// </summary>
        public double Pace { get; set; } = 1;

        /// <summary>After this much it refuses any more, as when the output it was playing through goes away.</summary>
        public double TakesSeconds { get; set; } = double.MaxValue;

        /// <summary>Having played this much it stops answering: no more is taken, and it says nothing has been heard.</summary>
        public double GoesAwayAtSeconds { get; set; } = double.MaxValue;

        /// <summary>Having played this much, its count of what has been heard starts again from nothing.</summary>
        public double CountRestartsAtSeconds { get; set; } = double.MaxValue;

        public bool WasOpened { get; private set; }
        public bool IsClosed { get; private set; }
        public bool IsPaused { get; private set; } = true;

        /// <summary>True if it was ever made to wait once it had begun to play.</summary>
        public bool WasHeldUp { get; private set; }


        /// <summary>Everything handed over so far: 16-bit stereo samples at 48 kHz.</summary>
        public byte[] Taken
        {
            get
            {
                lock (_gate) return _taken.ToArray();
            }
        }

        public double TakenSeconds
        {
            get
            {
                lock (_gate) return _taken.Length / (double)BytesASecond;
            }
        }

        /// <summary>How much has really been played, whatever it's saying.</summary>
        public double Played => Math.Min(_playing.Elapsed.TotalSeconds * Pace, TakenSeconds);

        public override bool Open()
        {
            if (OpenTakesMs > 0) Thread.Sleep(OpenTakesMs);
            WasOpened = true;
            return CanOpen;
        }

        public override bool Write(byte[] sound, int count, CancellationToken token)
        {
            if (!_hasWaited && FirstWriteWaitsMs > 0 && token.WaitHandle.WaitOne(FirstWriteWaitsMs)) return false;
            _hasWaited = true;

            while (!IsClosed && TakenSeconds < TakesSeconds && Played < GoesAwayAtSeconds)
            {
                if (TakenSeconds - Played < RoomSeconds)
                {
                    lock (_gate) _taken.Write(sound, 0, count);
                    return true;
                }
                if (token.WaitHandle.WaitOne(5)) return false;
            }
            return false;
        }

        public override double Seconds
        {
            get
            {
                double played = Played;
                if (played >= GoesAwayAtSeconds) return 0;
                return played >= CountRestartsAtSeconds ? played - CountRestartsAtSeconds : played;
            }
        }

        public override void Pause()
        {
            if (!IsPaused && _playing.Elapsed > TimeSpan.Zero) WasHeldUp = true;
            IsPaused = true;
            _playing.Stop();
        }

        public override void Resume()
        {
            IsPaused = false;
            if (!IsClosed) _playing.Start();
        }

        public override void Dispose()
        {
            IsClosed = true;
            _playing.Stop();
        }
    }
}
