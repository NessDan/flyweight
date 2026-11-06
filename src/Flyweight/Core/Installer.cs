using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Flyweight.Core
{
    /// <summary>Where an install puts things.</summary>
    public sealed class InstallTargets : IDisposable
    {
        public string ProgramFolder { get; set; }
        public string StartMenuFolder { get; set; }
        public string DesktopFolder { get; set; }
        public string SettingsFolder { get; set; }
        public RegistryKey Classes { get; set; }
        public RegistryKey UninstallRoot { get; set; }

        /// <summary>Everything lives under the user's own profile, which is why no admin rights are needed.</summary>
        public static InstallTargets ForCurrentUser()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new InstallTargets
            {
                ProgramFolder = Path.Combine(localAppData, "Programs", Installer.DisplayName),
                StartMenuFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                DesktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                SettingsFolder = Path.GetDirectoryName(AppSettings.DefaultPath),
                Classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes"),
                UninstallRoot = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            };
        }

        public void Dispose()
        {
            Classes?.Dispose();
            UninstallRoot?.Dispose();
        }
    }

    public static class Installer
    {
        public const string DisplayName = "Flyweight";
        private const string ExeName = "Flyweight.exe";
        private const string UninstallKey = "Flyweight";
        private const string ShortcutName = DisplayName + ".lnk";

        public static string InstalledExePath(InstallTargets targets)
        {
            return Path.Combine(targets.ProgramFolder, ExeName);
        }

        public static bool IsInstalledCopy(InstallTargets targets, string path)
        {
            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(InstalledExePath(targets)), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Installs or updates. Returns the path of the installed program.</summary>
        public static string Install(InstallTargets targets, string sourceExe, bool desktopIcon)
        {
            string exe = InstalledExePath(targets);
            Directory.CreateDirectory(targets.ProgramFolder);

            if (!IsInstalledCopy(targets, sourceExe)) ReplaceProgram(sourceExe, exe);

            var settings = AppSettings.Load(Path.Combine(targets.SettingsFolder, AppSettings.FileName));
            ShellMenu.Register(targets.Classes, exe, settings.CustomPresets);
            WriteUninstallEntry(targets, exe);

            CreateShortcut(Path.Combine(targets.StartMenuFolder, ShortcutName), exe);
            string desktopShortcut = Path.Combine(targets.DesktopFolder, ShortcutName);
            if (desktopIcon) CreateShortcut(desktopShortcut, exe);
            else DeleteQuietly(desktopShortcut);

            return exe;
        }

        /// <summary>
        /// Removes everything except videos, which were never touched. A running program can't delete
        /// its own file, so the uninstaller passes false here and calls <see cref="RemoveFolderAfterExit"/>.
        /// </summary>
        public static void Uninstall(InstallTargets targets, bool removeProgramFolder)
        {
            ShellMenu.Unregister(targets.Classes);
            targets.UninstallRoot.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

            DeleteQuietly(Path.Combine(targets.StartMenuFolder, ShortcutName));
            DeleteQuietly(Path.Combine(targets.DesktopFolder, ShortcutName));
            DeleteFolderQuietly(targets.SettingsFolder);

            if (removeProgramFolder) DeleteFolderQuietly(targets.ProgramFolder);
        }

        public static void RemoveFolderAfterExit(string folder)
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{folder}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }

        /// <summary>Tells Explorer the right-click menu changed.</summary>
        public static void NotifyShell()
        {
            const int associationsChanged = 0x08000000;
            SHChangeNotify(associationsChanged, 0, IntPtr.Zero, IntPtr.Zero);
        }

        private static void ReplaceProgram(string sourceExe, string exe)
        {
            string oldCopy = exe + ".old";
            DeleteQuietly(oldCopy);

            try
            {
                File.Copy(sourceExe, exe, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // A running program can't be overwritten, but it can be renamed out of the way. It
                // carries on undisturbed, and the new version takes over the next time the app opens.
                try
                {
                    File.Move(exe, oldCopy);
                    File.Copy(sourceExe, exe);
                }
                catch (Exception inner) when (inner is IOException || inner is UnauthorizedAccessException)
                {
                    if (!File.Exists(exe) && File.Exists(oldCopy)) File.Move(oldCopy, exe);
                    throw new CompressionException($"Close {DisplayName} and try again. It's in use, so it can't be updated.", inner.Message);
                }
            }
        }

        private static void WriteUninstallEntry(InstallTargets targets, string exe)
        {
            using (var entry = targets.UninstallRoot.CreateSubKey(UninstallKey))
            {
                entry.SetValue("DisplayName", DisplayName);
                entry.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
                entry.SetValue("Publisher", DisplayName);
                entry.SetValue("DisplayIcon", exe);
                entry.SetValue("InstallLocation", targets.ProgramFolder);
                entry.SetValue("UninstallString", $"\"{exe}\" --uninstall");
                entry.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall --quiet");
                entry.SetValue("NoModify", 1, RegistryValueKind.DWord);
                entry.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                entry.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        private static void CreateShortcut(string shortcutPath, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));

            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            try
            {
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = target;
                shortcut.WorkingDirectory = Path.GetDirectoryName(target);
                shortcut.Description = "Compress videos";
                shortcut.Save();
                Marshal.FinalReleaseComObject(shortcut);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
            {
                // Nothing to clean up, or it's in use; neither should stop the rest.
            }
        }

        private static void DeleteFolderQuietly(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Same as above.
            }
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
