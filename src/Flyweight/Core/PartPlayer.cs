using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Flyweight.Core
{
    /// <summary>
    /// Plays one part of a video, with its sound, for the trim controls to draw. ffmpeg reads the
    /// video twice at once: for its pictures, shrunk to the size they're shown at, and for its sound.
    /// Each picture is handed out when its moment comes round, and the clock that's judged by is the
    /// sound itself wherever there is some, which is what keeps the two in step. So it plays anything
    /// that can be compressed, and starts and ends on the frames a trim would.
    /// </summary>
    public sealed class PartPlayer : IDisposable
    {
        /// <summary>The colour of the box the pictures are shown in, for the space a video doesn't fill.</summary>
        internal const string Surround = "0x101010";

        // Pictures made ready ahead of the one on show: enough to ride out a busy moment.
        private const int Ahead = 4;

        /// <summary>How far behind a picture may fall before the sound is made to wait for it.</summary>
        private const double Patience = 0.25;

        private const double MostFramesASecond = 60;
        private const double FewestSteadyFramesASecond = 20;
        private const double AssumedFramesASecond = 30;

        private readonly FFmpegTools _tools;
        private readonly string _input;
        private readonly MediaInfo _media;
        private readonly Stopwatch _watch = new Stopwatch();

        private Run _run;
        private double _length;
        private bool _started;
        private bool _waiting;
        private double _heard;
        private double _clockFrom;
        private Picture _next;
        private Picture _showing;

        /// <summary>One picture, and which one it is, counting from the first of the part.</summary>
        private sealed class Picture
        {
            public byte[] Dots;
            public int Index;
        }

        /// <summary>
        /// What one go at playing shares with the two threads reading for it, which may outlast it by
        /// a moment. Whoever touches the ffmpegs or swaps the sound holds a lock on it meanwhile.
        /// </summary>
        private sealed class Run
        {
            public readonly CancellationTokenSource Stopping = new CancellationTokenSource();
            public readonly BlockingCollection<Picture> Spare = new BlockingCollection<Picture>();
            public readonly BlockingCollection<Picture> Ready = new BlockingCollection<Picture>();

            /// <summary>Which picture's moment it is now, as of the last time the screen asked.</summary>
            public volatile int DueIndex;

            /// <summary>True while as many pictures as there's room for are waiting to be shown: ffmpeg is keeping up.</summary>
            public volatile bool HasPlentyReady;

            public Process Pictures;
            public Process Sounds;
            public volatile SoundOut Sound;
            public volatile int PicturesRead;
            public volatile bool PicturesOver;
            public volatile bool CouldNotRead;
            public volatile bool SoundReady;
        }

        public PartPlayer(FFmpegTools tools, string input, MediaInfo media)
        {
            _tools = tools;
            _input = input;
            _media = media;
            FramesASecond = RateFor(media.Video.Fps);
        }

        /// <summary>What sound goes out through. Tests hand in something that can't be heard.</summary>
        internal Func<SoundOut> NewSound { get; set; } = () => new SoundOut();

        /// <summary>How many pictures a second it shows.</summary>
        public double FramesASecond { get; }

        /// <summary>The size of the pictures it's handing out, in dots.</summary>
        public int Width { get; private set; }

        public int Height { get; private set; }

        /// <summary>True from being told to play until being told to stop.</summary>
        public bool IsPlaying => _run != null;

        /// <summary>Where the picture last handed out is, in seconds from where playing started.</summary>
        public double Seconds { get; private set; }

        /// <summary>True once the part has played through to its end.</summary>
        public bool HasEnded
        {
            get
            {
                var run = _run;
                if (run == null) return false;
                // A stretch with sound but not one picture left in it is over before it began.
                return _started ? Now(run) >= _length : run.PicturesOver && run.PicturesRead == 0 && !run.CouldNotRead;
            }
        }

        /// <summary>True when ffmpeg couldn't make pictures of this video at all.</summary>
        public bool HasFailed => _run != null && _run.PicturesOver && _run.CouldNotRead;

        /// <summary>How many pictures ffmpeg has made so far, shown or not.</summary>
        internal int PicturesRead => _run?.PicturesRead ?? 0;

        /// <summary>
        /// Starts playing from a moment in the video, for so many seconds, in place of whatever was
        /// playing. Pictures come out this size exactly, with the video fitted inside.
        /// </summary>
        public void Play(double from, double length, int width, int height) => Play(from, length, width, height, _media.Video.IsHdr);

        /// <param name="toneMap">Whether to bring the picture into the range an ordinary window shows, as HDR video needs.</param>
        public void Play(double from, double length, int width, int height, bool toneMap)
        {
            Stop();

            var run = new Run();
            Width = width;
            Height = height;
            _length = length;
            for (int i = 0; i < Ahead + 2; i++) run.Spare.Add(new Picture { Dots = new byte[width * height * 4] });

            // Starting ffmpeg takes a moment, and so can waking whatever the sound goes out through,
            // so neither is waited for here: whoever asked has a window to keep answering.
            var pictures = PictureArgs(_input, _media, from, length, width, height, FramesASecond, toneMap);
            InBackground(() => ReadPictures(run, pictures));
            if (_media.Audio.Count > 0)
            {
                var sounds = SoundArgs(_input, _media, from, length);
                InBackground(() => FeedSound(run, sounds));
            }
            else
            {
                run.SoundReady = true;
            }

            _run = run;
        }

        /// <summary>
        /// The picture whose moment has now come, as plain bytes, four to a dot: blue, green, red and
        /// a spare. Null when the one handed out last is still the one to show. Its bytes are only
        /// good until the next call, so they're for copying to the screen there and then.
        /// </summary>
        public byte[] NextPicture()
        {
            var run = _run;
            if (run == null) return null;

            if (!_started)
            {
                // Nothing moves until there's a picture to show and, where there's sound, some of that to go with it.
                if (run.Ready.Count == 0 || !run.SoundReady) return null;
                _started = true;
                Go(run);
            }

            double now = Now(run);
            run.DueIndex = (int)(now * FramesASecond);
            // Asked before any are taken: taking them is what makes room for more.
            bool hadPlentyReady = run.HasPlentyReady;

            Picture newest = null;
            while (true)
            {
                if (_next == null && !run.Ready.TryTake(out _next)) break;
                if (_next.Index / FramesASecond > now) break;

                // Overtaken before it could be shown: the screen was busy, and the sound doesn't wait for that.
                if (newest != null) run.Spare.Add(newest);
                newest = _next;
                _next = null;
            }
            if (newest != null)
            {
                if (_showing != null) run.Spare.Add(_showing);
                _showing = newest;
                Seconds = newest.Index / FramesASecond;
            }

            // ffmpeg can't always make pictures as fast as they're shown. Rather than let the sound
            // run on ahead of pictures falling further and further behind, both wait for the next one.
            // Not when it's the screen that's slow, with pictures ready and waiting: there the sound
            // has nothing to wait for, and the reading skips to pictures from about now instead.
            int awaited = _showing == null ? 0 : _showing.Index + 1;
            bool late = !hadPlentyReady && _next == null && !run.PicturesOver && now > awaited / FramesASecond + Patience;
            if (late != _waiting)
            {
                _waiting = late;
                if (late) Hold(run);
                else Go(run);
            }
            return newest?.Dots;
        }

        /// <summary>Stops, and lets go of the video and of the sound.</summary>
        public void Stop()
        {
            var run = _run;
            _run = null;
            _started = false;
            _waiting = false;
            _next = null;
            _showing = null;
            _heard = 0;
            _clockFrom = 0;
            Seconds = 0;
            _watch.Reset();
            if (run == null) return;

            lock (run)
            {
                try
                {
                    run.Stopping.Cancel();
                    // Ending ffmpeg is the only way to break off a read that's waiting on it.
                    if (run.Pictures != null) ChildProcessGuard.Kill(run.Pictures);
                    if (run.Sounds != null) ChildProcessGuard.Kill(run.Sounds);
                }
                finally
                {
                    run.Sound?.Dispose();
                }
            }
        }

        public void Dispose() => Stop();

        private void Go(Run run)
        {
            _watch.Start();
            run.Sound?.Resume();
        }

        private void Hold(Run run)
        {
            _watch.Stop();
            run.Sound?.Pause();
        }

        /// <summary>
        /// How far into the part playing has got. The sound is the clock wherever it's moving. Between
        /// its ticks, and where there is none, before it starts or after it runs out, a stopwatch
        /// carries on from the last thing it said.
        /// </summary>
        private double Now(Run run)
        {
            double heard = run.Sound?.Seconds ?? 0;
            if (heard != _heard)
            {
                // It's how much further the sound has got that counts. A count that has gone down has
                // started again, or its output has gone, and there's nothing to learn from that.
                if (heard > _heard)
                {
                    _clockFrom += heard - _heard;
                    if (_watch.IsRunning) _watch.Restart();
                    else _watch.Reset();
                }
                _heard = heard;
            }
            return _clockFrom + _watch.Elapsed.TotalSeconds;
        }

        private void ReadPictures(Run run, List<string> args)
        {
            try
            {
                var ffmpeg = Begin(run, args, ref run.Pictures);
                if (ffmpeg == null)
                {
                    run.CouldNotRead = !run.Stopping.IsCancellationRequested;
                    return;
                }

                var from = ffmpeg.StandardOutput.BaseStream;
                int index = 0;
                bool more = true;
                while (more)
                {
                    // Waits here while every picture there's room for is still waiting to be shown,
                    // which is what stops ffmpeg racing ahead through the whole video.
                    bool hadToWait = !run.Spare.TryTake(out Picture picture);
                    if (hadToWait)
                    {
                        run.HasPlentyReady = true;
                        picture = run.Spare.Take(run.Stopping.Token);
                        run.HasPlentyReady = false;
                    }

                    do
                    {
                        more = Fill(from, picture.Dots) == picture.Dots.Length;
                        if (!more) break;
                        picture.Index = index++;
                        run.PicturesRead++;
                        // Having had to wait for room, the next few may have had their moment by now,
                        // if the screen is only being drawn a few times a second. Those are passed
                        // over, so that what's waiting to be shown is never far behind the clock.
                    }
                    while (hadToWait && picture.Index < run.DueIndex);

                    if (more) run.Ready.Add(picture);
                }

                // With not one picture, either ffmpeg couldn't read the video or that stretch of it has none.
                ffmpeg.WaitForExit();
                run.CouldNotRead = run.PicturesRead == 0 && ffmpeg.ExitCode != 0;
            }
            catch (Exception ex) when (IsStopping(ex))
            {
            }
            finally
            {
                run.PicturesOver = true;
                LetGo(run, ref run.Pictures);
            }
        }

        private void FeedSound(Run run, List<string> args)
        {
            try
            {
                // A PC with nothing to play sound through still gets the pictures, by the clock.
                var sound = NewSound();
                if (sound == null || !sound.Open()) return;
                lock (run)
                {
                    if (run.Stopping.IsCancellationRequested)
                    {
                        sound.Dispose();
                        return;
                    }
                    run.Sound = sound;
                }

                var ffmpeg = Begin(run, args, ref run.Sounds);
                if (ffmpeg == null) return;

                var from = ffmpeg.StandardOutput.BaseStream;
                var block = new byte[SoundOut.BlockBytes];
                int filled;
                // What it's handed to only takes it in as fast as it's heard, which paces the reading.
                while ((filled = Fill(from, block)) > 0 && sound.Write(block, filled, run.Stopping.Token)) run.SoundReady = true;
            }
            catch (Exception ex) when (IsStopping(ex))
            {
            }
            finally
            {
                // Whether or not any sound came, the pictures have nothing more to wait for.
                run.SoundReady = true;
                LetGo(run, ref run.Sounds);
            }
        }

        /// <summary>
        /// Starts ffmpeg for one go at playing and puts it where whoever stops that go can end it.
        /// Null if it wouldn't start, or if the stopping has already happened.
        /// </summary>
        private Process Begin(Run run, List<string> args, ref Process place)
        {
            var ffmpeg = Start(args);
            if (ffmpeg == null) return null;

            lock (run)
            {
                if (!run.Stopping.IsCancellationRequested) return place = ffmpeg;
            }
            ChildProcessGuard.Kill(ffmpeg);
            ffmpeg.Dispose();
            return null;
        }

        /// <summary>
        /// Ends ffmpeg if it's still going, which it is whenever the reading stopped before it did:
        /// with nobody reading, it would wait for ever with the video held open.
        /// </summary>
        private static void LetGo(Run run, ref Process place)
        {
            lock (run)
            {
                if (place == null) return;
                ChildProcessGuard.Kill(place);
                place.Dispose();
                place = null;
            }
        }

        /// <summary>What reading throws when it was told to stop, or ffmpeg has gone.</summary>
        private static bool IsStopping(Exception ex)
        {
            return ex is OperationCanceledException || ex is IOException || ex is InvalidOperationException;
        }

        /// <summary>Reads until the buffer is full or there's no more to come, and says how much it got.</summary>
        private static int Fill(Stream from, byte[] buffer)
        {
            int filled = 0;
            int read;
            while (filled < buffer.Length && (read = from.Read(buffer, filled, buffer.Length - filled)) > 0) filled += read;
            return filled;
        }

        private static void InBackground(ThreadStart work) => new Thread(work) { IsBackground = true }.Start();

        private Process Start(List<string> args)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(_tools.FFmpeg, ProcessArgs.Join(args))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                },
            };
            try
            {
                process.Start();
            }
            catch (Win32Exception)
            {
                process.Dispose();
                return null;
            }

            // So it can't outlive the app, whatever happens to it.
            ChildProcessGuard.Attach(process);
            return process;
        }

        /// <summary>
        /// How many pictures a second to show for a video with this many. More than a screen shows is
        /// wasted. Fewer than about twenty is the average of a recording that stood still now and
        /// then, or wasn't said at all, and those are better shown at an ordinary rate.
        /// </summary>
        internal static double RateFor(double fps)
        {
            return fps >= FewestSteadyFramesASecond ? Math.Min(fps, MostFramesASecond) : AssumedFramesASecond;
        }

        internal static List<string> PictureArgs(string input, MediaInfo media, double from, double length, int width, int height, double fps, bool toneMap)
        {
            var args = Reading(input, media, from, length, out string end);
            args.AddRange(new[] { "-map", "0:" + media.Video.Index, "-an", "-sn", "-dn" });

            // A steady rate counted from the part's first moment, so each picture's moment is known
            // from its place in the line: where the video's own pictures are further apart, the one
            // before fills in. Then the video is fitted, in the shape it's shown at, into the space
            // it's shown in, and the rest of that space is filled in around it. So every picture is
            // exactly that size, which is the only way to tell where one ends in a run of plain bytes.
            string wider = $"gt(dar,{width}/{height})";
            string filters = $"trim=end={end},fps={fps.ToString("0.###", CultureInfo.InvariantCulture)}:start_time=0,"
                + $"scale='if({wider},{width},max(2,round({height}*dar/2)*2))':'if({wider},max(2,round({width}/dar/2)*2),{height})',setsar=1"
                + (toneMap ? "," + CommandBuilder.ToneMapToSdr : "")
                + $",format=bgr0,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color={Surround}";

            args.AddRange(new[] { "-vf", filters, "-f", "rawvideo", "-" });
            return args;
        }

        internal static List<string> SoundArgs(string input, MediaInfo media, double from, double length)
        {
            var args = Reading(input, media, from, length, out _);
            // One track is enough to hear where a cut falls, and stereo is all a preview needs.
            args.AddRange(new[] { "-map", "0:" + media.Audio[0].Index, "-vn", "-sn", "-dn" });

            // Plain samples carry no times. A track that began a moment after the part did would be
            // played from the part's first instant, so it's led in with silence to where it belongs.
            args.AddRange(new[]
            {
                "-af", "aresample=async=1:first_pts=0", "-ac", "2", "-ar", SoundOut.Rate.ToString(CultureInfo.InvariantCulture),
                "-f", "s16le", "-",
            });
            return args;
        }

        /// <summary>How both begin: read only this part, cut the way a trim is cut. See CommandBuilder and Seeking.</summary>
        private static List<string> Reading(string input, MediaInfo media, double from, double length, out string end)
        {
            double runUp = Seeking.RunUp(media, from);
            end = TrimRange.Seconds(runUp + length);

            var args = new List<string>
            {
                "-hide_banner", "-nostdin", "-loglevel", "error",
                "-ss", TrimRange.Seconds(from - runUp), "-t", end, "-i", input,
            };
            // After the input, -ss decodes and then drops that much, which is how the lead-in goes.
            if (runUp > 0) args.AddRange(new[] { "-ss", TrimRange.Seconds(runUp) });
            return args;
        }
    }
}
