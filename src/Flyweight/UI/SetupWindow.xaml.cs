using System;
using System.Diagnostics;
using System.Windows;
using Flyweight.Core;

namespace Flyweight.UI
{
    public partial class SetupWindow : Window
    {
        private readonly string _sourceExe;

        public SetupWindow(string sourceExe, bool toolsFound, bool dark)
        {
            _sourceExe = sourceExe;
            Theme.Apply(this, dark);
            InitializeComponent();
            ToolsNote.Visibility = toolsFound ? Visibility.Collapsed : Visibility.Visible;
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string installed;
                using (var targets = InstallTargets.ForCurrentUser())
                {
                    installed = Installer.Install(targets, _sourceExe, DesktopIconBox.IsChecked == true);
                }
                Installer.NotifyShell();
                Process.Start(installed);
                Close();
            }
            catch (CompressionException ex)
            {
                ErrorText.Text = ex.Message;
                ErrorText.Visibility = Visibility.Visible;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
