using System;
using System.Linq;
using Microsoft.Win32;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>Works in a throwaway registry branch standing in for HKCU\Software\Classes.</summary>
    public class TrimMenuTests : IDisposable
    {
        private const string Exe = @"C:\Apps\Flyweight\Flyweight.exe";
        private const string TrimKey = @"Flyweight.Menu\shell\9_trim";
        private readonly string _branch = @"Software\FlyweightTests\" + Guid.NewGuid().ToString("N");
        private readonly RegistryKey _classes;

        public TrimMenuTests()
        {
            _classes = Registry.CurrentUser.CreateSubKey(_branch);
        }

        public void Dispose()
        {
            _classes.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(_branch, throwOnMissingSubKey: false);
        }

        [Fact]
        public void The_submenu_ends_with_trim_first_below_a_divider()
        {
            ShellMenu.Register(_classes, Exe);

            using (var item = _classes.OpenSubKey(TrimKey))
            using (var command = _classes.OpenSubKey(TrimKey + @"\command"))
            {
                Assert.NotNull(item);
                Assert.Equal("Trim or crop first…", item.GetValue("MUIVerb"));
                Assert.Equal(0x20, item.GetValue("CommandFlags"));
                Assert.Equal("Player", item.GetValue("MultiSelectModel"));
                Assert.Equal($"\"{Exe}\" --trim \"%1\"", command.GetValue(null));
            }
            using (var shell = _classes.OpenSubKey(@"Flyweight.Menu\shell"))
            {
                // Explorer lists these in key order, so the last key is the last entry.
                Assert.Equal("9_trim", shell.GetSubKeyNames().OrderBy(n => n, StringComparer.Ordinal).Last());
            }
        }

        [Fact]
        public void Changing_custom_presets_leaves_the_trim_entry_in_place()
        {
            ShellMenu.Register(_classes, Exe);
            var preset = new CustomPreset { Id = "ab12cd34", Name = "Discord", Settings = Presets.Default(LevelId.Balanced) };

            ShellMenu.UpdateCustomPresets(_classes, Exe, new[] { preset });
            ShellMenu.UpdateCustomPresets(_classes, Exe, new CustomPreset[0]);

            using (var item = _classes.OpenSubKey(TrimKey))
            {
                Assert.NotNull(item);
            }
        }

        [Fact]
        public void Removing_the_menu_takes_the_trim_entry_with_it()
        {
            ShellMenu.Register(_classes, Exe);
            ShellMenu.Unregister(_classes);
            Assert.Null(_classes.OpenSubKey(TrimKey));
        }
    }
}
