using System.Windows.Controls;
using Border = System.Windows.Controls.Border;
using ComboBox = System.Windows.Controls.ComboBox;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenPreviewDeviceController(
    ComboBox deviceCombo,
    ComboBox orientationCombo,
    Border deviceFrame,
    PairingManager pairingManager,
    Action renderPreview)
{
    public string Orientation =>
        (orientationCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "portrait";

    public CustomScreenViewport Viewport
    {
        get
        {
            var item = deviceCombo.SelectedItem as PreviewDeviceItem ??
                new PreviewDeviceItem(
                    "Generic phone",
                    new CustomScreenViewport(360, 640, "portrait"),
                    null);
            return Orientation == "landscape"
                ? new(item.Viewport.Height, item.Viewport.Width, "landscape")
                : new(item.Viewport.Width, item.Viewport.Height, "portrait");
        }
    }

    public bool ControlDepth =>
        deviceCombo.SelectedItem is PreviewDeviceItem { ClientId: { } clientId }
            ? pairingManager.GetDeviceControlDepth(clientId)
            : AppAppearanceSettings.DeviceControlDepth();

    public string? ClientId =>
        (deviceCombo.SelectedItem as PreviewDeviceItem)?.ClientId;

    public void Load()
    {
        deviceCombo.Items.Clear();
        deviceCombo.Items.Add(new PreviewDeviceItem(
            "Generic phone",
            new CustomScreenViewport(360, 640, "portrait"),
            null));
        deviceCombo.Items.Add(new PreviewDeviceItem(
            "Generic tablet",
            new CustomScreenViewport(800, 1180, "portrait"),
            null));
        foreach (var device in pairingManager.GetDevices())
        {
            deviceCombo.Items.Add(new PreviewDeviceItem(
                device.DeviceName,
                device.CustomScreenViewport ??
                    new CustomScreenViewport(390, 844, "portrait"),
                device.ClientId));
        }
        deviceCombo.SelectedIndex = 0;
    }

    public void ApplySize()
    {
        if (deviceCombo.SelectedItem is not PreviewDeviceItem)
        {
            return;
        }

        var viewport = Viewport;
        var width = viewport.Width;
        var height = viewport.Height;
        var scale = Math.Min(1d, 640d / Math.Max(width, height));
        deviceFrame.Width = Math.Max(300, width * scale);
        deviceFrame.Height = Math.Max(420, height * scale);
        renderPreview();
    }

    private sealed record PreviewDeviceItem(
        string Name,
        CustomScreenViewport Viewport,
        string? ClientId)
    {
        public override string ToString() => Name;
    }
}
