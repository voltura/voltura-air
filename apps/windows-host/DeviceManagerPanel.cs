using System.Drawing;

namespace VolturaAir.Host;

public sealed class DeviceManagerPanel : UserControl
{
    private readonly PairingManager _pairingManager;
    private readonly Icon _appIcon;
    private readonly Panel _devicesViewport = new();
    private readonly FlowLayoutPanel _devicesList = new();
    private readonly Panel _scrollTrack = new();
    private readonly Panel _scrollThumb = new();
    private readonly Label _emptyLabel = new();
    private readonly Button _cleanupDuplicatesButton = new();
    private readonly Button _disconnectAllButton = new();
    private readonly TableLayoutPanel _actions = new();
    private readonly List<Button> _buttons = new();
    private readonly Action? _onDisconnectAllCallback;
    private ThemePalette _theme = WindowsTheme.Current();
    private int _scrollOffset;
    private bool _isDraggingScrollThumb;
    private int _scrollDragStartY;
    private int _scrollDragStartOffset;

    public DeviceManagerPanel(PairingManager pairingManager, Icon appIcon, Action? onDisconnectAllCallback = null)
    {
        _pairingManager = pairingManager;
        _appIcon = appIcon;
        _onDisconnectAllCallback = onDisconnectAllCallback;

        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        Padding = Padding.Empty;

        BuildLayout();
        ApplyTheme(_theme);
        RefreshDevices();

        _pairingManager.ConnectionChanged += OnConnectionChanged;
    }

    public event EventHandler<DevicePermissionsRequestedEventArgs>? DevicePermissionsRequested;

    public event EventHandler? CloseRequested;

    public void RefreshDevices()
    {
        _devicesList.SuspendLayout();
        foreach (Control control in _devicesList.Controls.Cast<Control>().ToArray())
        {
            _devicesList.Controls.Remove(control);
            if (control != _emptyLabel)
            {
                control.Dispose();
            }
        }

        var devices = _pairingManager.GetDevices();
        var duplicateCleanupCandidates = _pairingManager.GetDuplicateCleanupCandidates();
        if (devices.Count == 0)
        {
            _emptyLabel.ForeColor = _theme.MutedText;
            _emptyLabel.Width = GetDeviceRowWidth();
            _devicesList.Controls.Add(_emptyLabel);
        }
        else
        {
            foreach (var device in devices)
            {
                _devicesList.Controls.Add(CreateDeviceRow(device));
            }
        }

        RefreshActionButtons(devices.Count > 0, duplicateCleanupCandidates.Count > 0);
        _devicesList.ResumeLayout();
        RefreshScrollLayout();
    }

    public void ApplyTheme(ThemePalette theme)
    {
        _theme = theme;

        BackColor = theme.Window;
        ForeColor = theme.Text;
        _devicesViewport.BackColor = theme.Window;
        _devicesList.BackColor = theme.Window;
        _scrollTrack.BackColor = theme.Window;
        _scrollThumb.BackColor = theme.MutedText;
        _actions.BackColor = theme.Window;

        foreach (var button in _buttons)
        {
            CommandButtonStyle.ApplyTheme(button, theme, CommandButtonKind.Danger);
        }

        RefreshDevices();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pairingManager.ConnectionChanged -= OnConnectionChanged;
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(CommandButtonStyle.ActionRowHeight)));

        _devicesViewport.Dock = DockStyle.Fill;
        _devicesViewport.Margin = Padding.Empty;
        _devicesViewport.TabStop = true;
        _devicesViewport.MouseEnter += (_, _) => _devicesViewport.Focus();
        _devicesViewport.MouseWheel += OnDevicesMouseWheel;
        _devicesViewport.Resize += (_, _) => RefreshScrollLayout();

        _devicesList.AutoSize = true;
        _devicesList.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _devicesList.FlowDirection = FlowDirection.TopDown;
        _devicesList.WrapContents = false;
        _devicesList.Margin = Padding.Empty;
        _devicesList.MouseEnter += (_, _) => _devicesViewport.Focus();
        _devicesList.MouseWheel += OnDevicesMouseWheel;

        _scrollTrack.Width = ScaleLogical(10);
        _scrollTrack.Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
        _scrollTrack.Cursor = Cursors.Hand;
        _scrollTrack.Visible = false;
        _scrollTrack.Click += OnScrollTrackClick;

        _scrollThumb.Width = ScaleLogical(4);
        _scrollThumb.Left = ScaleLogical(3);
        _scrollThumb.Cursor = Cursors.Hand;
        _scrollThumb.MouseDown += OnScrollThumbMouseDown;
        _scrollThumb.MouseMove += OnScrollThumbMouseMove;
        _scrollThumb.MouseUp += OnScrollThumbMouseUp;

        _scrollTrack.Controls.Add(_scrollThumb);
        _devicesViewport.Controls.Add(_devicesList);
        _devicesViewport.Controls.Add(_scrollTrack);

        _emptyLabel.Text = "No paired devices.";
        _emptyLabel.Width = GetDeviceRowWidth();
        _emptyLabel.Height = ScaleLogical(44);
        _emptyLabel.TextAlign = ContentAlignment.MiddleLeft;
        _emptyLabel.Font = new Font("Segoe UI", 9.5f);

        _actions.Dock = DockStyle.Fill;
        _actions.ColumnCount = 3;
        _actions.RowCount = 1;
        _actions.Padding = new Padding(0, ScaleLogical(CommandButtonStyle.ActionTopPadding), 0, 0);
        _actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _cleanupDuplicatesButton.Text = "Clean up duplicates";
        _cleanupDuplicatesButton.Click += (_, _) => CleanUpDuplicates();

        _disconnectAllButton.Text = "Disconnect all";
        _disconnectAllButton.Click += (_, _) => DisconnectAll();

        _buttons.AddRange(new[] { _cleanupDuplicatesButton, _disconnectAllButton });
        foreach (var button in _buttons)
        {
            button.Dock = DockStyle.Fill;
            button.Margin = Padding.Empty;
            CommandButtonStyle.Configure(button);
        }

        _actions.Controls.Add(_cleanupDuplicatesButton, 0, 0);
        _actions.Controls.Add(_disconnectAllButton, 2, 0);

        content.Controls.Add(_devicesViewport, 0, 0);
        content.Controls.Add(_actions, 0, 1);
        Controls.Add(content);
    }

    private Control CreateDeviceRow(PairedDeviceStatus device)
    {
        var row = new TableLayoutPanel
        {
            Width = GetDeviceRowWidth(),
            Height = ScaleLogical(108),
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, ScaleLogical(8)),
            Padding = new Padding(ScaleLogical(14), ScaleLogical(8), ScaleLogical(12), ScaleLogical(8))
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(20)));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(170)));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(200)));

        var statusMarker = new Panel
        {
            Width = ScaleLogical(10),
            Height = ScaleLogical(10),
            Margin = new Padding(0, ScaleLogical(38), ScaleLogical(8), 0),
            BackColor = device.IsActive ? _theme.Accent : _theme.MutedText
        };

        var deviceText = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(ScaleLogical(6), 0, ScaleLogical(14), 0)
        };
        deviceText.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));
        deviceText.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));
        deviceText.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));

        var nameLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = device.DeviceName,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            AutoEllipsis = true
        };

        var activityLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = GetDeviceActivityText(device),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9f),
            AutoEllipsis = true
        };

        var metadataLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = GetDeviceMetadataText(device),
            TextAlign = ContentAlignment.TopLeft,
            Font = new Font("Segoe UI", 8.5f),
            AutoEllipsis = true
        };

        var permissionsButton = new Button
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = ScaleLogical(CommandButtonStyle.ButtonHeight),
            Text = "Permissions",
            Margin = new Padding(ScaleLogical(8), 0, 0, 0)
        };
        CommandButtonStyle.Configure(permissionsButton);
        permissionsButton.Click += (_, _) => DevicePermissionsRequested?.Invoke(
            this,
            new DevicePermissionsRequestedEventArgs(device.ClientId, device.DeviceName));

        var actionButton = new Button
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = ScaleLogical(CommandButtonStyle.ButtonHeight),
            Text = device.IsActive ? "Disconnect" : "Remove",
            Margin = new Padding(ScaleLogical(8), 0, 0, 0)
        };
        CommandButtonStyle.Configure(actionButton);
        actionButton.Click += (_, _) => DisconnectDevice(device.ClientId);

        deviceText.Controls.Add(nameLabel, 0, 0);
        deviceText.Controls.Add(activityLabel, 0, 1);
        deviceText.Controls.Add(metadataLabel, 0, 2);
        row.Controls.Add(statusMarker, 0, 0);
        row.Controls.Add(deviceText, 1, 0);
        row.Controls.Add(permissionsButton, 2, 0);
        row.Controls.Add(actionButton, 3, 0);
        AttachDeviceScrollHandlers(row);
        ApplyRowTheme(row, deviceText, nameLabel, activityLabel, metadataLabel, permissionsButton, actionButton);
        return row;
    }

    private static string GetDeviceActivityText(PairedDeviceStatus device)
    {
        if (device.LastRenamedAt is not null &&
            device.LastRenamedAt >= device.AddedAt &&
            device.LastRenamedAt >= (device.LastConnectedAt ?? DateTimeOffset.MinValue) &&
            device.LastRenamedAt >= (device.LastDisconnectedAt ?? DateTimeOffset.MinValue))
        {
            return device.IsActive
                ? $"Connected - renamed {FormatDeviceTime(device.LastRenamedAt.Value)}"
                : $"Renamed {FormatDeviceTime(device.LastRenamedAt.Value)}";
        }

        if (device.IsActive)
        {
            return $"Connected since {FormatDeviceTime(device.LastConnectedAt ?? device.LatestActivityAt)}";
        }

        if (device.LastDisconnectedAt is not null && device.LastDisconnectedAt >= (device.LastConnectedAt ?? DateTimeOffset.MinValue))
        {
            return $"Disconnected {FormatDeviceTime(device.LastDisconnectedAt.Value)}";
        }

        if (device.LastConnectedAt is not null)
        {
            return $"Last connected {FormatDeviceTime(device.LastConnectedAt.Value)}";
        }

        return $"Added {FormatDeviceTime(device.AddedAt)}";
    }

    private static string GetDeviceMetadataText(PairedDeviceStatus device)
    {
        var displayMode = device.DisplayMode.Equals("installed", StringComparison.OrdinalIgnoreCase)
            ? "Installed app"
            : device.DisplayMode.Equals("browser", StringComparison.OrdinalIgnoreCase)
                ? "Browser"
                : string.Empty;
        var parts = new[] { device.Platform, device.Browser, displayMode }
            .Where(value => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return string.Join(" / ", parts);
    }

    private static string FormatDeviceTime(DateTimeOffset timestamp)
    {
        return timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    private int GetDeviceRowWidth()
    {
        var scrollbarGutter = _scrollTrack.Visible ? _scrollTrack.Width + ScaleLogical(16) : 0;
        return Math.Max(1, _devicesViewport.ClientSize.Width - scrollbarGutter);
    }

    private void RefreshScrollLayout()
    {
        if (_devicesViewport.ClientSize.Width <= 0 || _devicesViewport.ClientSize.Height <= 0)
        {
            return;
        }

        _devicesList.PerformLayout();
        _scrollTrack.Visible = _devicesList.Height > _devicesViewport.ClientSize.Height;

        foreach (Control control in _devicesList.Controls)
        {
            control.Width = GetDeviceRowWidth();
        }

        _devicesList.Width = GetDeviceRowWidth();
        _devicesList.PerformLayout();
        var maxOffset = GetMaxScrollOffset();
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxOffset);
        _devicesList.Location = new Point(0, -_scrollOffset);

        if (!_scrollTrack.Visible)
        {
            return;
        }

        var trackPadding = ScaleLogical(4);
        _scrollTrack.SetBounds(
            _devicesViewport.ClientSize.Width - _scrollTrack.Width,
            0,
            _scrollTrack.Width,
            _devicesViewport.ClientSize.Height);

        var availableTrackHeight = Math.Max(1, _scrollTrack.Height - trackPadding * 2);
        var contentHeight = Math.Max(1, _devicesList.Height);
        var thumbHeight = Math.Max(ScaleLogical(48), availableTrackHeight * _devicesViewport.ClientSize.Height / contentHeight);
        thumbHeight = Math.Min(availableTrackHeight, thumbHeight);

        var travel = Math.Max(1, availableTrackHeight - thumbHeight);
        var thumbTop = trackPadding + (_scrollOffset * travel / Math.Max(1, maxOffset));
        _scrollThumb.SetBounds(ScaleLogical(3), thumbTop, ScaleLogical(4), thumbHeight);
        _scrollTrack.BringToFront();
    }

    private void AttachDeviceScrollHandlers(Control control)
    {
        control.MouseEnter += (_, _) => _devicesViewport.Focus();
        control.MouseWheel += OnDevicesMouseWheel;

        foreach (Control child in control.Controls)
        {
            AttachDeviceScrollHandlers(child);
        }
    }

    private int GetMaxScrollOffset()
    {
        return Math.Max(0, _devicesList.Height - _devicesViewport.ClientSize.Height);
    }

    private void ScrollDevicesBy(int delta)
    {
        _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, GetMaxScrollOffset());
        RefreshScrollLayout();
    }

    private void OnDevicesMouseWheel(object? sender, MouseEventArgs e)
    {
        ScrollDevicesBy(-e.Delta);
    }

    private void OnScrollTrackClick(object? sender, EventArgs e)
    {
        var cursorY = _scrollTrack.PointToClient(Cursor.Position).Y;
        var direction = cursorY < _scrollThumb.Top ? -1 : 1;
        ScrollDevicesBy(direction * Math.Max(ScaleLogical(72), _devicesViewport.ClientSize.Height / 2));
    }

    private void OnScrollThumbMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _isDraggingScrollThumb = true;
        _scrollDragStartY = _scrollTrack.PointToClient(Cursor.Position).Y;
        _scrollDragStartOffset = _scrollOffset;
        _scrollThumb.Capture = true;
    }

    private void OnScrollThumbMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isDraggingScrollThumb)
        {
            return;
        }

        var trackPadding = ScaleLogical(4);
        var availableTrackHeight = Math.Max(1, _scrollTrack.Height - trackPadding * 2);
        var travel = Math.Max(1, availableTrackHeight - _scrollThumb.Height);
        var currentY = _scrollTrack.PointToClient(Cursor.Position).Y;
        var delta = currentY - _scrollDragStartY;
        _scrollOffset = Math.Clamp(_scrollDragStartOffset + delta * GetMaxScrollOffset() / travel, 0, GetMaxScrollOffset());
        RefreshScrollLayout();
    }

    private void OnScrollThumbMouseUp(object? sender, MouseEventArgs e)
    {
        _isDraggingScrollThumb = false;
        _scrollThumb.Capture = false;
    }

    private void RefreshActionButtons(bool hasDevices, bool hasDuplicateCleanupCandidates)
    {
        var showCleanup = hasDevices && hasDuplicateCleanupCandidates;
        var showDisconnectAll = hasDevices;
        var buttonGap = ScaleLogical(CommandButtonStyle.ButtonGap);

        _cleanupDuplicatesButton.Visible = showCleanup;
        _disconnectAllButton.Visible = showDisconnectAll;
        _cleanupDuplicatesButton.Margin = Padding.Empty;
        _disconnectAllButton.Margin = Padding.Empty;

        _actions.ColumnStyles.Clear();
        if (!showDisconnectAll)
        {
            _actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0f));
            _actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 0f));
        }
        else if (showCleanup)
        {
            _actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            _actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, buttonGap));
            _actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        }
        else
        {
            _actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 0f));
            _actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0f));
            _actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        }
    }

    private void DisconnectDevice(string clientId)
    {
        if (_pairingManager.DisconnectDevice(clientId))
        {
            RefreshDevices();
            if (_pairingManager.GetDevices().Count == 0)
            {
                _onDisconnectAllCallback?.Invoke();
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void CleanUpDuplicates()
    {
        var candidates = _pairingManager.GetDuplicateCleanupCandidates();
        if (candidates.Count == 0)
        {
            RefreshDevices();
            return;
        }

        if (!ConfirmDuplicateCleanup(candidates))
        {
            return;
        }

        _pairingManager.CleanUpDuplicateDevices();
        RefreshDevices();
    }

    private bool ConfirmDuplicateCleanup(IReadOnlyList<PairedDeviceStatus> candidates)
    {
        var dialogSize = new Size(1040, 760);
        using var dialog = new Form
        {
            Text = "Clean up duplicates",
            Icon = _appIcon,
            AutoScaleMode = AutoScaleMode.Dpi,
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
            Size = dialogSize,
            MinimumSize = dialogSize,
            BackColor = _theme.Window,
            ForeColor = _theme.Text
        };

        var titleLabel = new Label();
        var subtitleLabel = new Label();
        var root = DialogLayout.CreateRoot(rowCount: 4);
        root.BackColor = _theme.Window;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(CommandButtonStyle.ActionRowHeight)));

        var header = DialogLayout.CreateHeader(
            titleLabel,
            subtitleLabel,
            "Clean up duplicates?",
            "Older disconnected pairings with duplicate names will be removed.",
            bottomMargin: ScaleLogical(18));
        header.BackColor = _theme.Window;
        titleLabel.ForeColor = _theme.Text;
        subtitleLabel.ForeColor = _theme.MutedText;

        var names = candidates.Select(device => device.DeviceName).Distinct(StringComparer.CurrentCultureIgnoreCase).Take(4).ToArray();
        var body = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = _theme.MutedText,
            BackColor = _theme.Window,
            MaximumSize = new Size(ScaleLogical(900), 0),
            TextAlign = ContentAlignment.TopLeft,
            Text = $"This will remove {candidates.Count} older disconnected duplicate pairing{(candidates.Count == 1 ? string.Empty : "s")}. Connected devices are kept. For each duplicate name, the connected device or most recent activity is kept.\r\n\r\nDevice name{(names.Length == 1 ? string.Empty : "s")}: {string.Join(", ", names)}"
        };

        var spacer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _theme.Window
        };

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(0, ScaleLogical(CommandButtonStyle.ActionTopPadding), 0, 0),
            BackColor = _theme.Window
        };
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(CommandButtonStyle.ButtonGap)));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var removeButton = new Button { Text = "Remove duplicates", Dock = DockStyle.Fill, Margin = Padding.Empty, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", Dock = DockStyle.Fill, Margin = Padding.Empty, DialogResult = DialogResult.Cancel };
        CommandButtonStyle.Configure(removeButton);
        CommandButtonStyle.Configure(cancelButton);
        CommandButtonStyle.ApplyTheme(removeButton, _theme, CommandButtonKind.Danger);
        CommandButtonStyle.ApplyTheme(cancelButton, _theme, CommandButtonKind.Normal);

        actions.Controls.Add(removeButton, 0, 0);
        actions.Controls.Add(cancelButton, 2, 0);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(spacer, 0, 2);
        root.Controls.Add(actions, 0, 3);
        dialog.Controls.Add(root);
        dialog.AcceptButton = removeButton;
        dialog.CancelButton = cancelButton;

        using var chrome = ThemedWindowChrome.Install(dialog, _appIcon, canMaximize: false, canMinimize: false);
        var owner = FindForm();
        return owner is null
            ? dialog.ShowDialog() == DialogResult.OK
            : dialog.ShowDialog(owner) == DialogResult.OK;
    }

    private void DisconnectAll()
    {
        _pairingManager.ClearPairing();
        RefreshDevices();
        _onDisconnectAllCallback?.Invoke();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(RefreshDevices);
    }

    private void ApplyRowTheme(TableLayoutPanel row, TableLayoutPanel deviceText, Label nameLabel, Label activityLabel, Label metadataLabel, Button permissionsButton, Button actionButton)
    {
        row.BackColor = _theme.Surface;
        deviceText.BackColor = _theme.Surface;
        nameLabel.BackColor = _theme.Surface;
        nameLabel.ForeColor = _theme.Text;
        activityLabel.BackColor = _theme.Surface;
        activityLabel.ForeColor = _theme.MutedText;
        metadataLabel.BackColor = _theme.Surface;
        metadataLabel.ForeColor = _theme.MutedText;

        CommandButtonStyle.ApplyTheme(permissionsButton, _theme, CommandButtonKind.Normal);
        CommandButtonStyle.ApplyTheme(actionButton, _theme, CommandButtonKind.Danger);
    }

    private int ScaleLogical(int value)
    {
        return LogicalToDeviceUnits(value);
    }
}