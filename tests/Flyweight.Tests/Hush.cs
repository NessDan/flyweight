using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Flyweight.Tests
{
    /// <summary>
    /// Makes sure nothing these tests do can be heard. Before any of them runs, the volume Windows
    /// keeps for this one process is turned right down, on every output the PC has, and it's put back
    /// when the tests are over. It's the setting behind a program's own slider in the volume mixer,
    /// so nothing else on the PC gets any quieter. The few tests that really play through Windows
    /// play only silence, and check <see cref="IsInForce"/> first, which puts two things between them
    /// and the speakers.
    /// </summary>
    internal static class Hush
    {
        private static uint[] _before = new uint[0];

        [ModuleInitializer]
        internal static void Begin()
        {
            _before = new uint[waveOutGetNumDevs() + 1];
            for (int i = 0; i < _before.Length; i++)
            {
                waveOutGetVolume(Output(i), out _before[i]);
                waveOutSetVolume(Output(i), 0);
            }

            // Whichever of these this process gets to hear about.
            AppDomain.CurrentDomain.DomainUnload += (s, e) => End();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => End();
        }

        /// <summary>True when this process is known to be silent on every output there is.</summary>
        public static bool IsInForce
        {
            get
            {
                for (int i = 0; i < _before.Length; i++)
                {
                    if (waveOutGetVolume(Output(i), out uint volume) != 0 || volume != 0) return false;
                }
                return _before.Length > 1 && _before.Length == waveOutGetNumDevs() + 1;
            }
        }

        private static void End()
        {
            for (int i = 0; i < _before.Length; i++) waveOutSetVolume(Output(i), _before[i]);
        }

        // The first is "whichever output is the default one", and after it each output by number.
        private static IntPtr Output(int i) => new IntPtr(i - 1);

        [DllImport("winmm.dll")]
        private static extern int waveOutGetNumDevs();

        [DllImport("winmm.dll")]
        private static extern int waveOutGetVolume(IntPtr output, out uint volume);

        [DllImport("winmm.dll")]
        private static extern int waveOutSetVolume(IntPtr output, uint volume);
    }
}

namespace System.Runtime.CompilerServices
{
    /// <summary>Missing from this version of .NET, though the compiler only needs it to exist.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}
