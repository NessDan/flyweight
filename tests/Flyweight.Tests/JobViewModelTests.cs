using System;
using System.Collections.Generic;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    public class JobViewModelTests
    {
        private static JobViewModel NewJob() => new JobViewModel(@"C:\v\clip.mp4", Presets.Default(LevelId.Balanced));

        private static CompressionProgress Progress(double percent, int? secondsLeft = null, string note = null)
        {
            return new CompressionProgress
            {
                Percent = percent,
                Remaining = secondsLeft.HasValue ? TimeSpan.FromSeconds(secondsLeft.Value) : (TimeSpan?)null,
                Note = note,
            };
        }

        [Fact]
        public void A_new_job_is_waiting_and_can_be_cancelled()
        {
            var job = NewJob();
            Assert.Equal("clip.mp4", job.FileName);
            Assert.Equal("Waiting…", job.Status);
            Assert.True(job.CanCancel);
            Assert.False(job.IsFinished);
            Assert.False(job.IsRunning);
        }

        [Fact]
        public void Starting_shows_a_working_state()
        {
            var job = NewJob();
            job.MarkStarted();
            Assert.Equal("Starting…", job.Status);
            Assert.True(job.IsRunning);
            Assert.True(job.CanCancel);
        }

        [Fact]
        public void Progress_shows_percent_and_time_left()
        {
            var job = NewJob();
            job.MarkStarted();
            job.Apply(Progress(42.4, 72));
            Assert.Equal(42.4, job.Percent);
            Assert.Equal("42% · 1:12 left", job.Status);
        }

        [Fact]
        public void Progress_without_a_time_estimate_shows_just_the_percent()
        {
            var job = NewJob();
            job.MarkStarted();
            job.Apply(Progress(10));
            Assert.Equal("10%", job.Status);
        }

        [Fact]
        public void Progress_notes_explain_a_change_of_plan()
        {
            var job = NewJob();
            job.MarkStarted();
            job.Apply(Progress(0, null, "Graphics card unavailable, using the processor"));
            Assert.Contains("using the processor", job.Status);
        }

        [Fact]
        public void A_note_stays_visible_alongside_the_progress()
        {
            var job = NewJob();
            job.MarkStarted();
            job.Apply(Progress(42, 72, "Trying harder"));
            Assert.Equal("Trying harder · 42% · 1:12 left", job.Status);
        }

        [Fact]
        public void The_end_of_the_bar_reads_as_finishing_up()
        {
            var job = NewJob();
            job.MarkStarted();
            job.Apply(Progress(100, 0));
            Assert.Equal("Finishing…", job.Status);
        }

        [Fact]
        public void Success_shows_savings_and_offers_the_file()
        {
            var job = NewJob();
            job.MarkStarted();
            job.Apply(new CompressionResult
            {
                Outcome = JobOutcome.Done,
                InputBytes = 1000L * 1048576,
                OutputBytes = 250L * 1048576,
                OutputPath = @"C:\v\clip_compressed.mp4",
            });
            Assert.Contains("75% smaller", job.Status);
            Assert.Equal(100, job.Percent);
            Assert.True(job.IsFinished);
            Assert.False(job.IsRunning);
            Assert.False(job.CanCancel);
            Assert.True(job.CanShowFile);
            Assert.Equal(@"C:\v\clip_compressed.mp4", job.ResultPath);
            Assert.False(job.Failed);
        }

        [Fact]
        public void A_warning_is_shown_alongside_the_savings()
        {
            var job = NewJob();
            job.Apply(new CompressionResult
            {
                Outcome = JobOutcome.Done,
                InputBytes = 1000,
                OutputBytes = 500,
                OutputPath = "x",
                Message = "Couldn't get under 5 MB",
            });
            Assert.Contains("Couldn't get under 5 MB", job.Status);
        }

        [Fact]
        public void Already_small_explains_itself_and_points_to_the_untouched_original()
        {
            var job = NewJob();
            job.Apply(new CompressionResult { Outcome = JobOutcome.AlreadySmall, Message = "Already small", InputBytes = 5 });
            Assert.Equal("Already small", job.Status);
            Assert.True(job.IsFinished);
            Assert.False(job.Failed);
            Assert.True(job.CanShowFile);
            Assert.Equal(@"C:\v\clip.mp4", job.ResultPath);
        }

        [Fact]
        public void Failure_shows_the_message_and_keeps_the_details()
        {
            var job = NewJob();
            job.Apply(new CompressionResult { Outcome = JobOutcome.Failed, Message = "Couldn't compress", Details = "ffmpeg said no" });
            Assert.Equal("Couldn't compress", job.Status);
            Assert.True(job.IsFinished);
            Assert.True(job.Failed);
            Assert.False(job.CanShowFile);
            Assert.True(job.HasDetails);
            Assert.Equal("ffmpeg said no", job.Details);
        }

        [Fact]
        public void Cancelled_is_not_an_error()
        {
            var job = NewJob();
            job.Apply(new CompressionResult { Outcome = JobOutcome.Canceled });
            Assert.Equal("Cancelled", job.Status);
            Assert.True(job.IsFinished);
            Assert.False(job.Failed);
            Assert.False(job.HasDetails);
        }

        [Fact]
        public void Cancelling_a_waiting_job_finishes_it_without_running()
        {
            var job = NewJob();
            job.Cancel();
            Assert.Equal("Cancelled", job.Status);
            Assert.True(job.IsFinished);
            Assert.False(job.CanCancel);
        }

        [Fact]
        public void Cancelling_a_running_job_signals_the_token_and_waits_for_it_to_stop()
        {
            var job = NewJob();
            job.MarkStarted();
            job.Cancel();
            Assert.True(job.CancelToken.IsCancellationRequested);
            Assert.Equal("Cancelling…", job.Status);
            Assert.False(job.IsFinished);
            Assert.False(job.CanCancel);
        }

        [Fact]
        public void Changes_notify_the_window()
        {
            var job = NewJob();
            var changed = new List<string>();
            job.PropertyChanged += (s, e) => changed.Add(e.PropertyName);

            job.MarkStarted();
            job.Apply(Progress(50, 10));

            Assert.Contains(nameof(JobViewModel.Status), changed);
            Assert.Contains(nameof(JobViewModel.Percent), changed);
            Assert.Contains(nameof(JobViewModel.IsRunning), changed);
        }
    }
}
