using System.Globalization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Image = System.Windows.Controls.Image;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using SystemFonts = System.Windows.SystemFonts;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.DataObject;

namespace VolturaAir.Host;

public partial class MainWindow : Window
{
    private const string ProductSiteUrl = "https://voltura.se/air/";
    private const double FullDeviceRowMinimumWidth = 760;

    private readonly PairingManager _pairingManager;
    private readonly WebHostService _webHost;
    private readonly IWorkstationLockPolicy _workstationLockPolicy;
    private readonly ISystemPowerController _powerController;
    private readonly IAppLog _appLog;
    private readonly string _initialClientUrl;
    private readonly bool _usesServerUrlAsClientUrl;
    private readonly bool _usePublicScreenshotPairingUrl;
    private readonly List<Button> _navigationButtons;
    private HostPage _activePage;
    private string _serverUrl;
    private string _clientUrl;
    private string _pairingUrl;
    private ListBox? _devicesList;
    private StackPanel? _deviceDetailsPanel;
    private ListBox? _networkCandidateList;
    private ToggleButton? _networkAutomaticButton;
    private ToggleButton? _networkManualButton;
    private ToggleButton? _portAutomaticButton;
    private ToggleButton? _portManualButton;
    private TextBox? _manualPortTextBox;
    private TextBlock? _manualPortValidationText;
    private TextBlock? _connectionStatusText;
    private Border? _toast;
    private DispatcherTimer? _toastTimer;
    private int _lastPairedDeviceCount;
    private bool _isLoadingPreferences;
    private bool _allowClose;

    public MainWindow(
        PairingManager pairingManager,
        WebHostService webHost,
        string? clientUrl,
        bool usePublicScreenshotPairingUrl = false,
        IWorkstationLockPolicy? workstationLockPolicy = null,
        ISystemPowerController? powerController = null,
        IAppLog? appLog = null)
    {
        _pairingManager = pairingManager;
        _webHost = webHost;
        _workstationLockPolicy = workstationLockPolicy ?? webHost.WorkstationLockPolicy;
        _powerController = powerController ?? webHost.PowerController;
        _appLog = appLog ?? webHost.AppLog;
        _usePublicScreenshotPairingUrl = usePublicScreenshotPairingUrl;
        _serverUrl = webHost.ServerUrl;
        _usesServerUrlAsClientUrl = string.IsNullOrWhiteSpace(clientUrl);
        _initialClientUrl = string.IsNullOrWhiteSpace(clientUrl) ? webHost.ServerUrl : clientUrl.TrimEnd('/');
        _clientUrl = _initialClientUrl;
        _pairingUrl = CreatePairingUrl();
        _lastPairedDeviceCount = pairingManager.PairedDeviceCount;

        InitializeComponent();
        SetIcon(this);
        SetSidebarAppIcon();
        WpfTheme.Apply(this);
        _navigationButtons =
        [
            ConnectNavButton,
            DevicesNavButton,
            ConnectionNavButton,
            PreferencesNavButton,
            DiagnosticsNavButton
        ];

        _pairingManager.ConnectionChanged += OnConnectionChanged;
        AppThemeSettings.Changed += OnThemeChanged;
        SelectPage(HostPage.Connect);
    }

    public string PairingUrl => _pairingUrl;

    public string ServerUrl => _serverUrl;

    public void ShowPage(HostPage page)
    {
        SelectPage(page);
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ShowPairedStatus()
    {
        SelectPage(HostPage.Connect);
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void AllowClose()
    {
        _allowClose = true;
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

    protected override void OnClosed(EventArgs e)
    {
        _pairingManager.ConnectionChanged -= OnConnectionChanged;
        AppThemeSettings.Changed -= OnThemeChanged;
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void SelectPage(HostPage page)
    {
        _activePage = page;
        RefreshStatusText();
        RefreshNavigationTheme();

        switch (page)
        {
            case HostPage.Connect:
                PageTitleText.Text = "Connect";
                PageSubtitleText.Text = "Pair a phone, tablet, or browser on the same network.";
                PageContent.Content = BuildConnectPage();
                break;
            case HostPage.Devices:
                PageTitleText.Text = "Devices";
                PageSubtitleText.Text = "Manage trusted devices, active connections, and per-device permissions.";
                PageContent.Content = BuildDevicesPage();
                break;
            case HostPage.Connection:
                PageTitleText.Text = "Connection";
                PageSubtitleText.Text = "Choose the LAN address and port advertised to phones and tablets.";
                PageContent.Content = BuildConnectionPage();
                break;
            case HostPage.Preferences:
                PageTitleText.Text = "Preferences";
                PageSubtitleText.Text = "Startup, alerts, permissions, device defaults, and theme.";
                PageContent.Content = BuildPreferencesPage();
                break;
            case HostPage.Diagnostics:
                PageTitleText.Text = "Diagnostics";
                PageSubtitleText.Text = "Review application activity or inspect system details for troubleshooting.";
                PageContent.Content = BuildDiagnosticsPage();
                break;
        }
    }

    private void CopyToClipboard(string value, string confirmation)
    {
        System.Windows.Clipboard.SetText(value);
        ShowToast(confirmation);
    }

    private void ShowToast(string message)
    {
        if (_toast is not null)
        {
            MainContentRoot.Children.Remove(_toast);
        }

        _toastTimer?.Stop();
        _toast = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = (Brush)Resources["SurfaceRaisedBrush"],
            BorderBrush = (Brush)Resources["AccentBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new TextBlock
            {
                Text = message,
                Foreground = (Brush)Resources["TextBrush"],
                FontWeight = FontWeights.SemiBold
            }
        };
        Grid.SetRow(_toast, 1);
        MainContentRoot.Children.Add(_toast);

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer?.Stop();
            if (_toast is not null)
            {
                MainContentRoot.Children.Remove(_toast);
                _toast = null;
            }
        };
        _toastTimer.Start();
    }

    private CheckBox CreateCheckBox(string text, bool isChecked)
    {
        return new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = (Brush)Resources["TextBrush"]
        };
    }

    private BitmapSource CreateQrSource(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var code = new QRCode(data);
        using var bitmap = code.GetGraphic(18, System.Drawing.Color.Black, System.Drawing.Color.White, drawQuietZones: true);
        var handle = bitmap.GetHbitmap();
        try
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            _ = DeleteObject(handle);
        }
    }

    private static void OpenProductSite()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = ProductSiteUrl,
            UseShellExecute = true
        });
    }

    private void OnConnectNavClicked(object sender, RoutedEventArgs e) => SelectPage(HostPage.Connect);

    private void OnDevicesNavClicked(object sender, RoutedEventArgs e) => SelectPage(HostPage.Devices);

    private void OnConnectionNavClicked(object sender, RoutedEventArgs e) => SelectPage(HostPage.Connection);

    private void OnPreferencesNavClicked(object sender, RoutedEventArgs e) => SelectPage(HostPage.Preferences);

    private void OnDiagnosticsNavClicked(object sender, RoutedEventArgs e) => SelectPage(HostPage.Diagnostics);

    private void OnHideClicked(object sender, RoutedEventArgs e) => Hide();

    private sealed record DeviceListItem(string ClientId, string Name, string Status, string Activity, string Metadata);

    private sealed record CandidateListItem(LanAddressCandidate Candidate, string Adapter, string Address, string Status);

    private sealed record DiagnosticItem(string Name, string Value);

    private enum PermissionKind
    {
        PcSleep,
        VolumeControl,
        PcLock,
        BlackoutDisplay,
        DisplayOff,
        ScreenSaver,
        SignOut,
        Restart,
        Shutdown
    }
}

public enum HostPage
{
    Connect,
    Devices,
    Connection,
    Preferences,
    Diagnostics
}
