using System;
using System.Linq;
using Microsoft.Win32;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>Works in a throwaway registry branch standing in for HKCU\Software\Classes.</summary>
    public class ShellMenuTests : IDisposable
    {
        private const string Exe = @"C:\Apps\Flyweight\Flyweight.exe";
        private readonly string _branch = @"Software\FlyweightTests\" + Guid.NewGuid().ToString("N");
        private readonly RegistryKey _classes;

        public ShellMenuTests()
        {
            _classes = Registry.CurrentUser.CreateSubKey(_branch);
        }

        public void Dispose()
        {
            _classes.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(_branch, throwOnMissingSubKey: false);
        }

        private RegistryKey Open(string path) => _classes.OpenSubKey(path);

        [Theory]
        [InlineData(".mp4")]
        [InlineData(".mov")]
        [InlineData(".mkv")]
        [InlineData(".webm")]
        public void Video_types_get_a_compress_entry(string extension)
        {
            ShellMenu.Register(_classes, Exe);

            using (var verb = Open($@"SystemFileAssociations\{extension}\shell\Flyweight"))
            {
                Assert.NotNull(verb);
                Assert.Equal("Compress video", verb.GetValue("MUIVerb"));
                Assert.Equal("Flyweight.Menu", verb.GetValue("ExtendedSubCommandsKey"));
                Assert.Equal($"\"{Exe}\",0", verb.GetValue("Icon"));
                Assert.Equal("Player", verb.GetValue("MultiSelectModel"));
            }
        }

        [Fact]
        public void Every_unambiguous_video_type_is_covered()
        {
            ShellMenu.Register(_classes, Exe);
            foreach (string extension in VideoFiles.MenuExtensions)
            {
                using (var verb = Open($@"SystemFileAssociations\{extension}\shell\Flyweight"))
                {
                    Assert.NotNull(verb);
                }
            }
        }

        [Theory]
        [InlineData(".ts")]
        [InlineData(".mts")]
        public void Extensions_shared_with_source_code_get_no_menu(string extension)
        {
            // These are TypeScript files far more often than videos on a developer's PC.
            ShellMenu.Register(_classes, Exe);
            Assert.Null(Open($@"SystemFileAssociations\{extension}\shell\Flyweight"));
        }

        [Fact]
        public void Registering_cleans_up_entries_left_by_an_older_version()
        {
            using (var stale = _classes.CreateSubKey(@"SystemFileAssociations\.ts\shell\Flyweight"))
            {
                stale.SetValue("MUIVerb", "Compress video");
            }

            ShellMenu.Register(_classes, Exe);

            Assert.Null(Open(@"SystemFileAssociations\.ts"));
        }

        [Theory]
        [InlineData("1_light", "light", "Light (full resolution)")]
        [InlineData("2_balanced", "balanced", "Balanced (up to 1080p)")]
        [InlineData("3_maximum", "maximum", "Maximum (720p, 30 fps)")]
        public void The_submenu_has_the_three_levels_in_order(string key, string level, string label)
        {
            ShellMenu.Register(_classes, Exe);

            using (var item = Open($@"Flyweight.Menu\shell\{key}"))
            using (var command = Open($@"Flyweight.Menu\shell\{key}\command"))
            {
                Assert.Equal(label, item.GetValue("MUIVerb"));
                Assert.Equal($"\"{Exe}\" --level {level} \"%1\"", command.GetValue(null));
            }
        }

        private static CustomPreset Custom(string id, string name)
        {
            return new CustomPreset { Id = id, Name = name, Settings = Presets.Default(LevelId.Balanced) };
        }

        [Fact]
        public void Custom_presets_are_listed_after_the_levels_in_the_order_given()
        {
            ShellMenu.Register(_classes, Exe, new[] { Custom("aa", "Discord"), Custom("bb", "X") });

            using (var menu = Open(@"Flyweight.Menu\shell"))
            {
                var names = menu.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
                Assert.Equal(new[] { "1_light", "2_balanced", "3_maximum", "4_000", "4_001" }, names);
            }
            using (var item = Open(@"Flyweight.Menu\shell\4_000"))
            using (var command = Open(@"Flyweight.Menu\shell\4_000\command"))
            {
                Assert.Equal("Discord", item.GetValue("MUIVerb"));
                Assert.Equal($"\"{Exe}\" --preset aa \"%1\"", command.GetValue(null));
            }
            using (var item = Open(@"Flyweight.Menu\shell\4_001"))
            {
                Assert.Equal("X", item.GetValue("MUIVerb"));
            }
        }

        [Fact]
        public void A_divider_separates_custom_presets_from_the_levels()
        {
            const int separatorBefore = 0x20;
            ShellMenu.Register(_classes, Exe, new[] { Custom("aa", "Discord"), Custom("bb", "X") });

            using (var first = Open(@"Flyweight.Menu\shell\4_000"))
            using (var second = Open(@"Flyweight.Menu\shell\4_001"))
            {
                Assert.Equal(separatorBefore, first.GetValue("CommandFlags"));
                Assert.Null(second.GetValue("CommandFlags"));
            }
        }

        [Fact]
        public void An_ampersand_in_a_name_shows_up_rather_than_underlining_a_letter()
        {
            ShellMenu.Register(_classes, Exe, new[] { Custom("aa", "Q&A clips") });
            using (var item = Open(@"Flyweight.Menu\shell\4_000"))
            {
                Assert.Equal("Q&&A clips", item.GetValue("MUIVerb"));
            }
        }

        [Fact]
        public void Deleted_presets_leave_the_menu_when_it_is_rebuilt()
        {
            ShellMenu.Register(_classes, Exe, new[] { Custom("aa", "Discord"), Custom("bb", "X") });
            ShellMenu.Register(_classes, Exe, new[] { Custom("bb", "X") });

            using (var menu = Open(@"Flyweight.Menu\shell"))
            {
                Assert.DoesNotContain("4_001", menu.GetSubKeyNames());
            }
            using (var command = Open(@"Flyweight.Menu\shell\4_000\command"))
            {
                Assert.Contains("--preset bb", (string)command.GetValue(null));
            }
        }

        [Fact]
        public void Removing_clears_custom_presets_too()
        {
            ShellMenu.Register(_classes, Exe, new[] { Custom("aa", "Discord") });
            ShellMenu.Unregister(_classes);
            Assert.Null(Open("Flyweight.Menu"));
        }

        [Fact]
        public void Registering_twice_is_harmless()
        {
            ShellMenu.Register(_classes, Exe);
            ShellMenu.Register(_classes, @"D:\Elsewhere\Flyweight.exe");

            using (var command = Open(@"Flyweight.Menu\shell\1_light\command"))
            {
                Assert.Contains(@"D:\Elsewhere", (string)command.GetValue(null));
            }
        }

        [Fact]
        public void Removing_clears_everything_this_app_added()
        {
            ShellMenu.Register(_classes, Exe);
            ShellMenu.Unregister(_classes);

            Assert.Null(Open("Flyweight.Menu"));
            foreach (string extension in VideoFiles.Extensions)
            {
                Assert.Null(Open($@"SystemFileAssociations\{extension}"));
            }
        }

        [Fact]
        public void Removing_leaves_other_programs_entries_alone()
        {
            using (_classes.CreateSubKey(@"SystemFileAssociations\.mp4\shell\SomeoneElse")) { }
            using (_classes.CreateSubKey(@"SystemFileAssociations\.mov\OpenWithList")) { }
            ShellMenu.Register(_classes, Exe);

            ShellMenu.Unregister(_classes);

            Assert.NotNull(Open(@"SystemFileAssociations\.mp4\shell\SomeoneElse"));
            Assert.NotNull(Open(@"SystemFileAssociations\.mov\OpenWithList"));
            Assert.Null(Open(@"SystemFileAssociations\.mp4\shell\Flyweight"));
        }

        [Fact]
        public void Removing_when_nothing_is_registered_is_harmless()
        {
            ShellMenu.Unregister(_classes);
            Assert.DoesNotContain("Flyweight.Menu", _classes.GetSubKeyNames());
        }
    }
}
