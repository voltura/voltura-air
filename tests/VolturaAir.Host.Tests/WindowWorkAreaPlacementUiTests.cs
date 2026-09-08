using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void HiddenWindowNativeShrinkIsRecoveredOnTrayReopen()
        => VerifyHiddenWindowRecovery(interactiveResize: false, moveOnly: false);

    [Fact]
    public void HiddenWindowRecoveryPreservesInteractiveResize()
        => VerifyHiddenWindowRecovery(interactiveResize: true, moveOnly: false);

    [Fact]
    public void MovingWindowDoesNotAdoptNativeShrinkAsPreferredSize()
        => VerifyHiddenWindowRecovery(interactiveResize: false, moveOnly: true);

    private static void VerifyHiddenWindowRecovery(bool interactiveResize, bool moveOnly)
    {
        RunOnStaThread(() =>
        {
            // The test assembly owns the isolated settings scope.
            var window = new Window { Width = 700, Height = 500, ShowActivated = false };
            WindowWorkAreaPlacement.ConstrainAndCenterOnFirstLoad(window);
            WindowWorkAreaPlacement.KeepVisibleAfterDisplayChanges(window);
            try
            {
                window.Show();
                FlushPlacementEvents();
                var preferred = new System.Windows.Size(window.Width, window.Height);
                var handle = new WindowInteropHelper(window).Handle;
                var scale = PlacementGetDpiForWindow(handle) / 96d;
                if (interactiveResize || moveOnly)
                {
                    _ = PlacementSendMessage(handle, 0x0231, 0, 0); // WM_ENTERSIZEMOVE
                    if (interactiveResize)
                    {
                        var sizingRect = new PlacementTestRect { Right = 600, Bottom = 400 };
                        _ = PlacementSendSizingMessage(handle, 0x0214, 8, ref sizingRect);
                    }
                    Assert.True(PlacementSetWindowPos(handle, 0, 0, 0,
                        (int)(preferred.Width * scale * 0.8),
                        (int)(preferred.Height * scale * 0.8), 0x0016));
                    _ = PlacementSendMessage(handle, 0x007E, 0, 0); // Display change during the drag
                    FlushPlacementEvents();
                    var duringDrag = new System.Windows.Size(window.Width, window.Height);
                    if (interactiveResize) preferred = duringDrag;
                    WindowWorkAreaPlacement.EnsureVisibleOnCurrentMonitor(window);
                    Assert.Equal(duringDrag.Width, window.Width, 1);
                    _ = PlacementSendMessage(handle, 0x0232, 0, 0); // WM_EXITSIZEMOVE
                    FlushPlacementEvents();
                    Assert.Equal(preferred.Width, window.Width, 1);
                }
                window.Hide();
                Assert.True(PlacementSetWindowPos(handle, 0, 0, 0,
                    (int)(preferred.Width * scale * 0.65),
                    (int)(preferred.Height * scale * 0.65), 0x0016));
                FlushPlacementEvents();
                window.Show();
                window.WindowState = WindowState.Normal;
                WindowWorkAreaPlacement.EnsureVisibleOnCurrentMonitor(window);
                FlushPlacementEvents();
                Assert.Equal(preferred.Width, window.Width, 1);
                Assert.Equal(preferred.Height, window.Height, 1);
                // Closing with a recovery queued must detach the hook and abort the callback.
                _ = PlacementSendMessage(handle, 0x007E, 0, 0); // WM_DISPLAYCHANGE
            }
            finally
            {
                window.Close();
                FlushPlacementEvents();
            }
        });
    }

    private static void FlushPlacementEvents() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
            () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

    [StructLayout(LayoutKind.Sequential)]
    private struct PlacementTestRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint PlacementSendMessage(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint PlacementSendSizingMessage(nint window, uint message, nint wParam, ref PlacementTestRect rect);

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static partial uint PlacementGetDpiForWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlacementSetWindowPos(nint window, nint insertAfter,
        int x, int y, int width, int height, uint flags);
}
