using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    public class TrimJobTests
    {
        private static JobViewModel Trimmed()
        {
            return new JobViewModel(@"C:\v\clip.mp4", Presets.Default(LevelId.Balanced), new TrimRange(12.4, 54));
        }

        [Fact]
        public void A_trimmed_job_says_which_part_it_keeps()
        {
            Assert.Equal("clip.mp4 · 0:12 to 0:54", Trimmed().Label);
            Assert.Equal("clip.mp4", Trimmed().FileName);
        }

        [Fact]
        public void An_ordinary_job_is_labelled_with_just_its_name()
        {
            Assert.Equal("clip.mp4", new JobViewModel(@"C:\v\clip.mp4", Presets.Default(LevelId.Balanced)).Label);
        }

        [Fact]
        public void A_finished_trim_shows_its_length_and_size_instead_of_a_saving()
        {
            var job = Trimmed();
            job.Apply(new CompressionResult
            {
                Outcome = JobOutcome.Done, InputBytes = 105500000, OutputBytes = 10066330, OutputPath = @"C:\v\clip_trimmed.mp4", KeptSeconds = 41.6,
            });

            Assert.Equal("Trimmed to 0:42 · 9.6 MB", job.Status);
            Assert.True(job.CanShowFile);
            Assert.True(job.IsFinished);
        }

        private static readonly CropArea Quarter = new CropArea(0.25, 0.25, 0.75, 0.75);

        [Fact]
        public void A_cropped_job_says_so_after_the_part_it_keeps()
        {
            Assert.Equal("clip.mp4 · cropped", new JobViewModel(@"C:\v\clip.mp4", Presets.Default(LevelId.Balanced), null, Quarter).Label);
            Assert.Equal("clip.mp4 · 0:12 to 0:54 · cropped", new JobViewModel(@"C:\v\clip.mp4", Presets.Default(LevelId.Balanced), new TrimRange(12.4, 54), Quarter).Label);
        }

        [Fact]
        public void A_job_keeps_the_crop_it_was_given_to_hand_to_the_compress()
        {
            Assert.Same(Quarter, new JobViewModel(@"C:\v\clip.mp4", Presets.Default(LevelId.Balanced), null, Quarter).Crop);
            Assert.Null(Trimmed().Crop);
        }

        [Fact]
        public void A_finished_crop_shows_its_size_instead_of_a_saving()
        {
            // Smaller mostly for being a smaller picture, so a percentage would mislead, as it would for a trim.
            var job = new JobViewModel(@"C:\v\clip.mp4", Presets.Default(LevelId.Balanced), null, Quarter);
            job.Apply(new CompressionResult
            {
                Outcome = JobOutcome.Done, InputBytes = 105500000, OutputBytes = 10066330, OutputPath = @"C:\v\clip_cropped.mp4", Cropped = true,
            });

            Assert.Equal("Cropped · 9.6 MB", job.Status);
        }

        [Fact]
        public void A_finished_trim_still_passes_on_a_caveat()
        {
            var job = Trimmed();
            job.Apply(new CompressionResult
            {
                Outcome = JobOutcome.Done, InputBytes = 105500000, OutputBytes = 10066330, OutputPath = @"C:\v\x.mp4", KeptSeconds = 41.6,
                Message = "Fell just short of 5 MB",
            });

            Assert.Equal("Trimmed to 0:42 · 9.6 MB · Fell just short of 5 MB", job.Status);
        }
    }
}
