using System.Drawing;
using Microsoft.Win32;

namespace VolturaAir.Host;

public sealed class SettingsForm : Form
{
    private readonly Icon _appIcon;
    private readonly Label _titleLabel = new();
    private readonly Label _subtitleLabel = new();
    private readonly ThemedCheckBox _startWithWindowsCheckBox = new();
    private readonly ThemedCheckBox _showConnectionStatusNotificationsCheckBox = new();
    private readonly ThemedCheckBox _showPairingWindowOnDisconnectCheckBox = new();
    private readonly Button _connectionSettingsButton = new();
    private readonly Button _permissionsButton = new();
    private readonly Label _applicationSettingsLabel = new();
    private readonly Label _appearanceLabel = new();
    private readonly TableLayoutPanel _themeOptions = new();
    private readonly Button _systemThemeButton = new();
    private readonly Button _lightThemeButton = new();
    private readonly Button _darkThemeButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _closeButton = new();
    private ThemePalette _theme;

    public SettingsForm(Icon appIcon)
    {
        _appIcon = appIcon;
        _theme = WindowsTheme.Current();

        Text = "Voltura Air settings";
        Icon = _appIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(800, 1120);
        Size = new Size(800, 1120);

        BuildLayout();
        ApplyTheme();
        LoadSettings();

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        AppThemeSettings.Changed += OnAppThemeChanged;
        FormClosing += OnFormClosing;
    }

    public event EventHandler? ConnectionSettingsRequested;
    public event EventHandler? PermissionsRequested;

    public void ShowFor(IWin32Window owner)
    {
        LoadSettings();
        if (Visible)
        {
            Activate();
            return;
        }

        Show(owner);
        BeginInvoke(FocusDefaultControl);
    }

    public void ShowStandalone()
    {
        LoadSettings();
        if (Visible)
        {
            Activate();
            return;
        }

        Show();
        BeginInvoke(FocusDefaultControl);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            AppThemeSettings.Changed -= OnAppThemeChanged;
            FormClosing -= OnFormClosing;
            _appIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var themeButtonHeight = ScaleLogical((int)Math.Round(CommandButtonStyle.ButtonHeight * 0.7));
        var root = DialogLayout.CreateRoot(rowCount: 3);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(CommandButtonStyle.ActionRowHeight)));

        var header = DialogLayout.CreateHeader(
            _titleLabel,
            _subtitleLabel,
            "Settings",
            $"Windows host preferences - v{AppVersion.Display}",
            bottomMargin: ScaleLogical(22));

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            Padding = new Padding(0, ScaleLogical(4), 0, 0)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, themeButtonHeight));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, themeButtonHeight));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, themeButtonHeight));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        ConfigureSectionLabel(_applicationSettingsLabel, "APPLICATION SETTINGS", topMargin: 0);

        ConfigureSettingsCheckBox(
            _startWithWindowsCheckBox,
            "Start Voltura Air when I sign in to Windows",
            bottomMargin: 0);

        ConfigureSettingsCheckBox(
            _showConnectionStatusNotificationsCheckBox,
            "Show connection status notifications",
            bottomMargin: 0);

        ConfigureSettingsCheckBox(
            _showPairingWindowOnDisconnectCheckBox,
            "Show pairing window on disconnect",
            bottomMargin: ScaleLogical(8));

        _connectionSettingsButton.Text = "Connection";
        _connectionSettingsButton.Dock = DockStyle.Fill;
        _connectionSettingsButton.Height = themeButtonHeight;
        _connectionSettingsButton.MinimumSize = new Size(0, themeButtonHeight);
        _connectionSettingsButton.Margin = Padding.Empty;
        CommandButtonStyle.Configure(_connectionSettingsButton);
        _connectionSettingsButton.Click += (_, _) => ConnectionSettingsRequested?.Invoke(this, EventArgs.Empty);

        _permissionsButton.Text = "Permissions";
        _permissionsButton.Dock = DockStyle.Fill;
        _permissionsButton.Height = themeButtonHeight;
        _permissionsButton.MinimumSize = new Size(0, themeButtonHeight);
        _permissionsButton.Margin = new Padding(0, ScaleLogical(8), 0, 0);
        CommandButtonStyle.Configure(_permissionsButton);
        _permissionsButton.Click += (_, _) => PermissionsRequested?.Invoke(this, EventArgs.Empty);

        ConfigureSectionLabel(_appearanceLabel, "APPEARANCE", topMargin: ScaleLogical(18));

        _themeOptions.Dock = DockStyle.Fill;
        _themeOptions.Height = themeButtonHeight;
        _themeOptions.MinimumSize = new Size(0, themeButtonHeight);
        _themeOptions.ColumnCount = 3;
        _themeOptions.RowCount = 1;
        _themeOptions.Margin = Padding.Empty;
        _themeOptions.Padding = Padding.Empty;
        _themeOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        _themeOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        _themeOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));

        ConfigureThemeButton(_systemThemeButton, "System", AppThemeMode.System, isFirst: true, isLast: false);
        ConfigureThemeButton(_lightThemeButton, "Light", AppThemeMode.Light, isFirst: false, isLast: false);
        ConfigureThemeButton(_darkThemeButton, "Dark", AppThemeMode.Dark, isFirst: false, isLast: true);
        _themeOptions.Controls.Add(_systemThemeButton, 0, 0);
        _themeOptions.Controls.Add(_lightThemeButton, 1, 0);
        _themeOptions.Controls.Add(_darkThemeButton, 2, 0);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 2,
            Padding = new Padding(0, ScaleLogical(CommandButtonStyle.ActionTopPadding), 0, 0)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        _saveButton.Text = "Save";
        _saveButton.Dock = DockStyle.Fill;
        _saveButton.Margin = new Padding(0, 0, ScaleLogical(CommandButtonStyle.ButtonGap), 0);
        CommandButtonStyle.Configure(_saveButton);
        _saveButton.Click += (_, _) => SaveSettings();

        _closeButton.Text = "Close";
        _closeButton.Dock = DockStyle.Fill;
        CommandButtonStyle.Configure(_closeButton);
        _closeButton.Click += (_, _) => Hide();

        actions.Controls.Add(_saveButton, 0, 0);
        actions.Controls.Add(_closeButton, 1, 0);

        content.Controls.Add(_applicationSettingsLabel, 0, 0);
        content.Controls.Add(_startWithWindowsCheckBox, 0, 1);
        content.Controls.Add(_showConnectionStatusNotificationsCheckBox, 0, 2);
        content.Controls.Add(_showPairingWindowOnDisconnectCheckBox, 0, 3);
        content.Controls.Add(_connectionSettingsButton, 0, 4);
        content.Controls.Add(_permissionsButton, 0, 5);
        content.Controls.Add(_appearanceLabel, 0, 6);
        content.Controls.Add(_themeOptions, 0, 7);
        content.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 8);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(content, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }

    private void LoadSettings()
    {
        _startWithWindowsCheckBox.Checked = AppStartupSettings.IsEnabled();
        _showConnectionStatusNotificationsCheckBox.Checked = AppNotificationSettings.ShowConnectionStatusNotifications();
        _showPairingWindowOnDisconnectCheckBox.Checked = AppNotificationSettings.ShowPairingWindowOnDisconnect();
        UpdateThemeSelection(AppThemeSettings.GetMode());
    }

    private void SaveSettings()
    {
        AppStartupSettings.SetEnabled(_startWithWindowsCheckBox.Checked);
        AppNotificationSettings.SetShowConnectionStatusNotifications(_showConnectionStatusNotificationsCheckBox.Checked);
        AppNotificationSettings.SetShowPairingWindowOnDisconnect(_showPairingWindowOnDisconnectCheckBox.Checked);
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

    private void ApplyTheme()
    {
        _theme = WindowsTheme.Current();
        WindowsTheme.ApplyImmersiveDarkMode(this, _theme.IsDark);

        BackColor = _theme.Window;
        ForeColor = _theme.Text;
        _titleLabel.ForeColor = _theme.Text;
        _subtitleLabel.ForeColor = _theme.MutedText;
        _startWithWindowsCheckBox.ApplyTheme(_theme);
        _showConnectionStatusNotificationsCheckBox.ApplyTheme(_theme);
        _showPairingWindowOnDisconnectCheckBox.ApplyTheme(_theme);
        _applicationSettingsLabel.ForeColor = _theme.MutedText;
        _appearanceLabel.ForeColor = _theme.MutedText;
        _themeOptions.BackColor = _theme.Window;

        UpdateThemeSelection(AppThemeSettings.GetMode());
        ApplyButtonTheme(_connectionSettingsButton, isPrimary: false);
        ApplyButtonTheme(_permissionsButton, isPrimary: false);
        ApplyButtonTheme(_saveButton, isPrimary: true);
        ApplyButtonTheme(_closeButton, isPrimary: false);
    }

    private void ConfigureThemeButton(Button button, string text, AppThemeMode mode, bool isFirst, bool isLast)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(
            isFirst ? 0 : ScaleLogical(4),
            0,
            isLast ? 0 : ScaleLogical(4),
            0);
        CommandButtonStyle.Configure(button);
        button.Click += (_, _) =>
        {
            AppThemeSettings.SetMode(mode);
            UpdateThemeSelection(mode);
        };
    }

    private void ConfigureSettingsCheckBox(ThemedCheckBox checkBox, string text, int bottomMargin)
    {
        checkBox.Text = text;
        checkBox.Dock = DockStyle.Fill;
        var checkBoxHeight = checkBox.GetPreferredSize(Size.Empty).Height;
        checkBox.Height = checkBoxHeight;
        checkBox.MinimumSize = new Size(0, checkBoxHeight);
        checkBox.Margin = new Padding(0, 0, 0, bottomMargin);
        checkBox.Padding = new Padding(ScaleLogical(2), 0, 0, 0);
    }

    private void ConfigureSectionLabel(Label label, string text, int topMargin)
    {
        label.Text = text;
        label.Dock = DockStyle.Fill;
        label.AutoSize = true;
        label.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        label.Margin = new Padding(0, topMargin, 0, ScaleLogical(8));
    }

    private void UpdateThemeSelection(AppThemeMode activeMode)
    {
        ApplyThemeOption(_systemThemeButton, activeMode == AppThemeMode.System);
        ApplyThemeOption(_lightThemeButton, activeMode == AppThemeMode.Light);
        ApplyThemeOption(_darkThemeButton, activeMode == AppThemeMode.Dark);
    }

    private void ApplyThemeOption(Button button, bool isActive)
    {
        CommandButtonStyle.ApplyTheme(button, _theme, isActive ? CommandButtonKind.Primary : CommandButtonKind.Normal);
        button.Font = new Font("Segoe UI", 9f, isActive ? FontStyle.Bold : FontStyle.Regular);
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

    private void ApplyButtonTheme(Button button, bool isPrimary)
    {
        CommandButtonStyle.ApplyTheme(button, _theme, isPrimary ? CommandButtonKind.Primary : CommandButtonKind.Normal);
    }

    private void FocusDefaultControl()
    {
        _saveButton.Select();
    }

    private int ScaleLogical(int value)
    {
        return LogicalToDeviceUnits(value);
    }
}
