using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace Flyweight.Tests
{
    /// <summary>
    /// Helpers for driving real windows without showing them. WPF needs an STA thread, which xUnit
    /// doesn't provide, so each test hops onto its own.
    /// </summary>
    internal static class Ui
    {
        public static void Run(Action test)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // In the real app this is always in place; awaits rely on it to return to the UI thread.
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                    test();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        /// <summary>Lets the window get on with its work until something becomes true.</summary>
        public static void Pump(Func<bool> until, int timeoutMs = 60000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!until())
            {
                Assert.True(DateTime.UtcNow < deadline, "Timed out waiting");
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Background, (s, e) => frame.Continue = false, Dispatcher.CurrentDispatcher);
                Dispatcher.PushFrame(frame);
                timer.Stop();
            }
        }

        public static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        /// <summary>
        /// Turns the mouse wheel one notch with the pointer over something, the way Windows hands it to
        /// a window: first on its way down to whatever is under the pointer, then, if nothing took it,
        /// on its way back up.
        /// </summary>
        public static void Wheel(UIElement under, bool down)
        {
            int notch = down ? -Mouse.MouseWheelDeltaForOneLine : Mouse.MouseWheelDeltaForOneLine;
            var onTheWayDown = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, notch) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            under.RaiseEvent(onTheWayDown);
            if (onTheWayDown.Handled) return;

            under.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, notch) { RoutedEvent = Mouse.MouseWheelEvent });
        }

        /// <summary>Saves a picture of what a window would show, drawn in memory at twice the size.</summary>
        public static void Snapshot(Window window, string path, int width)
        {
            var root = LayOut(window, width);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            const double scale = 2;
            var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
        }

        /// <summary>Gives everything in a window that isn't on screen the size and place it would have at this width.</summary>
        public static FrameworkElement LayOut(Window window, int width)
        {
            var root = (FrameworkElement)window.Content;
            root.Width = width;
            root.Measure(new Size(width, double.PositiveInfinity));
            root.Arrange(new Rect(new Size(width, root.DesiredSize.Height)));
            root.UpdateLayout();
            return root;
        }

        public static T Named<T>(FrameworkElement root, string name) where T : class
        {
            var found = root.FindName(name) as T;
            Assert.True(found != null, "No element named " + name);
            return found;
        }
    }
}
