using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    public class TrimSelectionTests
    {
        [Fact]
        public void Starts_out_keeping_the_whole_video()
        {
            var selection = new TrimSelection(10, 60);
            Assert.Equal(10, selection.Duration, 6);
            Assert.Equal(0, selection.Start, 6);
            Assert.Equal(10, selection.End, 6);
            Assert.True(selection.IsWholeVideo);
            Assert.Null(selection.ToRange());
        }

        [Fact]
        public void Moving_the_handles_picks_a_part()
        {
            var selection = new TrimSelection(10, 60);
            selection.Move(TrimHandle.Start, 3.2);
            selection.Move(TrimHandle.End, 7);

            Assert.False(selection.IsWholeVideo);
            Assert.Equal(3.8, selection.Length, 6);
            var range = selection.ToRange();
            Assert.Equal(3.2, range.StartSeconds, 6);
            Assert.Equal(7, range.EndSeconds, 6);
        }

        [Fact]
        public void Cutting_only_the_end_is_still_a_trim()
        {
            var selection = new TrimSelection(10, 60);
            selection.Move(TrimHandle.End, 7);
            Assert.False(selection.IsWholeVideo);
            Assert.Equal(0, selection.ToRange().StartSeconds, 6);
        }

        [Fact]
        public void An_end_left_at_the_far_right_runs_to_the_very_end_of_the_file()
        {
            // The picture stops at 10 s here, but the sound runs on to 10.4 s. Cutting off the start
            // shouldn't also cut off that tail.
            var selection = new TrimSelection(10, 60);
            selection.Move(TrimHandle.Start, 2);

            var range = selection.ToRange(fileSeconds: 10.4);

            Assert.Equal(2, range.StartSeconds, 6);
            Assert.Equal(10.4, range.EndSeconds, 6);
        }

        [Fact]
        public void An_end_that_was_moved_in_stays_where_it_was_put()
        {
            var selection = new TrimSelection(10, 60);
            selection.Move(TrimHandle.End, 8);
            Assert.Equal(8, selection.ToRange(fileSeconds: 10.4).EndSeconds, 6);
        }

        [Fact]
        public void A_file_that_does_not_say_how_long_it_is_ends_where_the_picture_does()
        {
            var selection = new TrimSelection(10, 60);
            selection.Move(TrimHandle.Start, 2);
            Assert.Equal(10, selection.ToRange(fileSeconds: 0).EndSeconds, 6);
            Assert.Null(new TrimSelection(10, 60).ToRange(fileSeconds: 10.4));
        }

        [Fact]
        public void The_handles_stay_inside_the_video()
        {
            var selection = new TrimSelection(10, 60);
            selection.Move(TrimHandle.Start, 3);
            selection.Move(TrimHandle.End, 6);

            selection.Move(TrimHandle.Start, -4);
            selection.Move(TrimHandle.End, 99);

            Assert.Equal(0, selection.Start, 6);
            Assert.Equal(10, selection.End, 6);
        }

        [Fact]
        public void The_handles_cannot_cross()
        {
            var selection = new TrimSelection(10, 60);
            selection.Move(TrimHandle.End, 5);
            selection.Move(TrimHandle.Start, 9);
            Assert.Equal(4.9, selection.Start, 6);

            selection.Move(TrimHandle.Start, 2);
            selection.Move(TrimHandle.End, 0.5);
            Assert.Equal(2.1, selection.End, 6);
        }

        [Theory]
        [InlineData(10.0, 60.0, 0.1)]     // a tenth of a second, even when frames are shorter
        [InlineData(10.0, 5.0, 0.2)]      // or one whole frame, when frames are longer
        [InlineData(0.05, 60.0, 0.05)]    // or all of a very short video
        public void The_shortest_part_is_a_frame_or_a_tenth_of_a_second(double duration, double fps, double expected)
        {
            Assert.Equal(expected, new TrimSelection(duration, fps).MinLength, 6);
        }

        [Fact]
        public void Nudging_steps_one_frame_at_a_time()
        {
            var selection = new TrimSelection(10, 60);

            selection.Nudge(TrimHandle.Start, 1);
            Assert.Equal(0.016, selection.Start, 6);
            selection.Nudge(TrimHandle.Start, 1);
            Assert.Equal(0.033, selection.Start, 6);
            selection.Nudge(TrimHandle.Start, -1);
            Assert.Equal(0.016, selection.Start, 6);

            selection.Nudge(TrimHandle.End, -1);
            Assert.Equal(9.983, selection.End, 6);
        }

        [Fact]
        public void Nudging_from_between_frames_lands_on_the_neighbouring_frame()
        {
            var selection = new TrimSelection(10, 60);
            selection.Move(TrimHandle.Start, 3.517);   // the first frame at or after this is number 212, at 3.5333

            selection.Nudge(TrimHandle.Start, 1);
            Assert.Equal(3.55, selection.Start, 6);    // frame 213

            selection.Nudge(TrimHandle.Start, -2);
            Assert.Equal(3.516, selection.Start, 6);   // frame 211, at 3.5167
        }

        [Fact]
        public void A_nudge_cannot_push_a_handle_out_of_the_video()
        {
            var selection = new TrimSelection(10, 60);
            selection.Nudge(TrimHandle.Start, -5);
            selection.Nudge(TrimHandle.End, 5);
            Assert.Equal(0, selection.Start, 6);
            Assert.Equal(10, selection.End, 6);
        }

        [Fact]
        public void The_picture_for_the_end_handle_is_the_last_frame_kept()
        {
            var selection = new TrimSelection(10, 60);
            selection.Move(TrimHandle.Start, 2);
            selection.Move(TrimHandle.End, 5);   // frame 300 starts at 5.0 and is the first one left out

            Assert.Equal(2, selection.PreviewTime(TrimHandle.Start), 6);
            Assert.Equal(4.983, selection.PreviewTime(TrimHandle.End), 6);   // frame 299
        }

        [Fact]
        public void A_handle_put_on_a_time_keeps_the_frame_that_time_stands_for()
        {
            // The first frame at or after it, as for any time a handle is at.
            var selection = new TrimSelection(10, 30);

            selection.MoveOnto(TrimHandle.Start, 1.24);    // frame 38, at 1.2667
            Assert.Equal(1.24, selection.Start, 6);

            // Frame 17 begins at 0.5667. To the nearest thousandth that's 0.567, which is past it and
            // would start on frame 18, so the start goes on the thousandth before.
            selection.MoveOnto(TrimHandle.Start, 17 / 30.0);
            Assert.Equal(0.566, selection.Start, 6);

            // For the end, that frame is the last kept, so the end goes where the next one begins.
            selection.MoveOnto(TrimHandle.End, 1.24);      // frame 38, so up to 39 at 1.3
            Assert.Equal(1.3, selection.End, 6);
            selection.MoveOnto(TrimHandle.End, 46 / 30.0); // frame 46, so up to 47 at 1.5667, just before it
            Assert.Equal(1.566, selection.End, 6);
        }

        [Fact]
        public void The_frame_a_moving_picture_shows_is_the_last_one_due_before_the_next_picture()
        {
            // Thirty pictures a second of a video of thirty frames, a picture's moment a little either side.
            var selection = new TrimSelection(10, 30);
            Assert.Equal(1.233, selection.FrameShown(1.234, 30), 6);   // frame 37, at 1.2333
            Assert.Equal(1.266, selection.FrameShown(1.262, 30), 6);   // frame 38, at 1.2667

            // Ten frames a second played as thirty pictures: each frame is on show for three of them.
            var slow = new TrimSelection(10, 10);
            for (int picture = 3; picture <= 5; picture++) Assert.Equal(0.1, slow.FrameShown(picture / 30.0, 30), 6);
            Assert.Equal(0.2, slow.FrameShown(6 / 30.0, 30), 6);
        }

        [Fact]
        public void A_handle_put_on_the_frame_a_moving_picture_shows_keeps_that_frame_first_or_last()
        {
            var slow = new TrimSelection(10, 10);
            slow.MoveOntoShown(TrimHandle.Start, 5 / 30.0, 30);   // picture 5 is still frame 1
            Assert.Equal(0.1, slow.Start, 6);
            slow.MoveOntoShown(TrimHandle.End, 14 / 30.0, 30);    // picture 14 is still frame 4, at 0.4
            Assert.InRange(slow.End, 0.401, 0.5);                 // kept last, and frame 5 at 0.5 left out

            var selection = new TrimSelection(10, 30);
            selection.MoveOntoShown(TrimHandle.End, 2.398, 30);   // frame 72, at 2.4
            Assert.InRange(selection.End, 2.401, 2.433);          // and 73 at 2.4333 left out
        }

        [Fact]
        public void Put_onto_a_frame_a_handle_still_stays_inside_the_video_and_clear_of_the_other()
        {
            var selection = new TrimSelection(3.5, 30);

            // The last frame there is, kept last: that's to the end.
            selection.MoveOnto(TrimHandle.End, 104 / 30.0);
            Assert.Equal(3.5, selection.End, 6);
            Assert.True(selection.IsWholeVideo);

            selection.MoveOnto(TrimHandle.Start, 9);
            Assert.Equal(3.4, selection.Start, 6);
            selection.MoveOnto(TrimHandle.End, 0);
            Assert.Equal(3.5, selection.End, 6);
        }

        [Fact]
        public void A_video_with_no_known_frame_rate_is_treated_as_30_a_second()
        {
            var selection = new TrimSelection(10, 0);
            Assert.Equal(1 / 30.0, selection.FrameSeconds, 6);
            selection.Nudge(TrimHandle.Start, 3);
            Assert.Equal(0.1, selection.Start, 6);
        }
    }
}
