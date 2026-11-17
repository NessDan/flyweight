using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// Test classes that build the app's real windows and controls belong to this collection, which
    /// makes them run one class at a time. WPF reads a window's layout out of the program's own
    /// resources in a way that isn't safe from two threads at once. The app never does that, having
    /// one thread for its windows, but tests running side by side did. Every so often one collision
    /// broke that window for the rest of the run: a NullReferenceException from inside
    /// System.IO.Packaging for it and for every window built after it.
    /// They also run after the tests that can share the PC, not alongside them: some of them time
    /// a video playing, and a PC busy compressing test clips is no place to do that.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class WindowTests
    {
        public const string Name = "Windows";
    }

    /// <summary>
    /// For tests that time things against the clock without building a window. They run one at a
    /// time, once the tests that can share the PC have finished with it.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class ClockTests
    {
        public const string Name = "Clock";
    }
}
