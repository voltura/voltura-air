using System.ComponentModel;
using System.Drawing;
using System.Globalization;

namespace VolturaAir.Host;

public sealed class ConnectionSettingsPanel : UserControl
{
    private readonly WebHostService _webHost;
    private readonly PairingForm _pairingForm;
    private readonly Label _currentUrlLabel = new();
    private readonly Label _currentUrlValueLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _networkLabel = new();
    private readonly Label _portLabel = new();
    private readonly Label _manualPortLabel = new();
    private readonly Label _manualPortWarningLabel = new();
    private readonly Label _restartNoteLabel = new();
    private readonly Button _networkAutomaticButton = new();
    private readonly Button _networkManualButton = new();
    private readonly Button _portAutomaticButton = new();
    private readonly Button _portManualButton = new();
    private readonly ThemedCandidateListBox _candidateList = new();
    private readonly ThemedTextBox _manualPortTextBox = new();
    private readonly Button _saveButton = new();
    private readonly System.Windows.Forms.Timer _manualPortWarningTimer = new() { Interval = 2000 };
    private ThemePalette _theme = WindowsTheme.Current();
    private NetworkSelectionMode _networkMode;
    private PortSelectionMode _portMode;

    public ConnectionSettingsPanel(WebHostService webHost, PairingForm pairingForm)
    {
        _webHost = webHost;
        _pairingForm = pairingForm;

        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        BuildLayout();
        ApplyTheme(_theme);
        RefreshSettings();

        _manualPortWarningTimer.Tick += OnManualPortWarningTimerTick;
    }

    public void RefreshSettings()
    {
        LoadSettings();
    }

    public void ApplyTheme(ThemePalette theme)
    {
        _theme = theme;

        BackColor = theme.Window;
        ForeColor = theme.Text;
        _currentUrlLabel.ForeColor = theme.MutedText;
        _currentUrlValueLabel.ForeColor = theme.Text;
        _currentUrlValueLabel.BackColor = theme.Window;
        _statusLabel.BackColor = theme.Window;
        _networkLabel.ForeColor = theme.MutedText;
        _portLabel.ForeColor = theme.MutedText;
        _manualPortLabel.ForeColor = _portMode == PortSelectionMode.Manual ? theme.Text : theme.MutedText;
        _manualPortWarningLabel.ForeColor = theme.Danger;
        _restartNoteLabel.ForeColor = theme.MutedText;
        _manualPortTextBox.ApplyTheme(theme);
        _candidateList.ApplyTheme(theme);

        foreach (Control control in Controls)
        {
            control.BackColor = theme.Window;
        }

        UpdateModeButtons();
        CommandButtonStyle.ApplyTheme(_saveButton, theme, CommandButtonKind.Primary);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _manualPortWarningTimer.Tick -= OnManualPortWarningTimerTick;
            _manualPortWarningTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty,
            Padding = new Padding(0, ScaleLogical(4), 0, 0)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(44)));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(CommandButtonStyle.ActionRowHeight)));

        ConfigureSectionLabel(_currentUrlLabel, "CURRENT HOST URL", topMargin: 0);
        _currentUrlValueLabel.Dock = DockStyle.Fill;
        _currentUrlValueLabel.Font = new Font("Cascadia Mono", 8.5f);
        _currentUrlValueLabel.TextAlign = ContentAlignment.MiddleLeft;
        _currentUrlValueLabel.AutoEllipsis = true;

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.AutoSize = true;
        _statusLabel.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.Margin = new Padding(0, 0, 0, ScaleLogical(16));

        var settingsColumns = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        settingsColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68f));
        settingsColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(30)));
        settingsColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));

        var networkPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        networkPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        networkPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        networkPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(54)));
        networkPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        ConfigureSectionLabel(_networkLabel, "NETWORK", topMargin: ScaleLogical(6));
        var networkMode = CreateTwoButtonRow(_networkAutomaticButton, _networkManualButton);
        ConfigureModeButton(_networkAutomaticButton, "Automatic", () => SetNetworkMode(NetworkSelectionMode.Automatic));
        ConfigureModeButton(_networkManualButton, "Manual", () => SetNetworkMode(NetworkSelectionMode.Manual));

        _candidateList.Dock = DockStyle.Fill;
        _candidateList.MinimumSize = new Size(0, ScaleLogical(360));
        _candidateList.Margin = new Padding(0, ScaleLogical(8), 0, ScaleLogical(18));

        var portPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        portPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        portPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        portPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(54)));
        portPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        portPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(46)));
        portPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        portPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        ConfigureSectionLabel(_portLabel, "PORT", topMargin: ScaleLogical(6));
        var portMode = CreateTwoButtonRow(_portAutomaticButton, _portManualButton);
        ConfigureModeButton(_portAutomaticButton, "Automatic", () => SetPortMode(PortSelectionMode.Automatic));
        ConfigureModeButton(_portManualButton, "Manual port", () =>
        {
            SetPortMode(PortSelectionMode.Manual);
            _manualPortTextBox.Focus();
            _manualPortTextBox.SelectAll();
        });

        _manualPortLabel.Text = "Manual port number";
        _manualPortLabel.Dock = DockStyle.Fill;
        _manualPortLabel.AutoSize = true;
        _manualPortLabel.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _manualPortLabel.Margin = new Padding(0, ScaleLogical(8), 0, ScaleLogical(6));

        _manualPortTextBox.Dock = DockStyle.Fill;
        _manualPortTextBox.Font = new Font("Segoe UI", 12f);
        _manualPortTextBox.Margin = Padding.Empty;
        _manualPortTextBox.RejectedInput += OnManualPortRejectedInput;

        _manualPortWarningLabel.Dock = DockStyle.Fill;
        _manualPortWarningLabel.AutoSize = true;
        _manualPortWarningLabel.Font = new Font("Segoe UI", 8.75f, FontStyle.Bold);
        _manualPortWarningLabel.Margin = new Padding(0, ScaleLogical(6), 0, 0);
        _manualPortWarningLabel.Visible = false;

        _restartNoteLabel.Text = "Manual port changes apply after restarting Voltura Air. If the port is already in use, Voltura Air will keep the current setting.";
        _restartNoteLabel.Dock = DockStyle.Fill;
        _restartNoteLabel.AutoSize = true;
        _restartNoteLabel.Font = new Font("Segoe UI", 8.75f);
        _restartNoteLabel.Margin = new Padding(0, ScaleLogical(8), 0, 0);
        _restartNoteLabel.MaximumSize = new Size(ScaleLogical(420), 0);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 1,
            Padding = new Padding(0, ScaleLogical(CommandButtonStyle.ActionTopPadding), 0, 0),
            Margin = Padding.Empty
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _saveButton.Text = "Save";
        _saveButton.Dock = DockStyle.Fill;
        _saveButton.Margin = Padding.Empty;
        CommandButtonStyle.Configure(_saveButton);
        _saveButton.Click += (_, _) => SaveSettings();
        actions.Controls.Add(_saveButton, 0, 0);

        content.Controls.Add(_currentUrlLabel, 0, 0);
        content.Controls.Add(_currentUrlValueLabel, 0, 1);
        content.Controls.Add(_statusLabel, 0, 2);

        networkPanel.Controls.Add(_networkLabel, 0, 0);
        networkPanel.Controls.Add(networkMode, 0, 1);
        networkPanel.Controls.Add(_candidateList, 0, 2);

        portPanel.Controls.Add(_portLabel, 0, 0);
        portPanel.Controls.Add(portMode, 0, 1);
        portPanel.Controls.Add(_manualPortLabel, 0, 2);
        portPanel.Controls.Add(_manualPortTextBox, 0, 3);
        portPanel.Controls.Add(_manualPortWarningLabel, 0, 4);
        portPanel.Controls.Add(_restartNoteLabel, 0, 5);

        settingsColumns.Controls.Add(networkPanel, 0, 0);
        settingsColumns.Controls.Add(portPanel, 2, 0);
        content.Controls.Add(settingsColumns, 0, 3);
        content.Controls.Add(actions, 0, 4);

        Controls.Add(content);
    }

    private void LoadSettings()
    {
        var settings = AppNetworkSettings.Load();
        var candidates = LanAddressSelector.GetCandidates();
        var selection = LanAddressSelector.Select(candidates, settings);

        _networkMode = settings.NetworkMode;
        _portMode = settings.PortMode;
        _currentUrlValueLabel.Text = _webHost.ServerUrl;
        var status = selection?.Warning ?? _webHost.AddressSelectionWarning ?? string.Empty;
        _manualPortTextBox.Value = (settings.ManualPort ?? _webHost.Port).ToString(CultureInfo.InvariantCulture);

        PopulateCandidateList(candidates, selection?.Candidate);
        UpdateModeButtons();
        ApplyTheme(_theme);
        ShowStatus(status, isError: selection?.Warning is not null);
    }

    private void SaveSettings()
    {
        var current = AppNetworkSettings.Load();
        string? manualAddress = null;
        if (_networkMode == NetworkSelectionMode.Manual)
        {
            if (_candidateList.SelectedItem is not CandidateListItem selectedItem)
            {
                ShowStatus("Choose a network address before saving manual mode.", isError: true);
                return;
            }

            manualAddress = selectedItem.Candidate.Address.ToString();
        }

        int? manualPort = null;
        if (_portMode == PortSelectionMode.Manual)
        {
            if (!int.TryParse(_manualPortTextBox.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort))
            {
                ShowStatus("Manual port must be between 1 and 65535.", isError: true);
                return;
            }

            var portValidationError = PortSelector.GetManualPortValidationError(parsedPort);
            if (portValidationError is not null)
            {
                ShowStatus(portValidationError, isError: true);
                return;
            }

            if (parsedPort != _webHost.Port && !WebHostService.IsPortAvailable(parsedPort))
            {
                ShowStatus($"Port {parsedPort} is already in use.", isError: true);
                return;
            }

            manualPort = parsedPort;
        }

        var updated = current with
        {
            NetworkMode = _networkMode,
            ManualHostAddress = manualAddress,
            PortMode = _portMode,
            ManualPort = manualPort
        };
        AppNetworkSettings.Save(updated);

        var selection = LanAddressSelector.Select(LanAddressSelector.GetCandidates(), updated);
        var hostAddress = selection?.Address.ToString() ?? WebHostService.GetDnsLanAddressFallback() ?? "127.0.0.1";
        _webHost.UpdateAdvertisedHostAddress(hostAddress);
        _pairingForm.UpdateServerUrl(_webHost.ServerUrl);
        if (_networkMode == NetworkSelectionMode.Automatic)
        {
            AppNetworkSettings.SetLastAutomaticHostAddress(hostAddress);
        }

        LoadSettings();
        var status = selection?.Warning ?? "Connection settings saved.";
        if (_portMode == PortSelectionMode.Manual && manualPort != _webHost.Port)
        {
            status = $"{status} Port change will apply after restarting Voltura Air.";
        }

        ShowStatus(status, isError: selection?.Warning is not null);
    }

    private void PopulateCandidateList(IReadOnlyList<LanAddressCandidate> candidates, LanAddressCandidate? selectedCandidate)
    {
        _candidateList.BeginUpdate();
        _candidateList.ClearItems();

        var recommended = candidates.OrderByDescending(candidate => candidate.Score).FirstOrDefault();
        foreach (var candidate in candidates)
        {
            var suffix = candidate == recommended
                ? " - recommended"
                : candidate.IsLikelyVpnOrVirtual
                    ? " - not recommended"
                    : string.Empty;
            var status = candidate == recommended
                ? "Recommended"
                : candidate.IsLikelyVpnOrVirtual
                    ? "Not recommended"
                    : string.Empty;
            _candidateList.AddItem(new CandidateListItem(
                candidate,
                $"{GetAdapterTypeDisplayName(candidate)} - {GetAdapterDescription(candidate)}",
                candidate.Address.ToString(),
                status,
                $"{GetAdapterTypeDisplayName(candidate)} - {GetAdapterDescription(candidate)} - {candidate.Address}{suffix}"));
        }

        _candidateList.EndUpdate();

        if (selectedCandidate is not null)
        {
            for (var index = 0; index < _candidateList.ItemCount; index += 1)
            {
                if (_candidateList.GetItem(index).Candidate.Address.Equals(selectedCandidate.Address))
                {
                    _candidateList.SelectedIndex = index;
                    return;
                }
            }
        }

        if (_candidateList.ItemCount > 0)
        {
            _candidateList.SelectedIndex = 0;
        }
    }

    private void SetNetworkMode(NetworkSelectionMode mode)
    {
        _networkMode = mode;
        UpdateModeButtons();
    }

    private void SetPortMode(PortSelectionMode mode)
    {
        _portMode = mode;
        UpdateModeButtons();
    }

    private void UpdateModeButtons()
    {
        ApplyModeButton(_networkAutomaticButton, _networkMode == NetworkSelectionMode.Automatic);
        ApplyModeButton(_networkManualButton, _networkMode == NetworkSelectionMode.Manual);
        ApplyModeButton(_portAutomaticButton, _portMode == PortSelectionMode.Automatic);
        ApplyModeButton(_portManualButton, _portMode == PortSelectionMode.Manual);
        _manualPortTextBox.Enabled = _portMode == PortSelectionMode.Manual;
        _manualPortTextBox.TabStop = _portMode == PortSelectionMode.Manual;
        _manualPortLabel.ForeColor = _portMode == PortSelectionMode.Manual ? _theme.Text : _theme.MutedText;
    }

    private TableLayoutPanel CreateTwoButtonRow(Button first, Button second)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        first.Margin = new Padding(0, 0, ScaleLogical(CommandButtonStyle.ButtonGap), 0);
        second.Margin = Padding.Empty;
        row.Controls.Add(first, 0, 0);
        row.Controls.Add(second, 1, 0);
        return row;
    }

    private void ConfigureModeButton(Button button, string text, Action onClick)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        CommandButtonStyle.Configure(button);
        button.Click += (_, _) => onClick();
    }

    private void ApplyModeButton(Button button, bool isActive)
    {
        CommandButtonStyle.ApplyTheme(button, _theme, isActive ? CommandButtonKind.Primary : CommandButtonKind.Normal);
        button.Font = new Font("Segoe UI", 9f, isActive ? FontStyle.Bold : FontStyle.Regular);
    }

    private void ConfigureSectionLabel(Label label, string text, int topMargin)
    {
        label.Text = text;
        label.Dock = DockStyle.Fill;
        label.AutoSize = true;
        label.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        label.Margin = new Padding(0, topMargin, 0, ScaleLogical(8));
    }

    private void ShowStatus(string message, bool isError)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = isError ? _theme.Danger : _theme.Accent;
    }

    private void OnManualPortRejectedInput(object? sender, RejectedInputEventArgs e)
    {
        ShowManualPortWarning(e.Message);
    }

    private void ShowManualPortWarning(string message)
    {
        _manualPortWarningTimer.Stop();
        _manualPortWarningLabel.Text = message;
        _manualPortWarningLabel.Visible = true;
        _manualPortWarningTimer.Start();
    }

    private void OnManualPortWarningTimerTick(object? sender, EventArgs e)
    {
        _manualPortWarningTimer.Stop();
        _manualPortWarningLabel.Visible = false;
    }

    private int ScaleLogical(int value)
    {
        return LogicalToDeviceUnits(value);
    }

    private static string GetAdapterTypeDisplayName(LanAddressCandidate candidate)
    {
        return candidate.AdapterType switch
        {
            System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            System.Net.NetworkInformation.NetworkInterfaceType.Ethernet => "Ethernet",
            _ => candidate.AdapterType.ToString()
        };
    }

    private static string GetAdapterDescription(LanAddressCandidate candidate)
    {
        return string.IsNullOrWhiteSpace(candidate.AdapterDescription)
            ? candidate.AdapterName
            : candidate.AdapterDescription;
    }

    private sealed record CandidateListItem(
        LanAddressCandidate Candidate,
        string AdapterLabel,
        string AddressLabel,
        string StatusLabel,
        string AccessibilityLabel)
    {
        public override string ToString()
        {
            return AccessibilityLabel;
        }
    }

    private sealed class ThemedCandidateListBox : UserControl
    {
        private readonly List<CandidateListItem> _items = new();
        private ThemePalette _theme = WindowsTheme.Current();
        private int _selectedIndex = -1;
        private int _scrollOffset;
        private bool _isDraggingScrollThumb;
        private int _scrollDragStartY;
        private int _scrollDragStartOffset;
        private bool _isUpdating;

        public ThemedCandidateListBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            TabStop = true;
            Cursor = Cursors.Hand;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ItemCount => _items.Count;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                var newIndex = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
                if (_selectedIndex == newIndex)
                {
                    return;
                }

                _selectedIndex = newIndex;
                EnsureSelectedVisible();
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public object? SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

        public void ApplyTheme(ThemePalette theme)
        {
            _theme = theme;
            BackColor = theme.Surface;
            ForeColor = theme.Text;
            Invalidate();
        }

        public void BeginUpdate()
        {
            _isUpdating = true;
        }

        public void EndUpdate()
        {
            _isUpdating = false;
            ClampScrollOffset();
            Invalidate();
        }

        public void ClearItems()
        {
            _items.Clear();
            _selectedIndex = -1;
            _scrollOffset = 0;
            if (!_isUpdating)
            {
                Invalidate();
            }
        }

        public void AddItem(CandidateListItem item)
        {
            _items.Add(item);
            if (_selectedIndex < 0)
            {
                _selectedIndex = 0;
            }

            if (!_isUpdating)
            {
                Invalidate();
            }
        }

        public CandidateListItem GetItem(int index)
        {
            return _items[index];
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using var background = new SolidBrush(_theme.Surface);
            e.Graphics.FillRectangle(background, ClientRectangle);

            var rowHeight = GetRowHeight();
            var listWidth = ClientSize.Width - (NeedsScrollBar() ? ScaleLogical(16) : 0);
            var firstIndex = Math.Max(0, _scrollOffset / rowHeight);
            var y = -(_scrollOffset % rowHeight);

            for (var index = firstIndex; index < _items.Count && y < ClientSize.Height; index += 1)
            {
                var bounds = new Rectangle(0, y, listWidth, rowHeight);
                DrawItem(e.Graphics, bounds, _items[index], index == _selectedIndex);
                y += rowHeight;
            }

            if (NeedsScrollBar())
            {
                DrawScrollBar(e.Graphics);
            }

            if (Focused)
            {
                var focusBounds = ClientRectangle;
                focusBounds.Width -= 1;
                focusBounds.Height -= 1;
                ControlPaint.DrawFocusRectangle(e.Graphics, focusBounds, ForeColor, BackColor);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button != MouseButtons.Left)
            {
                base.OnMouseDown(e);
                return;
            }

            var thumbBounds = GetScrollThumbBounds();
            if (thumbBounds.Contains(e.Location))
            {
                _isDraggingScrollThumb = true;
                _scrollDragStartY = e.Y;
                _scrollDragStartOffset = _scrollOffset;
                Capture = true;
                return;
            }

            if (GetScrollTrackBounds().Contains(e.Location))
            {
                var direction = e.Y < thumbBounds.Top ? -1 : 1;
                ScrollBy(direction * Math.Max(GetRowHeight(), ClientSize.Height / 2));
                return;
            }

            var index = (_scrollOffset + e.Y) / GetRowHeight();
            if (index >= 0 && index < _items.Count)
            {
                SelectedIndex = index;
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_isDraggingScrollThumb)
            {
                base.OnMouseMove(e);
                return;
            }

            var trackBounds = GetScrollTrackBounds();
            var thumbBounds = GetScrollThumbBounds();
            var travel = Math.Max(1, trackBounds.Height - thumbBounds.Height - ScaleLogical(8));
            var delta = e.Y - _scrollDragStartY;
            _scrollOffset = Math.Clamp(_scrollDragStartOffset + delta * GetMaxScrollOffset() / travel, 0, GetMaxScrollOffset());
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _isDraggingScrollThumb = false;
            Capture = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(-e.Delta);
            base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up)
            {
                SelectedIndex = Math.Max(0, _selectedIndex - 1);
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Down)
            {
                SelectedIndex = Math.Min(_items.Count - 1, _selectedIndex + 1);
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScrollOffset();
        }

        private void DrawItem(Graphics graphics, Rectangle bounds, CandidateListItem item, bool selected)
        {
            var background = selected ? _theme.Accent : _theme.Surface;
            var foreground = selected ? _theme.AccentText : _theme.Text;
            using var backgroundBrush = new SolidBrush(background);
            graphics.FillRectangle(backgroundBrush, bounds);

            using var titleFont = new Font(Font, FontStyle.Bold);
            DrawTextLine(graphics, item.AdapterLabel, titleFont, foreground, bounds, ScaleLogical(5));
            DrawTextLine(graphics, item.AddressLabel, Font, selected ? foreground : _theme.MutedText, bounds, ScaleLogical(24));
            if (!string.IsNullOrEmpty(item.StatusLabel))
            {
                DrawTextLine(graphics, item.StatusLabel, Font, selected ? foreground : GetStatusColor(item), bounds, ScaleLogical(43));
            }

            using var borderPen = new Pen(_theme.Border);
            graphics.DrawLine(borderPen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
        }

        private void DrawScrollBar(Graphics graphics)
        {
            var trackBounds = GetScrollTrackBounds();
            using var trackBrush = new SolidBrush(_theme.Window);
            graphics.FillRectangle(trackBrush, trackBounds);

            using var thumbBrush = new SolidBrush(_theme.MutedText);
            graphics.FillRectangle(thumbBrush, GetScrollThumbBounds());
        }

        private bool NeedsScrollBar()
        {
            return GetContentHeight() > ClientSize.Height;
        }

        private int GetContentHeight()
        {
            return _items.Count * GetRowHeight();
        }

        private int GetMaxScrollOffset()
        {
            return Math.Max(0, GetContentHeight() - ClientSize.Height);
        }

        private void ScrollBy(int delta)
        {
            _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, GetMaxScrollOffset());
            Invalidate();
        }

        private void ClampScrollOffset()
        {
            _scrollOffset = Math.Clamp(_scrollOffset, 0, GetMaxScrollOffset());
        }

        private void EnsureSelectedVisible()
        {
            if (_selectedIndex < 0)
            {
                return;
            }

            var rowHeight = GetRowHeight();
            var itemTop = _selectedIndex * rowHeight;
            var itemBottom = itemTop + rowHeight;
            if (itemTop < _scrollOffset)
            {
                _scrollOffset = itemTop;
            }
            else if (itemBottom > _scrollOffset + ClientSize.Height)
            {
                _scrollOffset = itemBottom - ClientSize.Height;
            }

            ClampScrollOffset();
        }

        private Rectangle GetScrollTrackBounds()
        {
            return NeedsScrollBar()
                ? new Rectangle(ClientSize.Width - ScaleLogical(12), 0, ScaleLogical(12), ClientSize.Height)
                : Rectangle.Empty;
        }

        private Rectangle GetScrollThumbBounds()
        {
            if (!NeedsScrollBar())
            {
                return Rectangle.Empty;
            }

            var trackPadding = ScaleLogical(4);
            var trackBounds = GetScrollTrackBounds();
            var availableTrackHeight = Math.Max(1, trackBounds.Height - trackPadding * 2);
            var thumbHeight = Math.Max(ScaleLogical(48), availableTrackHeight * ClientSize.Height / Math.Max(1, GetContentHeight()));
            thumbHeight = Math.Min(availableTrackHeight, thumbHeight);
            var travel = Math.Max(1, availableTrackHeight - thumbHeight);
            var thumbTop = trackBounds.Top + trackPadding + (_scrollOffset * travel / Math.Max(1, GetMaxScrollOffset()));
            return new Rectangle(trackBounds.Left + ScaleLogical(4), thumbTop, ScaleLogical(4), thumbHeight);
        }

        private void DrawTextLine(Graphics graphics, string text, Font font, Color color, Rectangle itemBounds, int top)
        {
            var textBounds = new Rectangle(
                itemBounds.Left + ScaleLogical(14),
                itemBounds.Top + top,
                Math.Max(1, itemBounds.Width - ScaleLogical(28)),
                ScaleLogical(22));

            TextRenderer.DrawText(
                graphics,
                text,
                font,
                textBounds,
                color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private Color GetStatusColor(CandidateListItem item)
        {
            return item.StatusLabel.Equals("Not recommended", StringComparison.OrdinalIgnoreCase)
                ? _theme.Danger
                : _theme.Accent;
        }

        private int GetRowHeight()
        {
            return ScaleLogical(66);
        }

        private int ScaleLogical(int value)
        {
            return (int)Math.Round(value * DeviceDpi / 96f);
        }
    }

    private sealed class ThemedTextBox : UserControl
    {
        private readonly TextBox _textBox = new();
        private ThemePalette _theme = WindowsTheme.Current();
        private string _lastValidText = PortSelector.PreferredPort.ToString(CultureInfo.InvariantCulture);
        private bool _isRestoringText;
        private bool _hasFocus;

        public ThemedTextBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            Padding = new Padding(10, 4, 10, 4);
            TabStop = false;

            _textBox.BorderStyle = BorderStyle.None;
            _textBox.Dock = DockStyle.Fill;
            _textBox.Font = Font;
            _textBox.GotFocus += (_, _) =>
            {
                _hasFocus = true;
                Invalidate();
            };
            _textBox.LostFocus += (_, _) =>
            {
                _hasFocus = false;
                RestoreLastValidTextIfEmpty();
                Invalidate();
            };
            _textBox.KeyPress += OnTextBoxKeyPress;
            _textBox.TextChanged += OnTextBoxTextChanged;
            Controls.Add(_textBox);
        }

        public event EventHandler<RejectedInputEventArgs>? RejectedInput;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Value
        {
            get => _textBox.Text;
            set
            {
                var normalized = IsValidPortText(value) ? value : PortSelector.PreferredPort.ToString(CultureInfo.InvariantCulture);
                _lastValidText = normalized;
                _textBox.Text = normalized;
            }
        }

        public void ApplyTheme(ThemePalette theme)
        {
            _theme = theme;
            BackColor = theme.Surface;
            ForeColor = Enabled ? theme.Text : theme.MutedText;
            _textBox.BackColor = theme.Surface;
            _textBox.ForeColor = Enabled ? theme.Text : theme.MutedText;
            Invalidate();
        }

        public new bool Focus()
        {
            return _textBox.Focus();
        }

        public void SelectAll()
        {
            _textBox.SelectAll();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _textBox.Enabled = Enabled;
            ApplyTheme(_theme);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            _textBox.Font = Font;
        }

        private void OnTextBoxKeyPress(object? sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar))
            {
                return;
            }

            if (!char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
                RejectedInput?.Invoke(this, new RejectedInputEventArgs("Port must use numbers only."));
                return;
            }

            var proposed = GetProposedText(e.KeyChar.ToString());
            if (!IsPotentialManualPortText(proposed, out var rejectionMessage))
            {
                e.Handled = true;
                RejectedInput?.Invoke(this, new RejectedInputEventArgs(rejectionMessage));
            }
        }

        private void OnTextBoxTextChanged(object? sender, EventArgs e)
        {
            if (_isRestoringText)
            {
                return;
            }

            if (string.IsNullOrEmpty(_textBox.Text))
            {
                return;
            }

            if (IsPotentialManualPortText(_textBox.Text, out _))
            {
                _lastValidText = _textBox.Text;
                return;
            }

            var message = GetPortTextValidationMessage(_textBox.Text);
            var selectionStart = Math.Min(_textBox.SelectionStart, _lastValidText.Length);
            _isRestoringText = true;
            _textBox.Text = _lastValidText;
            _textBox.SelectionStart = selectionStart;
            _isRestoringText = false;
            RejectedInput?.Invoke(this, new RejectedInputEventArgs(message));
        }

        private string GetProposedText(string replacement)
        {
            var text = _textBox.Text;
            var selectionStart = _textBox.SelectionStart;
            var selectionLength = _textBox.SelectionLength;
            return text.Remove(selectionStart, selectionLength).Insert(selectionStart, replacement);
        }

        private void RestoreLastValidTextIfEmpty()
        {
            if (!string.IsNullOrEmpty(_textBox.Text))
            {
                return;
            }

            _isRestoringText = true;
            _textBox.Text = _lastValidText;
            _textBox.SelectionStart = _textBox.Text.Length;
            _isRestoringText = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var borderColor = _hasFocus ? _theme.Accent : _theme.Border;
            using var background = new SolidBrush(_theme.Surface);
            using var border = new Pen(borderColor);
            e.Graphics.FillRectangle(background, ClientRectangle);
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _textBox.Height = Math.Max(1, Height - Padding.Vertical);
        }

        private static bool IsValidPortText(string text)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) &&
                PortSelector.GetManualPortValidationError(port) is null;
        }

        private static bool IsPotentialManualPortText(string text, out string rejectionMessage)
        {
            rejectionMessage = GetPortTextValidationMessage(text);
            return rejectionMessage.Length == 0;
        }

        private static string GetPortTextValidationMessage(string text)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
            {
                return "Port must use numbers only.";
            }

            return PortSelector.GetManualPortValidationError(port) ?? string.Empty;
        }
    }
}
