using System.Collections.Generic;
using System.Linq;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    public class TrimCliTests
    {
        [Fact]
        public void The_right_click_trim_entry_passes_a_flag_and_a_file()
        {
            var cli = CliArgs.Parse(new[] { "--trim", @"C:\v\a.mp4" });
            Assert.True(cli.Trim);
            Assert.Equal(new[] { @"C:\v\a.mp4" }, cli.Files);
            Assert.Equal(CliMode.Run, cli.Mode);
        }

        [Fact]
        public void An_ordinary_request_is_not_a_trim()
        {
            Assert.False(CliArgs.Parse(new[] { "--level", "light", @"C:\v\a.mp4" }).Trim);
            Assert.False(CliArgs.FromMessage(new CliArgs { Files = new List<string> { "a.mp4" } }.ToMessage()).Trim);
        }

        [Fact]
        public void A_trim_request_round_trips_through_its_text_form()
        {
            var original = new CliArgs { Trim = true, Files = new List<string> { @"C:\v\a b.mp4", @"D:\c d.mkv" } };
            var copy = CliArgs.FromMessage(original.ToMessage());
            Assert.True(copy.Trim);
            Assert.Equal(original.Files, copy.Files);
        }

        [Fact]
        public void A_copy_from_before_trimming_existed_cannot_mistake_a_trim_for_a_compress()
        {
            // An older window that is still open reads every line after the first two as a file to
            // compress. Marking each line means it finds no such file and does nothing.
            string message = new CliArgs { Trim = true, Files = new List<string> { @"C:\v\a.mp4" } }.ToMessage();
            var lines = message.Split((char)10).Skip(2).ToList();
            Assert.NotEmpty(lines);
            Assert.All(lines, line => Assert.StartsWith("trim=", line));
        }
    }
}
