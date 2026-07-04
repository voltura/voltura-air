using System.Drawing;
using Microsoft.Win32;

namespace VolturaAir.Host;

public sealed class ConnectionSettingsForm : Form
{
    private readonly Icon _appIcon;
    private readonly Label _titleLabel = new();
    private readonly Label _subtitleLabel = new();
    private readonly ConnectionSettingsPanel _settingsPanel;
    private readonly Button _closeButton = new();
    private ThemePalette _theme;

    public ConnectionSettingsForm(WebHostService webHost, PairingForm pairingForm, Icon appIcon)
    {
        _appIcon = appIcon;
        _settingsPanel = new ConnectionSettingsPanel(webHost, pairingForm);
        _theme = WindowsTheme.Current();

        Text = "Voltura Air connection settings";
        Icon = _appIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1440, 940);
        Size = new Size(1520, 980);

        BuildLayout();
        ApplyTheme();
        LoadSettings();

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        AppThemeSettings.Changed += OnAppThemeChanged;
        FormClosing += OnFormClosing;
    }

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
        var root = DialogLayout.CreateRoot(rowCount: 3);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(CommandButtonStyle.ActionRowHeight)));

        var header = DialogLayout.CreateHeader(
            _titleLabel,
            _subtitleLabel,
            "Connection",
            "Choose the local network address Voltura Air advertises to phones and tablets.",
            bottomMargin: ScaleLogical(20));

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
        root.Controls.Add(_settingsPanel, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }

    private void LoadSettings()
    {
        _settingsPanel.RefreshSettings();
    }

    private void ApplyTheme()
    {
        _theme = WindowsTheme.Current();
        WindowsTheme.ApplyImmersiveDarkMode(this, _theme.IsDark);

        BackColor = _theme.Window;
        ForeColor = _theme.Text;
        _titleLabel.ForeColor = _theme.Text;
        _subtitleLabel.ForeColor = _theme.MutedText;
        _settingsPanel.ApplyTheme(_theme);
        CommandButtonStyle.ApplyTheme(_closeButton, _theme, CommandButtonKind.Normal);
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

    private void FocusDefaultControl()
    {
        _closeButton.Select();
    }

    private int ScaleLogical(int value)
    {
        return LogicalToDeviceUnits(value);
    }
}
