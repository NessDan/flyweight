using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace Flyweight.Tests
{
    /// <summary>
    /// Hands a video file over through a pipe at a pace of its choosing, to whoever opens the pipe as
    /// if it were the file: the first stretch of the video at once, and after that each frame no
    /// sooner than its moment comes round at that pace. Reading it is like reading a video on a PC
    /// that can't keep up with it.
    /// </summary>
    internal sealed class SlowFile : IDisposable
    {
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly NamedPipeServerStream _pipe;

        /// <param name="fullSpeedSeconds">How much of the video is handed over straight away.</param>
        /// <param name="pace">How fast the rest comes, where 1 would be as fast as it plays.</param>
        public SlowFile(string file, double fullSpeedSeconds, double pace)
        {
            string name = "fw-slow-" + Guid.NewGuid().ToString("N");
            Path = @"\\.\pipe\" + name;
            byte[] bytes = File.ReadAllBytes(file);
            var frames = TestVideos.FramePlaces(file);
            _pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            new Thread(() =>
            {
                try
                {
                    _pipe.WaitForConnectionAsync(_stop.Token).Wait();
                    var watch = Stopwatch.StartNew();
                    int sent = 0;
                    foreach (var frame in frames)
                    {
                        double due = (frame.Seconds - fullSpeedSeconds) / pace;
                        while (watch.Elapsed.TotalSeconds < due)
                        {
                            if (_stop.Token.WaitHandle.WaitOne(5)) return;
                        }

                        int upTo = (int)Math.Min(bytes.Length, frame.EndsAt);
                        if (upTo <= sent) continue;
                        _pipe.Write(bytes, sent, upTo - sent);
                        _pipe.Flush();
                        sent = upTo;
                    }
                    _pipe.Write(bytes, sent, bytes.Length - sent);
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is AggregateException || ex is OperationCanceledException)
                {
                    // Whoever was reading has gone, or nobody ever came.
                }
                finally
                {
                    _pipe.Dispose();
                }
            }) { IsBackground = true }.Start();
        }

        /// <summary>What to open in place of the file.</summary>
        public string Path { get; }

        public void Dispose() => _stop.Cancel();
    }
}
