using System.Drawing;
using System.Windows.Forms;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
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
            AssertPermissionViewportFitsFourRows(form);
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
            AssertPermissionViewportFitsFourRows(form);
        });
    }

    [Fact]
    public void SettingsFormKeepsCloseButtonVisibleAndSelectsRequestedPage()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var store = new TempPairingStore();
            using var appIcon = (Icon)SystemIcons.Application.Clone();
            using var inputInjector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector));
            using var pairingForm = new PairingForm(webHost.ServerUrl, manager);
            using var form = new SettingsForm(appIcon, manager, webHost, pairingForm);
            using var chrome = ThemedWindowChrome.Install(form, form.Icon!, canMaximize: false, canMinimize: false);

            try
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(32, 32);
                form.ShowStandalone(SettingsPage.Permissions);
                Application.DoEvents();
                form.PerformLayout();

                AssertSettingsCloseButtonLayout(form);
                AssertSettingsPageContentVisible(form, "Allow PC sleep");
                AssertSettingsPageContentUsesViewportWidth(form, "Allow PC sleep");
                Assert.DoesNotContain(FindDescendants(form).OfType<Panel>(), panel => panel.AutoScroll);
                var selectedButton = FindDescendants(form).OfType<Button>().Single(button => button.Text == "Permissions");
                Assert.True(selectedButton.Font.Bold);
            }
            finally
            {
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
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

    private static void AssertSettingsPageContentVisible(Form form, string text)
    {
        var pageViewport = FindDescendants(form)
            .OfType<Panel>()
            .Single(panel => panel.AccessibleName == "SettingsPageViewport");
        var content = FindDescendants(pageViewport).Single(control => control.Text == text);
        var viewportBounds = pageViewport.RectangleToScreen(pageViewport.ClientRectangle);
        var contentBounds = content.RectangleToScreen(content.ClientRectangle);

        Assert.True(content.Visible, $"Settings page content '{text}' must be visible.");
        Assert.True(
            viewportBounds.IntersectsWith(contentBounds),
            $"Settings page content '{text}' at {contentBounds} must be inside viewport {viewportBounds}.");
    }

    private static void AssertSettingsPageContentUsesViewportWidth(Form form, string text)
    {
        var pageViewport = FindDescendants(form)
            .OfType<Panel>()
            .Single(panel => panel.AccessibleName == "SettingsPageViewport");
        var content = FindDescendants(pageViewport).Single(control => control.Text == text);
        var row = content.Parent!;

        Assert.True(
            row.Width >= pageViewport.ClientSize.Width * 0.7,
            $"Settings content row width {row.Width} must use the available viewport width {pageViewport.ClientSize.Width}.");
    }

    private static void AssertPermissionViewportFitsFourRows(Form form)
    {
        var permissionViewport = FindDescendants(form)
            .OfType<Panel>()
            .Single(panel => panel.AutoScroll && panel.Controls.OfType<TableLayoutPanel>().Any());
        var permissionList = permissionViewport.Controls.OfType<TableLayoutPanel>().Single();
        var firstRow = permissionList.Controls.OfType<TableLayoutPanel>().First();
        var requiredHeight = (firstRow.Height + firstRow.Margin.Bottom) * 4;

        Assert.True(
            permissionViewport.ClientSize.Height >= requiredHeight,
            $"Permission viewport height {permissionViewport.ClientSize.Height} must fit four rows requiring {requiredHeight} before scrolling.");
    }

    private static void AssertSettingsCloseButtonLayout(Form form)
    {
        var closeButton = FindDescendants(form).OfType<Button>().Single(button => button.Text == "Close");
        var pageViewport = FindDescendants(form)
            .OfType<Panel>()
            .Single(panel => panel.AccessibleName == "SettingsPageViewport");

        var formBounds = form.RectangleToScreen(form.ClientRectangle);
        var closeBounds = closeButton.RectangleToScreen(closeButton.ClientRectangle);
        var viewportBounds = pageViewport.RectangleToScreen(pageViewport.ClientRectangle);

        Assert.True(formBounds.Contains(closeBounds), $"Close button {closeBounds} must stay inside form client area {formBounds}.");
        Assert.True(viewportBounds.Bottom <= closeBounds.Top, $"Settings page viewport {viewportBounds} must not overlap Close button {closeBounds}.");
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
}
