using System.Drawing;
using System.Windows.Forms;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class HostUiLayoutTests
{
    [Fact]
    public void GlobalPermissionsFormKeepsCloseButtonVisibleAndOutsidePermissionList()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var store = new TempPairingStore();
            using var appIcon = (Icon)SystemIcons.Application.Clone();
            using var form = new PermissionsForm(new PairingManager(store.Store), appIcon);
            using var chrome = ThemedWindowChrome.Install(form, form.Icon!, canMaximize: false, canMinimize: false);

            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(32, 32);
            form.ShowGlobal(NoOwner.Instance);
            Application.DoEvents();
            form.PerformLayout();

            AssertCloseButtonLayout(form);
        });
    }

    [Fact]
    public void DevicePermissionsFormKeepsCloseButtonVisibleAndOutsidePermissionList()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var store = new TempPairingStore();
            var manager = new PairingManager(store.Store);
            var token = manager.CreatePairingToken(DateTimeOffset.UtcNow);
            manager.Accept("client-a", "Phone", token, null);

            using var appIcon = (Icon)SystemIcons.Application.Clone();
            using var form = new PermissionsForm(manager, appIcon);
            using var chrome = ThemedWindowChrome.Install(form, form.Icon!, canMaximize: false, canMinimize: false);

            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(32, 32);
            form.ShowDevice(NoOwner.Instance, "client-a", "Phone");
            Application.DoEvents();
            form.PerformLayout();

            AssertCloseButtonLayout(form);
        });
    }

    private static void AssertCloseButtonLayout(Form form)
    {
        var closeButton = FindDescendants(form).OfType<Button>().Single(button => button.Text == "Close");
        var permissionViewport = FindDescendants(form)
            .OfType<Panel>()
            .Single(panel => panel.AutoScroll && panel.Controls.OfType<TableLayoutPanel>().Any());

        var formBounds = form.RectangleToScreen(form.ClientRectangle);
        var closeBounds = closeButton.RectangleToScreen(closeButton.ClientRectangle);
        var viewportBounds = permissionViewport.RectangleToScreen(permissionViewport.ClientRectangle);

        Assert.True(formBounds.Contains(closeBounds), $"Close button {closeBounds} must stay inside form client area {formBounds}.");
        Assert.True(viewportBounds.Bottom <= closeBounds.Top, $"Permission list {viewportBounds} must not overlap Close button {closeBounds}.");
        Assert.True(closeButton.Height > 0);
        Assert.True(closeButton.Width > 0);
    }

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in FindDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            throw exception;
        }
    }

    private static bool ShouldSkipNativeUiLayoutTests()
    {
        return string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NoOwner : IWin32Window
    {
        public static readonly NoOwner Instance = new();

        public IntPtr Handle => IntPtr.Zero;
    }
}
