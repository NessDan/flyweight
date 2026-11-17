using System;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// For a test that has to show a real window, or play through Windows for real. It runs only on
    /// the hidden desktop the tests are given, and never opens anything on a real one. Anywhere else
    /// it says it was skipped, rather than passing without having done anything.
    /// </summary>
    public sealed class OnHiddenDesktopFactAttribute : FactAttribute
    {
        private const string HiddenDesktopName = "VideoCompressorTests";

        public OnHiddenDesktopFactAttribute()
        {
            if (DesktopName() != HiddenDesktopName) Skip = "Shows a real window or opens the sound output, so it only runs on the hidden desktop the tests are given.";
        }

        private static string DesktopName()
        {
            var name = new StringBuilder(256);
            GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, name.Capacity * 2, out _);
            return name.ToString();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDesktop(int thread);

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder info, int length, out int needed);
    }
}
