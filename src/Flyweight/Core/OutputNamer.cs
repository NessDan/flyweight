using System;
using System.IO;

namespace Flyweight.Core
{
    public static class OutputNamer
    {
        public const string Suffix = "_compressed";
        public const string TrimmedSuffix = "_trimmed";
        public const string CroppedSuffix = "_cropped";

        /// <summary>Picks an output path that doesn't exist yet. The original is never overwritten.</summary>
        public static string Suggest(string input, string outputFolder, Func<string, bool> exists, string suffix = Suffix)
        {
            string folder = string.IsNullOrEmpty(outputFolder) ? Path.GetDirectoryName(input) : outputFolder;
            string stem = Path.GetFileNameWithoutExtension(input) + suffix;

            string candidate = Path.Combine(folder, stem + ".mp4");
            for (int n = 2; exists(candidate); n++)
            {
                candidate = Path.Combine(folder, $"{stem} ({n}).mp4");
            }
            return candidate;
        }

        /// <summary>Where ffmpeg writes until the file is finished, so a half-done video never looks done.</summary>
        public static string PartPath(string output)
        {
            return output + ".part";
        }
    }
}
