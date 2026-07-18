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
using VolturaAir.Host.Features.Connection;
using VolturaAir.Host.Features.Devices;
using VolturaAir.Host.Features.Diagnostics;
using VolturaAir.Host.Features.Preferences;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Image = System.Windows.Controls.Image;
using ListBox = System.Windows.Controls.ListBox;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using SystemFonts = System.Windows.SystemFonts;

namespace VolturaAir.Host;

public partial class MainWindow : Window
{
    private const string ProductSiteUrl = "https://voltura.se/air/";
    private const string PairingPath = "/pair";
    private readonly PairingManager _pairingManager;
    private readonly WebHostService _webHost;
    private readonly IWorkstationLockPolicy _workstationLockPolicy;
    private readonly ISystemPowerController _powerController;
    private readonly IAwakeService _awakeService;
    private readonly IAppLog _appLog;
    private readonly IClipboardTextWriter _clipboardTextWriter;
    private readonly CustomPointerService _customPointerService;
    private readonly string _initialClientUrl;
    private readonly bool _usesServerUrlAsClientUrl;
    private readonly bool _usePublicScreenshotPairingUrl;
    private readonly List<Button> _navigationButtons;
    private HostPage _activePage;
    private string _serverUrl;
    private string _clientUrl;
    private PairingDisplayCode _pairingCode;
    private ListBox? _devicesList;
    private StackPanel? _deviceDetailsPanel;
    private ConnectionPageView? _connectionPage;
    private Border? _toast;
    private DispatcherTimer? _toastTimer;
    private bool _isLoadingPreferences;
    private string? _preferencesSectionToOpen;
    private double? _preferencesScrollOffsetToRestore;
    private bool _pageNeedsRefresh = true;
    private bool _allowClose;

    public MainWindow(
        PairingManager pairingManager,
        WebHostService webHost,
        string? clientUrl,
        bool usePublicScreenshotPairingUrl = false,
        IWorkstationLockPolicy? workstationLockPolicy = null,
        IAwakeService? awakeService = null,
        ISystemPowerController? powerController = null,
        CustomPointerService? customPointerService = null,
        IAppLog? appLog = null,
        IClipboardTextWriter? clipboardTextWriter = null)
    {
        _pairingManager = pairingManager;
        _webHost = webHost;
        _workstationLockPolicy = workstationLockPolicy ?? webHost.WorkstationLockPolicy;
        _powerController = powerController ?? webHost.PowerController;
        _awakeService = awakeService ?? webHost.AwakeService;
        _appLog = appLog ?? webHost.AppLog;
        _clipboardTextWriter = clipboardTextWriter ?? new WindowsClipboardTextWriter();
        _customPointerService = customPointerService ?? new CustomPointerService();
        _usePublicScreenshotPairingUrl = usePublicScreenshotPairingUrl;
        _serverUrl = webHost.ServerUrl;
        _usesServerUrlAsClientUrl = string.IsNullOrWhiteSpace(clientUrl);
        _initialClientUrl = string.IsNullOrWhiteSpace(clientUrl) ? webHost.ServerUrl : clientUrl.TrimEnd('/');
        _clientUrl = _initialClientUrl;
        _pairingCode = CreatePairingCode();

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
        _pairingManager.PairingCodeInvalidated += OnPairingCodeInvalidated;
        AppThemeSettings.Changed += OnThemeChanged;
        _awakeService.StateChanged += OnAwakeStateChanged;
        IsVisibleChanged += OnWindowIsVisibleChanged;
        RefreshStatusText();
        RefreshNavigationTheme();
    }

    public string PairingUrl => _pairingCode.Url;

    public string ServerUrl => _serverUrl;

    public void ShowPage(HostPage page)
    {
        SelectPage(page);
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ShowPreferencesSectionForScreenshot(string sectionTitle)
    {
        ShowPage(HostPage.Preferences);
        if (PageContent.Content is not PreferencesPageView preferences)
        {
            return;
        }

        preferences.FindSection(sectionTitle)?.SetCurrentValue(Expander.IsExpandedProperty, true);
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
        _pairingManager.PairingCodeInvalidated -= OnPairingCodeInvalidated;
        AppThemeSettings.Changed -= OnThemeChanged;
        _awakeService.StateChanged -= OnAwakeStateChanged;
        IsVisibleChanged -= OnWindowIsVisibleChanged;
        base.OnClosed(e);
    }

    private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && _pageNeedsRefresh)
        {
            SelectPage(_activePage);
        }
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
        _pageNeedsRefresh = false;
        RefreshStatusText();
        RefreshNavigationTheme();

        switch (page)
        {
            case HostPage.Connect:
                RefreshPairingCodeIfDue(DateTimeOffset.UtcNow);
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
                RestorePreferencesScrollPosition();
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
        _clipboardTextWriter.WriteText(value);
        ShowToast(confirmation, "Clipboard");
    }

    private void ShowToast(string message, string? title = null)
    {
        if (_toast is not null)
        {
            MainContentRoot.Children.Remove(_toast);
        }

        _toastTimer?.Stop();
        _toast = new Border
        {
            Tag = title ?? GetToastTitle(),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = (Brush)Resources["SurfaceRaisedBrush"],
            BorderBrush = (Brush)Resources["AccentBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, UiTokens.SpaceSm),
            Child = new TextBlock
            {
                Text = message,
                Foreground = (Brush)Resources["TextBrush"],
                FontWeight = FontWeights.SemiBold
            }
        };
        Grid.SetRow(_toast, 2);
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

    private string GetToastTitle() => _activePage switch
    {
        HostPage.Connect => "Connect",
        HostPage.Devices => "Devices",
        HostPage.Connection => "Connection",
        HostPage.Preferences => "Preferences",
        HostPage.Diagnostics => "Diagnostics",
        _ => "Voltura Air"
    };

    private CheckBox CreateCheckBox(string text, bool isChecked)
    {
        return new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            Foreground = (Brush)Resources["TextBrush"]
        };
    }

    internal static BitmapSource CreateQrSource(string url)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAir-256.png");
        using var icon = File.Exists(iconPath) ? new System.Drawing.Bitmap(iconPath) : null;
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using var code = new QRCode(data);
        using var bitmap = code.GetGraphic(
            18,
            System.Drawing.Color.Black,
            System.Drawing.Color.White,
            icon,
            iconSizePercent: 15,
            iconBorderWidth: 6,
            drawQuietZones: true,
            iconBackgroundColor: System.Drawing.Color.White);
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            _ = DeleteObject(handle);
        }
    }

    private static void OpenProductSite()
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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

    private enum PermissionKind
    {
        PcSleep,
        VolumeControl,
        PresentationControl,
        RemoteAppLaunch,
        UrlOpen,
        PcLock,
        BlackoutDisplay,
        DisplayOff,
        ScreenSaver,
        AwakeControl,
        ClipboardRead,
        SignOut,
        Restart,
        Shutdown
    }

    public void ShowAwakePreferences()
    {
        _preferencesSectionToOpen = "Keep awake";
        ShowPage(HostPage.Preferences);
    }

    private void OnAwakeStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_activePage == HostPage.Preferences && IsVisible)
            {
                RefreshPreferencesPage();
            }
            else if (_activePage == HostPage.Preferences)
            {
                RememberExpandedPreferencesSection();
                _pageNeedsRefresh = true;
            }
        });
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
