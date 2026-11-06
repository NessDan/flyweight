using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Flyweight.Core
{
    public enum JobOutcome { Done, AlreadySmall, Canceled, Failed }

    public sealed class CompressionResult
    {
        public JobOutcome Outcome { get; set; }
        public string OutputPath { get; set; }
        public long InputBytes { get; set; }
        public long OutputBytes { get; set; }

        /// <summary>Explains an AlreadySmall or Failed outcome, or a caveat on a Done one.</summary>
        public string Message { get; set; }

        /// <summary>What ffmpeg said, when it failed.</summary>
        public string Details { get; set; }

        public EncoderKind? EncoderUsed { get; set; }
    }

    public sealed class CompressionProgress
    {
        public double Percent { get; set; }
        public TimeSpan? Remaining { get; set; }
        public string Note { get; set; }
    }

    /// <summary>Compresses one video. Blocking, so run it off the UI thread.</summary>
    public sealed class Compressor
    {
        private const int ErrorLinesKept = 20;
        private const double OvershootRetryMargin = 0.97;
        private const double WorthwhileRatio = 0.9;
        private const int TryHarderStep = 4;

        private readonly FFmpegTools _tools;
        private readonly int _cpuThreads = Environment.ProcessorCount;

        public Compressor(FFmpegTools tools)
        {
            _tools = tools;
        }

        public CompressionResult Run(string input, LevelSettings settings, string outputFolder,
            IProgress<CompressionProgress> progress, CancellationToken token)
        {
            string part = null;
            long inputBytes = 0;
            try
            {
                token.ThrowIfCancellationRequested();
                if (!File.Exists(input)) throw new CompressionException("This file no longer exists.");

                inputBytes = new FileInfo(input).Length;
                var media = Probe(input);
                if (media.Video == null) throw new CompressionException("This file doesn't contain a video.");

                long targetBytes = settings.TargetSizeMb * 1000000L;
                if (targetBytes > 0 && inputBytes <= targetBytes)
                {
                    return new CompressionResult
                    {
                        Outcome = JobOutcome.AlreadySmall,
                        InputBytes = inputBytes,
                        Message = $"Already under {settings.TargetSizeMb} MB, so the original was left as is",
                    };
                }

                if (!string.IsNullOrEmpty(outputFolder)) Directory.CreateDirectory(outputFolder);
                string output = OutputNamer.Suggest(input, outputFolder, File.Exists);
                part = OutputNamer.PartPath(output);

                var encoder = EncodeWithFallback(input, part, media, settings, progress, token);
                long outputBytes = new FileInfo(part).Length;

                if (ShouldTryHarder(settings, inputBytes, outputBytes))
                {
                    // Already tightly compressed video spends most of its bits preserving its own
                    // flaws, so a coarser setting costs it little and can shrink it a lot.
                    var harder = settings.Clone();
                    harder.Quality += TryHarderStep;
                    Encode(encoder, input, part, media, harder, 1.0, progress, token, "Already quite compressed, trying harder");
                    outputBytes = new FileInfo(part).Length;
                }

                string caveat = null;
                if (targetBytes > 0 && outputBytes > targetBytes)
                {
                    // Encoders overshoot on difficult footage; one correction lands it.
                    double scale = targetBytes / (double)outputBytes * OvershootRetryMargin;
                    Encode(encoder, input, part, media, settings, scale, progress, token, $"Getting it under {settings.TargetSizeMb} MB");
                    outputBytes = new FileInfo(part).Length;
                    if (outputBytes > targetBytes) caveat = $"Couldn't quite get under {settings.TargetSizeMb} MB";
                }

                if (outputBytes >= inputBytes)
                {
                    TryDelete(part);
                    return new CompressionResult
                    {
                        Outcome = JobOutcome.AlreadySmall,
                        InputBytes = inputBytes,
                        EncoderUsed = encoder,
                        Message = "Already well compressed, so the original was left as is",
                    };
                }

                File.Move(part, output);
                CopyDates(input, output);

                return new CompressionResult
                {
                    Outcome = JobOutcome.Done,
                    OutputPath = output,
                    InputBytes = inputBytes,
                    OutputBytes = outputBytes,
                    EncoderUsed = encoder,
                    Message = caveat,
                };
            }
            catch (OperationCanceledException)
            {
                TryDelete(part);
                return new CompressionResult { Outcome = JobOutcome.Canceled, InputBytes = inputBytes };
            }
            catch (CompressionException ex)
            {
                TryDelete(part);
                return new CompressionResult { Outcome = JobOutcome.Failed, InputBytes = inputBytes, Message = ex.Message, Details = ex.Details };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                TryDelete(part);
                return new CompressionResult { Outcome = JobOutcome.Failed, InputBytes = inputBytes, Message = "Couldn't save the compressed video.", Details = ex.Message };
            }
        }

        /// <summary>
        /// True when a preset barely helped. A size limit has its own retry, and a quality number the
        /// user set by hand is theirs to choose, so neither gets overridden.
        /// </summary>
        public static bool ShouldTryHarder(LevelSettings settings, long inputBytes, long outputBytes)
        {
            return settings.TargetSizeMb <= 0
                && Presets.IsPresetQuality(settings.Codec, settings.Quality)
                && outputBytes >= inputBytes * WorthwhileRatio;
        }

        private EncoderKind EncodeWithFallback(string input, string part, MediaInfo media, LevelSettings settings,
            IProgress<CompressionProgress> progress, CancellationToken token)
        {
            if (settings.Encoder == EncoderChoice.Cpu)
            {
                Encode(EncoderKind.Cpu, input, part, media, settings, 1.0, progress, token);
                return EncoderKind.Cpu;
            }

            try
            {
                Encode(EncoderKind.Nvenc, input, part, media, settings, 1.0, progress, token);
                return EncoderKind.Nvenc;
            }
            catch (CompressionException ex) when (!IsEnvironmentProblem(ex))
            {
                const string note = "Graphics card unavailable, using the processor";
                progress?.Report(new CompressionProgress { Percent = 0, Note = note });
                Encode(EncoderKind.Cpu, input, part, media, settings, 1.0, progress, token, note);
                return EncoderKind.Cpu;
            }
        }

        private static bool IsEnvironmentProblem(CompressionException ex)
        {
            // The processor can't fix a full disk or a read-only folder.
            string details = ex.Details ?? "";
            return details.IndexOf("No space left", StringComparison.OrdinalIgnoreCase) >= 0
                || details.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Encode(EncoderKind encoder, string input, string part, MediaInfo media, LevelSettings settings, double targetScale,
            IProgress<CompressionProgress> progress, CancellationToken token, string note = null)
        {
            var args = CommandBuilder.Build(input, part, media, settings, encoder, _cpuThreads, targetScale);
            var parser = new ProgressParser();
            double lastPercent = 0;

            int exitCode = RunProcess(_tools.FFmpeg, args, line =>
            {
                var snapshot = parser.Feed(line);
                if (snapshot == null || progress == null) return;

                lastPercent = Math.Max(lastPercent, snapshot.Percent(media.DurationSeconds));
                progress.Report(new CompressionProgress { Percent = lastPercent, Remaining = snapshot.Remaining(media.DurationSeconds), Note = note });
            }, out string errors, token);

            if (exitCode != 0)
            {
                throw new CompressionException(FriendlyMessage(errors), errors);
            }
        }

        private static string FriendlyMessage(string errors)
        {
            if (errors.IndexOf("No space left", StringComparison.OrdinalIgnoreCase) >= 0) return "The disk is full.";
            if (errors.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0) return "Windows wouldn't allow saving to that folder.";
            return "Couldn't compress this video.";
        }

        private MediaInfo Probe(string input)
        {
            var output = new StringBuilder();
            int exitCode = RunProcess(_tools.FFprobe,
                new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", "-i", input },
                line => output.AppendLine(line), out string errors, CancellationToken.None);

            if (exitCode != 0) throw new CompressionException("This file couldn't be read as a video.", errors);
            return MediaInfo.Parse(output.ToString());
        }

        private static int RunProcess(string exe, IEnumerable<string> args, Action<string> onOutputLine, out string errors, CancellationToken token)
        {
            var errorTail = new Queue<string>();
            var info = new ProcessStartInfo(exe, ProcessArgs.Join(args))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using (var process = new Process { StartInfo = info })
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) onOutputLine(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (errorTail)
                    {
                        errorTail.Enqueue(e.Data);
                        if (errorTail.Count > ErrorLinesKept) errorTail.Dequeue();
                    }
                };

                try
                {
                    process.Start();
                }
                catch (Win32Exception ex)
                {
                    throw new CompressionException("Couldn't start ffmpeg.", ex.Message);
                }

                ChildProcessGuard.Attach(process);
                LowerPriority(process);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (token.Register(() => TryKill(process)))
                {
                    process.WaitForExit();
                }

                token.ThrowIfCancellationRequested();
                lock (errorTail) errors = string.Join(Environment.NewLine, errorTail);
                return process.ExitCode;
            }
        }

        /// <summary>Whatever else is running on the PC comes first.</summary>
        private static void LowerPriority(Process process)
        {
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
            {
                // Already exited.
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                process.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
            {
                // Already exited.
            }
        }

        private static void TryDelete(string path)
        {
            if (path == null) return;
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Leftover partial file; it's clearly named and harmless.
            }
        }

        private static void CopyDates(string from, string to)
        {
            try
            {
                File.SetCreationTimeUtc(to, File.GetCreationTimeUtc(from));
                File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Dates are a nicety, not worth failing the job over.
            }
        }
    }
}
