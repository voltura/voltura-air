using System.Windows;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenBrowserPreviewLauncher(
    int port,
    ICustomScreenPreviewWindowLauncher? windowLauncher = null,
    PairingManager? pairingManager = null)
{
    private static readonly CustomScreenViewport DefaultViewport =
        new(360, 640, "portrait");
    private readonly ICustomScreenPreviewWindowLauncher _windowLauncher =
        windowLauncher ?? new WindowsCustomScreenPreviewWindowLauncher();

    public UrlOpenExecutionResult Open(
        string screenId,
        CustomScreenViewport? viewport = null,
        bool? controlDepth = null,
        string? selectedDeviceId = null)
    {
        var selectedViewport = viewport ?? DefaultViewport;
        var defaultControlDepth = AppAppearanceSettings.DeviceControlDepth();
        var selectedControlDepth =
            controlDepth ?? defaultControlDepth;
        var devices = pairingManager?.GetDevices()
            .Select(device => new CustomScreenPreviewDevice(
                device.ClientId,
                device.DeviceName,
                device.CustomScreenViewport ??
                    new CustomScreenViewport(390, 844, "portrait"),
                device.ControlDepth))
            .ToArray() ?? [];
        var size = FitToWorkArea(
            selectedViewport.Width,
            selectedViewport.Height,
            SystemParameters.WorkArea.Width,
            SystemParameters.WorkArea.Height);
        var previewUri = new UriBuilder(Uri.UriSchemeHttp, "127.0.0.1", port)
        {
            Query =
                $"customScreenPreview={Uri.EscapeDataString(screenId)}" +
                $"&controlDepth={selectedControlDepth.ToString().ToLowerInvariant()}"
        }.Uri;

        try
        {
            _windowLauncher.Open(new(
                previewUri,
                selectedViewport,
                size.Width,
                size.Height,
                selectedControlDepth,
                defaultControlDepth,
                selectedDeviceId,
                devices));
            return new(true, "accepted", "Preview window opened.", previewUri.AbsoluteUri);
        }
        catch (InvalidOperationException)
        {
            return new(
                false,
                "launch-failed",
                "Windows could not open the custom-screen preview window.",
                previewUri.AbsoluteUri);
        }
    }

    public void CloseAll() => _windowLauncher.CloseAll();

    internal static CustomScreenPreviewWindowSize FitToWorkArea(
        int width,
        int height,
        double workAreaWidth,
        double workAreaHeight)
    {
        var scale = Math.Min(
            1d,
            Math.Min(
                Math.Max(1d, workAreaWidth - 48d) / width,
                Math.Max(1d, workAreaHeight - 120d) / height));
        return new(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }
}

internal interface ICustomScreenPreviewWindowLauncher
{
    void Open(CustomScreenPreviewWindowRequest request);

    void CloseAll();
}

internal sealed record CustomScreenPreviewWindowRequest(
    Uri Uri,
    CustomScreenViewport Viewport,
    int Width,
    int Height,
    bool SelectedControlDepth,
    bool DefaultControlDepth,
    string? SelectedDeviceId,
    IReadOnlyList<CustomScreenPreviewDevice> Devices);

internal sealed record CustomScreenPreviewDevice(
    string ClientId,
    string Name,
    CustomScreenViewport Viewport,
    bool ControlDepth);

internal sealed record CustomScreenPreviewWindowSize(
    int Width,
    int Height);

internal sealed class WindowsCustomScreenPreviewWindowLauncher :
    ICustomScreenPreviewWindowLauncher
{
    private readonly HashSet<CustomScreenPreviewWindow> _windows = [];

    public void Open(CustomScreenPreviewWindowRequest request)
    {
        var window = new CustomScreenPreviewWindow(request);
        window.Closed += OnWindowClosed;
        _windows.Add(window);
        window.Show();
        window.Activate();
    }

    public void CloseAll()
    {
        foreach (var window in _windows.ToArray())
        {
            window.Close();
        }

        _windows.Clear();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is CustomScreenPreviewWindow window)
        {
            window.Closed -= OnWindowClosed;
            _windows.Remove(window);
        }
    }
}
