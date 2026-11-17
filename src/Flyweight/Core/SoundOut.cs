using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Flyweight.Core
{
    /// <summary>
    /// Plays sound through Windows as it's handed over, and says how much of it has been heard so far.
    /// That count is the clock a video's pictures are shown by, which is what keeps them in step with
    /// its sound. It takes plain 16-bit stereo at 48 kHz, which ffmpeg turns any sound track into.
    /// </summary>
    internal class SoundOut : IDisposable
    {
        public const int Rate = 48000;
        public const int BytesASecond = Rate * 2 * 2;

        /// <summary>A tenth of a second, which is the most that's handed over in one go.</summary>
        public const int BlockBytes = BytesASecond / 10;

        // Windows is kept this many blocks ahead: enough to ride out a busy moment, and little
        // enough that what's waiting to be heard is never far ahead of what's being read.
        private const int Blocks = 4;

        // A block is one of Windows' WAVEHDR headers with its sound straight after it.
        private static readonly int HeaderBytes = 4 * IntPtr.Size + 16;
        private static readonly int FlagsAt = 2 * IntPtr.Size + 8;
        private const int Prepared = 2;
        private const int InQueue = 0x10;

        private const int InBytes = 4;
        private const int InSamples = 2;

        private readonly object _gate = new object();
        private readonly int[] _time = new int[3];
        private IntPtr _device;
        private IntPtr _blocks;
        private int _next;

        /// <summary>
        /// Gets ready to play, holding off until <see cref="Resume"/>. False when this PC has nothing
        /// to play sound through, which is no reason not to show a video's pictures.
        /// </summary>
        public virtual bool Open()
        {
            var format = new WaveFormat { Tag = 1, Channels = 2, Rate = Rate, BytesASecond = BytesASecond, BlockAlign = 4, Bits = 16 };
            lock (_gate)
            {
                // -1 is whichever output Windows has as its default.
                if (waveOutOpen(out _device, -1, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
                {
                    _device = IntPtr.Zero;
                    return false;
                }
                waveOutPause(_device);

                int size = Blocks * (HeaderBytes + BlockBytes);
                _blocks = Marshal.AllocHGlobal(size);
                Marshal.Copy(new byte[size], 0, _blocks, size);
                return true;
            }
        }

        /// <summary>
        /// Hands over some sound, a block at most, waiting first while Windows still has plenty to be
        /// getting on with. False once this is closed, the token says stop, or Windows won't take it.
        /// </summary>
        public virtual bool Write(byte[] sound, int count, CancellationToken token)
        {
            count = Math.Min(count, BlockBytes);
            while (true)
            {
                lock (_gate)
                {
                    if (_device == IntPtr.Zero) return false;

                    IntPtr header = _blocks + _next * (HeaderBytes + BlockBytes);
                    int flags = Marshal.ReadInt32(header, FlagsAt);
                    if ((flags & InQueue) == 0)
                    {
                        if ((flags & Prepared) != 0) waveOutUnprepareHeader(_device, header, HeaderBytes);

                        Marshal.Copy(sound, 0, header + HeaderBytes, count);
                        Marshal.WriteIntPtr(header, header + HeaderBytes);
                        Marshal.WriteInt32(header, IntPtr.Size, count);
                        Marshal.WriteInt32(header, FlagsAt, 0);
                        _next = (_next + 1) % Blocks;
                        return waveOutPrepareHeader(_device, header, HeaderBytes) == 0 && waveOutWrite(_device, header, HeaderBytes) == 0;
                    }
                }

                // The block whose turn it is hasn't been played yet.
                if (token.WaitHandle.WaitOne(5)) return false;
            }
        }

        /// <summary>How much has been heard since it was opened, in seconds.</summary>
        public virtual double Seconds
        {
            get
            {
                lock (_gate)
                {
                    if (_device == IntPtr.Zero) return 0;

                    _time[0] = InBytes;
                    if (waveOutGetPosition(_device, _time, sizeof(int) * _time.Length) != 0) return 0;

                    // It answers in whichever measure it can: bytes, samples, or thousandths of a second.
                    double aSecond = _time[0] == InBytes ? BytesASecond : _time[0] == InSamples ? Rate : 1000;
                    return (uint)_time[1] / aSecond;
                }
            }
        }

        public virtual void Pause()
        {
            lock (_gate)
            {
                if (_device != IntPtr.Zero) waveOutPause(_device);
            }
        }

        public virtual void Resume()
        {
            lock (_gate)
            {
                if (_device != IntPtr.Zero) waveOutRestart(_device);
            }
        }

        /// <summary>Stops at once, wherever it had got to, and lets go of the output.</summary>
        public virtual void Dispose()
        {
            lock (_gate)
            {
                if (_device == IntPtr.Zero) return;

                // Hands every block back, played or not, which is what lets it be closed.
                waveOutReset(_device);
                for (int i = 0; i < Blocks; i++)
                {
                    IntPtr header = _blocks + i * (HeaderBytes + BlockBytes);
                    if ((Marshal.ReadInt32(header, FlagsAt) & Prepared) != 0) waveOutUnprepareHeader(_device, header, HeaderBytes);
                }
                // If Windows won't close it, it may yet reach for the blocks, so they're left where they are.
                if (waveOutClose(_device) == 0) Marshal.FreeHGlobal(_blocks);
                _device = IntPtr.Zero;
                _blocks = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct WaveFormat
        {
            public short Tag;
            public short Channels;
            public int Rate;
            public int BytesASecond;
            public short BlockAlign;
            public short Bits;
            public short Extra;
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr device, int id, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr device, IntPtr header, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr device, IntPtr header, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutGetPosition(IntPtr device, [In, Out] int[] time, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutPause(IntPtr device);

        [DllImport("winmm.dll")]
        private static extern int waveOutRestart(IntPtr device);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr device);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr device);
    }
}
