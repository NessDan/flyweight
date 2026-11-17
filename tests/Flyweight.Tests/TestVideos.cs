using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Flyweight.Core;

namespace Flyweight.Tests
{
    /// <summary>A temp folder plus real ffmpeg, for making small clips and inspecting results.</summary>
    internal sealed class TestVideos : IDisposable
    {
        public static readonly FFmpegTools Tools = FFmpegLocator.Find();
        private static readonly Lazy<bool> Nvenc = new Lazy<bool>(() => CheckNvenc());

        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "fw-it-" + Guid.NewGuid().ToString("N"));

        public static bool HasNvenc => Nvenc.Value;

        public TestVideos()
        {
            Directory.CreateDirectory(Folder);
        }

        public void Dispose()
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    Directory.Delete(Folder, true);
                    return;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(200);
                }
            }
        }

        public string PathOf(string name) => Path.Combine(Folder, name);

        /// <summary>Runs ffmpeg with the given arguments, writing to a file of the given name.</summary>
        public string Make(string name, params string[] args)
        {
            string path = PathOf(name);
            var all = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };
            all.AddRange(args);
            all.Add(path);
            var (code, _, err) = Run(Tools.FFmpeg, all);
            if (code != 0) throw new InvalidOperationException("Could not make test clip " + name + ": " + err);
            return path;
        }

        /// <summary>Busy synthetic video in a bulky old codec, so there's plenty to compress.</summary>
        public string MakeBulkyClip(string name, int seconds = 3, string size = "1280x720")
        {
            return Make(name,
                "-f", "lavfi", "-i", $"testsrc2=s={size}:r=30:d={seconds}",
                "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}",
                "-c:v", "mpeg4", "-q:v", "1", "-c:a", "aac", "-b:a", "320k");
        }

        /// <summary>
        /// Ten seconds at 30 frames a second whose only keyframe is the very first frame, which is the
        /// hardest case for an exact cut. A tone starts five seconds in.
        /// </summary>
        public string MakeOneKeyframeClip(string name)
        {
            return Make(name,
                "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=10",
                "-f", "lavfi", "-i", "aevalsrc='if(gte(t,5),0.5*sin(2*PI*1000*t),0)':d=10:s=48000",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "16",
                "-x264-params", "keyint=600:min-keyint=600:scenecut=0", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "128k");
        }

        /// <summary>
        /// A clip whose every frame says which one it is: two grey blocks side by side, the left as
        /// bright as the frame number's tens and the right as bright as its units. Kept without any
        /// loss, so the numbers survive. Good for about a hundred frames.
        /// </summary>
        /// <param name="keyframeEvery">How many frames apart the keyframes are. By default the first frame is the only one.</param>
        /// <param name="silent">With a sound track that has nothing on it, for the tests that play through Windows for real.</param>
        /// <param name="standsStillUntil">
        /// Up to this many seconds in, only every fifteenth frame is kept, as in a recording of a screen
        /// where little moved. Those that are left keep the numbers they had.
        /// </param>
        public string MakeNumberedClip(string name, double seconds = 3.5, int fps = 30, bool withSound = false, int keyframeEvery = 600, bool silent = false, double standsStillUntil = 0)
        {
            string length = seconds.ToString(CultureInfo.InvariantCulture);
            string still = standsStillUntil > 0 ? $",select='if(lt(t,{standsStillUntil.ToString(CultureInfo.InvariantCulture)}),not(mod(n,15)),1)'" : "";
            var args = new List<string>
            {
                "-f", "lavfi", "-i",
                $"color=c=black:s=320x180:r={fps}:d={length},format=yuv420p,geq=lum='16+20*if(lt(X,W/2),floor(N/10),mod(N,10))':cb=128:cr=128{still}",
            };
            if (silent) args.AddRange(new[] { "-f", "lavfi", "-t", length, "-i", "anullsrc=r=48000:cl=stereo" });
            else if (withSound) args.AddRange(new[] { "-f", "lavfi", "-i", $"sine=frequency=440:sample_rate=48000:duration={length}" });
            withSound |= silent;
            args.AddRange(new[]
            {
                "-c:v", "libx264", "-preset", "ultrafast", "-qp", "0", "-pix_fmt", "yuv420p",
                "-x264-params", $"keyint={keyframeEvery}:min-keyint={keyframeEvery}:scenecut=0",
            });
            if (withSound) args.AddRange(new[] { "-c:a", "aac", "-b:a", "128k" });
            if (standsStillUntil > 0) args.AddRange(new[] { "-fps_mode", "vfr" });
            return Make(name, args.ToArray());
        }

        /// <summary>When each frame of a file's video is, and where in the file its last byte lies, in the order they're stored.</summary>
        public static List<(double Seconds, long EndsAt)> FramePlaces(string path)
        {
            var (code, output, err) = Run(Tools.FFprobe, new[]
            {
                "-v", "error", "-select_streams", "v:0", "-show_entries", "packet=pts_time,size,pos", "-of", "csv=p=0", "-i", path,
            });
            if (code != 0) throw new InvalidOperationException("ffprobe failed: " + err);

            var places = new List<(double, long)>();
            foreach (string line in output.Split('\n'))
            {
                string[] parts = line.Trim().Split(',');
                if (parts.Length < 3 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)) continue;
                places.Add((seconds, long.Parse(parts[1], CultureInfo.InvariantCulture) + long.Parse(parts[2], CultureInfo.InvariantCulture)));
            }
            return places;
        }

        /// <summary>
        /// Which frame of a numbered clip a picture is. The picture is plain bytes, four to a dot:
        /// blue, green, red and a spare.
        /// </summary>
        public static int FrameNumber(byte[] picture, int width, int height)
        {
            // Video keeps its greys between 16 and 235, which a picture stretches to 0 to 255.
            int Digit(int x) => (int)Math.Round(picture[(height / 2 * width + x) * 4] / (20 * 255.0 / 219));
            return Digit(width / 4) * 10 + Digit(width * 3 / 4);
        }

        /// <summary>
        /// The brightness of every pixel of one frame, half a second in, the right way up: its stored
        /// pixels, one byte each, row after row.
        /// </summary>
        public static (int Width, int Height, byte[] Dots) Gray(string path)
        {
            var video = Probe(path).Video;
            string raw = Path.Combine(Path.GetTempPath(), "fw-gray-" + Guid.NewGuid().ToString("N") + ".raw");
            try
            {
                var (code, _, err) = Run(Tools.FFmpeg, new[]
                {
                    "-hide_banner", "-loglevel", "error", "-ss", "0.5", "-i", path, "-frames:v", "1",
                    "-f", "rawvideo", "-pix_fmt", "gray", "-y", raw,
                });
                if (code != 0) throw new InvalidOperationException("Couldn't read a frame: " + err);
                var dots = File.ReadAllBytes(raw);
                if (dots.Length != video.DisplayWidth * video.DisplayHeight) throw new InvalidOperationException($"{dots.Length} bytes for a frame {video.DisplayWidth} by {video.DisplayHeight}");
                return (video.DisplayWidth, video.DisplayHeight, dots);
            }
            finally
            {
                File.Delete(raw);
            }
        }

        /// <summary>How far apart a small picture is from the part of a bigger one at (x, y), on average, from 0 to 255.</summary>
        public static double Difference((int Width, int Height, byte[] Dots) whole, int x, int y, (int Width, int Height, byte[] Dots) part)
        {
            if (x + part.Width > whole.Width || y + part.Height > whole.Height) return 255;
            long total = 0;
            for (int row = 0; row < part.Height; row++)
            {
                for (int column = 0; column < part.Width; column++)
                {
                    total += Math.Abs(part.Dots[row * part.Width + column] - whole.Dots[(y + row) * whole.Width + x + column]);
                }
            }
            return total / (double)(part.Width * part.Height);
        }

        /// <summary>How many video frames a file really holds, counted by decoding it.</summary>
        public static int FrameCount(string path)
        {
            var (code, output, err) = Run(Tools.FFprobe, new[]
            {
                "-v", "error", "-count_frames", "-select_streams", "v:0",
                "-show_entries", "stream=nb_read_frames", "-of", "csv=p=0", "-i", path,
            });
            if (code != 0) throw new InvalidOperationException("ffprobe failed: " + err);
            return int.Parse(Regex.Match(output, @"\d+").Value);
        }

        /// <summary>
        /// How closely a clip matches the original from a given frame of the original onward, in decibels.
        /// It's high when the two line up and drops sharply when they are even one frame apart.
        /// </summary>
        public static double Likeness(string clip, string original, int originalStartFrame)
        {
            var (code, output, err) = Run(Tools.FFmpeg, new[]
            {
                "-hide_banner", "-loglevel", "error", "-i", clip, "-i", original,
                "-filter_complex",
                $"[1:v]trim=start_frame={originalStartFrame},setpts=PTS-STARTPTS[ref];[0:v]setpts=PTS-STARTPTS[cut];[cut][ref]psnr=stats_file=-:shortest=1",
                "-an", "-f", "null", "-",
            });
            if (code != 0) throw new InvalidOperationException("psnr failed: " + err);

            var values = Regex.Matches(output, @"psnr_avg:(inf|[0-9.]+)").Cast<Match>()
                .Select(m => m.Groups[1].Value == "inf" ? 100.0 : double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                .ToList();
            if (values.Count == 0) throw new InvalidOperationException("psnr printed nothing");
            return values.Average();
        }

        /// <summary>
        /// How many packets of sound a file really holds, including any that a player is told to skip.
        /// An AAC packet is 1024 samples, so at 48 kHz there are about 47 to the second.
        /// </summary>
        public static int SoundPackets(string path)
        {
            var (code, output, err) = Run(Tools.FFprobe, new[]
            {
                "-v", "error", "-ignore_editlist", "1", "-select_streams", "a:0", "-count_packets",
                "-show_entries", "stream=nb_read_packets", "-of", "csv=p=0", "-i", path,
            });
            if (code != 0) throw new InvalidOperationException("ffprobe failed: " + err);
            return int.Parse(Regex.Match(output, @"\d+").Value);
        }

        /// <summary>How much sound a file holds, in seconds, added up packet by packet.</summary>
        public static double SoundSeconds(string path)
        {
            var (code, output, err) = Run(Tools.FFprobe, new[]
            {
                "-v", "error", "-select_streams", "a:0", "-show_entries", "packet=duration_time", "-of", "csv=p=0", "-i", path,
            });
            if (code != 0) throw new InvalidOperationException("ffprobe failed: " + err);
            return Regex.Matches(output, @"\d+(\.\d+)?").Cast<Match>().Sum(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>When the first sound starts, in seconds, in a clip that opens with silence.</summary>
        public static double FirstSoundAt(string path)
        {
            var (code, _, err) = Run(Tools.FFmpeg, new[]
            {
                "-hide_banner", "-loglevel", "info", "-i", path, "-vn",
                "-af", "silencedetect=noise=-30dB:d=0.05", "-f", "null", "-",
            });
            if (code != 0) throw new InvalidOperationException("silencedetect failed: " + err);

            var match = Regex.Match(err, @"silence_end: ([0-9.]+)");
            if (!match.Success) throw new InvalidOperationException("found no silence to measure from");
            return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        public static MediaInfo Probe(string path)
        {
            var (code, output, err) = Run(Tools.FFprobe, new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", "-i", path });
            if (code != 0) throw new InvalidOperationException("ffprobe failed: " + err);
            return MediaInfo.Parse(output);
        }

        /// <summary>The brightest raw luma value in a file: tells whether pixels were really converted, not just relabelled.</summary>
        public static int MaxLuma(string path)
        {
            var (code, output, err) = Run(Tools.FFmpeg, new[]
            {
                "-hide_banner", "-loglevel", "error", "-i", path,
                "-vf", "signalstats,metadata=print:key=lavfi.signalstats.YMAX:file=-", "-f", "null", "-",
            });
            if (code != 0) throw new InvalidOperationException("signalstats failed: " + err);
            return Regex.Matches(output, @"YMAX=(\d+)").Cast<Match>().Max(m => int.Parse(m.Groups[1].Value));
        }

        private static bool CheckNvenc()
        {
            var (code, _, _) = Run(Tools.FFmpeg, new[]
            {
                "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=black:s=640x360:r=30:d=0.2",
                "-c:v", "hevc_nvenc", "-f", "null", "-",
            });
            return code == 0;
        }

        private static (int Code, string Output, string Error) Run(string exe, IEnumerable<string> args)
        {
            var info = new ProcessStartInfo(exe, ProcessArgs.Join(args))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using (var process = Process.Start(info))
            {
                var error = process.StandardError.ReadToEndAsync();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return (process.ExitCode, output, error.Result);
            }
        }
    }
}
