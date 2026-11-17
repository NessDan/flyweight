using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// Playing a part of a real clip. ffmpeg really reads it, and the pictures are taken as they come
    /// due, the way the screen would take them. No sound goes anywhere: a stand-in takes it.
    /// </summary>
    [Collection(ClockTests.Name)]
    public class PartPlayerTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        private sealed class Seen
        {
            public int Number;         // which frame of the clip it is
            public double Seconds;     // where the player says it is in the part
            public double At;          // when it was handed out, in seconds since watching began
            public double Heard;       // how much sound had been heard by then
        }

        /// <param name="sound">What the sound goes to. With none, the clip had better have no sound: nothing here may reach the real speakers.</param>
        private static PartPlayer PlayerFor(string clip, PretendSound sound = null)
        {
            var player = new PartPlayer(TestVideos.Tools, clip, TestVideos.Probe(clip));
            player.NewSound = () => sound ?? throw new InvalidOperationException("This test wasn't expecting any sound");
            return player;
        }

        /// <summary>Takes pictures as they come due, until the part is over or something else says to stop.</summary>
        /// <param name="everyMs">How often the screen comes back for the next picture. A window that's covered, or shown from another PC, is redrawn only a few times a second.</param>
        private static List<Seen> Watch(PartPlayer player, PretendSound sound = null, Func<List<Seen>, bool> until = null, int limitMs = 30000, int everyMs = 2)
        {
            var seen = new List<Seen>();
            var watch = Stopwatch.StartNew();
            while (until == null ? !player.HasEnded : !until(seen))
            {
                Assert.False(player.HasFailed, "It gave up on a clip it should be able to play");
                Assert.True(watch.ElapsedMilliseconds < limitMs, $"Still going after {limitMs / 1000} s, with {seen.Count} pictures shown");

                byte[] picture = player.NextPicture();
                if (picture != null)
                {
                    seen.Add(new Seen
                    {
                        Number = TestVideos.FrameNumber(picture, player.Width, player.Height),
                        Seconds = player.Seconds,
                        At = watch.Elapsed.TotalSeconds,
                        Heard = sound?.Seconds ?? 0,
                    });
                }
                Thread.Sleep(everyMs);
            }
            return seen;
        }

        [OnHiddenDesktopFact]
        public void With_real_sound_going_out_each_picture_comes_up_as_its_moment_is_heard()
        {
            Assert.True(Hush.IsInForce, "Couldn't be sure of silence, so nothing was played");
            // A sound track with nothing on it, played through Windows for real, with this process
            // turned right down as well. What's being tested is the clock the real thing keeps.
            string clip = _videos.MakeNumberedClip("numbered.mp4", silent: true);
            var sound = new SoundOut();
            var behind = new List<double>();
            using (var player = new PartPlayer(TestVideos.Tools, clip, TestVideos.Probe(clip)))
            {
                player.NewSound = () => sound;
                player.Play(0.5, 2, 320, 180);

                var watch = Stopwatch.StartNew();
                while (!player.HasEnded)
                {
                    Assert.True(watch.Elapsed.TotalSeconds < 20, "Still going after 20 s");
                    if (player.NextPicture() != null) behind.Add(sound.Seconds - player.Seconds);
                    Thread.Sleep(2);
                }
            }

            var sorted = behind.OrderBy(b => b).ToList();
            string spread = $"{behind.Count} pictures; behind the sound by {sorted.First():0.000} s at least, {sorted[sorted.Count / 2]:0.000} s in the middle, {sorted.Last():0.000} s at most";
            Assert.True(behind.Count >= 50, "Too few of the 60 pictures were shown: " + spread);
            Assert.True(sorted.First() > -0.012, "A picture came up before its sound: " + spread);
            // Asked for every couple of thousandths of a second, each should come within a frame of its moment.
            Assert.True(sorted[sorted.Count / 2] < 0.04, "Pictures were usually late: " + spread);
            Assert.True(sorted[sorted.Count * 9 / 10] < 0.08, "Too many pictures were late: " + spread);
        }

        [Fact]
        public void Drawn_only_a_few_times_a_second_the_picture_stays_near_the_sound_and_the_sound_is_never_made_to_wait()
        {
            string clip = _videos.MakeNumberedClip("numbered.mp4", withSound: true);
            var sound = new PretendSound();
            using (var player = PlayerFor(clip, sound))
            {
                player.Play(0, 3, 320, 180);
                var seen = Watch(player, sound, everyMs: 450);

                // ffmpeg is keeping up: it's the screen that's slow. So the sound has nothing to wait for...
                Assert.False(sound.WasHeldUp, "The sound was made to wait for pictures that were ready all along");
                // ...and each time the screen does come back, it's given a picture from since it
                // last looked, not the next in line from further and further back.
                foreach (var picture in seen)
                {
                    Assert.InRange(picture.Heard - picture.Seconds, -0.012, 0.45);
                }
                // The last time it looked was up to half a second before the end, and what it was
                // given then was from the half second before that. Handed out in turn instead, the
                // last one would have been from barely a second in.
                Assert.True(seen[seen.Count - 1].Seconds > 2, $"The last picture shown was from {seen[seen.Count - 1].Seconds:0.00} s into a part of 3 s");
            }
        }

        [Fact]
        public void It_shows_exactly_the_part_chosen_from_its_first_frame_to_its_last()
        {
            string clip = _videos.MakeNumberedClip("numbered.mp4");   // 30 frames a second
            using (var player = PlayerFor(clip))
            {
                // Times that fall between frames, where a cut is easiest to get one frame out.
                player.Play(1.066, 0.967, 320, 180);
                var seen = Watch(player);

                Assert.Equal(32, seen[0].Number);
                Assert.Equal(29, player.PicturesRead);   // frames 32 to 60
                Assert.InRange(seen[seen.Count - 1].Number, 59, 60);
                Assert.Equal(seen.Select(s => s.Number).OrderBy(n => n), seen.Select(s => s.Number));
            }
        }

        [Fact]
        public void Each_picture_is_shown_when_its_moment_comes_and_says_where_in_the_part_it_is()
        {
            string clip = _videos.MakeNumberedClip("numbered.mp4");
            using (var player = PlayerFor(clip))
            {
                player.Play(1, 1, 320, 180);
                var seen = Watch(player);

                Assert.True(seen.Count >= 20, $"Only {seen.Count} of 30 pictures were shown");
                foreach (var picture in seen)
                {
                    Assert.Equal((picture.Number - 30) / 30.0, picture.Seconds, 6);
                    // Counted from when the first one was shown, which is when the clock starts.
                    double late = picture.At - seen[0].At - picture.Seconds;
                    Assert.InRange(late, -0.005, 0.25);
                }
                // A second of video takes a second to play.
                Assert.InRange(seen[seen.Count - 1].At - seen[0].At, 0.85, 1.3);
            }
        }

        [Fact]
        public void A_video_with_no_sound_is_played_without_asking_for_anything_to_play_sound_through()
        {
            string clip = _videos.MakeNumberedClip("numbered.mp4");
            bool asked = false;
            using (var player = new PartPlayer(TestVideos.Tools, clip, TestVideos.Probe(clip)))
            {
                player.NewSound = () =>
                {
                    asked = true;
                    return new PretendSound();
                };

                player.Play(0, 0.3, 320, 180);
                Watch(player);

                Assert.False(asked);
            }
        }

        [Fact]
        public void Its_sound_is_the_part_chosen_too_and_starts_where_the_part_starts()
        {
            string clip = _videos.MakeOneKeyframeClip("clip.mp4");   // a tone starts five seconds in
            var sound = new PretendSound();
            using (var player = PlayerFor(clip, sound))
            {
                player.Play(4, 2, 320, 180);
                Watch(player, sound, until: seen => player.HasEnded);

                byte[] taken = sound.Taken;
                Assert.InRange(taken.Length / (double)SoundOut.BytesASecond, 1.97, 2.03);

                // The tone should come in one second into what was handed over.
                int firstLoud = Enumerable.Range(0, taken.Length / 4).First(i => Math.Abs(BitConverter.ToInt16(taken, i * 4)) > 3000);
                Assert.InRange(firstLoud / (double)SoundOut.Rate, 0.97, 1.01);
            }
        }

        [Fact]
        public void Pictures_keep_step_with_what_has_been_heard_not_with_the_clock_on_the_wall()
        {
            string clip = _videos.MakeNumberedClip("numbered.mp4", withSound: true);
            // Sound that comes out at half speed, which no real sound does, to tell the two clocks apart.
            var sound = new PretendSound { Pace = 0.5 };
            using (var player = PlayerFor(clip, sound))
            {
                player.Play(0.5, 1, 320, 180);
                var seen = Watch(player, sound);

                Assert.True(seen.Count >= 25, $"Only {seen.Count} of 30 pictures were shown");
                foreach (var picture in seen)
                {
                    // Never shown before its sound, and not long after.
                    Assert.InRange(picture.Heard - picture.Seconds, -0.012, 0.25);
                }
                // A second of video, at the two seconds its sound took.
                Assert.InRange(seen[seen.Count - 1].At - seen[0].At, 1.7, 2.6);
                Assert.False(sound.IsPaused);
            }
        }

        [Fact]
        public void A_stretch_with_sound_but_no_pictures_left_in_it_is_over_at_once_and_is_not_a_failure()
        {
            // One second of pictures under two seconds of sound.
            string clip = _videos.Make("tail.mp4",
                "-f", "lavfi", "-i", "testsrc2=s=320x180:r=30:d=1", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2",
                "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac");
            using (var player = PlayerFor(clip, new PretendSound()))
            {
                player.Play(1.5, 0.5, 320, 180);

                var watch = Stopwatch.StartNew();
                while (!player.HasEnded)
                {
                    Assert.False(player.HasFailed, "There being nothing to show there isn't the video being unreadable");
                    Assert.True(watch.Elapsed.TotalSeconds < 20, "It never noticed there were no pictures to come");
                    Assert.Null(player.NextPicture());
                    Thread.Sleep(5);
                }
            }
        }

        [Fact]
        public void Pictures_wait_for_sound_that_is_slow_to_come_rather_than_set_off_without_it()
        {
            string clip = _videos.MakeNumberedClip("numbered.mp4", withSound: true);
            var sound = new PretendSound { FirstWriteWaitsMs = 600 };
            using (var player = PlayerFor(clip, sound))
            {
                player.Play(0.5, 1, 320, 180);
                var seen = Watch(player, sound);

                Assert.Equal(15, seen[0].Number);
                foreach (var picture in seen)
                {
                    Assert.InRange(picture.Heard - picture.Seconds, -0.012, 0.25);
                }
            }
        }

        /// <summary>The longest any picture stayed up before the next one came, in seconds.</summary>
        private static double LongestWait(List<Seen> seen)
        {
            return Enumerable.Range(1, seen.Count - 1).Max(i => seen[i].At - seen[i - 1].At);
        }

        [Fact]
        public void When_what_the_sound_is_played_through_goes_away_the_pictures_carry_on_without_a_hitch()
        {
            string clip = _videos.MakeNumberedClip("numbered.mp4", withSound: true);
            // As when headphones are unplugged half a second in: no more is taken, and it says nothing has been heard.
            var sound = new PretendSound { GoesAwayAtSeconds = 0.5 };
            using (var player = PlayerFor(clip, sound))
            {
                player.Play(0, 2, 320, 180);
                var seen = Watch(player, sound);

                Assert.True(LongestWait(seen) < 0.3, $"The picture stood still for {LongestWait(seen):0.00} s");
                Assert.InRange(seen[seen.Count - 1].At - seen[0].At, 1.7, 2.5);
            }
        }

        [Fact]
        public void A_count_of_what_has_been_heard_that_starts_again_from_nothing_does_not_hold_the_pictures_up()
        {
            string clip = _videos.MakeNumberedClip("numbered.mp4", withSound: true);
            // As Windows may do when the sound is moved to another output part way through.
            var sound = new PretendSound { CountRestartsAtSeconds = 0.5 };
            using (var player = PlayerFor(clip, sound))
            {
                player.Play(0, 2, 320, 180);
                var seen = Watch(player, sound);

                Assert.True(LongestWait(seen) < 0.3, $"The picture stood still for {LongestWait(seen):0.00} s");
                Assert.InRange(seen[seen.Count - 1].At - seen[0].At, 1.7, 2.5);
            }
        }

        [Fact]
        public void Where_a_recording_stood_still_each_picture_stays_up_for_as_long_as_it_lasted()
        {
            // For its first two seconds, only every fifteenth frame: one each half second, as a
            // screen recording has while little moves. The numbers on them are the ones they began with.
            string clip = _videos.MakeNumberedClip("uneven.mp4", standsStillUntil: 2);
            using (var player = PlayerFor(clip))
            {
                // From a quarter of a second in: frame 15 is the first there is, a quarter of a second on.
                player.Play(0.25, 1.5, 320, 180);
                var seen = Watch(player);

                Assert.Equal(15, seen[0].Number);
                foreach (var picture in seen)
                {
                    // Frame 30 belongs three quarters of a second in, and frame 45 half a second after it.
                    if (picture.Seconds < 0.7) Assert.Equal(15, picture.Number);
                    else if (picture.Seconds > 0.8 && picture.Seconds < 1.2) Assert.Equal(30, picture.Number);
                    else if (picture.Seconds > 1.3) Assert.Equal(45, picture.Number);
                }
            }
        }

        [Fact]
        public void Told_to_play_it_gets_on_with_it_in_the_background_rather_than_keeping_whoever_asked_waiting()
        {
            string clip = _videos.MakeNumberedClip("numbered.mp4", withSound: true);
            // An output that has gone to sleep can take a moment to wake.
            var sound = new PretendSound { OpenTakesMs = 700 };
            using (var player = PlayerFor(clip, sound))
            {
                var watch = Stopwatch.StartNew();
                player.Play(0, 0.5, 320, 180);
                double asking = watch.Elapsed.TotalSeconds;

                var seen = Watch(player, sound);

                Assert.True(asking < 0.3, $"Asking it to play took {asking:0.00} s");
                Assert.Equal(0, seen[0].Number);
                Assert.InRange(sound.TakenSeconds, 0.45, 0.55);
            }
        }

        [Fact]
        public void Stopped_before_the_sound_had_even_opened_it_still_lets_go_of_everything()
        {
            string clip = _videos.MakeBulkyClip("long.mov", seconds: 40, size: "640x360");
            var sound = new PretendSound { OpenTakesMs = 400 };
            var player = PlayerFor(clip, sound);

            player.Play(0, 40, 320, 180);
            player.Stop();

            var watch = Stopwatch.StartNew();
            while (!sound.IsClosed)
            {
                Assert.True(watch.Elapsed.TotalSeconds < 5, "The sound was left open");
                Thread.Sleep(20);
            }
            AssertNothingIsReading(clip);
        }

        [Fact]
        public void When_pictures_come_slower_than_they_are_shown_the_clock_waits_for_them()
        {
            // Read through a pipe that hands over the first second at once and the rest at half
            // speed, as on a PC that can't make pictures of a heavy video as fast as it plays.
            string clip = _videos.MakeNumberedClip("heavy.mkv", seconds: 3, keyframeEvery: 15);
            var media = TestVideos.Probe(clip);
            using (var slowly = new SlowFile(clip, fullSpeedSeconds: 1, pace: 0.5))
            using (var player = new PartPlayer(TestVideos.Tools, slowly.Path, media))
            {
                player.Play(0, 3, 320, 180);
                var seen = Watch(player);

                // Every picture gets its turn, the last ones included, rather than the part being
                // called over at three seconds with the picture still a second short of the end.
                Assert.Equal(90, player.PicturesRead);
                Assert.InRange(seen[seen.Count - 1].Number, 88, 89);
                Assert.True(seen[seen.Count - 1].At - seen[0].At > 3.6, $"It was all over in {seen[seen.Count - 1].At - seen[0].At:0.0} s");
            }
        }

        [Fact]
        public void With_nothing_to_play_sound_through_the_pictures_are_still_played()
        {
            string clip = _videos.MakeNumberedClip("numbered.mp4", withSound: true);
            var sound = new PretendSound { CanOpen = false };
            using (var player = PlayerFor(clip, sound))
            {
                player.Play(0, 0.5, 320, 180);
                var seen = Watch(player, sound);

                Assert.True(sound.WasOpened);
                Assert.Empty(sound.Taken);
                Assert.Equal(0, seen[0].Number);
                Assert.Equal(15, player.PicturesRead);
            }
        }


        [Fact]
        public void Stopping_lets_go_of_the_video_and_of_the_sound()
        {
            string clip = _videos.MakeBulkyClip("long.mov", seconds: 40, size: "640x360");
            var sound = new PretendSound();
            var player = PlayerFor(clip, sound);
            player.Play(0, 40, 320, 180);
            Watch(player, sound, until: seen => seen.Count > 0);
            Assert.True(player.IsPlaying);

            player.Stop();

            Assert.False(player.IsPlaying);
            Assert.Null(player.NextPicture());
            Assert.True(sound.IsClosed);
            AssertNothingIsReading(clip);
        }

        [Fact]
        public void When_the_sound_stops_being_taken_the_pictures_carry_on_and_nothing_is_left_running()
        {
            string clip = _videos.MakeBulkyClip("long.mov", seconds: 40, size: "640x360");
            // As when whatever it was playing through is unplugged a moment in.
            var sound = new PretendSound { TakesSeconds = 0.2 };
            var player = PlayerFor(clip, sound);
            player.Play(0, 40, 320, 180);

            var seen = Watch(player, sound, until: shown => shown.Count > 0 && shown[shown.Count - 1].Seconds >= 1);

            Assert.InRange(sound.TakenSeconds, 0.2, 0.31);
            Assert.True(seen.Count > 10, $"Only {seen.Count} pictures in the first second");
            player.Stop();
            AssertNothingIsReading(clip);
        }

        /// <summary>While ffmpeg is reading a clip, Windows won't let it be deleted.</summary>
        private static void AssertNothingIsReading(string clip)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    File.Delete(clip);
                    return;
                }
                catch (IOException)
                {
                    Assert.True(watch.Elapsed.TotalSeconds < 5, "ffmpeg was still reading the clip 5 s after being stopped");
                    Thread.Sleep(50);
                }
            }
        }

        [Fact]
        public void Told_to_play_from_somewhere_else_it_starts_over_from_there()
        {
            string clip = _videos.MakeNumberedClip("numbered.mp4");
            using (var player = PlayerFor(clip))
            {
                player.Play(0, 3, 320, 180);
                Assert.Equal(0, Watch(player, until: seen => seen.Count > 0)[0].Number);

                player.Play(2, 1, 320, 180);
                var first = Watch(player, until: seen => seen.Count > 0)[0];

                Assert.Equal(60, first.Number);
                Assert.Equal(0, first.Seconds, 6);
            }
        }

        [Fact]
        public void A_stream_file_plays_from_the_frame_chosen_not_from_the_next_keyframe()
        {
            // Keyframes every second. Jumping straight to 1.566 s would start half a second late.
            string clip = _videos.MakeNumberedClip("stream.ts", keyframeEvery: 30);
            using (var player = PlayerFor(clip))
            {
                player.Play(1.566, 0.967, 320, 180);
                var seen = Watch(player);

                Assert.Equal(47, seen[0].Number);
                Assert.Equal(29, player.PicturesRead);
            }
        }

        [Fact]
        public void A_video_with_more_frames_than_a_screen_shows_is_played_at_what_a_screen_shows()
        {
            string clip = _videos.MakeNumberedClip("fast.mp4", seconds: 0.8, fps: 120);
            using (var player = PlayerFor(clip))
            {
                Assert.Equal(60, player.FramesASecond);

                player.Play(0, 0.5, 320, 180);
                Watch(player);

                Assert.Equal(30, player.PicturesRead);
            }
        }

        [Fact]
        public void A_video_of_another_shape_is_fitted_in_whole_with_the_rest_left_dark()
        {
            // Four by three, in a space of sixteen by nine: 240 wide in the middle of 320.
            string clip = _videos.Make("square.mp4", "-f", "lavfi", "-i", "color=c=white:s=320x240:r=30:d=1",
                "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p");
            using (var player = PlayerFor(clip))
            {
                player.Play(0, 0.3, 320, 180);
                byte[] picture = null;
                var watch = Stopwatch.StartNew();
                while ((picture = player.NextPicture()) == null) Assert.True(watch.Elapsed.TotalSeconds < 20, "No picture came");

                Assert.Equal(320 * 180 * 4, picture.Length);
                int Blue(int x) => picture[(90 * 320 + x) * 4];
                Assert.InRange(Blue(10), 12, 20);       // the colour of the box the picture sits in
                Assert.InRange(Blue(310), 12, 20);
                Assert.InRange(Blue(160), 230, 255);    // the video itself
                Assert.InRange(Blue(50), 230, 255);
            }
        }

        [Fact]
        public void Something_ffmpeg_cannot_read_is_reported_as_having_failed_not_as_having_ended()
        {
            string fake = _videos.PathOf("notes.mp4");
            File.WriteAllText(fake, "not a video");
            using (var player = new PartPlayer(TestVideos.Tools, fake, Fixtures.Media(null, 60)))
            {
                player.Play(0, 5, 320, 180);

                var watch = Stopwatch.StartNew();
                while (!player.HasFailed)
                {
                    Assert.True(watch.Elapsed.TotalSeconds < 20, "It never noticed there was nothing to play");
                    Assert.Null(player.NextPicture());
                    Thread.Sleep(5);
                }
                Assert.False(player.HasEnded);
            }
        }
    }
}
