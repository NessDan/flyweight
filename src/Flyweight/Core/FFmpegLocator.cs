using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Flyweight.Core
{
    public sealed class FFmpegTools
    {
        public string FFmpeg { get; set; }
        public string FFprobe { get; set; }
    }

    /// <summary>
    /// Finds an existing ffmpeg install. This app doesn't bundle its own: a static build is hundreds
    /// of megabytes, and an installed one keeps getting updates.
    /// </summary>
    public static class FFmpegLocator
    {
        public static FFmpegTools Find()
        {
            return FindIn(SearchFolders());
        }

        public static FFmpegTools FindIn(IEnumerable<string> folders)
        {
            foreach (string folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                try
                {
                    string ffmpeg = Path.Combine(folder, "ffmpeg.exe");
                    string ffprobe = Path.Combine(folder, "ffprobe.exe");
                    if (File.Exists(ffmpeg) && File.Exists(ffprobe))
                    {
                        return new FFmpegTools { FFmpeg = ffmpeg, FFprobe = ffprobe };
                    }
                }
                catch (ArgumentException)
                {
                    // PATH entries can contain anything.
                }
            }
            return null;
        }

        public static IEnumerable<string> SearchFolders()
        {
            yield return AppDomain.CurrentDomain.BaseDirectory;

            foreach (string folder in SplitPath(Environment.GetEnvironmentVariable("PATH"))) yield return folder;

            // Explorer can hold an old PATH from before ffmpeg was installed, so read the saved one too.
            foreach (string folder in SplitPath(ReadSavedPath(Registry.CurrentUser, "Environment"))) yield return folder;
            foreach (string folder in SplitPath(ReadSavedPath(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))) yield return folder;

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links");
            foreach (string folder in WingetPackageFolders(Path.Combine(localAppData, "Microsoft", "WinGet", "Packages"))) yield return folder;

            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "bin");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin");
            yield return @"C:\ffmpeg\bin";
        }

        private static IEnumerable<string> SplitPath(string path)
        {
            return (path ?? "").Split(';').Select(p => p.Trim().Trim('"')).Where(p => p.Length > 0);
        }

        private static string ReadSavedPath(RegistryKey hive, string subKey)
        {
            try
            {
                using (var key = hive.OpenSubKey(subKey))
                {
                    return key?.GetValue("Path") as string;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
                return null;
            }
        }

        private static IEnumerable<string> WingetPackageFolders(string packages)
        {
            if (!Directory.Exists(packages)) return Enumerable.Empty<string>();
            try
            {
                return Directory.GetDirectories(packages, "*FFmpeg*")
                    .SelectMany(package => Directory.GetDirectories(package))
                    .Select(build => Path.Combine(build, "bin"))
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Enumerable.Empty<string>();
            }
        }
    }
}
