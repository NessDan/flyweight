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

        /// <summary>How long the result is, when only part of the video was kept.</summary>
        public double? KeptSeconds { get; set; }

        /// <summary>True when only part of the picture was kept.</summary>
        public bool Cropped { get; set; }
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
            IProgress<CompressionProgress> progress, CancellationToken token, TrimRange trim = null, CropArea crop = null)
        {
            string part = null;
            long inputBytes = 0;
            try
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(input)) inputBytes = new FileInfo(input).Length;
                var media = ProbeVideo(input);

                if (trim != null)
                {
                    trim = trim.ClampTo(media.DurationSeconds);
                    if (trim == null) throw new CompressionException("The part to keep is past the end of this video.");
                }

                // A crop that comes to the whole picture is no crop.
                int width = media.Video.DisplayWidth, height = media.Video.DisplayHeight;
                if (crop?.In(width, height) == null) crop = null;
                bool edited = trim != null || crop != null;

                // What the result is weighed against: the whole file, or the part of it being kept.
                long comparableBytes = trim == null ? inputBytes : KeptBytes(input, media, trim, inputBytes, token);
                // A crop is taken to keep the same share of the bytes as of the picture.
                if (crop != null) comparableBytes = (long)Math.Round(comparableBytes * crop.ShareOf(width, height));

                // A trim or a crop is wanted for what it keeps, so the rules that leave a video alone don't apply to it.
                long targetBytes = settings.TargetSizeMb * 1000000L;
                if (!edited && targetBytes > 0 && inputBytes <= targetBytes)
                {
                    return new CompressionResult
                    {
                        Outcome = JobOutcome.AlreadySmall,
                        InputBytes = inputBytes,
                        Message = $"Already under {settings.TargetSizeMb} MB, so the original was left as is",
                    };
                }

                if (!string.IsNullOrEmpty(outputFolder)) Directory.CreateDirectory(outputFolder);
                string suffix = trim != null ? OutputNamer.TrimmedSuffix : crop != null ? OutputNamer.CroppedSuffix : OutputNamer.Suffix;
                string output = OutputNamer.Suggest(input, outputFolder, File.Exists, suffix);
                part = OutputNamer.PartPath(output);

                // A size limit only matters when the kept part wouldn't fit anyway. When it would, aiming
                // at the limit would pad the file out, so that part is compressed like any other video.
                bool limitIsLoose = edited && targetBytes > 0 && comparableBytes <= targetBytes;
                var firstTry = settings;
                if (limitIsLoose)
                {
                    firstTry = settings.Clone();
                    firstTry.TargetSizeMb = 0;
                }

                var encoder = EncodeWithFallback(input, part, media, firstTry, trim, crop, progress, token);
                long outputBytes = new FileInfo(part).Length;

                // Not for a crop, which is only weighed against a guess at its share of the file. The
                // guess knows nothing of the sound, which no crop makes smaller, nor of a size cap the
                // crop gets under, and is too rough to judge a pass by.
                if (crop == null && ShouldTryHarder(firstTry, comparableBytes, outputBytes))
                {
                    // Already tightly compressed video spends most of its bits preserving its own
                    // flaws, so a coarser setting costs it little and can shrink it a lot.
                    var harder = firstTry.Clone();
                    harder.Quality += TryHarderStep;
                    Encode(encoder, input, part, media, harder, trim, crop, 1.0, progress, token, "Already quite compressed, trying harder");
                    outputBytes = new FileInfo(part).Length;
                }

                string caveat = null;
                if (limitIsLoose && outputBytes > targetBytes)
                {
                    // The share was only an estimate and this part is heavier than it, so aim at the limit after all.
                    Encode(encoder, input, part, media, settings, trim, crop, 1.0, progress, token, $"Getting it under {settings.TargetSizeMb} MB");
                    outputBytes = new FileInfo(part).Length;
                }
                if (targetBytes > 0 && outputBytes > targetBytes)
                {
                    // Encoders overshoot on difficult footage; one correction lands it.
                    double scale = targetBytes / (double)outputBytes * OvershootRetryMargin;
                    Encode(encoder, input, part, media, settings, trim, crop, scale, progress, token, $"Getting it under {settings.TargetSizeMb} MB");
                    outputBytes = new FileInfo(part).Length;
                    if (outputBytes > targetBytes) caveat = $"Couldn't quite get under {settings.TargetSizeMb} MB";
                }

                if (!edited && outputBytes >= inputBytes)
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
                    KeptSeconds = trim?.LengthSeconds,
                    Cropped = crop != null,
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

        /// <summary>
        /// Roughly how much of a file the kept part accounts for, assuming it's spread evenly. Only a
        /// fallback for a file that can't be added up properly.
        /// </summary>
        /// <summary>
        /// How many bytes of the file the kept part really accounts for, added up from the file itself.
        /// A video's bytes are rarely spread evenly, so going by length can be wildly out: a minute of
        /// a still screen weighs almost nothing next to five busy seconds.
        /// </summary>
        internal long KeptBytes(string input, MediaInfo media, TrimRange trim, long inputBytes, CancellationToken token)
        {
            long byLength = ShareOf(inputBytes, trim.LengthSeconds, media.DurationSeconds);
            double from = trim.StartSeconds + media.StartSeconds;
            double to = trim.EndSeconds + media.StartSeconds;
            long total = 0;
            int counted = 0;

            int exitCode;
            try
            {
                // Reads on for a second past the end, because packets aren't always stored in the order they're shown.
                exitCode = RunProcess(_tools.FFprobe,
                    new[]
                    {
                        "-v", "error", "-read_intervals", OnFileClock(from) + "%" + OnFileClock(to + 1),
                        "-show_entries", "packet=pts_time,dts_time,size", "-of", "csv=p=0", "-i", input,
                    },
                    line =>
                    {
                        string[] fields = line.Split(',');
                        if (fields.Length < 3 || !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out long size)) return;
                        if (!TryParseSeconds(fields[0], out double time) && !TryParseSeconds(fields[1], out time)) return;
                        if (time < from || time >= to) return;

                        Interlocked.Add(ref total, size);
                        Interlocked.Increment(ref counted);
                    },
                    out _, token);
            }
            catch (CompressionException)
            {
                return byLength;
            }

            token.ThrowIfCancellationRequested();
            return exitCode == 0 && counted > 0 ? Interlocked.Read(ref total) : byLength;
        }

        private static string OnFileClock(double seconds) => seconds.ToString("0.######", CultureInfo.InvariantCulture);

        private static bool TryParseSeconds(string text, out double seconds)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
        }

        internal static long ShareOf(long inputBytes, double keptSeconds, double durationSeconds)
        {
            if (durationSeconds <= 0 || keptSeconds >= durationSeconds) return inputBytes;
            return (long)Math.Round(inputBytes * keptSeconds / durationSeconds);
        }

        private EncoderKind EncodeWithFallback(string input, string part, MediaInfo media, LevelSettings settings, TrimRange trim, CropArea crop,
            IProgress<CompressionProgress> progress, CancellationToken token)
        {
            if (settings.Encoder == EncoderChoice.Cpu)
            {
                Encode(EncoderKind.Cpu, input, part, media, settings, trim, crop, 1.0, progress, token);
                return EncoderKind.Cpu;
            }

            try
            {
                Encode(EncoderKind.Nvenc, input, part, media, settings, trim, crop, 1.0, progress, token);
                return EncoderKind.Nvenc;
            }
            catch (CompressionException ex) when (!IsEnvironmentProblem(ex))
            {
                const string note = "Graphics card unavailable, using the processor";
                progress?.Report(new CompressionProgress { Percent = 0, Note = note });
                Encode(EncoderKind.Cpu, input, part, media, settings, trim, crop, 1.0, progress, token, note);
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

        private void Encode(EncoderKind encoder, string input, string part, MediaInfo media, LevelSettings settings, TrimRange trim, CropArea crop,
            double targetScale, IProgress<CompressionProgress> progress, CancellationToken token, string note = null)
        {
            var args = CommandBuilder.Build(input, part, media, settings, encoder, _cpuThreads, targetScale, trim, crop);
            var parser = new ProgressParser();
            double seconds = trim?.LengthSeconds ?? media.DurationSeconds;
            double lastPercent = 0;

            int exitCode = RunProcess(_tools.FFmpeg, args, line =>
            {
                var snapshot = parser.Feed(line);
                if (snapshot == null || progress == null) return;

                lastPercent = Math.Max(lastPercent, snapshot.Percent(seconds));
                progress.Report(new CompressionProgress { Percent = lastPercent, Remaining = snapshot.Remaining(seconds), Note = note });
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

        /// <summary>Reads what a video holds, or says why it can't be used.</summary>
        public MediaInfo ProbeVideo(string input)
        {
            if (!File.Exists(input)) throw new CompressionException("This file no longer exists.");

            var media = Probe(input);
            if (media.Video == null) throw new CompressionException("This file doesn't contain a video.");
            return media;
        }

        /// <summary>Reads what a file holds: its video, its length and its sound.</summary>
        public MediaInfo Probe(string input)
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

                using (token.Register(() => ChildProcessGuard.Kill(process)))
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
