using System.Drawing;
using Microsoft.Win32;

namespace VolturaAir.Host;

public enum SettingsPage
{
    Application,
    Devices,
    Permissions,
    Connection,
    Appearance
}

public sealed class SettingsForm : Form
{
    private const int LogicalFormWidth = 1720;
    private const int LogicalFormHeight = 1120;
    private const int LogicalMinimumFormWidth = 1220;
    private const int LogicalMinimumFormHeight = 760;
    private const int LogicalScreenMargin = 48;
    private const int LogicalNavWidth = 200;
    private const int LogicalNavButtonHeight = 56;
    private const int LogicalCloseButtonWidth = 180;
    private const int LogicalPageGap = 12;
    private const int LogicalDevicePanelHeight = 660;
    private const int LogicalConnectionPanelHeight = 660;

    private readonly Icon _appIcon;
    private readonly Label _titleLabel = new();
    private readonly Label _subtitleLabel = new();
    private readonly TableLayoutPanel _navigation = new();
    private readonly Panel _pageViewport = new();
    private readonly Panel _pageCanvas = new();
    private readonly Panel _pageScrollTrack = new();
    private readonly Panel _pageScrollThumb = new();
    private readonly Button _applicationPageButton = new();
    private readonly Button _devicesPageButton = new();
    private readonly Button _permissionsPageButton = new();
    private readonly Button _connectionPageButton = new();
    private readonly Button _appearancePageButton = new();
    private readonly ThemedCheckBox _startWithWindowsCheckBox = new();
    private readonly ThemedCheckBox _showConnectionStatusNotificationsCheckBox = new();
    private readonly ThemedCheckBox _showPairingWindowOnDisconnectCheckBox = new();
    private readonly ThemedCheckBox _allowPcSleepCheckBox = new();
    private readonly ThemedCheckBox _allowVolumeControlCheckBox = new();
    private readonly TableLayoutPanel _themeOptions = new();
    private readonly Button _systemThemeButton = new();
    private readonly Button _lightThemeButton = new();
    private readonly Button _darkThemeButton = new();
    private readonly DeviceManagerPanel _deviceManagerPanel;
    private readonly ConnectionSettingsPanel _connectionSettingsPanel;
    private readonly Button _closeButton = new();
    private readonly List<Button> _navigationButtons = new();
    private readonly List<ThemedCheckBox> _checkBoxes = new();
    private ThemePalette _theme;
    private SettingsPage _activePage = SettingsPage.Application;
    private int _pageScrollOffset;
    private bool _isDraggingPageScrollThumb;
    private int _pageScrollDragStartY;
    private int _pageScrollDragStartOffset;
    private bool _isLoading;

    public SettingsForm(Icon appIcon, PairingManager pairingManager, WebHostService webHost, PairingForm pairingForm)
    {
        _appIcon = appIcon;
        _theme = WindowsTheme.Current();
        _deviceManagerPanel = new DeviceManagerPanel(pairingManager, _appIcon, () => pairingForm.ShowMainWindow());
        _connectionSettingsPanel = new ConnectionSettingsPanel(webHost, pairingForm);

        Text = "Voltura Air settings";
        Icon = _appIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ApplyFormSizeLimits();

        BuildLayout();
        WireSettingsAutosave();
        ApplyTheme();
        LoadSettings();
        SelectPage(SettingsPage.Application);

        _deviceManagerPanel.DevicePermissionsRequested += OnDevicePermissionsRequested;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        AppThemeSettings.Changed += OnAppThemeChanged;
        FormClosing += OnFormClosing;
    }

    public event EventHandler<DevicePermissionsRequestedEventArgs>? DevicePermissionsRequested;

    public void ShowFor(IWin32Window owner, SettingsPage page = SettingsPage.Application)
    {
        LoadSettings();
        SelectPage(page);
        if (Visible)
        {
            Activate();
            return;
        }

        Show(owner);
        BeginInvoke(FocusDefaultControl);
    }

    public void ShowStandalone(SettingsPage page = SettingsPage.Application)
    {
        LoadSettings();
        SelectPage(page);
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
            _deviceManagerPanel.DevicePermissionsRequested -= OnDevicePermissionsRequested;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            AppThemeSettings.Changed -= OnAppThemeChanged;
            FormClosing -= OnFormClosing;
            _appIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ApplyFormSizeLimits()
    {
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, LogicalFormWidth, LogicalFormHeight);
        var outerMargin = ScaleLogical(LogicalScreenMargin);
        var maxWidth = Math.Max(1, workingArea.Width - outerMargin);
        var maxHeight = Math.Max(1, workingArea.Height - outerMargin);
        var minimumWidth = Math.Min(ScaleLogical(LogicalMinimumFormWidth), maxWidth);
        var minimumHeight = Math.Min(ScaleLogical(LogicalMinimumFormHeight), maxHeight);
        var targetWidth = Math.Min(ScaleLogical(LogicalFormWidth), maxWidth);
        var targetHeight = Math.Min(ScaleLogical(LogicalFormHeight), maxHeight);

        MaximumSize = new Size(maxWidth, maxHeight);
        MinimumSize = new Size(minimumWidth, minimumHeight);
        Size = new Size(Math.Max(minimumWidth, targetWidth), Math.Max(minimumHeight, targetHeight));
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
            "Settings",
            $"Windows host preferences - v{AppVersion.Display}",
            bottomMargin: ScaleLogical(18));

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(LogicalNavWidth)));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(18)));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        ConfigureNavigation();

        _pageViewport.Dock = DockStyle.Fill;
        _pageViewport.Margin = Padding.Empty;
        _pageViewport.TabStop = true;
        _pageViewport.AccessibleName = "SettingsPageViewport";
        _pageViewport.MouseEnter += (_, _) => _pageViewport.Focus();
        _pageViewport.MouseWheel += OnPageMouseWheel;
        _pageViewport.Resize += (_, _) => RefreshPageScrollLayout();

        _pageCanvas.Margin = Padding.Empty;
        _pageCanvas.MouseEnter += (_, _) => _pageViewport.Focus();
        _pageCanvas.MouseWheel += OnPageMouseWheel;

        _pageScrollTrack.Width = ScaleLogical(10);
        _pageScrollTrack.Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
        _pageScrollTrack.Cursor = Cursors.Hand;
        _pageScrollTrack.Visible = false;
        _pageScrollTrack.Click += OnPageScrollTrackClick;

        _pageScrollThumb.Width = ScaleLogical(4);
        _pageScrollThumb.Left = ScaleLogical(3);
        _pageScrollThumb.Cursor = Cursors.Hand;
        _pageScrollThumb.MouseDown += OnPageScrollThumbMouseDown;
        _pageScrollThumb.MouseMove += OnPageScrollThumbMouseMove;
        _pageScrollThumb.MouseUp += OnPageScrollThumbMouseUp;

        _pageScrollTrack.Controls.Add(_pageScrollThumb);
        _pageViewport.Controls.Add(_pageCanvas);
        _pageViewport.Controls.Add(_pageScrollTrack);

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
        body.Controls.Add(_navigation, 0, 0);
        body.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 0);
        body.Controls.Add(_pageViewport, 2, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }

    private void ConfigureNavigation()
    {
        _navigation.Dock = DockStyle.Fill;
        _navigation.ColumnCount = 1;
        _navigation.RowCount = 6;
        _navigation.Margin = Padding.Empty;
        _navigation.Padding = Padding.Empty;
        _navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        ConfigureNavigationButton(_applicationPageButton, "Application", SettingsPage.Application);
        ConfigureNavigationButton(_devicesPageButton, "Devices", SettingsPage.Devices);
        ConfigureNavigationButton(_permissionsPageButton, "Permissions", SettingsPage.Permissions);
        ConfigureNavigationButton(_connectionPageButton, "Connection", SettingsPage.Connection);
        ConfigureNavigationButton(_appearancePageButton, "Appearance", SettingsPage.Appearance);

        _navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(LogicalNavButtonHeight)));
        _navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(LogicalNavButtonHeight)));
        _navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(LogicalNavButtonHeight)));
        _navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(LogicalNavButtonHeight)));
        _navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(LogicalNavButtonHeight)));
        _navigation.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _navigation.Controls.Add(_applicationPageButton, 0, 0);
        _navigation.Controls.Add(_devicesPageButton, 0, 1);
        _navigation.Controls.Add(_permissionsPageButton, 0, 2);
        _navigation.Controls.Add(_connectionPageButton, 0, 3);
        _navigation.Controls.Add(_appearancePageButton, 0, 4);
    }

    private void ConfigureNavigationButton(Button button, string text, SettingsPage page)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 0, 0, ScaleLogical(8));
        CommandButtonStyle.Configure(button);
        button.Click += (_, _) => SelectPage(page);
        _navigationButtons.Add(button);
    }

    private void SelectPage(SettingsPage page)
    {
        _activePage = page;
        _pageScrollOffset = 0;
        BuildActivePage();
        ApplyTheme();
    }

    private void BuildActivePage()
    {
        _pageCanvas.SuspendLayout();
        foreach (Control control in _pageCanvas.Controls.Cast<Control>().ToArray())
        {
            DetachReusableControls(control);
            _pageCanvas.Controls.Remove(control);
            control.Dispose();
        }

        var pageContent = CreatePageContent();
        switch (_activePage)
        {
            case SettingsPage.Application:
                BuildApplicationPage(pageContent);
                break;
            case SettingsPage.Devices:
                BuildDevicesPage(pageContent);
                break;
            case SettingsPage.Permissions:
                BuildPermissionsPage(pageContent);
                break;
            case SettingsPage.Connection:
                BuildConnectionPage(pageContent);
                break;
            case SettingsPage.Appearance:
                BuildAppearancePage(pageContent);
                break;
        }

        AttachPageScrollHandlers(pageContent);
        pageContent.Location = Point.Empty;
        _pageCanvas.Controls.Add(pageContent);
        _pageCanvas.ResumeLayout();
        RefreshPageScrollLayout();
    }

    private TableLayoutPanel CreatePageContent()
    {
        var pageContent = new TableLayoutPanel
        {
            Dock = DockStyle.None,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 0,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        pageContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return pageContent;
    }

    private void BuildApplicationPage(TableLayoutPanel pageContent)
    {
        AddPageHeader(pageContent, "Application", "Host startup and notification preferences.");
        AddSettingsCheckBox(pageContent, _startWithWindowsCheckBox, "Start Voltura Air when I sign in to Windows");
        AddSettingsCheckBox(pageContent, _showConnectionStatusNotificationsCheckBox, "Show connection status notifications");
        AddSettingsCheckBox(pageContent, _showPairingWindowOnDisconnectCheckBox, "Show pairing window on disconnect");
        AddPageFiller(pageContent);
    }

    private void BuildDevicesPage(TableLayoutPanel pageContent)
    {
        AddPageHeader(pageContent, "Devices", "Connected and paired devices.");
        _deviceManagerPanel.RefreshDevices();
        AddControlRow(pageContent, _deviceManagerPanel, SizeType.Absolute, ScaleLogical(LogicalDevicePanelHeight));
        AddPageFiller(pageContent);
    }

    private void BuildPermissionsPage(TableLayoutPanel pageContent)
    {
        AddPageHeader(pageContent, "Permissions", "Default rights for connected devices.");
        AddSettingsCheckBox(pageContent, _allowPcSleepCheckBox, "Allow PC sleep");
        AddSettingsCheckBox(pageContent, _allowVolumeControlCheckBox, "Allow volume control");
        AddPageFiller(pageContent);
    }

    private void BuildConnectionPage(TableLayoutPanel pageContent)
    {
        AddPageHeader(pageContent, "Connection", "Choose the local network address Voltura Air advertises to phones and tablets.");
        _connectionSettingsPanel.RefreshSettings();
        AddControlRow(pageContent, _connectionSettingsPanel, SizeType.Absolute, ScaleLogical(LogicalConnectionPanelHeight));
        AddPageFiller(pageContent);
    }

    private void BuildAppearancePage(TableLayoutPanel pageContent)
    {
        AddPageHeader(pageContent, "Appearance", "Theme preference.");
        var themeButtonHeight = ScaleLogical((int)Math.Round(CommandButtonStyle.ButtonHeight * 0.7));
        _themeOptions.Dock = DockStyle.Top;
        _themeOptions.Height = themeButtonHeight;
        _themeOptions.MinimumSize = new Size(0, themeButtonHeight);
        _themeOptions.ColumnCount = 3;
        _themeOptions.RowCount = 1;
        _themeOptions.Margin = new Padding(0, 0, 0, ScaleLogical(LogicalPageGap));
        _themeOptions.Padding = Padding.Empty;
        _themeOptions.ColumnStyles.Clear();
        _themeOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        _themeOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        _themeOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
        _themeOptions.Controls.Clear();

        ConfigureThemeButton(_systemThemeButton, "System", AppThemeMode.System, isFirst: true, isLast: false);
        ConfigureThemeButton(_lightThemeButton, "Light", AppThemeMode.Light, isFirst: false, isLast: false);
        ConfigureThemeButton(_darkThemeButton, "Dark", AppThemeMode.Dark, isFirst: false, isLast: true);
        _themeOptions.Controls.Add(_systemThemeButton, 0, 0);
        _themeOptions.Controls.Add(_lightThemeButton, 1, 0);
        _themeOptions.Controls.Add(_darkThemeButton, 2, 0);

        AddControlRow(pageContent, _themeOptions, SizeType.Absolute, themeButtonHeight);
        AddPageFiller(pageContent);
    }

    private void AddPageHeader(TableLayoutPanel pageContent, string title, string subtitle)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, ScaleLogical(18)),
            Padding = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Margin = Padding.Empty
        };

        var subtitleLabel = new Label
        {
            Text = subtitle,
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 9.5f),
            Margin = new Padding(0, ScaleLogical(4), 0, 0)
        };

        header.Controls.Add(titleLabel, 0, 0);
        header.Controls.Add(subtitleLabel, 0, 1);
        AddControlRow(pageContent, header, SizeType.AutoSize, 0);
    }

    private void AddSettingsCheckBox(TableLayoutPanel pageContent, ThemedCheckBox checkBox, string text)
    {
        checkBox.Text = text;
        checkBox.Dock = DockStyle.Fill;
        checkBox.Margin = Padding.Empty;
        checkBox.Padding = new Padding(ScaleLogical(2), 0, 0, 0);
        var preferredHeight = Math.Max(ScaleLogical(56), checkBox.GetPreferredSize(Size.Empty).Height);
        var row = new Panel
        {
            Dock = DockStyle.Top,
            Height = preferredHeight,
            MinimumSize = new Size(0, preferredHeight),
            Margin = new Padding(0, 0, 0, ScaleLogical(LogicalPageGap)),
            Padding = new Padding(ScaleLogical(14), ScaleLogical(8), ScaleLogical(14), ScaleLogical(8))
        };
        row.Controls.Add(checkBox);
        AddControlRow(pageContent, row, SizeType.Absolute, preferredHeight + ScaleLogical(LogicalPageGap));
    }

    private void AddPageFiller(TableLayoutPanel pageContent)
    {
    }

    private static void AddControlRow(TableLayoutPanel pageContent, Control control, SizeType sizeType, float height)
    {
        var row = pageContent.RowCount;
        pageContent.RowStyles.Add(new RowStyle(sizeType, height));
        pageContent.RowCount++;
        pageContent.Controls.Add(control, 0, row);
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
        button.Click -= OnThemeButtonClick;
        button.Tag = mode;
        button.Click += OnThemeButtonClick;
    }

    private void WireSettingsAutosave()
    {
        _checkBoxes.AddRange(new[]
        {
            _startWithWindowsCheckBox,
            _showConnectionStatusNotificationsCheckBox,
            _showPairingWindowOnDisconnectCheckBox,
            _allowPcSleepCheckBox,
            _allowVolumeControlCheckBox
        });

        _startWithWindowsCheckBox.CheckedChanged += (_, _) =>
        {
            if (!_isLoading)
            {
                AppStartupSettings.SetEnabled(_startWithWindowsCheckBox.Checked);
            }
        };
        _showConnectionStatusNotificationsCheckBox.CheckedChanged += (_, _) =>
        {
            if (!_isLoading)
            {
                AppNotificationSettings.SetShowConnectionStatusNotifications(_showConnectionStatusNotificationsCheckBox.Checked);
            }
        };
        _showPairingWindowOnDisconnectCheckBox.CheckedChanged += (_, _) =>
        {
            if (!_isLoading)
            {
                AppNotificationSettings.SetShowPairingWindowOnDisconnect(_showPairingWindowOnDisconnectCheckBox.Checked);
            }
        };
        _allowPcSleepCheckBox.CheckedChanged += (_, _) => SaveGlobalPermissions();
        _allowVolumeControlCheckBox.CheckedChanged += (_, _) => SaveGlobalPermissions();
    }

    private void LoadSettings()
    {
        _isLoading = true;
        _startWithWindowsCheckBox.Checked = AppStartupSettings.IsEnabled();
        _showConnectionStatusNotificationsCheckBox.Checked = AppNotificationSettings.ShowConnectionStatusNotifications();
        _showPairingWindowOnDisconnectCheckBox.Checked = AppNotificationSettings.ShowPairingWindowOnDisconnect();

        var permissions = AppPermissionSettings.Load();
        _allowPcSleepCheckBox.Checked = permissions.AllowPcSleep;
        _allowVolumeControlCheckBox.Checked = permissions.AllowVolumeControl;
        _deviceManagerPanel.RefreshDevices();
        _connectionSettingsPanel.RefreshSettings();
        UpdateThemeSelection(AppThemeSettings.GetMode());
        _isLoading = false;
    }

    private void SaveGlobalPermissions()
    {
        if (_isLoading)
        {
            return;
        }

        AppPermissionSettings.Save(new HostPermissionSet(
            AllowPcSleep: _allowPcSleepCheckBox.Checked,
            AllowVolumeControl: _allowVolumeControlCheckBox.Checked));
    }

    private void OnThemeButtonClick(object? sender, EventArgs e)
    {
        if (sender is Button { Tag: AppThemeMode mode })
        {
            AppThemeSettings.SetMode(mode);
            UpdateThemeSelection(mode);
        }
    }

    private void OnDevicePermissionsRequested(object? sender, DevicePermissionsRequestedEventArgs e)
    {
        DevicePermissionsRequested?.Invoke(this, e);
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

    private void ApplyTheme()
    {
        _theme = WindowsTheme.Current();
        WindowsTheme.ApplyImmersiveDarkMode(this, _theme.IsDark);

        BackColor = _theme.Window;
        ForeColor = _theme.Text;
        _titleLabel.ForeColor = _theme.Text;
        _subtitleLabel.ForeColor = _theme.MutedText;
        _navigation.BackColor = _theme.Window;
        _pageViewport.BackColor = _theme.Window;
        _themeOptions.BackColor = _theme.Window;

        foreach (var control in FindDescendants(this))
        {
            switch (control)
            {
                case Label label:
                    label.BackColor = label.Parent is Panel labelPanel && labelPanel.Controls.OfType<ThemedCheckBox>().Any()
                        ? _theme.Surface
                        : _theme.Window;
                    label.ForeColor = _theme.Text;
                    break;
                case Panel surfacePanel:
                    surfacePanel.BackColor = surfacePanel.Controls.OfType<ThemedCheckBox>().Any()
                        ? _theme.Surface
                        : _theme.Window;
                    break;
            }
        }

        foreach (var checkBox in _checkBoxes)
        {
            checkBox.ApplyTheme(_theme);
        }

        _deviceManagerPanel.ApplyTheme(_theme);
        _connectionSettingsPanel.ApplyTheme(_theme);
        _pageCanvas.BackColor = _theme.Window;
        _pageScrollTrack.BackColor = _theme.Window;
        _pageScrollThumb.BackColor = _theme.MutedText;
        ApplyNavigationTheme();
        UpdateThemeSelection(AppThemeSettings.GetMode());
        CommandButtonStyle.ApplyTheme(_closeButton, _theme, CommandButtonKind.Normal);
        RefreshPageScrollLayout();
    }

    private void ApplyNavigationTheme()
    {
        ApplyNavigationButtonTheme(_applicationPageButton, SettingsPage.Application);
        ApplyNavigationButtonTheme(_devicesPageButton, SettingsPage.Devices);
        ApplyNavigationButtonTheme(_permissionsPageButton, SettingsPage.Permissions);
        ApplyNavigationButtonTheme(_connectionPageButton, SettingsPage.Connection);
        ApplyNavigationButtonTheme(_appearancePageButton, SettingsPage.Appearance);
    }

    private void ApplyNavigationButtonTheme(Button button, SettingsPage page)
    {
        var selected = _activePage == page;
        CommandButtonStyle.ApplyTheme(button, _theme, selected ? CommandButtonKind.Primary : CommandButtonKind.Normal);
        button.Font = new Font("Segoe UI", 9f, selected ? FontStyle.Bold : FontStyle.Regular);
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

    private void RefreshPageScrollLayout()
    {
        if (_pageCanvas.Controls.Count == 0 || _pageViewport.ClientSize.Width <= 0 || _pageViewport.ClientSize.Height <= 0)
        {
            return;
        }

        var content = _pageCanvas.Controls[0];
        var fullContentWidth = GetPageContentWidth(scrollbarVisible: false);
        ApplyPageContentSize(content, fullContentWidth);

        var needsScrollbar = content.Height > _pageViewport.ClientSize.Height;
        var contentWidth = GetPageContentWidth(needsScrollbar);
        ApplyPageContentSize(content, contentWidth);

        var maxOffset = Math.Max(0, content.Height - _pageViewport.ClientSize.Height);
        _pageScrollOffset = Math.Clamp(_pageScrollOffset, 0, maxOffset);
        _pageCanvas.SetBounds(0, -_pageScrollOffset, content.Width, content.Height);
        _pageScrollTrack.Visible = needsScrollbar;

        if (!needsScrollbar)
        {
            return;
        }

        var trackPadding = ScaleLogical(4);
        _pageScrollTrack.SetBounds(
            _pageViewport.ClientSize.Width - _pageScrollTrack.Width,
            0,
            _pageScrollTrack.Width,
            _pageViewport.ClientSize.Height);

        var availableTrackHeight = Math.Max(1, _pageScrollTrack.Height - trackPadding * 2);
        var contentHeight = Math.Max(1, _pageCanvas.Height);
        var thumbHeight = Math.Max(ScaleLogical(48), availableTrackHeight * _pageViewport.ClientSize.Height / contentHeight);
        thumbHeight = Math.Min(availableTrackHeight, thumbHeight);

        var travel = Math.Max(1, availableTrackHeight - thumbHeight);
        var thumbTop = trackPadding + (_pageScrollOffset * travel / Math.Max(1, maxOffset));
        _pageScrollThumb.SetBounds(ScaleLogical(3), thumbTop, ScaleLogical(4), thumbHeight);
        _pageScrollTrack.BringToFront();
    }

    private int GetPageContentWidth(bool scrollbarVisible)
    {
        var scrollbarGutter = scrollbarVisible ? _pageScrollTrack.Width + ScaleLogical(16) : 0;
        return Math.Max(1, _pageViewport.ClientSize.Width - scrollbarGutter);
    }

    private static void ApplyPageContentSize(Control content, int width)
    {
        content.MinimumSize = new Size(width, 0);
        content.Width = width;
        var preferredHeight = content.GetPreferredSize(new Size(width, 0)).Height;
        content.Height = Math.Max(1, preferredHeight);
        content.PerformLayout();
    }

    private int GetMaxPageScrollOffset()
    {
        return Math.Max(0, _pageCanvas.Height - _pageViewport.ClientSize.Height);
    }

    private void ScrollPageBy(int delta)
    {
        _pageScrollOffset = Math.Clamp(_pageScrollOffset + delta, 0, GetMaxPageScrollOffset());
        RefreshPageScrollLayout();
    }

    private void OnPageMouseWheel(object? sender, MouseEventArgs e)
    {
        ScrollPageBy(-e.Delta);
    }

    private void OnPageScrollTrackClick(object? sender, EventArgs e)
    {
        var cursorY = _pageScrollTrack.PointToClient(Cursor.Position).Y;
        var direction = cursorY < _pageScrollThumb.Top ? -1 : 1;
        ScrollPageBy(direction * Math.Max(ScaleLogical(72), _pageViewport.ClientSize.Height / 2));
    }

    private void OnPageScrollThumbMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _isDraggingPageScrollThumb = true;
        _pageScrollDragStartY = _pageScrollTrack.PointToClient(Cursor.Position).Y;
        _pageScrollDragStartOffset = _pageScrollOffset;
        _pageScrollThumb.Capture = true;
    }

    private void OnPageScrollThumbMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isDraggingPageScrollThumb)
        {
            return;
        }

        var trackPadding = ScaleLogical(4);
        var availableTrackHeight = Math.Max(1, _pageScrollTrack.Height - trackPadding * 2);
        var travel = Math.Max(1, availableTrackHeight - _pageScrollThumb.Height);
        var currentY = _pageScrollTrack.PointToClient(Cursor.Position).Y;
        var delta = currentY - _pageScrollDragStartY;
        _pageScrollOffset = Math.Clamp(_pageScrollDragStartOffset + delta * GetMaxPageScrollOffset() / travel, 0, GetMaxPageScrollOffset());
        RefreshPageScrollLayout();
    }

    private void OnPageScrollThumbMouseUp(object? sender, MouseEventArgs e)
    {
        _isDraggingPageScrollThumb = false;
        _pageScrollThumb.Capture = false;
    }

    private void AttachPageScrollHandlers(Control control)
    {
        control.MouseEnter += (_, _) => _pageViewport.Focus();
        control.MouseWheel += OnPageMouseWheel;

        foreach (Control child in control.Controls)
        {
            AttachPageScrollHandlers(child);
        }
    }

    private void DetachReusableControls(Control root)
    {
        foreach (Control child in root.Controls.Cast<Control>().ToArray())
        {
            if (IsReusableControl(child))
            {
                root.Controls.Remove(child);
                continue;
            }

            DetachReusableControls(child);
        }
    }

    private bool IsReusableControl(Control control)
    {
        return ReferenceEquals(control, _startWithWindowsCheckBox) ||
            ReferenceEquals(control, _showConnectionStatusNotificationsCheckBox) ||
            ReferenceEquals(control, _showPairingWindowOnDisconnectCheckBox) ||
            ReferenceEquals(control, _allowPcSleepCheckBox) ||
            ReferenceEquals(control, _allowVolumeControlCheckBox) ||
            ReferenceEquals(control, _themeOptions) ||
            ReferenceEquals(control, _systemThemeButton) ||
            ReferenceEquals(control, _lightThemeButton) ||
            ReferenceEquals(control, _darkThemeButton) ||
            ReferenceEquals(control, _deviceManagerPanel) ||
            ReferenceEquals(control, _connectionSettingsPanel);
    }

    private void FocusDefaultControl()
    {
        var activeButton = _activePage switch
        {
            SettingsPage.Application => _applicationPageButton,
            SettingsPage.Devices => _devicesPageButton,
            SettingsPage.Permissions => _permissionsPageButton,
            SettingsPage.Connection => _connectionPageButton,
            SettingsPage.Appearance => _appearancePageButton,
            _ => _applicationPageButton
        };
        activeButton.Select();
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

    private int ScaleLogical(int value)
    {
        return LogicalToDeviceUnits(value);
    }
}
