using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace Flyweight.Core
{
    /// <summary>
    /// The right-click "Compress video" menu. It's plain registry entries rather than a shell
    /// extension DLL, so nothing loads into Explorer and it costs nothing until it's clicked.
    /// </summary>
    public static class ShellMenu
    {
        private const string VerbName = "Flyweight";
        private const string MenuKey = "Flyweight.Menu";
        private const string CustomKeyPrefix = "4_";
        private const int SeparatorBefore = 0x20;

        private static readonly (string Key, string Level, string Label)[] Items =
        {
            ("1_light", "light", "Light (full resolution)"),
            ("2_balanced", "balanced", "Balanced (up to 1080p)"),
            ("3_maximum", "maximum", "Maximum (720p, 30 fps)"),
        };

        /// <param name="classes">Normally HKCU\Software\Classes, which needs no admin rights.</param>
        public static void Register(RegistryKey classes, string exePath, IEnumerable<CustomPreset> customPresets = null)
        {
            // Start clean, so an update also removes anything an older version added that's gone now.
            Unregister(classes);

            foreach (var item in Items)
            {
                using (var key = classes.CreateSubKey($@"{MenuKey}\shell\{item.Key}"))
                {
                    key.SetValue("MUIVerb", item.Label);
                    key.SetValue("MultiSelectModel", "Player");
                }
                using (var command = classes.CreateSubKey($@"{MenuKey}\shell\{item.Key}\command"))
                {
                    command.SetValue(null, $"\"{exePath}\" --level {item.Level} \"%1\"");
                }
            }

            foreach (string extension in VideoFiles.MenuExtensions)
            {
                // SystemFileAssociations applies whichever player is the default for the file type.
                using (var verb = classes.CreateSubKey($@"SystemFileAssociations\{extension}\shell\{VerbName}"))
                {
                    verb.SetValue("MUIVerb", "Compress video");
                    verb.SetValue("Icon", $"\"{exePath}\",0");
                    verb.SetValue("ExtendedSubCommandsKey", MenuKey);
                    verb.SetValue("MultiSelectModel", "Player");
                }
            }

            UpdateCustomPresets(classes, exePath, customPresets);
        }

        /// <summary>Rewrites just the custom preset entries, which is quick enough to do as a name is typed.</summary>
        public static void UpdateCustomPresets(RegistryKey classes, string exePath, IEnumerable<CustomPreset> presets)
        {
            using (var shell = classes.CreateSubKey($@"{MenuKey}\shell"))
            {
                foreach (string name in shell.GetSubKeyNames().Where(n => n.StartsWith(CustomKeyPrefix, StringComparison.Ordinal)))
                {
                    shell.DeleteSubKeyTree(name);
                }
            }

            int index = 0;
            foreach (var preset in presets ?? Enumerable.Empty<CustomPreset>())
            {
                // Explorer sorts these by key name, so the number keeps them in the order given.
                string key = $@"{MenuKey}\shell\{CustomKeyPrefix}{index:000}";
                using (var item = classes.CreateSubKey(key))
                {
                    // A lone & would underline the next letter instead of showing up.
                    item.SetValue("MUIVerb", preset.Name.Replace("&", "&&"));
                    item.SetValue("MultiSelectModel", "Player");
                    if (index == 0) item.SetValue("CommandFlags", SeparatorBefore, RegistryValueKind.DWord);
                }
                using (var command = classes.CreateSubKey(key + @"\command"))
                {
                    command.SetValue(null, $"\"{exePath}\" --preset {preset.Id} \"%1\"");
                }
                index++;
            }
        }

        public static void Unregister(RegistryKey classes)
        {
            classes.DeleteSubKeyTree(MenuKey, throwOnMissingSubKey: false);

            foreach (string extension in VideoFiles.Extensions)
            {
                string association = $@"SystemFileAssociations\{extension}";
                classes.DeleteSubKeyTree($@"{association}\shell\{VerbName}", throwOnMissingSubKey: false);
                DeleteIfEmpty(classes, $@"{association}\shell");
                DeleteIfEmpty(classes, association);
            }
        }

        private static void DeleteIfEmpty(RegistryKey classes, string path)
        {
            using (var key = classes.OpenSubKey(path))
            {
                if (key == null || key.SubKeyCount > 0 || key.ValueCount > 0) return;
            }
            classes.DeleteSubKey(path, throwOnMissingSubKey: false);
        }
    }
}
