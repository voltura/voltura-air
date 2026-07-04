using System.Drawing;
using Microsoft.Win32;

namespace VolturaAir.Host;

public sealed class DeviceManagerForm : Form
{
    private readonly Icon _appIcon;
    private readonly Label _titleLabel = new();
    private readonly Label _subtitleLabel = new();
    private readonly DeviceManagerPanel _deviceManagerPanel;
    private readonly Button _closeButton = new();
    private ThemePalette _theme;

    public DeviceManagerForm(PairingManager pairingManager, Icon appIcon, Action? onDisconnectAllCallback = null)
    {
        _appIcon = appIcon;
        _deviceManagerPanel = new DeviceManagerPanel(pairingManager, appIcon, onDisconnectAllCallback);
        _theme = WindowsTheme.Current();

        Text = "Voltura Air device manager";
        Icon = _appIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1460, 760);
        Size = new Size(1500, 800);

        BuildLayout();
        ApplyTheme();
        RefreshDevices();

        _deviceManagerPanel.DevicePermissionsRequested += OnPanelDevicePermissionsRequested;
        _deviceManagerPanel.CloseRequested += OnPanelCloseRequested;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        AppThemeSettings.Changed += OnAppThemeChanged;
        FormClosing += OnFormClosing;
    }

    public event EventHandler<DevicePermissionsRequestedEventArgs>? DevicePermissionsRequested;

    public void ShowFor(IWin32Window owner)
    {
        RefreshDevices();
        if (Visible)
        {
            Activate();
            return;
        }

        Show(owner);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deviceManagerPanel.DevicePermissionsRequested -= OnPanelDevicePermissionsRequested;
            _deviceManagerPanel.CloseRequested -= OnPanelCloseRequested;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            AppThemeSettings.Changed -= OnAppThemeChanged;
            FormClosing -= OnFormClosing;
            _appIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var root = DialogLayout.CreateRoot(rowCount: 3);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(CommandButtonStyle.ActionRowHeight)));

        var header = DialogLayout.CreateHeader(
            _titleLabel,
            _subtitleLabel,
            "Device manager",
            "Connected and paired devices",
            bottomMargin: ScaleLogical(18));

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 2,
            Padding = new Padding(0, ScaleLogical(CommandButtonStyle.ActionTopPadding), 0, 0)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(180)));

        _closeButton.Text = "Close";
        _closeButton.Dock = DockStyle.Fill;
        _closeButton.Margin = Padding.Empty;
        CommandButtonStyle.Configure(_closeButton);
        _closeButton.Click += (_, _) => Hide();

        actions.Controls.Add(_closeButton, 1, 0);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_deviceManagerPanel, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }

    private void RefreshDevices()
    {
        _deviceManagerPanel.RefreshDevices();
    }

    private void ApplyTheme()
    {
        _theme = WindowsTheme.Current();
        WindowsTheme.ApplyImmersiveDarkMode(this, _theme.IsDark);

        BackColor = _theme.Window;
        ForeColor = _theme.Text;
        _titleLabel.ForeColor = _theme.Text;
        _subtitleLabel.ForeColor = _theme.MutedText;
        _deviceManagerPanel.ApplyTheme(_theme);
        CommandButtonStyle.ApplyTheme(_closeButton, _theme, CommandButtonKind.Normal);
    }

    private void OnPanelDevicePermissionsRequested(object? sender, DevicePermissionsRequestedEventArgs e)
    {
        DevicePermissionsRequested?.Invoke(this, e);
    }

    private void OnPanelCloseRequested(object? sender, EventArgs e)
    {
        Hide();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            BeginInvoke(ApplyTheme);
        }
    }

    private void OnAppThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        if (IsHandleCreated)
        {
            BeginInvoke(ApplyTheme);
            return;
        }

        ApplyTheme();
    }

    private int ScaleLogical(int value)
    {
        return LogicalToDeviceUnits(value);
    }
}

public sealed class DevicePermissionsRequestedEventArgs : EventArgs
{
    public DevicePermissionsRequestedEventArgs(string clientId, string deviceName)
    {
        ClientId = clientId;
        DeviceName = deviceName;
    }

    public string ClientId { get; }

    public string DeviceName { get; }
}
