using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace VolturaAir.Host.Ui;

internal static class WindowFocusReset
{
    public static void AfterShow(Window window)
    {
        _ = window.Dispatcher.BeginInvoke(() =>
        {
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(window, null);
        }, DispatcherPriority.ContextIdle);
    }
}
