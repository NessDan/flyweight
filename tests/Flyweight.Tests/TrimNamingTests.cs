using System;
using System.Collections.Generic;
using System.IO;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    public class TrimNamingTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "fw-trim-" + Guid.NewGuid().ToString("N"));

        public TrimNamingTests() => Directory.CreateDirectory(_folder);
        public void Dispose() => Directory.Delete(_folder, true);

        private static bool Nothing(string _) => false;

        [Fact]
        public void A_trimmed_result_is_named_for_what_it_is()
        {
            Assert.Equal(@"C:\v\clip_trimmed.mp4", OutputNamer.Suggest(@"C:\v\clip.mov", null, Nothing, OutputNamer.TrimmedSuffix));
        }

        [Fact]
        public void A_second_trim_of_the_same_video_gets_a_number()
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\v\clip_trimmed.mp4" };
            Assert.Equal(@"C:\v\clip_trimmed (2).mp4", OutputNamer.Suggest(@"C:\v\clip.mp4", null, taken.Contains, OutputNamer.TrimmedSuffix));
        }

        [Fact]
        public void Dropping_a_folder_skips_results_this_app_trimmed_earlier()
        {
            foreach (string name in new[] { "a.mp4", "a_trimmed.mp4", "a_trimmed (2).mp4" })
            {
                File.WriteAllText(Path.Combine(_folder, name), "x");
            }

            Assert.Equal(new[] { Path.Combine(_folder, "a.mp4") }, VideoFiles.Expand(new[] { _folder }));
        }
    }
}
