using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// Sound really going out through Windows. Only ever silence, with this process turned down as
    /// well (see <see cref="Hush"/>), and only on the hidden desktop the tests are given.
    /// </summary>
    [Collection(ClockTests.Name)]
    public class SoundOutTests
    {
        private static readonly byte[] Silence = new byte[SoundOut.BlockBytes];   // a tenth of a second

        private static SoundOut Opened()
        {
            Assert.True(Hush.IsInForce, "Couldn't be sure of silence, so nothing was played");
            var sound = new SoundOut();
            Assert.True(sound.Open(), "This PC should have something to play sound through");
            return sound;
        }

        private static void Give(SoundOut sound, int tenths)
        {
            for (int i = 0; i < tenths; i++) Assert.True(sound.Write(Silence, Silence.Length, CancellationToken.None));
        }

        private static double WaitFor(SoundOut sound, double seconds)
        {
            var watch = Stopwatch.StartNew();
            while (sound.Seconds < seconds - 0.0005)
            {
                Assert.True(watch.Elapsed.TotalSeconds < 5, $"Only {sound.Seconds:0.000} s had been played after 5 s");
                Thread.Sleep(5);
            }
            return watch.Elapsed.TotalSeconds;
        }

        [OnHiddenDesktopFact]
        public void Nothing_is_played_until_it_is_told_to_and_then_it_plays_at_the_pace_of_a_clock()
        {
            using (var sound = Opened())
            {
                Give(sound, 4);
                Thread.Sleep(150);
                Assert.Equal(0, sound.Seconds);

                sound.Resume();
                double took = WaitFor(sound, 0.4);

                Assert.InRange(took, 0.3, 1.2);
                // With nothing more to play, that's where it stays.
                Thread.Sleep(150);
                Assert.Equal(0.4, sound.Seconds, 3);
            }
        }

        [OnHiddenDesktopFact]
        public void Handed_more_than_it_has_room_for_it_takes_the_rest_as_room_comes_free()
        {
            using (var sound = Opened())
            {
                sound.Resume();
                var watch = Stopwatch.StartNew();

                Give(sound, 8);

                // Four tenths fit at once. The other four had to wait their turn, a tenth of a second apart.
                Assert.InRange(watch.Elapsed.TotalSeconds, 0.25, 1.5);
                WaitFor(sound, 0.8);
            }
        }

        [OnHiddenDesktopFact]
        public void Handed_more_at_once_than_it_takes_in_one_go_it_takes_only_that_much()
        {
            using (var sound = Opened())
            {
                sound.Resume();

                // Three tenths of a second in one piece. Room is only ever made for one.
                Assert.True(sound.Write(new byte[SoundOut.BlockBytes * 3], SoundOut.BlockBytes * 3, CancellationToken.None));

                WaitFor(sound, 0.1);
                Thread.Sleep(250);
                Assert.Equal(0.1, sound.Seconds, 3);
            }
        }

        [OnHiddenDesktopFact]
        public void Pausing_holds_it_where_it_is_and_carrying_on_plays_the_rest()
        {
            using (var sound = Opened())
            {
                Give(sound, 4);
                sound.Resume();
                WaitFor(sound, 0.1);

                sound.Pause();
                Thread.Sleep(60);
                double held = sound.Seconds;
                Thread.Sleep(250);
                Assert.Equal(held, sound.Seconds);
                Assert.InRange(held, 0.1, 0.35);

                sound.Resume();
                WaitFor(sound, 0.4);
            }
        }

        [OnHiddenDesktopFact]
        public void Waiting_for_room_ends_as_soon_as_it_is_told_to_stop()
        {
            using (var sound = Opened())
            using (var stop = new CancellationTokenSource())
            {
                Give(sound, 4);   // full, and not playing, so no room will come free
                var waiting = Task.Run(() => sound.Write(Silence, Silence.Length, stop.Token));
                Assert.False(waiting.Wait(200), "It should still be waiting for room");

                stop.Cancel();

                Assert.True(waiting.Wait(2000), "It should have given up waiting");
                Assert.False(waiting.Result);
            }
        }

        [OnHiddenDesktopFact]
        public void Closed_with_sound_still_waiting_it_lets_go_at_once_and_is_harmless_afterwards()
        {
            var sound = Opened();
            Give(sound, 4);
            var waiting = Task.Run(() => sound.Write(Silence, Silence.Length, CancellationToken.None));
            Assert.False(waiting.Wait(100));

            var watch = Stopwatch.StartNew();
            sound.Dispose();

            Assert.True(waiting.Wait(2000), "The write that was waiting should have come back");
            Assert.False(waiting.Result);
            Assert.True(watch.Elapsed.TotalSeconds < 1, $"Closing took {watch.Elapsed.TotalSeconds:0.0} s");

            Assert.False(sound.Write(Silence, Silence.Length, CancellationToken.None));
            sound.Pause();
            sound.Resume();
            Assert.Equal(0, sound.Seconds);
            sound.Dispose();
        }
    }
}
