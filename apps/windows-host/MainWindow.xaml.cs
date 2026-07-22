using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Features.Connect;
using VolturaAir.Host.Features.Connection;
using VolturaAir.Host.Features.Devices;
using VolturaAir.Host.Features.Diagnostics;
using VolturaAir.Host.Features.Preferences;
using VolturaAir.Host.Ui;
using Button = System.Windows.Controls.Button;

namespace VolturaAir.Host;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WPF Window ownership is released deterministically from OnClosed.")]
public partial class MainWindow : Window
{
    private readonly PairingManager _pairingManager;
    private readonly IAwakeService _awakeService;
    private readonly HostVisualFactory _visuals;
    private readonly HostToastPresenter _toasts;
    private readonly ConnectPageController _connectPage;
    private readonly DevicesPageController _devicesPage;
    private readonly ConnectionPageController _connectionPage;
    private readonly PreferencesPageController _preferencesPage;
    private readonly DiagnosticsPageController _diagnosticsPage;
    private readonly CustomPointerService? _ownedCustomPointerService;
    private readonly List<Button> _navigationButtons;
    private readonly OwnedDispatcherAction _connectionChangedAction;
    private readonly OwnedDispatcherAction _pairingCodeInvalidatedAction;
    private readonly OwnedDispatcherAction _deviceProfileChangedAction;
    private readonly OwnedDispatcherAction _themeChangedAction;
    private readonly OwnedDispatcherAction _awakeStateChangedAction;
    private HostPage _activePage;
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
        IClipboardTextWriter? clipboardTextWriter = null,
        Action? requestRestart = null)
    {
        _pairingManager = pairingManager;
        _awakeService = awakeService ?? webHost.AwakeService;
        var effectiveLockPolicy = workstationLockPolicy ?? webHost.WorkstationLockPolicy;
        var effectivePowerController = powerController ?? webHost.PowerController;
        _ownedCustomPointerService = null;
        var effectiveCustomPointerService = customPointerService ?? (_ownedCustomPointerService = new CustomPointerService());
        var effectiveAppLog = appLog ?? webHost.AppLog;

        InitializeComponent();
        WpfTheme.Apply(this);
        WindowArtwork.Apply(this, SidebarAppIcon);
        _visuals = new HostVisualFactory(Resources);
        _toasts = new HostToastPresenter(MainContentRoot, _visuals, GetToastTitle);
        var clipboard = new HostClipboardFeedback(
            clipboardTextWriter ?? new WindowsClipboardTextWriter(),
            _toasts);

        _connectPage = new ConnectPageController(
            pairingManager,
            webHost,
            clientUrl,
            usePublicScreenshotPairingUrl,
            clipboard,
            RefreshConnectPagePresentation,
            () => SelectPage(HostPage.Connection));
        _devicesPage = new DevicesPageController(
            this,
            pairingManager,
            effectivePowerController,
            () => SelectPage(HostPage.Devices));
        _connectionPage = new ConnectionPageController(
            this,
            pairingManager,
            webHost,
            requestRestart ?? (static () => { }),
            effectiveAppLog);
        _preferencesPage = new PreferencesPageController(
            this,
            effectivePowerController,
            effectiveLockPolicy,
            _awakeService,
            effectiveCustomPointerService,
            effectiveAppLog,
            webHost.AppLaunchService,
            _visuals,
            _toasts,
            () => SelectPage(HostPage.Preferences),
            SetPreferencesTitle);
        var applicationLog = new ApplicationLogController(
            this,
            effectiveAppLog,
            _visuals,
            new AppLogVisualFactory(_visuals),
            clipboard,
            _toasts);
        _diagnosticsPage = new DiagnosticsPageController(
            pairingManager,
            webHost,
            effectiveLockPolicy,
            effectiveAppLog,
            applicationLog,
            clipboard,
            SetDiagnosticsTitle);

        _navigationButtons =
        [
            ConnectNavButton,
            DevicesNavButton,
            ConnectionNavButton,
            PreferencesNavButton,
            DiagnosticsNavButton
        ];
        _connectionChangedAction = new OwnedDispatcherAction(Dispatcher, HandleConnectionChanged);
        _pairingCodeInvalidatedAction = new OwnedDispatcherAction(Dispatcher, _connectPage.CreateNewCode);
        _deviceProfileChangedAction = new OwnedDispatcherAction(Dispatcher, HandleDeviceProfileChanged);
        _themeChangedAction = new OwnedDispatcherAction(Dispatcher, HandleThemeChanged);
        _awakeStateChangedAction = new OwnedDispatcherAction(Dispatcher, HandleAwakeStateChanged);
        WpfTheme.TrackAccessibilityChanges(this, RefreshAfterSystemThemeChange);

        _pairingManager.ConnectionChanged += OnConnectionChanged;
        _pairingManager.DeviceProfileChanged += OnDeviceProfileChanged;
        _pairingManager.PairingCodeInvalidated += OnPairingCodeInvalidated;
        AppThemeSettings.Changed += OnThemeChanged;
        _awakeService.StateChanged += OnAwakeStateChanged;
        IsVisibleChanged += OnWindowIsVisibleChanged;
        RefreshStatusText();
        RefreshNavigationTheme();
    }

    public string PairingUrl => _connectPage.PairingUrl;

    public string ServerUrl => _connectPage.ServerUrl;

    internal event EventHandler? HiddenToTray;

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
        if (PageContent.Content is PreferencesPageView preferences)
        {
            preferences.FindSection(sectionTitle)?.SetCurrentValue(Expander.IsExpandedProperty, true);
        }
    }

    public void ShowPairedStatus()
    {
        ShowPage(HostPage.Connect);
    }

    public void ShowAwakePreferences()
    {
        _preferencesPage.OpenSection("Keep awake");
        ShowPage(HostPage.Preferences);
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    public void UpdateServerUrl(string serverUrl)
    {
        _connectPage.UpdateServerUrl(serverUrl);
    }

    protected override void OnClosed(EventArgs e)
    {
        _pairingManager.ConnectionChanged -= OnConnectionChanged;
        _pairingManager.DeviceProfileChanged -= OnDeviceProfileChanged;
        _pairingManager.PairingCodeInvalidated -= OnPairingCodeInvalidated;
        AppThemeSettings.Changed -= OnThemeChanged;
        _awakeService.StateChanged -= OnAwakeStateChanged;
        IsVisibleChanged -= OnWindowIsVisibleChanged;
        _connectionChangedAction.Dispose();
        _pairingCodeInvalidatedAction.Dispose();
        _deviceProfileChangedAction.Dispose();
        _themeChangedAction.Dispose();
        _awakeStateChangedAction.Dispose();
        _toasts.Dispose();
        _ownedCustomPointerService?.Dispose();
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }

    private void SelectPage(HostPage page)
    {
        var previousPage = _activePage;
        if (previousPage == HostPage.Connection && page != HostPage.Connection && !_connectionPage.TryLeavePage())
        {
            return;
        }

        if (_activePage == HostPage.Devices && page != HostPage.Devices)
        {
            _devicesPage.ResetDisclosureState();
        }

        _activePage = page;
        _pageNeedsRefresh = false;
        RefreshStatusText();
        RefreshNavigationTheme();

        switch (page)
        {
            case HostPage.Connect:
                PageTitleText.Text = "Connect";
                PageSubtitleText.Text = "Pair a phone, tablet, or browser on the same network.";
                PageContent.Content = _connectPage.CreateView();
                break;
            case HostPage.Devices:
                PageTitleText.Text = "Devices";
                PageSubtitleText.Text = "Manage trusted devices, active connections, and per-device permissions.";
                PageContent.Content = _devicesPage.CreateView();
                break;
            case HostPage.Connection:
                PageTitleText.Text = "Connection";
                PageSubtitleText.Text = "Voltura Air selects connection settings automatically. Change them only if a device cannot connect.";
                PageContent.Content = _connectionPage.CreateView(preserveState: previousPage == HostPage.Connection);
                break;
            case HostPage.Preferences:
                PageTitleText.Text = "Preferences";
                PageSubtitleText.Text = "Startup, alerts, permissions, device defaults, and theme.";
                PageContent.Content = _preferencesPage.CreateView();
                _preferencesPage.RestoreScrollPosition();
                break;
            case HostPage.Diagnostics:
                PageTitleText.Text = "Diagnostics";
                PageSubtitleText.Text = "Review application activity or inspect system details for troubleshooting.";
                PageContent.Content = _diagnosticsPage.CreateView();
                break;
        }
    }

    private void RefreshConnectPagePresentation()
    {
        if (_activePage == HostPage.Connect && IsVisible)
        {
            SelectPage(HostPage.Connect);
        }
        else if (_activePage == HostPage.Connect)
        {
            _pageNeedsRefresh = true;
        }
    }

    private void RefreshStatusText()
    {
        NavStatusText.Text = _pairingManager.HasActiveController
            ? $"Connected to {_pairingManager.ActiveDeviceSummary}"
            : _pairingManager.IsPaired
                ? $"{_pairingManager.PairedDeviceCount} paired device{Plural(_pairingManager.PairedDeviceCount)}"
                : "Ready to pair";
    }

    private void RefreshNavigationTheme()
    {
        foreach (var button in _navigationButtons)
        {
            var isActive = button == GetButtonForPage(_activePage);
            button.Background = _visuals.Brush(isActive ? "AccentBrush" : "SurfaceRaisedBrush");
            button.Foreground = _visuals.Brush(isActive ? "AccentTextBrush" : "TextBrush");
            button.BorderBrush = _visuals.Brush(isActive ? "AccentBrush" : "BorderBrush");
        }
    }

    private Button GetButtonForPage(HostPage page) => page switch
    {
        HostPage.Connect => ConnectNavButton,
        HostPage.Devices => DevicesNavButton,
        HostPage.Connection => ConnectionNavButton,
        HostPage.Preferences => PreferencesNavButton,
        HostPage.Diagnostics => DiagnosticsNavButton,
        _ => ConnectNavButton
    };

    private void SetPreferencesTitle(string? sectionTitle)
    {
        if (_activePage == HostPage.Preferences)
        {
            PageTitleText.Text = string.IsNullOrWhiteSpace(sectionTitle)
                ? "Preferences"
                : $"Preferences > {sectionTitle}";
        }
    }

    private void SetDiagnosticsTitle(string viewTitle)
    {
        if (_activePage == HostPage.Diagnostics)
        {
            PageTitleText.Text = $"Diagnostics > {viewTitle}";
        }
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

    private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && _pageNeedsRefresh)
        {
            SelectPage(_activePage);
        }
    }

    private void OnConnectionChanged(object? sender, EventArgs e) => _connectionChangedAction.Queue();

    private void HandleConnectionChanged()
    {
        RefreshStatusText();
        if (!IsVisible)
        {
            _pageNeedsRefresh = true;
            return;
        }

        if (_activePage is HostPage.Connect or HostPage.Devices or HostPage.Diagnostics || _pageNeedsRefresh)
        {
            SelectPage(_activePage);
        }
    }

    private void OnPairingCodeInvalidated(object? sender, EventArgs e) => _pairingCodeInvalidatedAction.Queue();

    private void OnDeviceProfileChanged(object? sender, EventArgs e) => _deviceProfileChangedAction.Queue();

    private void HandleDeviceProfileChanged()
    {
        if (_activePage == HostPage.Devices && IsVisible)
        {
            _devicesPage.RefreshDeviceProfiles();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => _themeChangedAction.Queue();

    private void HandleThemeChanged()
    {
        WpfTheme.Apply(this);
        if (IsVisible)
        {
            if (_activePage == HostPage.Preferences)
            {
                _preferencesPage.RefreshPreservingState();
            }
            else
            {
                SelectPage(_activePage);
            }
        }
        else
        {
            _pageNeedsRefresh = true;
            RefreshNavigationTheme();
        }
    }

    private void RefreshAfterSystemThemeChange()
    {
        if (IsVisible)
        {
            if (_activePage == HostPage.Preferences)
            {
                _preferencesPage.RefreshPreservingState();
            }
            else
            {
                SelectPage(_activePage);
            }
        }
        else
        {
            _pageNeedsRefresh = true;
            RefreshNavigationTheme();
        }
    }

    private void OnAwakeStateChanged(object? sender, EventArgs e) => _awakeStateChangedAction.Queue();

    private void HandleAwakeStateChanged()
    {
        if (_activePage == HostPage.Preferences && IsVisible)
        {
            _preferencesPage.RefreshPreservingState();
        }
        else if (_activePage == HostPage.Preferences)
        {
            _preferencesPage.RememberViewState();
            _pageNeedsRefresh = true;
        }
    }

    private void OnConnectNavClicked(object sender, RoutedEventArgs e) => SelectPage(HostPage.Connect);
    private void OnDevicesNavClicked(object sender, RoutedEventArgs e) => SelectPage(HostPage.Devices);
    private void OnConnectionNavClicked(object sender, RoutedEventArgs e) => SelectPage(HostPage.Connection);
    private void OnPreferencesNavClicked(object sender, RoutedEventArgs e) => SelectPage(HostPage.Preferences);
    private void OnDiagnosticsNavClicked(object sender, RoutedEventArgs e) => SelectPage(HostPage.Diagnostics);

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}

public enum HostPage
{
    Connect,
    Devices,
    Connection,
    Preferences,
    Diagnostics
}
