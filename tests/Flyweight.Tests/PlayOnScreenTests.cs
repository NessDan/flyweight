using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// The real thing in a real window: the real button, pictures drawn as the window redraws, and
    /// sound really handed to Windows. So it runs only on the hidden desktop, the clip's sound track
    /// is silence, and this process is turned right down as well (see <see cref="Hush"/>).
    /// </summary>
    [Collection(WindowTests.Name)]
    public class PlayOnScreenTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        [OnHiddenDesktopFact]
        public void In_a_real_window_a_part_plays_in_step_with_real_sound_and_stops_on_the_cut()
        {
            Assert.True(Hush.IsInForce, "Couldn't be sure of silence, so nothing was played");
            Ui.Run(() =>
            {
                string clip = _videos.MakeNumberedClip("clip.mp4", silent: true);
                var panel = new TrimPanel(clip, TestVideos.Probe(clip), TestVideos.Tools);
                // The real one, kept hold of so the test can ask how much of it has been heard.
                var sound = new SoundOut();
                panel.Player.NewSound = () => sound;
                var window = new Window
                {
                    Content = panel, Width = 592, SizeToContent = SizeToContent.Height, ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = 300, Top = 120,
                };
                window.Show();
                try
                {
                    Ui.Pump(() => panel.ActualWidth > 0);
                    foreach (var typed in new[] { ("StartBox", "1"), ("EndBox", "2.5") })
                    {
                        var field = Ui.Named<TextBox>(panel, typed.Item1);
                        field.Text = typed.Item2;
                        field.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                    }
                    var image = Ui.Named<Image>(panel, "PreviewImage");

                    var label = Ui.Named<TextBlock>(panel, "PreviewLabel");

                    // As each picture goes up, the label says so, and that's when to ask how much
                    // sound has been heard. On a hidden desktop a window is drawn about five times a
                    // second, and putting a picture up takes a tenth of a second by itself, so how
                    // closely the two keep step can't be judged finely here. That's done without a
                    // window, in PartPlayerTests. What this shows is that in a real window, with the
                    // real sound going out, they stay together within what such a screen allows.
                    var behind = new List<double>();
                    string lastSaid = null;
                    DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(label, (s, e) =>
                    {
                        if (!label.Text.StartsWith("Playing · ")) return;
                        lastSaid = label.Text;
                        behind.Add(sound.Seconds - (panel.ShownSeconds.Value - 1));
                    });

                    var watch = Stopwatch.StartNew();
                    Ui.Click(Ui.Named<Button>(panel, "PlayButton"));
                    Ui.Pump(() => image.Source is WriteableBitmap, 20000);
                    var moving = (BitmapSource)image.Source;
                    Ui.Pump(() => !panel.Player.IsPlaying, 20000);
                    double took = watch.Elapsed.TotalSeconds;

                    var toDots = PresentationSource.FromVisual(panel).CompositionTarget.TransformToDevice;
                    // The picture's own size within the box, in the screen's dots: what the crop box is drawn around.
                    var shown = Ui.Named<CropBox>(panel, "Cropper").Picture;
                    Assert.Equal((int)(shown.Width * toDots.M11 / 2) * 2, moving.PixelWidth);
                    Assert.Equal((int)(shown.Height * toDots.M22 / 2) * 2, moving.PixelHeight);

                    var sorted = behind.OrderBy(b => b).ToList();
                    string spread = $"{behind.Count} pictures; behind the sound by {sorted.First():0.000} s at least, {sorted[sorted.Count / 2]:0.000} s in the middle, {sorted.Last():0.000} s at most";
                    Assert.True(behind.Count >= 5, "Too few pictures were shown to tell: " + spread);
                    Assert.True(sorted.First() > -0.02, "A picture went up before its sound: " + spread);
                    // Not falling further and further behind, which is what a picture does when it's
                    // handed out in turn to a screen too slow to take them all.
                    Assert.True(sorted.Last() < 0.35, "Pictures fell behind the sound: " + spread);
                    Assert.StartsWith("Playing · 0:0", lastSaid);
                    // A second and a half of video, plus getting started, and then it stops by itself.
                    Assert.True(took > 1.4 && took < 10, $"It took {took:0.0} s");
                    Assert.Equal("End · 0:02.500", label.Text);
                    Assert.Equal(Visibility.Collapsed, Ui.Named<TextBlock>(panel, "PlayNote").Visibility);
                }
                finally
                {
                    panel.Close();
                    window.Close();
                }
            });
        }
    }
}
