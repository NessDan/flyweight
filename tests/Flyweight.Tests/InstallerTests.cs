using System;
using System.IO;
using Microsoft.Win32;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>Installs into throwaway folders and a throwaway registry branch.</summary>
    public class InstallerTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "fw-install-" + Guid.NewGuid().ToString("N"));
        private readonly string _branch = @"Software\FlyweightTests\" + Guid.NewGuid().ToString("N");
        private readonly InstallTargets _targets;
        private readonly string _sourceExe;

        public InstallerTests()
        {
            Directory.CreateDirectory(_root);
            _sourceExe = Path.Combine(_root, "FlyweightSetup.exe");
            File.WriteAllBytes(_sourceExe, new byte[] { 1, 2, 3 });

            _targets = new InstallTargets
            {
                ProgramFolder = Path.Combine(_root, "Programs", "Flyweight"),
                StartMenuFolder = Directory.CreateDirectory(Path.Combine(_root, "StartMenu")).FullName,
                DesktopFolder = Directory.CreateDirectory(Path.Combine(_root, "Desktop")).FullName,
                SettingsFolder = Path.Combine(_root, "AppData", "Flyweight"),
                Classes = Registry.CurrentUser.CreateSubKey(_branch + @"\Classes"),
                UninstallRoot = Registry.CurrentUser.CreateSubKey(_branch + @"\Uninstall"),
            };
        }

        public void Dispose()
        {
            _targets.Classes.Dispose();
            _targets.UninstallRoot.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(_branch, throwOnMissingSubKey: false);
            Directory.Delete(_root, true);
        }

        private string InstalledExe => Path.Combine(_targets.ProgramFolder, "Flyweight.exe");

        [Fact]
        public void Copies_the_program_into_place_under_its_real_name()
        {
            string exe = Installer.Install(_targets, _sourceExe, desktopIcon: true);

            Assert.Equal(InstalledExe, exe);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(exe));
        }

        [Fact]
        public void Adds_the_right_click_menu_pointing_at_the_installed_copy()
        {
            Installer.Install(_targets, _sourceExe, desktopIcon: true);

            using (var command = _targets.Classes.OpenSubKey(@"Flyweight.Menu\shell\2_balanced\command"))
            {
                Assert.Contains(InstalledExe, (string)command.GetValue(null));
            }
        }

        [Fact]
        public void Updating_keeps_custom_presets_in_the_right_click_menu()
        {
            var settings = AppSettings.Load(Path.Combine(_targets.SettingsFolder, "settings.json"));
            settings.AddCustom("Discord", Presets.Default(LevelId.Balanced));
            settings.Save(Path.Combine(_targets.SettingsFolder, "settings.json"));

            Installer.Install(_targets, _sourceExe, desktopIcon: false);

            using (var item = _targets.Classes.OpenSubKey(@"Flyweight.Menu\shell\4_000"))
            {
                Assert.Equal("Discord", item.GetValue("MUIVerb"));
            }
        }

        [Fact]
        public void Adds_start_menu_and_desktop_shortcuts()
        {
            Installer.Install(_targets, _sourceExe, desktopIcon: true);

            Assert.True(File.Exists(Path.Combine(_targets.StartMenuFolder, "Flyweight.lnk")));
            Assert.True(File.Exists(Path.Combine(_targets.DesktopFolder, "Flyweight.lnk")));
        }

        [Fact]
        public void The_desktop_icon_is_optional()
        {
            Installer.Install(_targets, _sourceExe, desktopIcon: false);

            Assert.True(File.Exists(Path.Combine(_targets.StartMenuFolder, "Flyweight.lnk")));
            Assert.False(File.Exists(Path.Combine(_targets.DesktopFolder, "Flyweight.lnk")));
        }

        [Fact]
        public void Shows_up_in_apps_and_features_with_a_working_uninstall_command()
        {
            Installer.Install(_targets, _sourceExe, desktopIcon: true);

            using (var entry = _targets.UninstallRoot.OpenSubKey("Flyweight"))
            {
                Assert.Equal("Flyweight", entry.GetValue("DisplayName"));
                Assert.Equal($"\"{InstalledExe}\" --uninstall", entry.GetValue("UninstallString"));
                Assert.Equal($"\"{InstalledExe}\" --uninstall --quiet", entry.GetValue("QuietUninstallString"));
                Assert.Equal(InstalledExe, entry.GetValue("DisplayIcon"));
                Assert.Equal(_targets.ProgramFolder, entry.GetValue("InstallLocation"));
                Assert.Equal(1, entry.GetValue("NoModify"));
                Assert.Equal(1, entry.GetValue("NoRepair"));
                Assert.False(string.IsNullOrEmpty((string)entry.GetValue("DisplayVersion")));
            }
        }

        [Fact]
        public void Installing_again_updates_the_program()
        {
            Installer.Install(_targets, _sourceExe, desktopIcon: true);
            File.WriteAllBytes(_sourceExe, new byte[] { 9, 9 });

            Installer.Install(_targets, _sourceExe, desktopIcon: true);

            Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(InstalledExe));
        }

        [Fact]
        public void Running_install_from_the_installed_copy_does_not_try_to_copy_it_over_itself()
        {
            Installer.Install(_targets, _sourceExe, desktopIcon: false);

            string exe = Installer.Install(_targets, InstalledExe, desktopIcon: false);

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(exe));
        }

        [Fact]
        public void Updating_while_the_app_is_open_swaps_the_program_underneath_it()
        {
            Installer.Install(_targets, _sourceExe, desktopIcon: false);
            File.WriteAllBytes(_sourceExe, new byte[] { 9, 9 });

            // A running program's file can be renamed but not overwritten, which this sharing mode mimics.
            using (File.Open(InstalledExe, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                string exe = Installer.Install(_targets, _sourceExe, desktopIcon: false);
                Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(exe));
            }
        }

        [Fact]
        public void The_old_copy_from_an_earlier_update_gets_cleaned_up()
        {
            Installer.Install(_targets, _sourceExe, desktopIcon: false);
            string old = InstalledExe + ".old";
            File.WriteAllBytes(old, new byte[] { 7 });

            Installer.Install(_targets, _sourceExe, desktopIcon: false);

            Assert.False(File.Exists(old));
        }

        [Fact]
        public void A_locked_program_file_gives_a_readable_error()
        {
            Installer.Install(_targets, _sourceExe, desktopIcon: false);
            File.WriteAllBytes(_sourceExe, new byte[] { 9, 9 });

            using (File.Open(InstalledExe, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var ex = Assert.Throws<CompressionException>(() => Installer.Install(_targets, _sourceExe, desktopIcon: false));
                Assert.Contains("Close Flyweight", ex.Message);
            }
        }

        [Fact]
        public void Uninstalling_removes_everything_it_added()
        {
            Installer.Install(_targets, _sourceExe, desktopIcon: true);
            Directory.CreateDirectory(_targets.SettingsFolder);
            File.WriteAllText(Path.Combine(_targets.SettingsFolder, "settings.json"), "{}");

            Installer.Uninstall(_targets, removeProgramFolder: true);

            Assert.False(Directory.Exists(_targets.ProgramFolder));
            Assert.False(Directory.Exists(_targets.SettingsFolder));
            Assert.False(File.Exists(Path.Combine(_targets.StartMenuFolder, "Flyweight.lnk")));
            Assert.False(File.Exists(Path.Combine(_targets.DesktopFolder, "Flyweight.lnk")));
            Assert.Null(_targets.UninstallRoot.OpenSubKey("Flyweight"));
            Assert.Null(_targets.Classes.OpenSubKey("Flyweight.Menu"));
        }

        [Fact]
        public void Uninstalling_can_leave_the_running_program_for_later_removal()
        {
            Installer.Install(_targets, _sourceExe, desktopIcon: false);

            Installer.Uninstall(_targets, removeProgramFolder: false);

            Assert.True(File.Exists(InstalledExe));
            Assert.Null(_targets.UninstallRoot.OpenSubKey("Flyweight"));
        }

        [Fact]
        public void Uninstalling_twice_is_harmless()
        {
            Installer.Install(_targets, _sourceExe, desktopIcon: false);
            Installer.Uninstall(_targets, removeProgramFolder: true);
            Installer.Uninstall(_targets, removeProgramFolder: true);
        }

        [Fact]
        public void Knows_whether_a_path_is_the_installed_copy()
        {
            Assert.True(Installer.IsInstalledCopy(_targets, InstalledExe));
            Assert.True(Installer.IsInstalledCopy(_targets, InstalledExe.ToUpperInvariant()));
            Assert.False(Installer.IsInstalledCopy(_targets, _sourceExe));
        }
    }
}
