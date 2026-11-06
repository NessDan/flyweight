using System;
using System.Collections.Generic;
using System.Linq;

namespace Flyweight.Core
{
    public enum CliMode { Run, Install, Uninstall }

    /// <summary>The command line, and the same request as passed to an already-running window.</summary>
    public sealed class CliArgs
    {
        private const string LevelPrefix = "level=";
        private const string PresetPrefix = "preset=";

        public CliMode Mode { get; set; } = CliMode.Run;
        public bool Quiet { get; set; }
        public LevelId? Level { get; set; }
        public string PresetId { get; set; }
        public List<string> Files { get; set; } = new List<string>();

        public static CliArgs Parse(string[] args)
        {
            var result = new CliArgs();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--install":
                        result.Mode = CliMode.Install;
                        break;
                    case "--uninstall":
                        result.Mode = CliMode.Uninstall;
                        break;
                    case "--quiet":
                        result.Quiet = true;
                        break;
                    case "--level":
                        if (i + 1 < args.Length) result.Level = ParseLevel(args[++i]);
                        break;
                    case "--preset":
                        if (i + 1 < args.Length) result.PresetId = args[++i];
                        break;
                    default:
                        result.Files.Add(args[i]);
                        break;
                }
            }
            return result;
        }

        public string ToMessage()
        {
            var lines = new List<string> { LevelPrefix + (Level?.ToString() ?? ""), PresetPrefix + (PresetId ?? "") };
            lines.AddRange(Files);
            return string.Join("\n", lines);
        }

        public static CliArgs FromMessage(string message)
        {
            var lines = (message ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var result = new CliArgs();
            if (lines.Count > 0 && lines[0].StartsWith(LevelPrefix, StringComparison.Ordinal))
            {
                result.Level = ParseLevel(lines[0].Substring(LevelPrefix.Length));
                lines.RemoveAt(0);
            }
            if (lines.Count > 0 && lines[0].StartsWith(PresetPrefix, StringComparison.Ordinal))
            {
                string id = lines[0].Substring(PresetPrefix.Length);
                result.PresetId = id.Length > 0 ? id : null;
                lines.RemoveAt(0);
            }
            result.Files = lines;
            return result;
        }

        private static LevelId? ParseLevel(string text)
        {
            return Enum.TryParse(text, ignoreCase: true, out LevelId level) && Enum.IsDefined(typeof(LevelId), level) ? level : (LevelId?)null;
        }
    }
}
