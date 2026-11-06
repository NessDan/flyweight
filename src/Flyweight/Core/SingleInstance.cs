using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Flyweight.Core
{
    /// <summary>
    /// Keeps one window running. Selecting ten videos and using the right-click menu makes Explorer
    /// start ten copies of this app; all but the first hand over their file here and exit, so the
    /// videos queue up in one window instead of competing for the graphics card.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private readonly string _id;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private Mutex _mutex;

        public SingleInstance(string id)
        {
            _id = id;
        }

        public bool TryBecomePrimary()
        {
            var mutex = new Mutex(true, MutexName(_id), out bool createdNew);
            if (createdNew)
            {
                _mutex = mutex;
                return true;
            }
            mutex.Dispose();
            return false;
        }

        public void StartListening(Action<string> onMessage)
        {
            var thread = new Thread(() => Listen(onMessage)) { IsBackground = true, Name = "SingleInstance listener" };
            thread.Start();
        }

        /// <summary>Returns false if nobody picked up within the timeout.</summary>
        public static bool Send(string id, string message, int timeoutMs)
        {
            int remaining = timeoutMs;
            while (true)
            {
                int started = Environment.TickCount;
                try
                {
                    using (var pipe = new NamedPipeClientStream(".", PipeName(id), PipeDirection.Out))
                    {
                        pipe.Connect(Math.Max(1, remaining));
                        byte[] bytes = Encoding.UTF8.GetBytes(message);
                        pipe.Write(bytes, 0, bytes.Length);
                        pipe.Flush();
                        return true;
                    }
                }
                catch (Exception ex) when (ex is TimeoutException || ex is IOException)
                {
                    // Everyone's busy, or the listener is between connections. Try again until time runs out.
                    remaining -= Math.Max(Environment.TickCount - started, 20);
                    if (remaining <= 0) return false;
                    Thread.Sleep(20);
                }
            }
        }

        public void Dispose()
        {
            _stop.Set();
            if (_mutex == null) return;

            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Only the thread that took a mutex can release it. Closing the handle below is enough.
            }
            _mutex.Dispose();
            _mutex = null;
        }

        private void Listen(Action<string> onMessage)
        {
            while (!_stop.WaitOne(0))
            {
                try
                {
                    using (var server = new NamedPipeServerStream(PipeName(_id), PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        // Wait on the stop signal directly. Giving the pipe a cancellation token instead
                        // leaves callbacks behind that throw when cancelled, because their pipes are closed.
                        var connecting = server.BeginWaitForConnection(null, null);
                        if (WaitHandle.WaitAny(new[] { connecting.AsyncWaitHandle, _stop }) == 1) return;
                        server.EndWaitForConnection(connecting);

                        using (var reader = new StreamReader(server, Encoding.UTF8))
                        {
                            string message = reader.ReadToEnd();
                            try
                            {
                                onMessage(message);
                            }
                            catch (Exception)
                            {
                                // One bad request must not stop later ones from getting through.
                            }
                        }
                    }
                }
                catch (IOException)
                {
                    // A client vanished mid-message. Carry on.
                }
            }
        }

        private static string MutexName(string id) => @"Local\" + id;

        // Named pipes are visible across sessions, so keep each Windows user's separate.
        private static string PipeName(string id) => id + "-" + Environment.UserName;
    }
}
