using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace VolturaAir.Host.Features.CustomScreens;

public partial class CustomScreenPreviewWindow : Window
{
    private readonly CustomScreenPreviewWindowRequest _request;
    private readonly int _selectedPortraitWidth;
    private readonly int _selectedPortraitHeight;
    private bool _synchronizing;
    private bool _closed;

    internal CustomScreenPreviewWindow(CustomScreenPreviewWindowRequest request)
    {
        _request = request;
        (_selectedPortraitWidth, _selectedPortraitHeight) =
            ToPortraitDimensions(request.Viewport);

        InitializeComponent();
        WpfTheme.Apply(this);
        WpfTheme.TrackAccessibilityChanges(this, static () => { });
        PopulateDevices();
        OrientationCombo.SelectedIndex =
            request.Viewport.Orientation == "landscape" ? 1 : 0;
        DeviceCombo.SelectionChanged += OnPreviewSettingChanged;
        OrientationCombo.SelectionChanged += OnPreviewSettingChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
        ApplyViewport(request.Width, request.Height);
    }

    internal CustomScreenViewport Viewport => ResolveViewport();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Voltura Air",
                "WebView2 Preview");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder);
            await PreviewBrowser.EnsureCoreWebView2Async(environment);
            if (_closed)
            {
                return;
            }
            PreviewBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            PreviewBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            PreviewBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            PreviewBrowser.CoreWebView2.Settings.IsZoomControlEnabled = false;
            PreviewBrowser.CoreWebView2.NavigationStarting += OnNavigationStarting;
            PreviewBrowser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            PreviewBrowser.Source = _request.Uri;
            ApplyViewport();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!_closed)
            {
                ShowInitializationError();
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        DeviceCombo.SelectionChanged -= OnPreviewSettingChanged;
        OrientationCombo.SelectionChanged -= OnPreviewSettingChanged;
        PreviewBrowser.CoreWebView2?.NavigationStarting -= OnNavigationStarting;
        PreviewBrowser.CoreWebView2?.NavigationCompleted -= OnNavigationCompleted;
        PreviewBrowser.Dispose();
    }

    private void OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var requestedUri) ||
            requestedUri != _request.Uri)
        {
            e.Cancel = true;
        }
    }

    private async void OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            await ApplyControlDepthSafelyAsync();
        }
    }

    private async void OnPreviewSettingChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_synchronizing)
        {
            ApplyViewport();
            await ApplyControlDepthSafelyAsync();
        }
    }

    private void OnRotateClicked(object sender, RoutedEventArgs e)
    {
        OrientationCombo.SelectedIndex =
            OrientationCombo.SelectedIndex == 1 ? 0 : 1;
    }

    private void PopulateDevices()
    {
        _synchronizing = true;
        var presets = new List<PreviewDeviceOption>
        {
            new(
                "Generic phone (360 × 640)",
                360,
                640,
                _request.DefaultControlDepth,
                null),
            new(
                "Generic tablet (800 × 1180)",
                800,
                1180,
                _request.DefaultControlDepth,
                null)
        };
        foreach (var device in _request.Devices)
        {
            var dimensions = ToPortraitDimensions(device.Viewport);
            presets.Add(new(
                device.Name,
                dimensions.Width,
                dimensions.Height,
                device.ControlDepth,
                device.ClientId));
        }

        var selectedIndex = _request.SelectedDeviceId is null
            ? -1
            : presets.FindIndex(option =>
                option.ClientId == _request.SelectedDeviceId);
        if (selectedIndex < 0)
        {
            selectedIndex = presets.FindIndex(IsSelectedViewport);
        }
        if (selectedIndex < 0)
        {
            selectedIndex = presets.Count;
            presets.Add(new(
                $"Mobile device ({_selectedPortraitWidth} × {_selectedPortraitHeight})",
                _selectedPortraitWidth,
                _selectedPortraitHeight,
                _request.SelectedControlDepth,
                _request.SelectedDeviceId));
        }
        presets.AddRange(
        [
            new("Compact Android (360 × 780)", 360, 780, _request.DefaultControlDepth, null),
            new("iPhone SE Small (375 × 667)", 375, 667, _request.DefaultControlDepth, null),
            new("Common iPhone (390 × 844)", 390, 844, _request.DefaultControlDepth, null),
            new("iPhone Pro (393 × 852)", 393, 852, _request.DefaultControlDepth, null),
            new("Large Android (412 × 915)", 412, 915, _request.DefaultControlDepth, null),
            new("iPhone Pro Max (430 × 932)", 430, 932, _request.DefaultControlDepth, null),
            new("Small Tablet (768 × 1024)", 768, 1024, _request.DefaultControlDepth, null),
            new("iPad Air (820 × 1180)", 820, 1180, _request.DefaultControlDepth, null)
        ]);
        foreach (var preset in presets)
        {
            DeviceCombo.Items.Add(preset);
        }
        DeviceCombo.SelectedIndex = selectedIndex;
        _synchronizing = false;
    }

    private bool IsSelectedViewport(PreviewDeviceOption option) =>
        option.PortraitWidth == _selectedPortraitWidth &&
        option.PortraitHeight == _selectedPortraitHeight;

    private CustomScreenViewport ResolveViewport()
    {
        var selected = DeviceCombo.SelectedItem as PreviewDeviceOption ??
            new PreviewDeviceOption(
                "Selected",
                _selectedPortraitWidth,
                _selectedPortraitHeight,
                _request.SelectedControlDepth,
                _request.SelectedDeviceId);
        return OrientationCombo.SelectedIndex == 1
            ? new(selected.PortraitHeight, selected.PortraitWidth, "landscape")
            : new(selected.PortraitWidth, selected.PortraitHeight, "portrait");
    }

    private void ApplyViewport()
    {
        var viewport = ResolveViewport();
        var fitted = CustomScreenBrowserPreviewLauncher.FitToWorkArea(
            viewport.Width,
            viewport.Height,
            SystemParameters.WorkArea.Width,
            SystemParameters.WorkArea.Height);
        ApplyViewport(fitted.Width, fitted.Height);
    }

    private void ApplyViewport(int width, int height)
    {
        var viewport = ResolveViewport();
        PreviewViewportHost.Width = width;
        PreviewViewportHost.Height = height;
        PreviewBrowser.ZoomFactor = Math.Min(
            (double)width / viewport.Width,
            (double)height / viewport.Height);
        Dispatcher.BeginInvoke(() =>
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + Math.Max(0d, (workArea.Width - ActualWidth) / 2d);
            Top = workArea.Top + Math.Max(0d, (workArea.Height - ActualHeight) / 2d);
        }, DispatcherPriority.Loaded);
    }

    private async Task ApplyControlDepthSafelyAsync()
    {
        if (_closed || PreviewBrowser.CoreWebView2 is null)
        {
            return;
        }

        var enabled =
            (DeviceCombo.SelectedItem as PreviewDeviceOption)?.ControlDepth ==
            true;
        try
        {
            await PreviewBrowser.CoreWebView2.ExecuteScriptAsync(
                "document.querySelector('.custom-screen-browser-preview')" +
                $"?.classList.toggle('control-depth', {enabled.ToString().ToLowerInvariant()});");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!_closed)
            {
                ShowInitializationError();
            }
        }
    }

    private void ShowInitializationError()
    {
        PreviewBrowser.Visibility = Visibility.Collapsed;
        PreviewErrorText.Text =
            "The preview could not start. Install or repair the Microsoft Edge WebView2 Runtime, then try again.";
        PreviewErrorText.Visibility = Visibility.Visible;
    }

    private static (int Width, int Height) ToPortraitDimensions(
        CustomScreenViewport viewport) =>
        viewport.Orientation == "landscape"
            ? (viewport.Height, viewport.Width)
            : (viewport.Width, viewport.Height);

    private sealed record PreviewDeviceOption(
        string Name,
        int PortraitWidth,
        int PortraitHeight,
        bool ControlDepth,
        string? ClientId)
    {
        public override string ToString() => Name;
    }
}
