using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Flyweight.Core
{
    public static class VideoFiles
    {
        /// <summary>The file types the window accepts.</summary>
        public static readonly string[] Extensions =
        {
            ".mp4", ".m4v", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".mpg", ".mpeg", ".mpe", ".m2v",
            ".ts", ".mts", ".m2ts", ".flv", ".f4v", ".3gp", ".3g2", ".ogv", ".vob", ".mxf", ".asf", ".divx",
        };

        /// <summary>
        /// The file types that get the right-click menu. TypeScript uses .ts and .mts as well, and a
        /// video menu on source files would be wrong, so those videos go through the window instead.
        /// </summary>
        public static readonly string[] MenuExtensions = Extensions.Except(new[] { ".ts", ".mts" }).ToArray();

        private static readonly HashSet<string> ExtensionSet = new HashSet<string>(Extensions, StringComparer.OrdinalIgnoreCase);
        private static readonly Regex AlreadyCompressed = new Regex(Regex.Escape(OutputNamer.Suffix) + @"( \(\d+\))?\.mp4$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsVideo(string path)
        {
            return ExtensionSet.Contains(Path.GetExtension(path) ?? "");
        }

        /// <summary>
        /// Turns dropped paths into the videos to compress. A folder means the videos directly inside
        /// it, leaving out ones this app already made so dropping a folder twice doesn't recompress them.
        /// </summary>
        public static List<string> Expand(IEnumerable<string> paths)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            foreach (string path in paths)
            {
                IEnumerable<string> candidates;
                if (Directory.Exists(path))
                {
                    candidates = Directory.EnumerateFiles(path)
                        .Where(f => IsVideo(f) && !AlreadyCompressed.IsMatch(f))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                }
                else if (File.Exists(path) && IsVideo(path))
                {
                    candidates = new[] { path };
                }
                else
                {
                    continue;
                }

                foreach (string file in candidates)
                {
                    if (seen.Add(file)) result.Add(file);
                }
            }

            return result;
        }
    }
}
