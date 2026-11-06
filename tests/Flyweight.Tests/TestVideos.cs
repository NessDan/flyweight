using System;
using System.Collections.Generic;
using System.Diagnostics;
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
