using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Flyweight.UI
{
    /// <summary>Follows the light or dark mode chosen in Windows settings.</summary>
    internal static class Theme
    {
        private const int DwmUseImmersiveDarkMode = 20;

        public static bool SystemUsesDarkMode()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    return key?.GetValue("AppsUseLightTheme") is int useLight && useLight == 0;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Call before InitializeComponent so the window's XAML can find its styles.</summary>
        public static void Apply(Window window, bool dark)
        {
            var dictionaries = window.Resources.MergedDictionaries;
            dictionaries.Add(Load(dark ? "Dark" : "Light"));
            dictionaries.Add(Load("Controls"));

            window.FontFamily = new FontFamily("Segoe UI");
            window.FontSize = 13;
            window.SetResourceReference(Window.BackgroundProperty, "Bg");
            window.SetResourceReference(Window.ForegroundProperty, "Ink");

            window.SourceInitialized += (s, e) => MatchTitleBar(window, dark);
            window.Loaded += (s, e) => MatchTextToDisplay(window);
        }

        private static ResourceDictionary Load(string name)
        {
            return new ResourceDictionary { Source = new Uri($"pack://application:,,,/Flyweight;component/Themes/{name}.xaml") };
        }

        private static void MatchTitleBar(Window window, bool dark)
        {
            int enabled = dark ? 1 : 0;
            DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
        }

        /// <summary>Small text is crispest pixel-snapped on ordinary displays and smoothest unsnapped on high-DPI ones.</summary>
        private static void MatchTextToDisplay(Window window)
        {
            bool highDpi = VisualTreeHelper.GetDpi(window).DpiScaleX >= 1.5;
            TextOptions.SetTextFormattingMode(window, highDpi ? TextFormattingMode.Ideal : TextFormattingMode.Display);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
