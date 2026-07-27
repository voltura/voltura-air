using System.Windows;
using WpfSize = System.Windows.Size;

namespace VolturaAir.Host;

internal static class WindowWorkAreaPlacement
{
    public static void ConstrainAndCenterOnFirstLoad(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Loaded += OnLoaded;

        void OnLoaded(object sender, RoutedEventArgs eventArgs)
        {
            window.Loaded -= OnLoaded;
            Apply(window, SystemParameters.WorkArea);
        }
    }

    internal static Rect CalculateBounds(Rect workArea, WpfSize requestedSize)
    {
        var width = Math.Min(requestedSize.Width, workArea.Width);
        var height = Math.Min(requestedSize.Height, workArea.Height);
        var left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        var top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
        return new Rect(left, top, width, height);
    }

    private static void Apply(Window window, Rect workArea)
    {
        var bounds = CalculateBounds(workArea, new WpfSize(window.Width, window.Height));
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
    }
}
