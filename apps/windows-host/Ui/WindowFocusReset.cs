using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace VolturaAir.Host.Ui;

internal static class WindowFocusReset
{
    private static readonly WindowsWindowActivator WindowActivator = new();

    public static void AfterShow(Window window)
    {
        _ = window.Dispatcher.BeginInvoke(() =>
        {
            WindowActivator.TryBringWindowForwardPreservingState(new WindowInteropHelper(window).Handle);
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(window, null);
        }, DispatcherPriority.ContextIdle);
    }
}
