namespace VolturaAir.Host;

public sealed class TrayApplicationContext : ApplicationContext
{
    private const int MaxTrayTooltipLength = 63;
    private const int TrayMenuRowHeight = 42;
    private const int TrayMenuHorizontalPadding = 12;
    private const int TrayMenuWidthPadding = 48;
    private const float TrayMenuFontSizeIncrease = 1f;
    private const string ProductSiteUrl = "https://voltura.se/air/";
    private readonly System.ComponentModel.IContainer _components = new System.ComponentModel.Container();
    private readonly PairingForm _form;
    private readonly WebHostService _webHost;
    private readonly PairingManager _pairingManager;
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _trayIconImage;
    private readonly ContextMenuStrip _trayMenu;
    private readonly DeviceManagerForm _deviceManagerForm;
    private readonly SettingsForm _settingsForm;
    private readonly PermissionsForm _permissionsForm;
    private readonly ConnectionSettingsForm _connectionSettingsForm;
    private readonly TechnicalDetailsForm _technicalDetailsForm;
    private readonly ThemedWindowChrome _deviceManagerChrome;
    private readonly ThemedWindowChrome _settingsChrome;
    private readonly ThemedWindowChrome _permissionsChrome;
    private readonly ThemedWindowChrome _connectionSettingsChrome;
    private readonly ThemedWindowChrome _technicalDetailsChrome;
    private bool _hadActiveController;

    public TrayApplicationContext(PairingForm form, WebHostService webHost, PairingManager pairingManager, bool showMainWindow)
    {
        _form = form;
        _webHost = webHost;
        _pairingManager = pairingManager;
        _deviceManagerForm = new DeviceManagerForm(pairingManager, form.CloneAppIcon(), () => _form.ShowMainWindow());
        _settingsForm = new SettingsForm(form.CloneAppIcon());
        _permissionsForm = new PermissionsForm(pairingManager, form.CloneAppIcon());
        _connectionSettingsForm = new ConnectionSettingsForm(webHost, form, form.CloneAppIcon());
        _technicalDetailsForm = new TechnicalDetailsForm(form.CloneAppIcon());
        _deviceManagerChrome = ThemedWindowChrome.Install(_deviceManagerForm, _deviceManagerForm.Icon!);
        _settingsChrome = ThemedWindowChrome.Install(_settingsForm, _settingsForm.Icon!);
        _permissionsChrome = ThemedWindowChrome.Install(_permissionsForm, _permissionsForm.Icon!);
        _connectionSettingsChrome = ThemedWindowChrome.Install(_connectionSettingsForm, _connectionSettingsForm.Icon!);
        _technicalDetailsChrome = ThemedWindowChrome.Install(_technicalDetailsForm, _technicalDetailsForm.Icon!);
        _hadActiveController = pairingManager.HasActiveController;
        _form.FormClosed += (_, _) => ExitThread();
        _form.DeviceManagerRequested += OnDeviceManagerRequested;
        _settingsForm.ConnectionSettingsRequested += OnConnectionSettingsRequested;
        _settingsForm.PermissionsRequested += OnPermissionsRequested;
        _deviceManagerForm.DevicePermissionsRequested += OnDevicePermissionsRequested;
        _pairingManager.ConnectionChanged += OnConnectionChanged;
        AppThemeSettings.Changed += OnAppThemeChanged;

        _trayMenu = BuildMenu();
        _trayIconImage = LoadTrayIcon();
        _trayIcon = new NotifyIcon(_components)
        {
            ContextMenuStrip = _trayMenu,
            Icon = _trayIconImage,
            Text = BuildTrayTooltip(),
            Visible = true
        };
        ApplyMenuTheme();
        _trayIcon.DoubleClick += (_, _) => _form.ShowMainWindow();

        TrayIconVisibilityPromoter.PromoteWhenReady(_components, _trayIcon);
        if (showMainWindow && !_pairingManager.HasActiveController && AppNotificationSettings.ShowPairingWindowOnDisconnect())
        {
            _form.Show();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pairingManager.ConnectionChanged -= OnConnectionChanged;
            _form.DeviceManagerRequested -= OnDeviceManagerRequested;
            _settingsForm.ConnectionSettingsRequested -= OnConnectionSettingsRequested;
            _settingsForm.PermissionsRequested -= OnPermissionsRequested;
            _deviceManagerForm.DevicePermissionsRequested -= OnDevicePermissionsRequested;
            AppThemeSettings.Changed -= OnAppThemeChanged;
            _trayIcon.Visible = false;
            _deviceManagerChrome.Dispose();
            _settingsChrome.Dispose();
            _permissionsChrome.Dispose();
            _connectionSettingsChrome.Dispose();
            _technicalDetailsChrome.Dispose();
            _deviceManagerForm.Dispose();
            _settingsForm.Dispose();
            _permissionsForm.Dispose();
            _connectionSettingsForm.Dispose();
            _technicalDetailsForm.Dispose();
            _components.Dispose();
            _trayIconImage.Dispose();
            _form.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip(_components);
        menu.Font = new Font(menu.Font.FontFamily, menu.Font.SizeInPoints + TrayMenuFontSizeIncrease, menu.Font.Style);
        var showQrCodeItem = menu.Items.Add("▦  Show QR code", null, (_, _) => _form.ShowMainWindow());
        showQrCodeItem.Font = new Font(showQrCodeItem.Font, FontStyle.Bold);
        menu.Items.Add("▣  Device manager", null, (_, _) => ShowDeviceManager());
        menu.Items.Add("⚙  Settings", null, (_, _) => ShowSettings());
        menu.Items.Add("ⓘ  Technical details", null, (_, _) => ShowTechnicalDetails());
        menu.Items.Add("↗  Open product page", null, (_, _) => OpenProductSite());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("⏻  Exit", null, (_, _) =>
        {
            _form.AllowExit();
            _form.Close();
            Application.Exit();
        });
        return menu;
    }

    private void ApplyMenuTheme()
    {
        var theme = WindowsTheme.Current();
        var menuItemSize = MeasureMenuItemSize();
        _trayMenu.RenderMode = ToolStripRenderMode.Professional;
        _trayMenu.Renderer = new ThemedToolStripRenderer(theme);
        _trayMenu.BackColor = theme.Surface;
        _trayMenu.ForeColor = theme.Text;
        _trayMenu.ShowImageMargin = false;
        _trayMenu.Padding = new Padding(ScaleLogical(10), ScaleLogical(8), ScaleLogical(10), ScaleLogical(8));

        foreach (ToolStripItem item in _trayMenu.Items)
        {
            item.BackColor = theme.Surface;
            item.ForeColor = theme.Text;

            if (item is ToolStripSeparator)
            {
                item.AutoSize = true;
                item.Margin = new Padding(ScaleLogical(10), ScaleLogical(4), ScaleLogical(10), ScaleLogical(4));
                item.Padding = Padding.Empty;
                continue;
            }

            item.AutoSize = false;
            item.Size = menuItemSize;
            item.Margin = Padding.Empty;
            item.Padding = new Padding(ScaleLogical(TrayMenuHorizontalPadding), 0, ScaleLogical(TrayMenuHorizontalPadding), 0);
            item.TextAlign = ContentAlignment.MiddleLeft;
        }
    }

    private Size MeasureMenuItemSize()
    {
        var width = 0;
        foreach (ToolStripItem item in _trayMenu.Items)
        {
            if (item is ToolStripSeparator)
            {
                continue;
            }

            width = Math.Max(width, TextRenderer.MeasureText(item.Text, item.Font).Width);
        }

        return new Size(width + ScaleLogical(TrayMenuWidthPadding), ScaleLogical(TrayMenuRowHeight));
    }

    private static void OpenProductSite()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = ProductSiteUrl,
            UseShellExecute = true
        });
    }

    private void ShowTechnicalDetails()
    {
        _technicalDetailsForm.ShowDetails(_form, new[]
        {
            new TechnicalDetail("Version", AppVersion.Display),
            new TechnicalDetail("Host URL", _form.ServerUrl),
            new TechnicalDetail("Advertised IP", _webHost.AdvertisedHostAddress),
            new TechnicalDetail("Pairing link", _form.PairingUrl),
            new TechnicalDetail("Paired devices", _pairingManager.PairedDeviceSummary),
            new TechnicalDetail("Active devices", _pairingManager.HasActiveController ? _pairingManager.ActiveDeviceSummary : "none"),
            new TechnicalDetail("Data folder", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltura Air")),
            new TechnicalDetail("Executable", Environment.ProcessPath ?? string.Empty)
        });
    }

    private void OnDeviceManagerRequested(object? sender, EventArgs e)
    {
        ShowDeviceManager();
    }

    private void ShowDeviceManager()
    {
        _deviceManagerForm.ShowFor(_form);
    }

    private void ShowSettings()
    {
        _settingsForm.ShowStandalone();
    }

    private void OnPermissionsRequested(object? sender, EventArgs e)
    {
        _permissionsForm.ShowGlobal(_settingsForm);
    }

    private void OnDevicePermissionsRequested(object? sender, DevicePermissionsRequestedEventArgs e)
    {
        _permissionsForm.ShowDevice(_deviceManagerForm, e.ClientId, e.DeviceName);
    }

    private void OnConnectionSettingsRequested(object? sender, EventArgs e)
    {
        ShowConnectionSettings();
    }

    private void ShowConnectionSettings()
    {
        _connectionSettingsForm.ShowFor(_form);
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        var hasActiveController = _pairingManager.HasActiveController;
        _form.BeginInvoke(() => _trayIcon.Text = BuildTrayTooltip());

        if (!_hadActiveController && hasActiveController)
        {
            _form.BeginInvoke(() =>
            {
                _form.ShowPairedStatus();
                _form.HideToTray();
                ShowConnectionStatusNotification(
                    "Voltura Air paired",
                    $"{_pairingManager.ActiveDeviceSummary} connected.",
                    ToolTipIcon.Info);
            });
        }
        else if (_hadActiveController && !hasActiveController)
        {
            _form.BeginInvoke(() =>
            {
                if (AppNotificationSettings.ShowPairingWindowOnDisconnect())
                {
                    _form.ShowMainWindow();
                }

                ShowConnectionStatusNotification(
                    "Voltura Air disconnected",
                    "No connected devices.",
                    ToolTipIcon.Info);
            });
        }

        _hadActiveController = hasActiveController;
    }

    private void ShowConnectionStatusNotification(string title, string message, ToolTipIcon icon)
    {
        if (!AppNotificationSettings.ShowConnectionStatusNotifications())
        {
            return;
        }

        _trayIcon.ShowBalloonTip(3000, title, message, icon);
    }

    private void OnAppThemeChanged(object? sender, EventArgs e)
    {
        _form.BeginInvoke(ApplyMenuTheme);
    }

    private string BuildTrayTooltip()
    {
        var status = _pairingManager.HasActiveController
            ? $"Connected: {_pairingManager.ActiveDeviceSummary}"
            : "No connected devices";

        return TruncateTrayTooltip($"Voltura Air - {status}");
    }

    private static string TruncateTrayTooltip(string value)
    {
        return value.Length <= MaxTrayTooltipLength
            ? value
            : $"{value[..(MaxTrayTooltipLength - 3)]}...";
    }

    private static Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAirTray.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
    }

    private int ScaleLogical(int value)
    {
        using var graphics = _form.CreateGraphics();
        return (int)Math.Round(value * graphics.DpiX / 96f);
    }
}
