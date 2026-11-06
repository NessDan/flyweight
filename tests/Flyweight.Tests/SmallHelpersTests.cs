using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    public class ProgressParserTests
    {
        [Fact]
        public void Reports_once_per_ffmpeg_progress_block()
        {
            var p = new ProgressParser();
            Assert.Null(p.Feed("frame=120"));
            Assert.Null(p.Feed("out_time_us=5000000"));
            Assert.Null(p.Feed("speed=2.5x"));
            var snap = p.Feed("progress=continue");
            Assert.Equal(5.0, snap.Seconds);
            Assert.Equal(2.5, snap.Speed);
        }

        [Fact]
        public void The_final_block_reports_too()
        {
            var p = new ProgressParser();
            p.Feed("out_time_us=9000000");
            Assert.Equal(9.0, p.Feed("progress=end").Seconds);
        }

        [Fact]
        public void Ignores_values_ffmpeg_has_not_worked_out_yet()
        {
            var p = new ProgressParser();
            p.Feed("out_time_us=N/A");
            p.Feed("speed=N/A");
            var snap = p.Feed("progress=continue");
            Assert.Equal(0, snap.Seconds);
            Assert.Equal(0, snap.Speed);
        }

        [Fact]
        public void Survives_garbage_lines()
        {
            var p = new ProgressParser();
            Assert.Null(p.Feed(null));
            Assert.Null(p.Feed(""));
            Assert.Null(p.Feed("no equals sign"));
        }

        [Theory]
        [InlineData(30, 60, 50)]
        [InlineData(0, 60, 0)]
        [InlineData(90, 60, 100)]
        [InlineData(10, 0, 0)]
        public void Percent_stays_between_0_and_100(double seconds, double duration, double expected)
        {
            Assert.Equal(expected, new ProgressSnapshot { Seconds = seconds }.Percent(duration));
        }

        [Fact]
        public void Remaining_time_uses_encoding_speed()
        {
            var snap = new ProgressSnapshot { Seconds = 30, Speed = 3 };
            Assert.Equal(TimeSpan.FromSeconds(10), snap.Remaining(60));
        }

        [Fact]
        public void Remaining_time_is_unknown_until_speed_is_known()
        {
            Assert.Null(new ProgressSnapshot { Seconds = 30, Speed = 0 }.Remaining(60));
        }
    }

    public class OutputNamerTests
    {
        private static bool Nothing(string _) => false;

        [Fact]
        public void Output_sits_next_to_the_original_as_an_mp4()
        {
            Assert.Equal(@"C:\v\clip_compressed.mp4", OutputNamer.Suggest(@"C:\v\clip.mov", null, Nothing));
        }

        [Fact]
        public void Never_overwrites_an_existing_file()
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"C:\v\clip_compressed.mp4",
                @"C:\v\clip_compressed (2).mp4",
            };
            Assert.Equal(@"C:\v\clip_compressed (3).mp4", OutputNamer.Suggest(@"C:\v\clip.mp4", null, taken.Contains));
        }

        [Fact]
        public void Can_save_into_a_chosen_folder()
        {
            Assert.Equal(@"D:\out\clip_compressed.mp4", OutputNamer.Suggest(@"C:\v\clip.mp4", @"D:\out", Nothing));
        }

        [Fact]
        public void Keeps_unusual_names_intact()
        {
            Assert.Equal(@"C:\v\Chicago's Top 8 (5)_compressed.mp4", OutputNamer.Suggest(@"C:\v\Chicago's Top 8 (5).mp4", null, Nothing));
        }

        [Fact]
        public void Partial_output_gets_its_own_extension()
        {
            Assert.Equal(@"C:\v\clip_compressed.mp4.part", OutputNamer.PartPath(@"C:\v\clip_compressed.mp4"));
        }
    }

    public class VideoFilesTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "fw-tests-" + Guid.NewGuid().ToString("N"));

        public VideoFilesTests() => Directory.CreateDirectory(_dir);
        public void Dispose() => Directory.Delete(_dir, true);

        private string Touch(string name)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, "x");
            return path;
        }

        [Theory]
        [InlineData("a.mp4", true)]
        [InlineData("a.MOV", true)]
        [InlineData("a.mkv", true)]
        [InlineData("a.webm", true)]
        [InlineData("a.txt", false)]
        [InlineData("a.jpg", false)]
        [InlineData("noextension", false)]
        public void Recognises_videos_by_extension(string name, bool expected)
        {
            Assert.Equal(expected, VideoFiles.IsVideo(name));
        }

        [Theory]
        [InlineData("recording.ts")]
        [InlineData("camcorder.mts")]
        public void Transport_stream_videos_can_still_be_dropped_on_the_window(string name)
        {
            Assert.True(VideoFiles.IsVideo(name));
            Assert.DoesNotContain(System.IO.Path.GetExtension(name), VideoFiles.MenuExtensions);
        }

        [Fact]
        public void Expanding_drops_non_videos_and_files_that_do_not_exist()
        {
            var video = Touch("a.mp4");
            var text = Touch("notes.txt");
            Assert.Equal(new[] { video }, VideoFiles.Expand(new[] { video, text, Path.Combine(_dir, "gone.mp4") }));
        }

        [Fact]
        public void Expanding_a_folder_takes_the_videos_directly_inside_it()
        {
            var a = Touch("a.mp4");
            var b = Touch("b.mov");
            Touch("notes.txt");
            var sub = Directory.CreateDirectory(Path.Combine(_dir, "sub")).FullName;
            File.WriteAllText(Path.Combine(sub, "deep.mp4"), "x");
            Assert.Equal(new[] { a, b }, VideoFiles.Expand(new[] { _dir }).OrderBy(x => x));
        }

        [Fact]
        public void Expanding_a_folder_skips_files_already_compressed_by_this_app()
        {
            var a = Touch("a.mp4");
            Touch("a_compressed.mp4");
            Touch("a_compressed (2).mp4");
            Assert.Equal(new[] { a }, VideoFiles.Expand(new[] { _dir }));
        }

        [Fact]
        public void A_compressed_file_can_still_be_chosen_directly()
        {
            var compressed = Touch("a_compressed.mp4");
            Assert.Equal(new[] { compressed }, VideoFiles.Expand(new[] { compressed }));
        }

        [Fact]
        public void Duplicates_are_removed()
        {
            var a = Touch("a.mp4");
            Assert.Equal(new[] { a }, VideoFiles.Expand(new[] { a, a.ToUpperInvariant() }));
        }
    }

    public class CliArgsTests
    {
        [Fact]
        public void No_arguments_opens_the_window()
        {
            var cli = CliArgs.Parse(new string[0]);
            Assert.Equal(CliMode.Run, cli.Mode);
            Assert.Empty(cli.Files);
            Assert.Null(cli.Level);
        }

        [Theory]
        [InlineData("light", LevelId.Light)]
        [InlineData("balanced", LevelId.Balanced)]
        [InlineData("maximum", LevelId.Maximum)]
        [InlineData("BALANCED", LevelId.Balanced)]
        public void Right_click_passes_a_level_and_a_file(string text, LevelId expected)
        {
            var cli = CliArgs.Parse(new[] { "--level", text, @"C:\v\a.mp4" });
            Assert.Equal(expected, cli.Level);
            Assert.Equal(new[] { @"C:\v\a.mp4" }, cli.Files);
        }

        [Fact]
        public void Files_alone_use_the_saved_level()
        {
            // Dragging onto the desktop icon passes just the paths.
            var cli = CliArgs.Parse(new[] { @"C:\v\a.mp4", @"C:\v\b.mkv" });
            Assert.Null(cli.Level);
            Assert.Equal(2, cli.Files.Count);
        }

        [Fact]
        public void An_unknown_level_falls_back_to_the_saved_level_rather_than_failing()
        {
            var cli = CliArgs.Parse(new[] { "--level", "ultra", @"C:\v\a.mp4" });
            Assert.Null(cli.Level);
            Assert.Equal(new[] { @"C:\v\a.mp4" }, cli.Files);
        }

        [Fact]
        public void Right_click_on_a_custom_preset_passes_its_id()
        {
            var cli = CliArgs.Parse(new[] { "--preset", "ab12cd34", @"C:\v\a.mp4" });
            Assert.Equal("ab12cd34", cli.PresetId);
            Assert.Null(cli.Level);
            Assert.Equal(new[] { @"C:\v\a.mp4" }, cli.Files);
        }

        [Fact]
        public void A_custom_preset_request_round_trips_through_its_text_form()
        {
            var copy = CliArgs.FromMessage(new CliArgs { PresetId = "ab12cd34", Files = new List<string> { "a.mp4" } }.ToMessage());
            Assert.Equal("ab12cd34", copy.PresetId);
            Assert.Null(copy.Level);
            Assert.Equal(new[] { "a.mp4" }, copy.Files);
        }

        [Fact]
        public void Recognises_install_and_uninstall()
        {
            Assert.Equal(CliMode.Install, CliArgs.Parse(new[] { "--install" }).Mode);
            Assert.Equal(CliMode.Uninstall, CliArgs.Parse(new[] { "--uninstall" }).Mode);
            Assert.True(CliArgs.Parse(new[] { "--uninstall", "--quiet" }).Quiet);
            Assert.False(CliArgs.Parse(new[] { "--uninstall" }).Quiet);
        }

        [Fact]
        public void Request_round_trips_through_its_text_form()
        {
            var original = new CliArgs { Level = LevelId.Maximum, Files = new List<string> { @"C:\v\a b.mp4", @"D:\c's.mkv" } };
            var copy = CliArgs.FromMessage(original.ToMessage());
            Assert.Equal(LevelId.Maximum, copy.Level);
            Assert.Equal(original.Files, copy.Files);
        }

        [Fact]
        public void Request_without_a_level_round_trips()
        {
            var copy = CliArgs.FromMessage(new CliArgs { Files = new List<string> { "a.mp4" } }.ToMessage());
            Assert.Null(copy.Level);
            Assert.Equal(new[] { "a.mp4" }, copy.Files);
        }
    }

    public class ProcessArgsTests
    {
        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("has space", "\"has space\"")]
        [InlineData("", "\"\"")]
        [InlineData(@"C:\a b\", "\"C:\\a b\\\\\"")]
        [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
        [InlineData(@"C:\v\Chicago's.mp4", @"C:\v\Chicago's.mp4")]
        [InlineData(@"back\slash", @"back\slash")]
        public void Quotes_follow_windows_command_line_rules(string input, string expected)
        {
            Assert.Equal(expected, ProcessArgs.Quote(input));
        }

        [Fact]
        public void Join_quotes_each_argument()
        {
            Assert.Equal("-i \"a b.mp4\" -y", ProcessArgs.Join(new[] { "-i", "a b.mp4", "-y" }));
        }

        [Fact]
        public void Split_understands_quotes()
        {
            Assert.Equal(new[] { "-metadata", "title=My Clip", "-g", "120" }, ProcessArgs.Split("-metadata title=\"My Clip\"  -g 120"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Split_of_nothing_is_empty(string text)
        {
            Assert.Empty(ProcessArgs.Split(text));
        }
    }

    public class FormatTests
    {
        [Theory]
        [InlineData(0, "0 KB")]
        [InlineData(1536, "2 KB")]
        [InlineData(10066330L, "9.6 MB")]
        [InlineData(1048576L * 86, "86 MB")]
        [InlineData(1048576L * 412, "412 MB")]
        [InlineData(1073741824L * 3 / 2, "1.5 GB")]
        public void Sizes_read_naturally(long bytes, string expected)
        {
            Assert.Equal(expected, Format.Size(bytes));
        }

        [Theory]
        [InlineData(5, "0:05")]
        [InlineData(72, "1:12")]
        [InlineData(3723, "1:02:03")]
        public void Durations_read_like_a_clock(int seconds, string expected)
        {
            Assert.Equal(expected, Format.Clock(TimeSpan.FromSeconds(seconds)));
        }

        [Fact]
        public void Shows_what_was_saved()
        {
            Assert.Equal("1.0 GB → 200 MB · 80% smaller", Format.Savings(1073741824L, 1048576L * 205 - 1048576L * 5));
        }

        [Fact]
        public void Tiny_savings_round_sensibly()
        {
            Assert.Contains("1% smaller", Format.Savings(1000000, 990000));
        }
    }

    public class WindowSizingTests
    {
        private static (double Width, double Height)? Fit(double width, double height)
        {
            return WindowSizing.Fit(width, height, minWidth: 480, minHeight: 300, screenWidth: 1920, screenHeight: 1040);
        }

        [Fact]
        public void Never_resized_means_nothing_to_bring_back()
        {
            Assert.Null(Fit(0, 0));
        }

        [Fact]
        public void A_size_that_fits_comes_back_as_it_was()
        {
            Assert.Equal((900.0, 700.0), Fit(900, 700).Value);
        }

        [Fact]
        public void A_size_too_big_for_this_screen_is_brought_down_to_fit()
        {
            Assert.Equal((1920.0, 1040.0), Fit(3840, 2000).Value);
        }

        [Fact]
        public void A_size_below_the_minimum_is_brought_up_to_it()
        {
            Assert.Equal((480.0, 300.0), Fit(100, 50).Value);
        }

        [Theory]
        [InlineData(double.NaN, 700)]
        [InlineData(900, double.PositiveInfinity)]
        [InlineData(-900, 700)]
        public void A_nonsense_size_is_ignored(double width, double height)
        {
            Assert.Null(Fit(width, height));
        }
    }

    public class AppSettingsTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "fw-settings-" + Guid.NewGuid().ToString("N"), "settings.json");

        public void Dispose()
        {
            var dir = Path.GetDirectoryName(_path);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        [Fact]
        public void A_missing_file_gives_defaults_with_balanced_selected()
        {
            var s = AppSettings.Load(_path);
            Assert.Equal(LevelId.Balanced, s.SelectedLevel);
            Assert.Null(s.OutputFolder);
            Assert.True(s.Get(LevelId.Light).SameAs(Presets.Default(LevelId.Light)));
        }

        [Fact]
        public void Changes_survive_a_save_and_load()
        {
            var s = AppSettings.Load(_path);
            s.SelectedLevel = LevelId.Maximum;
            s.OutputFolder = @"D:\out";
            s.Get(LevelId.Balanced).Codec = VideoCodec.Av1;
            s.Get(LevelId.Balanced).TargetSizeMb = 10;
            s.Save(_path);

            var loaded = AppSettings.Load(_path);
            Assert.Equal(LevelId.Maximum, loaded.SelectedLevel);
            Assert.Equal(@"D:\out", loaded.OutputFolder);
            Assert.Equal(VideoCodec.Av1, loaded.Get(LevelId.Balanced).Codec);
            Assert.Equal(10, loaded.Get(LevelId.Balanced).TargetSizeMb);
            Assert.True(loaded.Get(LevelId.Light).SameAs(Presets.Default(LevelId.Light)));
        }

        // ---- settings from when the app was called Video Compressor ----

        private string FormerPath => Path.Combine(Path.GetDirectoryName(_path), "former", "settings.json");

        private void WriteFormerSettings()
        {
            var former = AppSettings.Load(FormerPath);
            former.AddCustom("Discord", Presets.Default(LevelId.Balanced)).Settings.TargetSizeMb = 10;
            former.OutputFolder = @"D:\out";
            former.Save(FormerPath);
        }

        [Fact]
        public void Settings_from_the_former_name_are_brought_across()
        {
            WriteFormerSettings();

            Assert.True(AppSettings.AdoptFormerSettings(FormerPath, _path));

            var adopted = AppSettings.Load(_path);
            Assert.Equal("Discord", adopted.CustomPresets.Single().Name);
            Assert.Equal(@"D:\out", adopted.OutputFolder);
        }

        [Fact]
        public void Bringing_settings_across_leaves_the_old_file_alone()
        {
            // "VideoCompressor" is a common enough folder name that it might belong to something else.
            WriteFormerSettings();
            string before = File.ReadAllText(FormerPath);

            AppSettings.AdoptFormerSettings(FormerPath, _path);

            Assert.Equal(before, File.ReadAllText(FormerPath));
        }

        [Fact]
        public void Existing_settings_are_never_replaced_by_the_former_ones()
        {
            WriteFormerSettings();
            var current = AppSettings.Load(_path);
            current.OutputFolder = @"E:\mine";
            current.Save(_path);

            Assert.False(AppSettings.AdoptFormerSettings(FormerPath, _path));

            Assert.Equal(@"E:\mine", AppSettings.Load(_path).OutputFolder);
        }

        [Fact]
        public void With_no_former_settings_there_is_nothing_to_bring_across()
        {
            Assert.False(AppSettings.AdoptFormerSettings(FormerPath, _path));
            Assert.False(File.Exists(_path));
        }

        [Fact]
        public void The_window_size_survives_a_save_and_load()
        {
            var s = AppSettings.Load(_path);
            s.WindowWidth = 900;
            s.WindowHeight = 700;
            s.Save(_path);

            var loaded = AppSettings.Load(_path);
            Assert.Equal(900, loaded.WindowWidth);
            Assert.Equal(700, loaded.WindowHeight);
        }

        [Fact]
        public void A_corrupt_file_falls_back_to_defaults()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "{ this is not json");
            Assert.Equal(LevelId.Balanced, AppSettings.Load(_path).SelectedLevel);
        }

        [Fact]
        public void A_partial_file_fills_in_the_rest_from_defaults()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "{ \"OutputFolder\": \"D:\\\\x\" }");
            var s = AppSettings.Load(_path);
            Assert.Equal(LevelId.Balanced, s.SelectedLevel);
            Assert.Equal(@"D:\x", s.OutputFolder);
            Assert.True(s.Get(LevelId.Maximum).SameAs(Presets.Default(LevelId.Maximum)));
        }

        [Fact]
        public void Only_changed_levels_are_saved_so_untouched_ones_can_improve_in_updates()
        {
            var s = AppSettings.Load(_path);
            s.Get(LevelId.Balanced).MaxFps = 24;
            s.Save(_path);

            string json = File.ReadAllText(_path);
            Assert.Contains("\"MaxFps\":24", json);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"Quality\"").Cast<System.Text.RegularExpressions.Match>());
        }

        [Fact]
        public void Changed_levels_still_load_alongside_defaults_for_the_rest()
        {
            var s = AppSettings.Load(_path);
            s.Get(LevelId.Balanced).MaxFps = 24;
            s.Save(_path);

            var loaded = AppSettings.Load(_path);
            Assert.Equal(24, loaded.Get(LevelId.Balanced).MaxFps);
            Assert.False(loaded.IsDefault(LevelId.Balanced));
            Assert.True(loaded.IsDefault(LevelId.Light));
            Assert.True(loaded.IsDefault(LevelId.Maximum));
        }

        [Fact]
        public void Saving_does_not_disturb_the_settings_still_in_use()
        {
            var s = AppSettings.Load(_path);
            var light = s.Get(LevelId.Light);
            s.Save(_path);
            Assert.Same(light, s.Get(LevelId.Light));
        }

        [Fact]
        public void A_file_from_the_first_version_with_every_level_written_out_still_loads()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "{\"Balanced\":{\"AudioKbps\":128,\"Codec\":1,\"Encoder\":0,\"ExtraArgs\":\"\",\"MaxFps\":0,\"MaxShortSide\":1080,\"Quality\":31,\"TargetSizeMb\":0},"
                + "\"Light\":{\"AudioKbps\":192,\"Codec\":1,\"Encoder\":0,\"ExtraArgs\":\"\",\"MaxFps\":0,\"MaxShortSide\":0,\"Quality\":28,\"TargetSizeMb\":0},"
                + "\"Maximum\":{\"AudioKbps\":96,\"Codec\":1,\"Encoder\":0,\"ExtraArgs\":\"\",\"MaxFps\":30,\"MaxShortSide\":720,\"Quality\":34,\"TargetSizeMb\":0},"
                + "\"OutputFolder\":null,\"SelectedLevel\":0}");

            var loaded = AppSettings.Load(_path);

            Assert.Equal(LevelId.Light, loaded.SelectedLevel);
            Assert.True(loaded.IsDefault(LevelId.Light));
            Assert.True(loaded.IsDefault(LevelId.Balanced));
            Assert.True(loaded.IsDefault(LevelId.Maximum));
        }

        // ---- custom presets ----

        [Fact]
        public void A_new_preset_starts_as_a_copy_and_stays_separate_from_what_it_copied()
        {
            var s = AppSettings.Load(_path);
            s.Get(LevelId.Maximum).MaxFps = 24;

            var preset = s.AddCustom("Discord", s.Get(LevelId.Maximum));
            preset.Settings.TargetSizeMb = 10;

            Assert.Equal("Discord", preset.Name);
            Assert.Equal(24, preset.Settings.MaxFps);
            Assert.Equal(0, s.Get(LevelId.Maximum).TargetSizeMb);
        }

        [Fact]
        public void Custom_presets_survive_a_save_and_load()
        {
            var s = AppSettings.Load(_path);
            var preset = s.AddCustom("Discord", Presets.Default(LevelId.Balanced));
            preset.Settings.TargetSizeMb = 10;
            s.SelectCustom(preset.Id);
            s.Save(_path);

            var loaded = AppSettings.Load(_path);

            var back = loaded.CustomPresets.Single();
            Assert.Equal("Discord", back.Name);
            Assert.Equal(preset.Id, back.Id);
            Assert.Equal(10, back.Settings.TargetSizeMb);
            Assert.Equal(preset.Id, loaded.SelectedCustomId);
            Assert.Same(back.Settings, loaded.Selected);
        }

        [Fact]
        public void Every_preset_gets_its_own_id_even_with_the_same_name()
        {
            var s = AppSettings.Load(_path);
            var a = s.AddCustom("Discord", Presets.Default(LevelId.Light));
            var b = s.AddCustom("Discord", Presets.Default(LevelId.Light));
            Assert.NotEqual(a.Id, b.Id);
        }

        [Fact]
        public void Suggested_names_never_repeat_an_existing_one()
        {
            var s = AppSettings.Load(_path);
            Assert.Equal("My preset", s.SuggestPresetName());
            s.AddCustom("My preset", Presets.Default(LevelId.Light));
            Assert.Equal("My preset 2", s.SuggestPresetName());
            s.AddCustom("My Preset 2", Presets.Default(LevelId.Light));
            Assert.Equal("My preset 3", s.SuggestPresetName());
        }

        [Fact]
        public void Selecting_a_custom_preset_makes_it_the_active_one()
        {
            var s = AppSettings.Load(_path);
            var preset = s.AddCustom("X", Presets.Default(LevelId.Light));

            s.SelectCustom(preset.Id);
            Assert.Same(preset.Settings, s.Selected);

            s.SelectLevel(LevelId.Maximum);
            Assert.Null(s.SelectedCustomId);
            Assert.Same(s.Get(LevelId.Maximum), s.Selected);
        }

        [Fact]
        public void Removing_the_selected_preset_falls_back_to_a_built_in_level()
        {
            var s = AppSettings.Load(_path);
            s.SelectLevel(LevelId.Light);
            var preset = s.AddCustom("X", Presets.Default(LevelId.Light));
            s.SelectCustom(preset.Id);

            s.RemoveCustom(preset.Id);

            Assert.Empty(s.CustomPresets);
            Assert.Null(s.SelectedCustomId);
            Assert.Same(s.Get(LevelId.Light), s.Selected);
        }

        [Fact]
        public void A_selected_preset_that_no_longer_exists_falls_back_when_loading()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "{ \"SelectedCustomId\": \"gone\", \"SelectedLevel\": 2 }");

            var s = AppSettings.Load(_path);

            Assert.Null(s.SelectedCustomId);
            Assert.Same(s.Get(LevelId.Maximum), s.Selected);
        }

        [Fact]
        public void Settings_from_before_custom_presets_existed_simply_have_none()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "{ \"SelectedLevel\": 0 }");
            Assert.Empty(AppSettings.Load(_path).CustomPresets);
        }

        [Fact]
        public void Finding_a_preset_that_does_not_exist_gives_nothing()
        {
            Assert.Null(AppSettings.Load(_path).FindCustom("nope"));
        }

        [Fact]
        public void A_level_can_be_reset_to_its_defaults()
        {
            var s = AppSettings.Load(_path);
            s.Get(LevelId.Light).Quality = 1;
            Assert.False(s.IsDefault(LevelId.Light));
            s.Reset(LevelId.Light);
            Assert.True(s.IsDefault(LevelId.Light));
        }
    }
}
