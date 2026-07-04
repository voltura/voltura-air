using System.Drawing;
using Microsoft.Win32;

namespace VolturaAir.Host;

public sealed class PermissionsForm : Form
{
    private const int LogicalFormWidth = 1180;
    private const int LogicalMinimumFormHeight = 780;
    private const int LogicalInitialFormHeight = 900;
    private const int LogicalPermissionRowHeight = 64;
    private const int LogicalPermissionRowGap = 8;
    private const int LogicalCloseButtonWidth = 360;

    private enum PermissionScope
    {
        Global,
        Device
    }

    private enum DevicePermissionChoice
    {
        UseGlobal,
        Allow,
        Block
    }

    private enum PermissionKind
    {
        PcSleep,
        VolumeControl
    }

    private readonly PairingManager _pairingManager;
    private readonly Icon _appIcon;
    private readonly Label _titleLabel = new();
    private readonly Label _subtitleLabel = new();
    private readonly Panel _permissionsViewport = new();
    private readonly TableLayoutPanel _permissionsList = new();
    private readonly Button _closeButton = new();
    private readonly List<Control> _rowSurfaces = new();
    private readonly List<Label> _labels = new();
    private ThemedCheckBox? _allowPcSleepCheckBox;
    private ThemedCheckBox? _allowVolumeControlCheckBox;
    private Button? _sleepUseGlobalButton;
    private Button? _sleepAllowButton;
    private Button? _sleepBlockButton;
    private Button? _volumeUseGlobalButton;
    private Button? _volumeAllowButton;
    private Button? _volumeBlockButton;
    private ThemePalette _theme;
    private PermissionScope _scope = PermissionScope.Global;
    private string _deviceClientId = string.Empty;
    private DevicePermissionChoice _allowPcSleepDeviceChoice = DevicePermissionChoice.UseGlobal;
    private DevicePermissionChoice _allowVolumeControlDeviceChoice = DevicePermissionChoice.UseGlobal;
    private bool _isLoading;

    public PermissionsForm(PairingManager pairingManager, Icon appIcon)
    {
        _pairingManager = pairingManager;
        _appIcon = appIcon;
        _theme = WindowsTheme.Current();

        Text = "Voltura Air permissions";
        Icon = _appIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(LogicalFormWidth, LogicalMinimumFormHeight);
        Size = new Size(LogicalFormWidth, LogicalInitialFormHeight);

        BuildLayout();
        ApplyTheme();

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        AppThemeSettings.Changed += OnAppThemeChanged;
        FormClosing += OnFormClosing;
    }

    public void ShowGlobal(IWin32Window owner)
    {
        _isLoading = true;
        _scope = PermissionScope.Global;
        _deviceClientId = string.Empty;
        Text = "Voltura Air global permissions";
        ConfigureHeader("Global permissions", "Default rights for connected devices.");
        BuildPermissionRows();
        LoadGlobalPermissions();
        _isLoading = false;
        ShowOrActivate(owner);
    }

    public void ShowDevice(IWin32Window owner, string clientId, string deviceName)
    {
        _isLoading = true;
        _scope = PermissionScope.Device;
        _deviceClientId = clientId;
        Text = "Voltura Air device permissions";
        ConfigureHeader("Device permissions", deviceName);
        BuildPermissionRows();
        LoadDevicePermissions();
        _isLoading = false;
        ShowOrActivate(owner);
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
            "Permissions",
            "Default rights for connected devices.",
            bottomMargin: ScaleLogical(18));

        _permissionsViewport.Dock = DockStyle.Fill;
        _permissionsViewport.Margin = Padding.Empty;
        _permissionsViewport.AutoScroll = true;
        _permissionsViewport.Resize += (_, _) => RefreshPermissionRowWidths();

        _permissionsList.Dock = DockStyle.Top;
        _permissionsList.AutoSize = true;
        _permissionsList.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _permissionsList.ColumnCount = 1;
        _permissionsList.RowCount = 1;
        _permissionsList.Margin = Padding.Empty;
        _permissionsList.Padding = Padding.Empty;
        _permissionsList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _permissionsViewport.Controls.Add(_permissionsList);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 2,
            Padding = new Padding(0, ScaleLogical(CommandButtonStyle.ActionTopPadding), 0, 0)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(LogicalCloseButtonWidth)));

        _closeButton.Text = "Close";
        _closeButton.Dock = DockStyle.Fill;
        _closeButton.Margin = Padding.Empty;
        CommandButtonStyle.Configure(_closeButton);
        _closeButton.Click += (_, _) => Hide();

        actions.Controls.Add(_closeButton, 1, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_permissionsViewport, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }

    private void BuildPermissionRows()
    {
        _permissionsList.SuspendLayout();
        foreach (Control control in _permissionsList.Controls.Cast<Control>().ToArray())
        {
            _permissionsList.Controls.Remove(control);
            control.Dispose();
        }

        _rowSurfaces.Clear();
        _labels.Clear();
        _allowPcSleepCheckBox = null;
        _allowVolumeControlCheckBox = null;
        _sleepUseGlobalButton = null;
        _sleepAllowButton = null;
        _sleepBlockButton = null;
        _volumeUseGlobalButton = null;
        _volumeAllowButton = null;
        _volumeBlockButton = null;

        _permissionsList.RowStyles.Clear();
        _permissionsList.RowCount = 2;
        _permissionsList.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _permissionsList.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _permissionsList.Controls.Add(
            _scope == PermissionScope.Global
                ? CreateGlobalPermissionRow("Allow PC sleep", PermissionKind.PcSleep)
                : CreateDevicePermissionRow("Allow PC sleep", PermissionKind.PcSleep),
            0,
            0);
        _permissionsList.Controls.Add(
            _scope == PermissionScope.Global
                ? CreateGlobalPermissionRow("Allow volume control", PermissionKind.VolumeControl)
                : CreateDevicePermissionRow("Allow volume control", PermissionKind.VolumeControl),
            0,
            1);
        _permissionsList.ResumeLayout();
        ApplyTheme();
    }

    private Control CreateGlobalPermissionRow(string text, PermissionKind permission)
    {
        var row = CreatePermissionRow(columnCount: 1);
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        var checkBox = new ThemedCheckBox
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(ScaleLogical(2), 0, 0, 0)
        };
        checkBox.CheckedChanged += (_, _) => SaveGlobalPermissions();
        if (permission == PermissionKind.PcSleep)
        {
            _allowPcSleepCheckBox = checkBox;
        }
        else
        {
            _allowVolumeControlCheckBox = checkBox;
        }

        row.Controls.Add(checkBox, 0, 0);
        return row;
    }

    private Control CreateDevicePermissionRow(string text, PermissionKind permission)
    {
        var row = CreatePermissionRow(columnCount: 2);
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));

        var label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 9.5f)
        };
        _labels.Add(label);

        var choices = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29f));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29f));

        var useGlobalButton = CreateChoiceButton("Use global", permission, DevicePermissionChoice.UseGlobal, isFirst: true);
        var allowButton = CreateChoiceButton("Allow", permission, DevicePermissionChoice.Allow, isFirst: false);
        var blockButton = CreateChoiceButton("Block", permission, DevicePermissionChoice.Block, isFirst: false);
        if (permission == PermissionKind.PcSleep)
        {
            _sleepUseGlobalButton = useGlobalButton;
            _sleepAllowButton = allowButton;
            _sleepBlockButton = blockButton;
        }
        else
        {
            _volumeUseGlobalButton = useGlobalButton;
            _volumeAllowButton = allowButton;
            _volumeBlockButton = blockButton;
        }

        choices.Controls.Add(useGlobalButton, 0, 0);
        choices.Controls.Add(allowButton, 1, 0);
        choices.Controls.Add(blockButton, 2, 0);

        row.Controls.Add(label, 0, 0);
        row.Controls.Add(choices, 1, 0);
        return row;
    }

    private TableLayoutPanel CreatePermissionRow(int columnCount)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Width = Math.Max(1, _permissionsViewport.ClientSize.Width - ScaleLogical(24)),
            Height = ScaleLogical(LogicalPermissionRowHeight),
            ColumnCount = columnCount,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, ScaleLogical(8)),
            Padding = new Padding(ScaleLogical(14), ScaleLogical(8), ScaleLogical(14), ScaleLogical(8))
        };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _rowSurfaces.Add(row);
        return row;
    }

    private void RefreshPermissionRowWidths()
    {
        var rowWidth = Math.Max(1, _permissionsViewport.ClientSize.Width - ScaleLogical(24));
        _permissionsList.Width = rowWidth;
        foreach (var surface in _rowSurfaces)
        {
            surface.Width = rowWidth;
        }
    }

    private Button CreateChoiceButton(string text, PermissionKind permission, DevicePermissionChoice choice, bool isFirst)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(isFirst ? 0 : ScaleLogical(6), 0, 0, 0)
        };
        CommandButtonStyle.Configure(button);
        button.Click += (_, _) =>
        {
            if (permission == PermissionKind.PcSleep)
            {
                _allowPcSleepDeviceChoice = choice;
            }
            else
            {
                _allowVolumeControlDeviceChoice = choice;
            }

            ApplyDeviceChoiceTheme();
            SaveDevicePermissions();
        };
        return button;
    }

    private void LoadGlobalPermissions()
    {
        var permissions = AppPermissionSettings.Load();
        if (_allowPcSleepCheckBox is not null)
        {
            _allowPcSleepCheckBox.Checked = permissions.AllowPcSleep;
        }

        if (_allowVolumeControlCheckBox is not null)
        {
            _allowVolumeControlCheckBox.Checked = permissions.AllowVolumeControl;
        }
    }

    private void LoadDevicePermissions()
    {
        var overrides = _pairingManager.GetDevicePermissionOverrides(_deviceClientId);
        _allowPcSleepDeviceChoice = overrides.AllowPcSleep switch
        {
            true => DevicePermissionChoice.Allow,
            false => DevicePermissionChoice.Block,
            _ => DevicePermissionChoice.UseGlobal
        };
        _allowVolumeControlDeviceChoice = overrides.AllowVolumeControl switch
        {
            true => DevicePermissionChoice.Allow,
            false => DevicePermissionChoice.Block,
            _ => DevicePermissionChoice.UseGlobal
        };
        ApplyDeviceChoiceTheme();
    }

    private void SaveGlobalPermissions()
    {
        if (_isLoading || _scope != PermissionScope.Global)
        {
            return;
        }

        AppPermissionSettings.Save(new HostPermissionSet(
            AllowPcSleep: _allowPcSleepCheckBox?.Checked == true,
            AllowVolumeControl: _allowVolumeControlCheckBox?.Checked == true));
    }

    private void SaveDevicePermissions()
    {
        if (_isLoading || _scope != PermissionScope.Device || string.IsNullOrEmpty(_deviceClientId))
        {
            return;
        }

        _pairingManager.SetDevicePermissionOverrides(
            _deviceClientId,
            new DevicePermissionOverrides(
                AllowPcSleep: ToOverride(_allowPcSleepDeviceChoice),
                AllowVolumeControl: ToOverride(_allowVolumeControlDeviceChoice)));
    }

    private void ConfigureHeader(string title, string subtitle)
    {
        _titleLabel.Text = title;
        _subtitleLabel.Text = subtitle;
    }

    private void ShowOrActivate(IWin32Window owner)
    {
        if (Visible)
        {
            Activate();
            return;
        }

        Show(owner);
        BeginInvoke(() => _closeButton.Select());
    }

    private void ApplyTheme()
    {
        _theme = WindowsTheme.Current();
        WindowsTheme.ApplyImmersiveDarkMode(this, _theme.IsDark);

        BackColor = _theme.Window;
        ForeColor = _theme.Text;
        _titleLabel.ForeColor = _theme.Text;
        _subtitleLabel.ForeColor = _theme.MutedText;
        _permissionsViewport.BackColor = _theme.Window;
        _permissionsList.BackColor = _theme.Window;

        foreach (var surface in _rowSurfaces)
        {
            surface.BackColor = _theme.Surface;
        }

        foreach (var label in _labels)
        {
            label.BackColor = _theme.Surface;
            label.ForeColor = _theme.Text;
        }

        _allowPcSleepCheckBox?.ApplyTheme(_theme);
        _allowVolumeControlCheckBox?.ApplyTheme(_theme);
        CommandButtonStyle.ApplyTheme(_closeButton, _theme, CommandButtonKind.Normal);
        ApplyDeviceChoiceTheme();
    }

    private void ApplyDeviceChoiceTheme()
    {
        var global = AppPermissionSettings.Load();
        ApplyDevicePermissionTheme(
            _allowPcSleepDeviceChoice,
            global.AllowPcSleep,
            _sleepUseGlobalButton,
            _sleepAllowButton,
            _sleepBlockButton);
        ApplyDevicePermissionTheme(
            _allowVolumeControlDeviceChoice,
            global.AllowVolumeControl,
            _volumeUseGlobalButton,
            _volumeAllowButton,
            _volumeBlockButton);
    }

    private void ApplyDevicePermissionTheme(DevicePermissionChoice choice, bool globalAllows, Button? useGlobalButton, Button? allowButton, Button? blockButton)
    {
        var useGlobal = choice == DevicePermissionChoice.UseGlobal;

        ApplyChoiceTheme(useGlobalButton, useGlobal ? ChoiceButtonState.Selected : ChoiceButtonState.Normal);
        ApplyChoiceTheme(allowButton, choice == DevicePermissionChoice.Allow
            ? ChoiceButtonState.Selected
            : useGlobal && globalAllows
                ? ChoiceButtonState.Inherited
                : ChoiceButtonState.Normal);
        ApplyChoiceTheme(blockButton, choice == DevicePermissionChoice.Block
            ? ChoiceButtonState.Selected
            : useGlobal && !globalAllows
                ? ChoiceButtonState.Inherited
                : ChoiceButtonState.Normal);
    }

    private void ApplyChoiceTheme(Button? button, ChoiceButtonState state)
    {
        if (button is null)
        {
            return;
        }

        if (state == ChoiceButtonState.Inherited)
        {
            ApplyInheritedChoiceTheme(button);
            return;
        }

        var selected = state == ChoiceButtonState.Selected;
        CommandButtonStyle.ApplyTheme(button, _theme, selected ? CommandButtonKind.Primary : CommandButtonKind.Normal);
        button.Font = new Font("Segoe UI", 9f, selected ? FontStyle.Bold : FontStyle.Regular);
    }

    private void ApplyInheritedChoiceTheme(Button button)
    {
        var background = Blend(_theme.SurfaceRaised, _theme.Accent, _theme.IsDark ? 0.34f : 0.16f);
        var hover = Blend(_theme.SurfaceRaised, _theme.Accent, _theme.IsDark ? 0.44f : 0.24f);
        var pressed = Blend(_theme.SurfaceRaised, _theme.Accent, _theme.IsDark ? 0.26f : 0.12f);
        var border = Blend(_theme.Border, _theme.Accent, _theme.IsDark ? 0.6f : 0.45f);

        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = background;
        button.ForeColor = _theme.Text;
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.FlatAppearance.MouseDownBackColor = pressed;
        button.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private enum ChoiceButtonState
    {
        Normal,
        Inherited,
        Selected
    }

    private static bool? ToOverride(DevicePermissionChoice choice)
    {
        return choice switch
        {
            DevicePermissionChoice.Allow => true,
            DevicePermissionChoice.Block => false,
            _ => null
        };
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
