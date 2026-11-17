using System.Windows;
using System.Windows.Controls;
using Flyweight.UI;

namespace Flyweight.Tests
{
    /// <summary>How the main window is arranged at one moment, noted down to be checked afterwards.</summary>
    internal sealed class Look
    {
        public object InSlot;
        public Visibility Slot, DropZones, Controls;
        public string Title;
        public double Width, Height, MinWidth, MinHeight;
        public SizeToContent Fit;

        public bool IsTrimming => Slot == Visibility.Visible;

        public static Look At(MainWindow window)
        {
            var slot = Ui.Named<Decorator>(window, "TrimSlot");
            return new Look
            {
                InSlot = slot.Child,
                Slot = slot.Visibility,
                DropZones = Ui.Named<FrameworkElement>(window, "DropZones").Visibility,
                Controls = Ui.Named<FrameworkElement>(window, "MainControls").Visibility,
                Title = window.Title,
                Width = window.Width,
                Height = window.Height,
                MinWidth = window.MinWidth,
                MinHeight = window.MinHeight,
                Fit = window.SizeToContent,
            };
        }
    }
}
