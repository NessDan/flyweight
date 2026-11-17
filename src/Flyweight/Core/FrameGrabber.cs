using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Flyweight.Core
{
    /// <summary>
    /// Pulls single pictures out of a video with ffmpeg, for choosing where to trim. Using the same
    /// ffmpeg that does the compressing means any video the app can compress can be previewed, and
    /// the picture shown for a moment is the frame a cut at that moment really starts on.
    /// </summary>
    public sealed class FrameGrabber
    {
        private const double FirstReach = 2;
        private const double ReachGrowth = 5;

        // Half the precision ffprobe prints times to, so a frame exactly at a moment never counts as before it.
        private const double Hair = 0.0000005;

        private readonly string _ffmpeg;
        private readonly string _ffprobe;
        private readonly string _input;
        private readonly MediaInfo _media;
        private readonly int _streamIndex;

        // What worked last time, so an awkward file or an old ffmpeg costs a retry once, not on every picture.
        private volatile bool _toneMap;
        private volatile bool _lean = true;
        private volatile bool _quickWorks = true;

        public FrameGrabber(FFmpegTools tools, string input, MediaInfo media)
        {
            _ffmpeg = tools.FFmpeg;
            _ffprobe = tools.FFprobe;
            _input = input;
            _media = media;
            _streamIndex = media.Video.Index;
            _toneMap = media.Video.IsHdr;
        }

        /// <summary>
        /// Whether pictures are being brought into the range an ordinary window shows, as HDR video
        /// needs. It stops being true if the ffmpeg on this PC turns out not to be able to.
        /// </summary>
        internal bool ToneMaps => _toneMap;

        /// <summary>How long ffmpeg gets for one picture before it's stopped.</summary>
        internal int TimeLimitMs { get; set; } = 30000;

        /// <summary>
        /// The picture at a moment, as the bytes of a BMP file, or null when there's none to show.
        /// An exact picture is the first frame at or after that moment. A quick one is the keyframe
        /// before it, which takes a fraction of the time however far apart the keyframes are.
        /// </summary>
        /// <exception cref="TimeoutException">ffmpeg had to be stopped, which says nothing about whether there's a frame.</exception>
        public byte[] Grab(double seconds, int height, bool exact, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!_quickWorks) exact = true;

            while (true)
            {
                bool toneMap = _toneMap;
                bool lean = _lean;
                double runUp = Seeking.RunUp(_media, seconds);
                byte[] picture = Run(_ffmpeg, BuildArgs(_input, _streamIndex, seconds, height, exact, toneMap, lean, runUp), token, out bool worked);
                if (worked) return picture.Length > 0 ? picture : null;

                // ffmpeg wouldn't do it. Drop whichever nicety might be the reason and ask again.
                if (!exact)
                {
                    _quickWorks = false;
                    exact = true;
                }
                else if (toneMap)
                {
                    _toneMap = false;
                }
                else if (lean)
                {
                    _lean = false;
                }
                else
                {
                    return null;
                }
            }
        }

        /// <param name="runUp">How far before the moment to start reading. See <see cref="Seeking"/>.</param>
        internal static List<string> BuildArgs(string input, int streamIndex, double seconds, int height, bool exact, bool toneMap, bool lean, double runUp)
        {
            var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error" };
            seconds = Math.Max(seconds, 0);
            if (!exact) runUp = 0;   // any keyframe nearby will do for a quick one

            // The file's layout is already known, so ffmpeg needn't study it again for every picture.
            if (lean) args.AddRange(new[] { "-probesize", "2M", "-analyzeduration", "200000" });

            if (!exact) args.AddRange(new[] { "-skip_frame", "nokey", "-noaccurate_seek" });
            args.AddRange(new[] { "-ss", TrimRange.Seconds(seconds - runUp), "-i", input });
            // After the input, -ss decodes and then drops that much, which is how the lead-in goes.
            if (runUp > 0) args.AddRange(new[] { "-ss", TrimRange.Seconds(runUp) });
            args.AddRange(new[] { "-map", "0:" + streamIndex, "-an", "-sn", "-dn" });

            // Without this, ffmpeg throws the keyframe away for being earlier than the moment asked for.
            if (!exact) args.AddRange(new[] { "-fps_mode", "passthrough" });

            // Sized from the shape the video is shown at: camcorders and DVDs store a narrower picture
            // than they show, and a plain picture file has no way to say so.
            string filters = $"scale='max(2,round(oh*dar/2)*2)':'min({height},ih)',setsar=1"
                + (toneMap ? "," + CommandBuilder.ToneMapToSdr : "") + ",format=bgr24";
            args.AddRange(new[] { "-frames:v", "1", "-vf", filters, "-f", "image2pipe", "-c:v", "bmp", "-" });
            return args;
        }

        /// <summary>
        /// When the last frame before a moment is, in seconds from the start of the file. That's the
        /// last frame a cut ending at that moment keeps. It's read from the file rather than worked
        /// out from the frame rate, because recordings often don't space their frames evenly: a screen
        /// recording has none while nothing moves, then sixty a second. Null when there's no such
        /// frame or the file wouldn't say.
        /// </summary>
        public double? LastFrameBefore(double seconds, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (seconds <= 0) return null;

            double fileStart = _media.StartSeconds;
            for (double reach = FirstReach; ; reach *= ReachGrowth)
            {
                // Looks a little way back first, then further, for the stretches where nothing moved.
                double from = Math.Max(0, seconds - reach);
                var args = new List<string>
                {
                    "-v", "error", "-select_streams", _streamIndex.ToString(CultureInfo.InvariantCulture),
                    "-read_intervals", Clock(from + fileStart) + "%" + Clock(seconds + fileStart),
                    "-show_entries", "packet=pts_time", "-of", "csv=p=0", "-i", _input,
                };
                string listed = Encoding.ASCII.GetString(Run(_ffprobe, args, token, out bool worked));
                if (!worked) return null;

                double? last = null;
                foreach (string line in listed.Split('\n'))
                {
                    string first = line.Split(',')[0].Trim();
                    if (!double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out double onFileClock)) continue;

                    double time = onFileClock - fileStart;
                    if (time < seconds - Hair && (last == null || time > last.Value)) last = time;
                }

                // Rounded down to the millisecond, so asking for the frame at this time finds this frame.
                if (last != null) return Math.Max(0, Math.Floor(last.Value * 1000 + 0.001) / 1000);
                if (from <= 0) return null;
            }
        }

        private static string Clock(double seconds) => seconds.ToString("0.######", CultureInfo.InvariantCulture);

        /// <summary>Runs ffmpeg or ffprobe and returns everything it wrote, which is nothing if it couldn't start.</summary>
        /// <param name="worked">False when it gave up with an error, as opposed to having nothing to give.</param>
        private byte[] Run(string program, List<string> args, CancellationToken token, out bool worked)
        {
            worked = false;
            var info = new ProcessStartInfo(program, ProcessArgs.Join(args))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (var process = new Process { StartInfo = info })
            {
                try
                {
                    process.Start();
                }
                catch (Win32Exception)
                {
                    return new byte[0];
                }

                ChildProcessGuard.Attach(process);
                // Read and dropped, so it can't stall on a full pipe while explaining a problem.
                process.ErrorDataReceived += (s, e) => { };
                process.BeginErrorReadLine();

                bool timedOut = false;
                using (token.Register(() => ChildProcessGuard.Kill(process)))
                using (new Timer(_ => { timedOut = true; ChildProcessGuard.Kill(process); }, null, TimeLimitMs, Timeout.Infinite))
                using (var written = new MemoryStream())
                {
                    // Ends when the program closes its output, whether it finished or was stopped.
                    process.StandardOutput.BaseStream.CopyTo(written);
                    process.WaitForExit();
                    token.ThrowIfCancellationRequested();

                    // Asking again in another way wouldn't be any quicker, and it isn't the same as
                    // there being nothing to show, so whoever asked gets to hear about it.
                    if (timedOut) throw new TimeoutException("Getting a picture of this video took too long.");

                    worked = process.ExitCode == 0;
                    return written.ToArray();
                }
            }
        }
    }
}
