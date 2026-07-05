using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;
using DrawingFontStyle = System.Drawing.FontStyle;
using WpfApplication = System.Windows.Application;

namespace VolturaAir.Host;

internal sealed class WpfTrayApplicationContext : IDisposable
{
    private const int MaxTrayTooltipLength = 63;
    private const int DisconnectNotificationDelayMs = 1800;
    private const string ProductSiteUrl = "https://voltura.se/air/";

    private readonly MainWindow _mainWindow;
    private readonly PairingManager _pairingManager;
    private readonly WebHostService _webHost;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ContextMenuStrip _trayMenu = new();
    private readonly IReadOnlyDictionary<TrayConnectionState, Icon> _trayIcons;
    private CancellationTokenSource? _pendingDisconnectNotification;
    private bool _hadActiveController;
    private bool _disposed;

    public WpfTrayApplicationContext(MainWindow mainWindow, WebHostService webHost, PairingManager pairingManager)
    {
        _mainWindow = mainWindow;
        _webHost = webHost;
        _pairingManager = pairingManager;
        _hadActiveController = pairingManager.HasActiveController;
        _pairingManager.ConnectionChanged += OnConnectionChanged;
        AppThemeSettings.Changed += OnAppThemeChanged;

        BuildMenu();
        _trayIcons = LoadTrayIcons();
        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _trayMenu,
            Icon = GetTrayIcon(GetTrayConnectionState()),
            Text = BuildTrayTooltip(),
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => _mainWindow.ShowPage(HostPage.Connect);
        ApplyMenuTheme();
    }

    private enum TrayConnectionState
    {
        NoDevicesRegistered,
        Disconnected,
        Connected
    }

    private void BuildMenu()
    {
        var showQrCodeItem = _trayMenu.Items.Add("Show Voltura Air", null, (_, _) => _mainWindow.ShowPage(HostPage.Connect));
        showQrCodeItem.Font = new Font(showQrCodeItem.Font, DrawingFontStyle.Bold);
        _trayMenu.Items.Add("Devices", null, (_, _) => _mainWindow.ShowPage(HostPage.Devices));
        _trayMenu.Items.Add("Preferences", null, (_, _) => _mainWindow.ShowPage(HostPage.Preferences));
        _trayMenu.Items.Add("Open product page", null, (_, _) => OpenProductSite());
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());
    }

    private void ApplyMenuTheme()
    {
        var theme = WindowsTheme.Current();
        _trayMenu.RenderMode = Forms.ToolStripRenderMode.Professional;
        _trayMenu.Renderer = new ThemedToolStripRenderer(theme);
        _trayMenu.BackColor = theme.Surface;
        _trayMenu.ForeColor = theme.Text;
        _trayMenu.ShowImageMargin = false;

        foreach (Forms.ToolStripItem item in _trayMenu.Items)
        {
            item.BackColor = theme.Surface;
            item.ForeColor = theme.Text;
        }
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        var hasActiveController = _pairingManager.HasActiveController;
        WpfApplication.Current.Dispatcher.BeginInvoke(ApplyTrayConnectionState);

        if (!_hadActiveController && hasActiveController)
        {
            var cancelledTransientDisconnect = CancelPendingDisconnectNotification();
            if (!cancelledTransientDisconnect)
            {
                WpfApplication.Current.Dispatcher.BeginInvoke(() =>
                {
                    ShowConnectionStatusNotification(
                        "Voltura Air paired",
                        $"{_pairingManager.ActiveDeviceSummary} connected.",
                        Forms.ToolTipIcon.Info);
                });
            }
        }
        else if (_hadActiveController && !hasActiveController)
        {
            ScheduleDisconnectNotification();
        }

        _hadActiveController = hasActiveController;
    }

    private void ScheduleDisconnectNotification()
    {
        CancelPendingDisconnectNotification();

        var pending = new CancellationTokenSource();
        _pendingDisconnectNotification = pending;
        var token = pending.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DisconnectNotificationDelayMs, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested || _disposed)
            {
                return;
            }

            _ = WpfApplication.Current.Dispatcher.BeginInvoke(() => ShowPendingDisconnectNotification(pending));
        });
    }

    private bool CancelPendingDisconnectNotification()
    {
        var pending = _pendingDisconnectNotification;
        if (pending is null)
        {
            return false;
        }

        _pendingDisconnectNotification = null;
        pending.Cancel();
        return true;
    }

    private void ShowPendingDisconnectNotification(CancellationTokenSource pending)
    {
        if (_disposed || !ReferenceEquals(_pendingDisconnectNotification, pending))
        {
            return;
        }

        _pendingDisconnectNotification = null;
        if (_pairingManager.HasActiveController)
        {
            return;
        }

        if (AppNotificationSettings.ShowPairingWindowOnDisconnect())
        {
            _mainWindow.ShowPage(HostPage.Connect);
        }

        ShowConnectionStatusNotification(
            "Voltura Air disconnected",
            "No connected devices.",
            Forms.ToolTipIcon.Info);
    }

    private void ShowConnectionStatusNotification(string title, string message, Forms.ToolTipIcon icon)
    {
        if (AppNotificationSettings.ShowConnectionStatusNotifications())
        {
            _trayIcon.ShowBalloonTip(3000, title, message, icon);
        }
    }

    private void OnAppThemeChanged(object? sender, EventArgs e)
    {
        WpfApplication.Current.Dispatcher.BeginInvoke(ApplyMenuTheme);
    }

    private void ApplyTrayConnectionState()
    {
        if (_disposed)
        {
            return;
        }

        var state = GetTrayConnectionState();
        _trayIcon.Text = BuildTrayTooltip(state);
        _trayIcon.Icon = GetTrayIcon(state);
    }

    private TrayConnectionState GetTrayConnectionState()
    {
        if (_pairingManager.HasActiveController)
        {
            return TrayConnectionState.Connected;
        }

        return _pairingManager.IsPaired
            ? TrayConnectionState.Disconnected
            : TrayConnectionState.NoDevicesRegistered;
    }

    private Icon GetTrayIcon(TrayConnectionState state)
    {
        return _trayIcons.TryGetValue(state, out var icon)
            ? icon
            : _trayIcons[TrayConnectionState.NoDevicesRegistered];
    }

    private string BuildTrayTooltip()
    {
        return BuildTrayTooltip(GetTrayConnectionState());
    }

    private string BuildTrayTooltip(TrayConnectionState state)
    {
        var status = state switch
        {
            TrayConnectionState.Connected => BuildConnectedTooltipStatus(),
            TrayConnectionState.Disconnected => "no devices connected",
            _ => "no devices paired yet"
        };

        return TruncateTrayTooltip($"Voltura Air - {status}");
    }

    private string BuildConnectedTooltipStatus()
    {
        var activeDeviceCount = _pairingManager.ActiveDeviceNames.Count;
        if (activeDeviceCount <= 0)
        {
            return "connected";
        }

        var deviceLabel = activeDeviceCount == 1 ? "device" : "devices";
        return $"{activeDeviceCount} {deviceLabel} connected: {_pairingManager.ActiveDeviceSummary}";
    }

    private static string TruncateTrayTooltip(string value)
    {
        return value.Length <= MaxTrayTooltipLength ? value : $"{value[..(MaxTrayTooltipLength - 3)]}...";
    }

    private static void OpenProductSite()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = ProductSiteUrl,
            UseShellExecute = true
        });
    }

    private void ExitApplication()
    {
        try
        {
            _mainWindow.AllowClose();
            _trayIcon.Visible = false;

            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                Environment.Exit(0);
            });

            var application = WpfApplication.Current;
            if (application.Dispatcher.CheckAccess())
            {
                application.Shutdown();
            }
            else
            {
                application.Dispatcher.BeginInvoke(() => application.Shutdown());
            }
        }
        catch (InvalidOperationException)
        {
            Environment.Exit(0);
        }
    }

    private static IReadOnlyDictionary<TrayConnectionState, Icon> LoadTrayIcons()
    {
        var normal = LoadTrayIcon();
        return new Dictionary<TrayConnectionState, Icon>
        {
            [TrayConnectionState.NoDevicesRegistered] = normal,
            [TrayConnectionState.Disconnected] = CreateTintedTrayIcon(normal, Color.FromArgb(215, 88, 88)),
            [TrayConnectionState.Connected] = CreateTintedTrayIcon(normal, Color.FromArgb(72, 166, 92))
        };
    }

    private static Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAirTray.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
    }

    private static Icon CreateTintedTrayIcon(Icon sourceIcon, Color tint)
    {
        try
        {
            using var source = sourceIcon.ToBitmap();
            using var tinted = TintBitmap(source, tint);
            return CreateIconFromBitmap(tinted);
        }
        catch
        {
            return (Icon)sourceIcon.Clone();
        }
    }

    private static Bitmap TintBitmap(Bitmap source, Color tint)
    {
        var target = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                if (pixel.A == 0)
                {
                    target.SetPixel(x, y, Color.Transparent);
                    continue;
                }

                var luminance = ((0.2126 * pixel.R) + (0.7152 * pixel.G) + (0.0722 * pixel.B)) / 255d;
                var shade = 0.42d + (luminance * 0.72d);
                target.SetPixel(
                    x,
                    y,
                    Color.FromArgb(
                        pixel.A,
                        ClampToByte(tint.R * shade),
                        ClampToByte(tint.G * shade),
                        ClampToByte(tint.B * shade)));
            }
        }

        return target;
    }

    private static int ClampToByte(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value >= 255 ? 255 : (int)Math.Round(value);
    }

    private static Icon CreateIconFromBitmap(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelPendingDisconnectNotification();
        _pairingManager.ConnectionChanged -= OnConnectionChanged;
        AppThemeSettings.Changed -= OnAppThemeChanged;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayMenu.Dispose();

        foreach (var icon in _trayIcons.Values.Distinct())
        {
            icon.Dispose();
        }
    }
}
