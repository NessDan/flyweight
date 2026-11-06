using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using Flyweight.Core;
using Flyweight.UI;

namespace Flyweight
{
    public static class Program
    {
        private const string InstanceId = "Flyweight-Instance";
        private const int HandOverTimeoutMs = 5000;
        private const int HandOverAttempts = 3;

        [STAThread]
        public static int Main(string[] args)
        {
            var cli = CliArgs.Parse(args);
            string exe = Assembly.GetExecutingAssembly().Location;

            if (cli.Mode == CliMode.Install) return RunInstall(cli, exe);
            if (cli.Mode == CliMode.Uninstall) return RunUninstall(cli, exe);

            bool notInstalledYet;
            using (var targets = InstallTargets.ForCurrentUser())
            {
                notInstalledYet = !Installer.IsInstalledCopy(targets, exe);
            }
            if (notInstalledYet && cli.Files.Count == 0) return RunSetupWindow(exe);

            return RunApp(cli);
        }

        private static int RunApp(CliArgs cli)
        {
            using (var instance = new SingleInstance(InstanceId))
            {
                // Hand over before loading any UI, so a right-click on many files stays light.
                for (int attempt = 0; attempt < HandOverAttempts; attempt++)
                {
                    if (instance.TryBecomePrimary()) return RunWindow(cli, instance);
                    if (SingleInstance.Send(InstanceId, cli.ToMessage(), HandOverTimeoutMs)) return 0;
                }
                // The other copy is stuck. Better to do the work separately than to drop it.
                return RunWindow(cli, instance);
            }
        }

        private static int RunWindow(CliArgs cli, SingleInstance instance)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, Installer.DisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };

            bool adopted = AppSettings.AdoptFormerSettings(AppSettings.FormerPath, AppSettings.DefaultPath);
            var settings = AppSettings.Load(AppSettings.DefaultPath);
            if (adopted) UpdateRightClickMenu(settings);

            var tools = FFmpegLocator.Find();
            bool compact = cli.Files.Count > 0;
            var window = new MainWindow(settings, AppSettings.DefaultPath, tools, compact, Theme.SystemUsesDarkMode(), UpdateRightClickMenu);

            instance.StartListening(message =>
                window.Dispatcher.BeginInvoke(new Action(() => window.HandleRequest(CliArgs.FromMessage(message)))));

            window.Loaded += (s, e) =>
            {
                if (compact) window.AddPaths(cli.Files, cli.Level, cli.PresetId);
            };

            return app.Run(window);
        }

        private static void UpdateRightClickMenu(AppSettings settings)
        {
            try
            {
                using (var targets = InstallTargets.ForCurrentUser())
                {
                    string exe = Installer.InstalledExePath(targets);
                    if (File.Exists(exe)) ShellMenu.UpdateCustomPresets(targets.Classes, exe, settings.CustomPresets);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                // The preset is still saved and usable in the window, and the next install rebuilds the menu.
            }
        }

        private static int RunSetupWindow(string exe)
        {
            var app = new Application();
            var window = new SetupWindow(exe, FFmpegLocator.Find() != null, Theme.SystemUsesDarkMode());
            return app.Run(window);
        }

        private static int RunInstall(CliArgs cli, string exe)
        {
            try
            {
                using (var targets = InstallTargets.ForCurrentUser())
                {
                    Installer.Install(targets, exe, desktopIcon: true);
                }
                Installer.NotifyShell();
                return 0;
            }
            catch (CompressionException ex)
            {
                if (!cli.Quiet) MessageBox.Show(ex.Message, Installer.DisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        private static int RunUninstall(CliArgs cli, string exe)
        {
            if (!cli.Quiet)
            {
                var answer = MessageBox.Show($"Remove {Installer.DisplayName} from this PC?\n\nYour videos won't be touched.",
                    Installer.DisplayName, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return 1;
            }

            using (var targets = InstallTargets.ForCurrentUser())
            {
                bool isInstalledCopy = Installer.IsInstalledCopy(targets, exe);
                Installer.Uninstall(targets, removeProgramFolder: !isInstalledCopy);
                if (isInstalledCopy) Installer.RemoveFolderAfterExit(targets.ProgramFolder);
            }
            Installer.NotifyShell();
            return 0;
        }
    }
}
