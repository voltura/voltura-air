using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Features.Connect;
using VolturaAir.Host.Features.Connection;
using VolturaAir.Host.Features.CustomScreens;
using VolturaAir.Host.Features.Devices;
using VolturaAir.Host.Features.Diagnostics;
using VolturaAir.Host.Features.Preferences;
using VolturaAir.Host.Features.Presentations;
using VolturaAir.Host.Ui;

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
    private readonly CustomScreensPageController _customScreensPage;
    private readonly PresentationsPageController _presentationsPage;
    private readonly ConnectionPageController _connectionPage;
    private readonly PreferencesPageController _preferencesPage;
    private readonly DiagnosticsPageController _diagnosticsPage;
    private readonly MainWindowNavigationController _navigation;
    private readonly OwnedDispatcherAction _connectionChangedAction;
    private readonly OwnedDispatcherAction _pairingCodeInvalidatedAction;
    private readonly OwnedDispatcherAction _deviceProfileChangedAction;
    private readonly OwnedDispatcherAction _themeChangedAction;
    private readonly OwnedDispatcherAction _awakeStateChangedAction;
    private bool _pageNeedsRefresh = true;
    private bool _allowClose;

    internal MainWindow(
        PairingManager pairingManager,
        WebHostService webHost,
        string? clientUrl,
        bool usePublicScreenshotPairingUrl = false,
        IWorkstationLockPolicy? workstationLockPolicy = null,
        IAwakeService? awakeService = null,
        ISystemPowerController? powerController = null,
        ICursorOverrideController? cursorOverrides = null,
        IAppLog? appLog = null,
        IClipboardTextWriter? clipboardTextWriter = null,
        Action? requestRestart = null)
    {
        _pairingManager = pairingManager;
        _awakeService = awakeService ?? webHost.AwakeService;
        var effectiveLockPolicy = workstationLockPolicy ?? webHost.WorkstationLockPolicy;
        var effectivePowerController = powerController ?? webHost.PowerController;
        var effectiveCursorOverrides = cursorOverrides ?? InertCursorOverrideController.Instance;
        var effectiveAppLog = appLog ?? webHost.AppLog;

        InitializeComponent();
        WindowWorkAreaPlacement.ConstrainAndCenterOnFirstLoad(this);
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
        var customScreenActivityLog =
            new CustomScreenEditorActivityLog(effectiveAppLog);
        var customScreenPreview =
            new CustomScreenBrowserPreviewLauncher(
                webHost.Port,
                pairingManager: pairingManager);
        _customScreensPage = new CustomScreensPageController(
            this,
            webHost.CustomScreenService,
            pairingManager,
            customScreenPreview.Open,
            customScreenPreview.CloseAll,
            customScreenActivityLog,
            message => _toasts.Show(message));
        _presentationsPage = new PresentationsPageController(
            webHost.PresentationReportStore,
            webHost,
            SetPresentationReportHeader);
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
            effectiveCursorOverrides,
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

        _navigation = new MainWindowNavigationController(
            _visuals,
            new Dictionary<HostPage, System.Windows.Controls.Button>
            {
                [HostPage.Connect] = ConnectNavButton,
                [HostPage.Devices] = DevicesNavButton,
                [HostPage.CustomScreens] = CustomScreensNavButton,
                [HostPage.Presentations] = PresentationsNavButton,
                [HostPage.Connection] = ConnectionNavButton,
                [HostPage.Preferences] = PreferencesNavButton,
                [HostPage.Diagnostics] = DiagnosticsNavButton
            },
            PageTitleText,
            PageSubtitleText,
            PageTypeBadge,
            PageContent,
            _connectPage,
            _devicesPage,
            _customScreensPage,
            _presentationsPage,
            _connectionPage,
            _preferencesPage,
            _diagnosticsPage,
            RefreshStatusText);
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
        AppAppearanceSettings.HostControlDepthChanged += OnThemeChanged;
        AppDeveloperSettings.Changed += OnThemeChanged;
        _awakeService.StateChanged += OnAwakeStateChanged;
        IsVisibleChanged += OnWindowIsVisibleChanged;
        RefreshStatusText();
        _navigation.RefreshTheme();
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
        WindowFocusReset.AfterShow(this);
    }

    public void ShowPreferencesSectionForScreenshot(string sectionTitle)
    {
        ShowPage(HostPage.Preferences);
        if (PageContent.Content is PreferencesPageView preferences)
        {
            preferences.FindSection(sectionTitle)?.SetCurrentValue(Expander.IsExpandedProperty, true);
        }
    }

    public void ShowCustomScreenEditorForScreenshot()
    {
        ShowPage(HostPage.CustomScreens);
        WindowState = WindowState.Maximized;
        _customScreensPage.OpenFirstForScreenshot();
    }

    internal async Task OpenCatalogImportAsync(CatalogImportRequest request)
    {
        await Dispatcher.InvokeAsync(ShowCatalogImportPage);
        try
        {
            var bytes = await CatalogImportDownloader.DownloadAsync(request);
            await Dispatcher.InvokeAsync(() =>
            {
                ShowCatalogImportPage();
                _customScreensPage.ImportBytes(bytes);
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                ShowCatalogImportPage();
                ThemedConfirmationDialog.ShowInformation(
                    this,
                    "Custom screen catalog",
                    ex.Message,
                    ConfirmationTone.Warning);
            });
        }
    }

    private void ShowCatalogImportPage()
    {
        SelectPage(HostPage.CustomScreens);
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        WindowFocusReset.AfterShow(this);
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

    internal bool ShouldCloseAfterDeviceConnected() =>
        IsVisible &&
        (_navigation.ActivePage == HostPage.Connect || WindowState == WindowState.Minimized);

    public void UpdateServerUrl(string serverUrl)
    {
        _connectPage.UpdateServerUrl(serverUrl);
    }

    protected override void OnClosed(EventArgs e)
    {
        _customScreensPage.ClosePreviews();
        _pairingManager.ConnectionChanged -= OnConnectionChanged;
        _pairingManager.DeviceProfileChanged -= OnDeviceProfileChanged;
        _pairingManager.PairingCodeInvalidated -= OnPairingCodeInvalidated;
        AppThemeSettings.Changed -= OnThemeChanged;
        AppAppearanceSettings.HostControlDepthChanged -= OnThemeChanged;
        AppDeveloperSettings.Changed -= OnThemeChanged;
        _awakeService.StateChanged -= OnAwakeStateChanged;
        IsVisibleChanged -= OnWindowIsVisibleChanged;
        _connectionChangedAction.Dispose();
        _pairingCodeInvalidatedAction.Dispose();
        _deviceProfileChangedAction.Dispose();
        _themeChangedAction.Dispose();
        _awakeStateChangedAction.Dispose();
        _toasts.Dispose();
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

    private void RefreshConnectPagePresentation()
    {
        if (_navigation.ActivePage == HostPage.Connect && IsVisible)
        {
            SelectPage(HostPage.Connect);
        }
        else if (_navigation.ActivePage == HostPage.Connect)
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

    private void SetPreferencesTitle(string? sectionTitle)
    {
        if (_navigation.ActivePage == HostPage.Preferences)
        {
            PageTitleText.Text = string.IsNullOrWhiteSpace(sectionTitle)
                ? "Preferences"
                : $"Preferences > {sectionTitle}";
        }
    }

    private void SetDiagnosticsTitle(string viewTitle)
    {
        if (_navigation.ActivePage == HostPage.Diagnostics)
        {
            PageTitleText.Text = $"Diagnostics > {viewTitle}";
        }
    }

    private void SetPresentationReportHeader(PresentationReport? report)
    {
        if (_navigation.ActivePage != HostPage.Presentations)
        {
            return;
        }

        if (report is null)
        {
            PageTitleText.Text = "Presentations";
            PageSubtitleText.Text = "Saved presentations";
            PageSubtitleText.Visibility = Visibility.Visible;
            PageTypeBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var localStart = report.StartedAt.ToOffset(TimeSpan.FromMinutes(report.UtcOffsetMinutes));
        PageTitleText.Text = $"Presentations > {PresentationReportNames.DisplayName(report)}";
        PageSubtitleText.Text = $"{localStart:yyyy-MM-dd HH:mm} · {report.DeviceName}";
        PageSubtitleText.Visibility = Visibility.Visible;
        PageTypeBadge.Content = report.Target switch
        {
            "powerpoint" => "PowerPoint",
            "google-slides" => "Google Slides",
            "pdf" => "PDF / browser",
            _ => "Presentation"
        };
        PageTypeBadge.Visibility = Visibility.Visible;
    }

    private string GetToastTitle() => _navigation.GetToastTitle();

    private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && _pageNeedsRefresh)
        {
            SelectPage(_navigation.ActivePage);
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

        if (_navigation.ActivePage is
            HostPage.Connect or HostPage.Devices or HostPage.Diagnostics ||
            _pageNeedsRefresh)
        {
            SelectPage(_navigation.ActivePage);
        }
    }

    private void OnPairingCodeInvalidated(object? sender, EventArgs e) => _pairingCodeInvalidatedAction.Queue();

    private void OnDeviceProfileChanged(object? sender, EventArgs e) => _deviceProfileChangedAction.Queue();

    private void HandleDeviceProfileChanged()
    {
        if (_navigation.ActivePage == HostPage.Devices && IsVisible)
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
            if (_navigation.ActivePage == HostPage.Preferences)
            {
                _preferencesPage.RefreshPreservingState();
            }
            else
            {
                SelectPage(_navigation.ActivePage);
            }
        }
        else
        {
            _pageNeedsRefresh = true;
            _navigation.RefreshTheme();
        }
    }

    private void RefreshAfterSystemThemeChange()
    {
        if (IsVisible)
        {
            if (_navigation.ActivePage == HostPage.Preferences)
            {
                _preferencesPage.RefreshPreservingState();
            }
            else
            {
                SelectPage(_navigation.ActivePage);
            }
        }
        else
        {
            _pageNeedsRefresh = true;
            _navigation.RefreshTheme();
        }
    }

    private void OnAwakeStateChanged(object? sender, EventArgs e) => _awakeStateChangedAction.Queue();

    private void HandleAwakeStateChanged()
    {
        if (_navigation.ActivePage == HostPage.Preferences && IsVisible)
        {
            _preferencesPage.RefreshPreservingState();
        }
        else if (_navigation.ActivePage == HostPage.Preferences)
        {
            _preferencesPage.RememberViewState();
            _pageNeedsRefresh = true;
        }
    }

    private void SelectPage(HostPage page)
    {
        if (_navigation.TrySelect(page))
        {
            _pageNeedsRefresh = false;
        }
    }

    private void OnConnectNavClicked(object sender, RoutedEventArgs e) =>
        SelectPage(HostPage.Connect);

    private void OnDevicesNavClicked(object sender, RoutedEventArgs e) =>
        SelectPage(HostPage.Devices);

    private void OnCustomScreensNavClicked(object sender, RoutedEventArgs e) =>
        SelectPage(HostPage.CustomScreens);

    private void OnPresentationsNavClicked(object sender, RoutedEventArgs e) =>
        SelectPage(HostPage.Presentations);

    private void OnConnectionNavClicked(object sender, RoutedEventArgs e) =>
        SelectPage(HostPage.Connection);

    private void OnPreferencesNavClicked(object sender, RoutedEventArgs e) =>
        SelectPage(HostPage.Preferences);

    private void OnDiagnosticsNavClicked(object sender, RoutedEventArgs e) =>
        SelectPage(HostPage.Diagnostics);

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
