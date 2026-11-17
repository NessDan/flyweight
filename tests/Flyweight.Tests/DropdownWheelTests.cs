using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Flyweight.Core;
using Flyweight.UI;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// A dropdown that has the keyboard, as one does after a choice is made in it, takes the mouse
    /// wheel to mean "next choice". In this window every choice is saved as it's made, so scrolling
    /// with the pointer left over that dropdown would quietly change a level. Whether a dropdown has
    /// the keyboard is something only a real window can say, so this shows one.
    /// </summary>
    [Collection(WindowTests.Name)]
    public class DropdownWheelTests : IDisposable
    {
        private readonly TestVideos _videos = new TestVideos();

        public void Dispose() => _videos.Dispose();

        [OnHiddenDesktopFact]
        public void The_wheel_over_a_dropdown_that_has_the_keyboard_scrolls_the_page_and_leaves_the_choice_alone()
        {
            Ui.Run(() =>
            {
                string settingsPath = _videos.PathOf("settings.json");
                var window = new MainWindow(AppSettings.Load(settingsPath), settingsPath, TestVideos.Tools, compact: false, dark: false);
                // Too short for Advanced options to fit, so the page scrolls.
                window.WorkArea = () => new Rect(0, 0, 1200, 500);
                window.Show();
                try
                {
                    Ui.Pump(() => window.ActualWidth > 0);
                    window.Activate();
                    Ui.Named<ToggleButton>(window, "AdvancedToggle").IsChecked = true;
                    var page = Ui.Named<ScrollViewer>(window, "Root");
                    var box = Ui.Named<ComboBox>(window, "ResolutionBox");
                    Ui.Pump(() => page.ScrollableHeight > 0, 5000);
                    box.Focus();
                    Assert.True(box.IsKeyboardFocusWithin, "The dropdown has to have the keyboard for this to mean anything");
                    int chosen = box.SelectedIndex;

                    Ui.Wheel(box, down: true);
                    int turns = 0;
                    Ui.Pump(() => ++turns > 5);

                    Assert.Equal(chosen, box.SelectedIndex);
                    Assert.Equal(1080, AppSettings.Load(settingsPath).Get(LevelId.Balanced).MaxShortSide);
                    Assert.True(page.VerticalOffset > 0, "The page should have scrolled instead");
                }
                finally
                {
                    window.Close();
                }
            });
        }
    }
}
