using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>Looks, checked by eye: set FLYWEIGHT_SNAPSHOT_DIR to write pictures of the trim screens.</summary>
    [Collection(WindowTests.Name)]
    public class TrimSnapshotTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        [Fact]
        public void Snapshots_when_asked()
        {
            string dir = Environment.GetEnvironmentVariable("FLYWEIGHT_SNAPSHOT_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);

            Ui.Run(() =>
            {
                string clip = _videos.MakeOneKeyframeClip("holiday.mp4");
                string tall = _videos.Make("phone.mp4", "-f", "lavfi", "-i", "testsrc2=s=720x1280:r=30:d=6",
                    "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p");
                string settingsPath = _videos.PathOf("settings.json");

                foreach (bool dark in new[] { false, true })
                {
                    string theme = dark ? "dark" : "light";
                    string At(string name) => Path.Combine(dir, theme + "-" + name + ".png");

                    // As it opens: the whole video kept, pictures in.
                    Shoot(dark, At("9-trim-opened"), new[] { clip });

                    // With a part picked out, looking at where it ends.
                    Shoot(dark, At("10-trim-picked"), new[] { clip }, arrange: async (window, trim) =>
                    {
                        var image = Ui.Named<Image>(trim, "PreviewImage");
                        var before = image.Source;
                        Type(trim, "StartBox", "2.4");
                        Type(trim, "EndBox", "7.25");
                        while (image.Source == before) await Task.Delay(30);
                    });

                    // Playing, and held part way: the line on the strip, and where it has got to.
                    Shoot(dark, At("16-trim-paused"), new[] { clip }, arrange: async (window, trim) =>
                    {
                        trim.Player.NewSound = () => new PretendSound();
                        Type(trim, "StartBox", "2.4");
                        Type(trim, "EndBox", "7.25");
                        Type(trim, "StartBox", "2.4");
                        // Pictures are played at the size of the box they're shown in, so it needs one first.
                        Ui.LayOut(window, 576);
                        trim.PlayFrom(4);
                        while (!(trim.ShownSeconds >= 4.6)) await Task.Delay(15);
                        trim.PlayOrPause();
                        await Task.Delay(100);
                    });

                    // Cropped as well as trimmed: the box over the picture, the size it keeps, and Reset crop.
                    Shoot(dark, At("17-trim-cropped"), new[] { clip }, arrange: async (window, trim) =>
                    {
                        var image = Ui.Named<Image>(trim, "PreviewImage");
                        var before = image.Source;
                        Type(trim, "StartBox", "2.4");
                        Type(trim, "EndBox", "7.25");
                        while (image.Source == before) await Task.Delay(30);
                        Ui.LayOut(window, 576);
                        var box = Ui.Named<CropBox>(trim, "Cropper");
                        var picture = box.Picture;
                        foreach (var (from, to) in new[] { (0.0, 0.28), (1.0, 0.74) })
                        {
                            var start = new Point(picture.Left + from * picture.Width, picture.Top + from * picture.Height);
                            var end = new Point(picture.Left + to * picture.Width, picture.Top + (from == 0 ? 0.1 : 0.88) * picture.Height);
                            box.Press(start);
                            box.MoveTo(end);
                            box.Release(end);
                        }
                    });

                    // A corner held still: the picture and the box four times nearer around it, at full detail.
                    Shoot(dark, At("18-trim-zoomed"), new[] { clip }, arrange: async (window, trim) =>
                    {
                        var image = Ui.Named<Image>(trim, "PreviewImage");
                        var before = image.Source;
                        Type(trim, "EndBox", "7.25");
                        while (image.Source == before) await Task.Delay(30);
                        Ui.LayOut(window, 576);
                        var box = Ui.Named<CropBox>(trim, "Cropper");
                        var picture = box.Picture;
                        box.Press(picture.TopLeft);
                        box.MoveTo(new Point(picture.Left + 0.3 * picture.Width, picture.Top + 0.2 * picture.Height));
                        box.Rest();
                        var blurred = image.Source;
                        while (image.Source == blurred) await Task.Delay(30);
                    });

                    // A phone video, which gets a taller preview.
                    Shoot(dark, At("12-trim-tall"), new[] { tall });

                    // One of several sent at once, which adds a way out of the rest.
                    Shoot(dark, At("13-trim-waiting"), new[] { clip, tall, tall });

                    // Changing a level while trimming: narrow, where Advanced options go underneath...
                    Shoot(dark, At("14-trim-advanced"), new[] { clip }, arrange: (window, trim) =>
                    {
                        Ui.Named<ToggleButton>(window, "AdvancedToggle").IsChecked = true;
                        return Task.CompletedTask;
                    });

                    // ...and wide, where they go beside.
                    Shoot(dark, At("15-trim-advanced-wide"), new[] { clip }, width: 1036, arrange: (window, trim) =>
                    {
                        Ui.Named<ToggleButton>(window, "AdvancedToggle").IsChecked = true;
                        window.ArrangeForWidth(1052);
                        return Task.CompletedTask;
                    });

                    // The queue afterwards: one trim under way, one finished.
                    var main = new MainWindow(AppSettings.Load(settingsPath), settingsPath, TestVideos.Tools, compact: false, dark: dark);
                    var running = new JobViewModel(@"C:\Videos\holiday.mp4", Presets.Default(LevelId.Balanced), new TrimRange(12, 54));
                    running.MarkStarted();
                    running.Apply(new CompressionProgress { Percent = 40, Remaining = TimeSpan.FromSeconds(9) });
                    var done = new JobViewModel(@"C:\Videos\match.mkv", Presets.Default(LevelId.Balanced), new TrimRange(63, 105));
                    done.Apply(new CompressionResult { Outcome = JobOutcome.Done, OutputPath = @"C:\Videos\match_trimmed.mp4", InputBytes = 400000000, OutputBytes = 9600000, KeptSeconds = 42 });
                    main.Jobs.Add(done);
                    main.Jobs.Add(running);
                    main.RefreshQueueArea();
                    Ui.Snapshot(main, At("11-trim-queue"), 480);
                }
            });
        }

        /// <summary>Puts the first video's trim controls up in a main window, pictures and all, and saves how it looks.</summary>
        private void Shoot(bool dark, string path, string[] clips, int width = 576, Func<MainWindow, TrimPanel, Task> arrange = null)
        {
            string settingsPath = _videos.PathOf("settings.json");
            bool shot = false;
            MainWindow window = null;
            window = new MainWindow(AppSettings.Load(settingsPath), settingsPath, TestVideos.Tools, compact: false, dark: dark);
            window.ShowTrim = async trim =>
            {
                var strip = Ui.Named<TrimStrip>(trim, "Strip");
                var image = Ui.Named<Image>(trim, "PreviewImage");
                trim.Start();
                while (strip.PictureCount < TrimStrip.Cells || image.Source == null) await Task.Delay(30);
                if (arrange != null) await arrange(window, trim);

                Ui.Snapshot(window, path, width);
                shot = true;
                Ui.Click(Ui.Named<Button>(trim, "SkipAllButton"));
            };

            window.TrimFirst(clips);
            Ui.Pump(() => shot && !Look.At(window).IsTrimming);
            window.Close();
        }

        private static void Type(TrimPanel trim, string box, string text)
        {
            var field = Ui.Named<TextBox>(trim, box);
            field.Text = text;
            field.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        }
    }
}
