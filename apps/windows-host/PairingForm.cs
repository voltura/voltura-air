using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;
using QRCoder;

namespace VolturaAir.Host;

public sealed class PairingForm : Form
{
    private const int StartupLoadingDurationMs = 4000;
    private const int StartupAnimationIntervalMs = 450;
    private const int StartupIconPixels = 132;
    private const string StartupLoadingText = "Starting connection services";
    private const int QrQuietZoneModules = 4;
    private const int PreferredQrPixels = 1040;
    private const int MinQrModulePixels = 8;
    private readonly PairingManager _pairingManager;
    private readonly Icon _appIcon;
    private readonly List<Button> _buttons = new();
    private readonly Panel _qrPanel = new();
    private readonly PictureBox _qrPicture = new();
    private readonly Label _titleLabel = new();
    private readonly Label _subtitleLabel = new();
    private readonly Label _statusLabel = new();
    private readonly ToolTip _toolTip = new();
    private readonly TableLayoutPanel _actionButtons = new();
    private readonly Button _newPairingButton = new();
    private readonly Button _copyLinkButton = new();
    private readonly Button _devicesButton = new();
    private readonly Panel _contentPanel = new();
    private readonly Panel _startupPanel = new();
    private readonly PictureBox _startupIcon = new();
    private readonly Label _startupTitleLabel = new();
    private readonly Label _startupSubtitleLabel = new();
    private readonly ProgressBar _startupSpinner = new();
    private readonly System.Windows.Forms.Timer _startupLoadingTimer = new() { Interval = StartupLoadingDurationMs };
    private readonly System.Windows.Forms.Timer _startupAnimationTimer = new() { Interval = StartupAnimationIntervalMs };
    private readonly bool _usesServerUrlAsClientUrl;
    private string _serverUrl;
    private string _clientUrl;
    private ThemePalette _theme;
    private string _pairingUrl;
    private int _lastPairedDeviceCount;
    private int _startupLoadingDotCount = 1;
    private Size _lastQrPanelSize;
    private bool _allowClose;

    public PairingForm(string serverUrl, PairingManager pairingManager, string? clientUrl = null)
    {
        _theme = WindowsTheme.Current();
        _serverUrl = serverUrl;
        _usesServerUrlAsClientUrl = string.IsNullOrWhiteSpace(clientUrl);
        _clientUrl = string.IsNullOrWhiteSpace(clientUrl) ? serverUrl : clientUrl.TrimEnd('/');
        _pairingManager = pairingManager;
        _pairingUrl = CreatePairingUrl();
        _lastPairedDeviceCount = pairingManager.PairedDeviceCount;
        _appIcon = LoadAppIcon();

        Text = "Voltura Air";
        Icon = _appIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 1128);
        Size = new Size(1040, 1180);

        BuildLayout();
        ApplyTheme();
        RefreshQr();
        _startupLoadingTimer.Tick += OnStartupLoadingTimerTick;
        _startupAnimationTimer.Tick += OnStartupAnimationTimerTick;
        UpdateStartupLoadingText();
        _startupAnimationTimer.Start();
        _startupLoadingTimer.Start();

        _pairingManager.ConnectionChanged += OnConnectionChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        AppThemeSettings.Changed += OnAppThemeChanged;
        FormClosing += OnFormClosing;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                EndStartupLoading();
                Hide();
            }
        };
    }

    public event EventHandler? DeviceManagerRequested;

    public string PairingUrl => _pairingUrl;

    public string ServerUrl => _serverUrl;

    public Icon CloneAppIcon()
    {
        return (Icon)_appIcon.Clone();
    }

    public void AllowExit()
    {
        _allowClose = true;
    }

    public void NewPairing()
    {
        _pairingUrl = CreatePairingUrl();
        RefreshQr();
    }

    public void UpdateServerUrl(string serverUrl)
    {
        if (string.Equals(_serverUrl, serverUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _serverUrl = serverUrl;
        if (_usesServerUrlAsClientUrl)
        {
            _clientUrl = serverUrl;
        }

        NewPairing();
    }

    public void ShowMainWindow()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    public void HideToTray()
    {
        EndStartupLoading();
        WindowState = FormWindowState.Minimized;
        Hide();
    }

    public void ShowPairedStatus()
    {
        _statusLabel.Text = $"Paired with {_pairingManager.ActiveDeviceSummary}";
        _statusLabel.ForeColor = _theme.Accent;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(ScaleLogical(24), ScaleLogical(4), ScaleLogical(24), ScaleLogical(24)),
            RowCount = 1,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _contentPanel.Dock = DockStyle.Fill;
        _contentPanel.Padding = Padding.Empty;

        var contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1
        };
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(50)));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(34)));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(34)));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(CommandButtonStyle.ActionRowHeight)));

        _titleLabel.Text = "Pair your device";
        _titleLabel.Dock = DockStyle.Fill;
        _titleLabel.Font = new Font("Segoe UI", 18, FontStyle.Bold);
        _titleLabel.TextAlign = ContentAlignment.MiddleCenter;
        _titleLabel.Margin = Padding.Empty;

        _subtitleLabel.Text = "Scan this QR code from your device.";
        _subtitleLabel.Dock = DockStyle.Fill;
        _subtitleLabel.Font = new Font("Segoe UI", 10.5f);
        _subtitleLabel.TextAlign = ContentAlignment.MiddleCenter;
        _subtitleLabel.Margin = Padding.Empty;
        _subtitleLabel.Padding = new Padding(0, 0, 0, ScaleLogical(4));

        _qrPanel.Dock = DockStyle.Fill;
        _qrPanel.Margin = new Padding(0, ScaleLogical(4), 0, ScaleLogical(8));
        _qrPanel.Resize += (_, _) => ResizeQrToPanel();

        _qrPicture.SizeMode = PictureBoxSizeMode.Normal;
        _qrPicture.Margin = Padding.Empty;
        _qrPanel.Controls.Add(_qrPicture);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

        _actionButtons.Dock = DockStyle.Fill;
        _actionButtons.RowCount = 1;
        _actionButtons.ColumnCount = 3;
        _actionButtons.Padding = new Padding(0, ScaleLogical(CommandButtonStyle.ActionTopPadding), 0, 0);

        _newPairingButton.Text = "New code";
        _newPairingButton.Click += (_, _) => NewPairing();

        _copyLinkButton.Text = "Copy link";
        _copyLinkButton.Click += (_, _) => CopyPairingUrl();

        _devicesButton.Text = "Devices";
        _devicesButton.Click += (_, _) => DeviceManagerRequested?.Invoke(this, EventArgs.Empty);

        _buttons.AddRange(new[] { _newPairingButton, _copyLinkButton, _devicesButton });
        foreach (var button in _buttons)
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, ScaleLogical(CommandButtonStyle.ButtonGap), 0);
            CommandButtonStyle.Configure(button);
        }
        _devicesButton.Margin = Padding.Empty;

        _actionButtons.Controls.Add(_newPairingButton, 0, 0);
        _actionButtons.Controls.Add(_copyLinkButton, 1, 0);
        _actionButtons.Controls.Add(_devicesButton, 2, 0);

        BuildStartupPanel();

        contentLayout.Controls.Add(_titleLabel, 0, 0);
        contentLayout.Controls.Add(_subtitleLabel, 0, 1);
        contentLayout.Controls.Add(_qrPanel, 0, 2);
        contentLayout.Controls.Add(_statusLabel, 0, 3);
        contentLayout.Controls.Add(_actionButtons, 0, 4);

        _contentPanel.Controls.Add(contentLayout);
        _contentPanel.Controls.Add(_startupPanel);
        _startupPanel.BringToFront();
        root.Controls.Add(_contentPanel, 0, 0);
        Controls.Add(root);
        _toolTip.SetToolTip(_copyLinkButton, "Copy the pairing URL to the clipboard.");
        _toolTip.SetToolTip(_devicesButton, "Open connected and paired devices.");
    }

    private void BuildStartupPanel()
    {
        _startupPanel.Dock = DockStyle.Fill;
        _startupPanel.Padding = new Padding(ScaleLogical(48));

        var startupLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6
        };
        startupLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        startupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        startupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(176)));
        startupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(62)));
        startupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(34)));
        startupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(24)));
        startupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _startupIcon.Dock = DockStyle.Fill;
        _startupIcon.Image = CreateStartupIconBitmap();
        _startupIcon.SizeMode = PictureBoxSizeMode.CenterImage;
        _startupIcon.Margin = Padding.Empty;

        _startupTitleLabel.Text = "Voltura Air";
        _startupTitleLabel.Dock = DockStyle.Fill;
        _startupTitleLabel.Font = new Font("Segoe UI", 24, FontStyle.Bold);
        _startupTitleLabel.TextAlign = ContentAlignment.MiddleCenter;
        _startupTitleLabel.Margin = Padding.Empty;

        var startupSubtitleHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        _startupSubtitleLabel.Text = $"{StartupLoadingText}.";
        _startupSubtitleLabel.AutoSize = false;
        _startupSubtitleLabel.Font = new Font("Segoe UI", 10.5f);
        _startupSubtitleLabel.TextAlign = ContentAlignment.TopLeft;
        _startupSubtitleLabel.Margin = Padding.Empty;
        _startupSubtitleLabel.Size = new Size(
            TextRenderer.MeasureText($"{StartupLoadingText}...", _startupSubtitleLabel.Font).Width,
            ScaleLogical(28));
        startupSubtitleHost.Controls.Add(_startupSubtitleLabel);
        startupSubtitleHost.Resize += (_, _) => CenterStartupControl(startupSubtitleHost, _startupSubtitleLabel);
        CenterStartupControl(startupSubtitleHost, _startupSubtitleLabel);

        var startupSpinnerHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        _startupSpinner.Anchor = AnchorStyles.Top;
        _startupSpinner.Style = ProgressBarStyle.Marquee;
        _startupSpinner.MarqueeAnimationSpeed = 35;
        _startupSpinner.Minimum = 0;
        _startupSpinner.Maximum = 100;
        _startupSpinner.Size = new Size(ScaleLogical(156), ScaleLogical(6));
        _startupSpinner.Margin = Padding.Empty;
        startupSpinnerHost.Controls.Add(_startupSpinner);
        startupSpinnerHost.Resize += (_, _) => CenterStartupSpinner(startupSpinnerHost);
        CenterStartupSpinner(startupSpinnerHost);

        startupLayout.Controls.Add(_startupIcon, 0, 1);
        startupLayout.Controls.Add(_startupTitleLabel, 0, 2);
        startupLayout.Controls.Add(startupSubtitleHost, 0, 3);
        startupLayout.Controls.Add(startupSpinnerHost, 0, 4);
        _startupPanel.Controls.Add(startupLayout);
    }

    private void CenterStartupSpinner(Control host)
    {
        CenterStartupControl(host, _startupSpinner);
    }

    private static void CenterStartupControl(Control host, Control control)
    {
        control.Location = new Point(Math.Max(0, (host.ClientSize.Width - control.Width) / 2), 0);
    }

    private string CreatePairingUrl()
    {
        var token = _pairingManager.CreatePairingToken();
        var url = new UriBuilder(_clientUrl)
        {
            Query = $"t={Uri.EscapeDataString(token)}"
        };

        if (!string.Equals(_clientUrl, _serverUrl, StringComparison.OrdinalIgnoreCase))
        {
            url.Query = $"{url.Query.TrimStart('?')}&h={Uri.EscapeDataString(_serverUrl)}";
        }

        return url.Uri.ToString();
    }

    private void RefreshQr()
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(_pairingUrl, QRCodeGenerator.ECCLevel.M);
            var newImage = CreateQrBitmap(data);
            _qrPicture.Image?.Dispose();
            _qrPicture.Image = newImage;
            _qrPicture.Size = newImage.Size;
            CenterQrPicture();
        }
        catch
        {
            _qrPicture.Image?.Dispose();
            _qrPicture.Image = null;
        }

        RefreshStatus();
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        BeginInvoke(() =>
        {
            EndStartupLoading();

            var pairedDeviceCount = _pairingManager.PairedDeviceCount;
            if (pairedDeviceCount != _lastPairedDeviceCount)
            {
                _lastPairedDeviceCount = pairedDeviceCount;
                NewPairing();
                return;
            }

            RefreshStatus();
        });
    }

    private void RefreshStatus()
    {
        _statusLabel.Text = _pairingManager.IsPaired
            ? _pairingManager.HasActiveController
                ? $"Connected to {_pairingManager.ActiveDeviceSummary}"
                : $"{_pairingManager.PairedDeviceCount} paired device{Plural(_pairingManager.PairedDeviceCount)}. Ready for another."
            : "Waiting for a phone or tablet on the same network";
        _statusLabel.ForeColor = _pairingManager.HasActiveController ? _theme.Accent : _theme.MutedText;
        RefreshActionButtons();
    }

    private void OnStartupLoadingTimerTick(object? sender, EventArgs e)
    {
        EndStartupLoading();
    }

    private void OnStartupAnimationTimerTick(object? sender, EventArgs e)
    {
        _startupLoadingDotCount = _startupLoadingDotCount % 3 + 1;
        UpdateStartupLoadingText();
    }

    private void UpdateStartupLoadingText()
    {
        _startupSubtitleLabel.Text = $"{StartupLoadingText}{new string('.', _startupLoadingDotCount)}";
    }

    private void EndStartupLoading()
    {
        if (_startupLoadingTimer.Enabled)
        {
            _startupLoadingTimer.Stop();
        }

        if (_startupAnimationTimer.Enabled)
        {
            _startupAnimationTimer.Stop();
        }

        _startupPanel.Visible = false;
    }

    private void RefreshActionButtons()
    {
        var showDevices = _pairingManager.PairedDeviceCount > 0;
        _devicesButton.Visible = showDevices;
        _copyLinkButton.Margin = showDevices ? new Padding(0, 0, ScaleLogical(CommandButtonStyle.ButtonGap), 0) : Padding.Empty;
        _devicesButton.Margin = Padding.Empty;
        _actionButtons.ColumnStyles.Clear();
        _actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, showDevices ? 33.34f : 50f));
        _actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, showDevices ? 33.33f : 50f));
        _actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, showDevices ? 33.33f : 0f));
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_allowClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        AppThemeSettings.Changed -= OnAppThemeChanged;
        _pairingManager.ConnectionChanged -= OnConnectionChanged;
        _qrPicture.Image?.Dispose();
        _startupIcon.Image?.Dispose();
        _startupLoadingTimer.Dispose();
        _toolTip.Dispose();
        _appIcon.Dispose();
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
        _contentPanel.BackColor = _theme.Window;
        _qrPanel.BackColor = _theme.Window;
        _qrPicture.BackColor = _theme.QrBackground;
        _startupPanel.BackColor = _theme.Window;
        _startupIcon.BackColor = _theme.Window;
        _startupSpinner.BackColor = _theme.Window;
        _startupSpinner.ForeColor = _theme.Accent;
        _titleLabel.ForeColor = _theme.Text;
        _subtitleLabel.ForeColor = _theme.MutedText;
        _startupTitleLabel.ForeColor = _theme.Text;
        _startupSubtitleLabel.ForeColor = _theme.MutedText;

        foreach (var button in _buttons)
        {
            CommandButtonStyle.ApplyTheme(
                button,
                _theme,
                button == _newPairingButton ? CommandButtonKind.Primary : CommandButtonKind.Normal);
        }

        RefreshStatus();
    }

    private Bitmap CreateQrBitmap(QRCodeData qrData)
    {
        var matrix = qrData.ModuleMatrix;
        var totalModules = matrix.Count + QrQuietZoneModules * 2;
        var availablePixels = Math.Min(_qrPanel.ClientSize.Width, _qrPanel.ClientSize.Height);
        var preferredPixels = ScaleLogical(PreferredQrPixels);
        var targetPixels = availablePixels > 0 ? Math.Min(preferredPixels, availablePixels) : preferredPixels;
        var modulePixels = Math.Max(ScaleLogical(MinQrModulePixels), targetPixels / totalModules);
        modulePixels = Math.Max(1, Math.Min(modulePixels, Math.Max(1, availablePixels) / totalModules));
        var side = totalModules * modulePixels;
        var quietZonePixels = QrQuietZoneModules * modulePixels;
        var bitmap = new Bitmap(side, side);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(_theme.QrBackground);

        using var moduleBrush = new SolidBrush(Color.Black);
        for (var row = 0; row < matrix.Count; row += 1)
        {
            for (var column = 0; column < matrix[row].Length; column += 1)
            {
                if (!matrix[row][column])
                {
                    continue;
                }

                graphics.FillRectangle(
                    moduleBrush,
                    quietZonePixels + column * modulePixels,
                    quietZonePixels + row * modulePixels,
                    modulePixels,
                    modulePixels);
            }
        }

        return bitmap;
    }

    private void CenterQrPicture()
    {
        if (_qrPicture.Image is null)
        {
            return;
        }

        _qrPicture.Location = new Point(
            Math.Max(0, (_qrPanel.ClientSize.Width - _qrPicture.Width) / 2),
            Math.Max(0, (_qrPanel.ClientSize.Height - _qrPicture.Height) / 2));
    }

    private void ResizeQrToPanel()
    {
        if (_qrPanel.ClientSize == _lastQrPanelSize)
        {
            CenterQrPicture();
            return;
        }

        _lastQrPanelSize = _qrPanel.ClientSize;
        RefreshQr();
    }

    private void CopyPairingUrl()
    {
        Clipboard.SetText(_pairingUrl);
        _statusLabel.Text = "Pairing link copied to clipboard";
        _statusLabel.ForeColor = _theme.Accent;
    }

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAir.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
    }

    private Bitmap CreateStartupIconBitmap()
    {
        var targetPixels = ScaleLogical(StartupIconPixels);
        using var source = LoadStartupIconImage();
        var bitmap = new Bitmap(targetPixels, targetPixels);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, targetPixels, targetPixels));
        return bitmap;
    }

    private Image LoadStartupIconImage()
    {
        var imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAir-256.png");
        return File.Exists(imagePath) ? Image.FromFile(imagePath) : _appIcon.ToBitmap();
    }

    private static string Plural(int count)
    {
        return count == 1 ? string.Empty : "s";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            FormClosing -= OnFormClosing;
            _startupLoadingTimer.Tick -= OnStartupLoadingTimerTick;
            _startupAnimationTimer.Tick -= OnStartupAnimationTimerTick;
            _startupAnimationTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private int ScaleLogical(int value)
    {
        return LogicalToDeviceUnits(value);
    }
}
