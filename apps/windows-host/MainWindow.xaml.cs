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

    private readonly PairingManager _pairingManager;
    private readonly WebHostService _webHost;
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

    public MainWindow(PairingManager pairingManager, WebHostService webHost, string? clientUrl, bool usePublicScreenshotPairingUrl = false)
    {
        _pairingManager = pairingManager;
        _webHost = webHost;
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
                PageSubtitleText.Text = "Startup, notifications, global permissions, and appearance.";
                PageContent.Content = BuildPreferencesPage();
                break;
            case HostPage.Diagnostics:
                PageTitleText.Text = "Diagnostics";
                PageSubtitleText.Text = "Technical details useful for troubleshooting.";
                PageContent.Content = BuildDiagnosticsPage();
                break;
        }
    }

    private UIElement BuildConnectPage()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });

        var qrHost = new Border
        {
            Background = (Brush)Resources["QrBackgroundBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24),
            Child = new Image
            {
                Source = CreateQrSource(GetVisiblePairingUrl()),
                Stretch = Stretch.Uniform,
                MaxWidth = 560,
                MaxHeight = 560,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(qrHost, 0);
        root.Children.Add(qrHost);

        var side = CreateSectionPanel();
        Grid.SetColumn(side, 2);
        side.Children.Add(CreateCardText("Current status", GetConnectionStatus(), emphasize: true));
        if (!string.IsNullOrWhiteSpace(_webHost.AddressSelectionWarning))
        {
            side.Children.Add(CreateNotice(_webHost.AddressSelectionWarning, isError: false));
        }
        if (!string.IsNullOrWhiteSpace(_webHost.PortSelectionWarning))
        {
            side.Children.Add(CreateNotice(_webHost.PortSelectionWarning, isError: false));
        }

        var details = new Expander
        {
            Header = "Details",
            IsExpanded = false,
            Foreground = (Brush)Resources["TextBrush"],
            Background = (Brush)Resources["WindowBrush"],
            Margin = new Thickness(0, 4, 0, 0),
            Content = new StackPanel
            {
                Margin = new Thickness(0, 12, 0, 0),
                Children =
                {
                    CreateCardText("Pairing link", GetVisiblePairingUrl(), monospace: true),
                    CreateCardText("Host URL", _webHost.ServerUrl, monospace: true),
                    CreateCardText("Selected IP", _webHost.AdvertisedHostAddress, monospace: true),
                    CreateCardText("Selected port", _webHost.Port.ToString(CultureInfo.InvariantCulture), monospace: true)
                }
            }
        };
        side.Children.Add(details);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        actions.Children.Add(CreateButton("New code", (_, _) => NewPairing(), primary: true));
        actions.Children.Add(CreateButton("Copy link", (_, _) => CopyToClipboard(GetVisiblePairingUrl(), "Link copied")));
        side.Children.Add(actions);
        root.Children.Add(side);
        return root;
    }

    private UIElement BuildDevicesPage()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _devicesList = CreateModernList(GetDeviceItems(), CreateDeviceListRow);
        _devicesList.SelectionChanged += (_, _) => RefreshDeviceDetails();
        Grid.SetColumn(_devicesList, 0);
        root.Children.Add(_devicesList);

        _deviceDetailsPanel = CreateSectionPanel();
        Grid.SetColumn(_deviceDetailsPanel, 2);
        root.Children.Add(_deviceDetailsPanel);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(CreateButton("Clean up duplicates", (_, _) => CleanUpDuplicates()));
        actions.Children.Add(CreateButton("Disconnect all", (_, _) => DisconnectAllDevices(), danger: true));
        Grid.SetColumn(actions, 0);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);
        RefreshDeviceDetails();
        return root;
    }

    private UIElement BuildConnectionPage()
    {
        var settings = AppNetworkSettings.Load();
        var candidates = LanAddressSelector.GetCandidates();
        var selection = LanAddressSelector.Select(candidates, settings);
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var networkPanel = new Grid
        {
            Background = (Brush)Resources["WindowBrush"]
        };
        networkPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        networkPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        networkPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        networkPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        networkPanel.Children.Add(CreateSectionHeading("Network"));
        var networkDescription = CreateMutedText("Automatic uses the best available private IPv4 address. Manual mode pins Voltura Air to a selected adapter.");
        Grid.SetRow(networkDescription, 1);
        networkPanel.Children.Add(networkDescription);
        _networkAutomaticButton = CreateSegmentButton("Automatic", settings.NetworkMode == NetworkSelectionMode.Automatic);
        _networkManualButton = CreateSegmentButton("Manual", settings.NetworkMode == NetworkSelectionMode.Manual);
        WireSegmentPair(_networkAutomaticButton, _networkManualButton);
        var networkModeRow = CreateSegmentRow(_networkAutomaticButton, _networkManualButton);
        Grid.SetRow(networkModeRow, 2);
        networkPanel.Children.Add(networkModeRow);

        _networkCandidateList = CreateModernList(GetCandidateItems(candidates, selection?.Candidate), CreateCandidateListRow);
        _networkCandidateList.MinHeight = 260;
        if (selection?.Candidate is not null)
        {
            _networkCandidateList.SelectedItem = _networkCandidateList.Items
                .OfType<ListBoxItem>()
                .FirstOrDefault(item => item.Tag is CandidateListItem candidate && candidate.Candidate.Address.Equals(selection.Candidate.Address));
        }

        Grid.SetRow(_networkCandidateList, 3);
        networkPanel.Children.Add(_networkCandidateList);
        Grid.SetColumn(networkPanel, 0);
        root.Children.Add(networkPanel);

        var portPanel = CreateSectionPanel();
        Grid.SetColumn(portPanel, 2);
        portPanel.Children.Add(CreateSectionHeading("Port"));
        portPanel.Children.Add(CreateMutedText("Automatic is recommended. Manual port changes apply after restarting Voltura Air."));
        _portAutomaticButton = CreateSegmentButton("Automatic", settings.PortMode == PortSelectionMode.Automatic);
        _portManualButton = CreateSegmentButton("Manual", settings.PortMode == PortSelectionMode.Manual);
        WireSegmentPair(_portAutomaticButton, _portManualButton);
        _portAutomaticButton.Click += (_, _) => UpdatePortInputState();
        _portManualButton.Click += (_, _) => UpdatePortInputState();
        portPanel.Children.Add(CreateSegmentRow(_portAutomaticButton, _portManualButton));
        portPanel.Children.Add(CreateLabel("Manual port number"));
        _manualPortTextBox = new TextBox
        {
            Text = (settings.ManualPort ?? _webHost.Port).ToString(CultureInfo.InvariantCulture),
            Margin = new Thickness(0, 4, 0, 4)
        };
        _manualPortTextBox.PreviewTextInput += OnManualPortPreviewTextInput;
        _manualPortTextBox.TextChanged += OnManualPortTextChanged;
        WpfDataObject.AddPastingHandler(_manualPortTextBox, OnManualPortPaste);
        portPanel.Children.Add(_manualPortTextBox);
        _manualPortValidationText = CreateMutedText(string.Empty);
        _manualPortValidationText.Margin = new Thickness(0, 0, 0, 12);
        portPanel.Children.Add(_manualPortValidationText);
        UpdatePortInputState();
        portPanel.Children.Add(CreateCardText("Current host URL", _webHost.ServerUrl, monospace: true));
        portPanel.Children.Add(CreateCardText("Selected IP", _webHost.AdvertisedHostAddress, monospace: true));
        portPanel.Children.Add(CreateCardText("Selected port", _webHost.Port.ToString(CultureInfo.InvariantCulture), monospace: true));
        _connectionStatusText = CreateMutedText(selection?.Warning ?? _webHost.AddressSelectionWarning ?? _webHost.PortSelectionWarning ?? string.Empty);
        portPanel.Children.Add(_connectionStatusText);
        root.Children.Add(portPanel);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(CreateButton("Save", (_, _) => SaveConnectionSettings(), primary: true));
        actions.Children.Add(CreateButton("Refresh adapters", (_, _) => SelectPage(HostPage.Connection)));
        Grid.SetColumn(actions, 0);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);
        return root;
    }

    private UIElement BuildPreferencesPage()
    {
        _isLoadingPreferences = true;
        var root = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = CreateSectionPanel()
        };
        var panel = (StackPanel)root.Content;
        var globalPermissions = AppPermissionSettings.Load();

        var start = CreateCheckBox("Start Voltura Air when I sign in to Windows", AppStartupSettings.IsEnabled());
        start.Checked += (_, _) => AppStartupSettings.SetEnabled(true);
        start.Unchecked += (_, _) => AppStartupSettings.SetEnabled(false);
        panel.Children.Add(start);

        var startHidden = CreateCheckBox("Start Voltura Air hidden in the tray", AppWindowSettings.StartHiddenInTray());
        startHidden.Checked += (_, _) => AppWindowSettings.SetStartHiddenInTray(true);
        startHidden.Unchecked += (_, _) => AppWindowSettings.SetStartHiddenInTray(false);
        panel.Children.Add(startHidden);

        var notify = CreateCheckBox("Show connection status notifications", AppNotificationSettings.ShowConnectionStatusNotifications());
        notify.Checked += (_, _) => AppNotificationSettings.SetShowConnectionStatusNotifications(true);
        notify.Unchecked += (_, _) => AppNotificationSettings.SetShowConnectionStatusNotifications(false);
        panel.Children.Add(notify);

        var showOnDisconnect = CreateCheckBox("Show Voltura Air when the last device disconnects", AppNotificationSettings.ShowPairingWindowOnDisconnect());
        showOnDisconnect.Checked += (_, _) => AppNotificationSettings.SetShowPairingWindowOnDisconnect(true);
        showOnDisconnect.Unchecked += (_, _) => AppNotificationSettings.SetShowPairingWindowOnDisconnect(false);
        panel.Children.Add(showOnDisconnect);

        panel.Children.Add(CreateSectionHeading("Global permissions"));
        var sleep = CreateCheckBox("Allow paired devices to request PC sleep", globalPermissions.AllowPcSleep);
        var volume = CreateCheckBox("Allow paired devices to control volume", globalPermissions.AllowVolumeControl);
        sleep.Checked += (_, _) => SaveGlobalPermissions(sleep, volume);
        sleep.Unchecked += (_, _) => SaveGlobalPermissions(sleep, volume);
        volume.Checked += (_, _) => SaveGlobalPermissions(sleep, volume);
        volume.Unchecked += (_, _) => SaveGlobalPermissions(sleep, volume);
        panel.Children.Add(sleep);
        panel.Children.Add(volume);

        panel.Children.Add(CreateSectionHeading("Remote"));
        panel.Children.Add(CreateMutedText("Choose the initial Remote mode for newly connected phones. Mobile settings can still override this per PC."));
        var activeRemoteMode = AppRemoteSettings.GetDefaultRemoteMode();
        var standardRemote = CreateSegmentButton("Standard", activeRemoteMode == AppRemoteMode.Standard);
        var youtubeRemote = CreateSegmentButton("YouTube", activeRemoteMode == AppRemoteMode.Youtube);
        var kodiRemote = CreateSegmentButton("Kodi", activeRemoteMode == AppRemoteMode.Kodi);
        WireSegmentGroup(standardRemote, youtubeRemote, kodiRemote);
        standardRemote.Click += (_, _) => SetDefaultRemoteMode(AppRemoteMode.Standard);
        youtubeRemote.Click += (_, _) => SetDefaultRemoteMode(AppRemoteMode.Youtube);
        kodiRemote.Click += (_, _) => SetDefaultRemoteMode(AppRemoteMode.Kodi);
        panel.Children.Add(CreateLabel("Default remote mode"));
        panel.Children.Add(CreateSegmentRow(standardRemote, youtubeRemote, kodiRemote));

        panel.Children.Add(CreateSectionHeading("Developer tools"));
        var developerMode = CreateCheckBox("Developer mode", AppDeveloperSettings.DeveloperMode());
        developerMode.Checked += (_, _) => AppDeveloperSettings.SetDeveloperMode(true);
        developerMode.Unchecked += (_, _) => AppDeveloperSettings.SetDeveloperMode(false);
        panel.Children.Add(developerMode);

        var gestureDebug = CreateCheckBox("Show gesture debug screen in the mobile app", AppDeveloperSettings.EnableGestureDebug());
        gestureDebug.Checked += (_, _) => AppDeveloperSettings.SetEnableGestureDebug(true);
        gestureDebug.Unchecked += (_, _) => AppDeveloperSettings.SetEnableGestureDebug(false);
        panel.Children.Add(gestureDebug);

        panel.Children.Add(CreateSectionHeading("Appearance"));
        var activeTheme = AppThemeSettings.GetMode();
        var systemTheme = CreateSegmentButton("System", activeTheme == AppThemeMode.System);
        var lightTheme = CreateSegmentButton("Light", activeTheme == AppThemeMode.Light);
        var darkTheme = CreateSegmentButton("Dark", activeTheme == AppThemeMode.Dark);
        WireSegmentGroup(systemTheme, lightTheme, darkTheme);
        systemTheme.Click += (_, _) => SetThemeMode(AppThemeMode.System);
        lightTheme.Click += (_, _) => SetThemeMode(AppThemeMode.Light);
        darkTheme.Click += (_, _) => SetThemeMode(AppThemeMode.Dark);
        panel.Children.Add(CreateSegmentRow(systemTheme, lightTheme, darkTheme));

        _isLoadingPreferences = false;
        return root;
    }

    private UIElement BuildDiagnosticsPage()
    {
        var details = GetDiagnostics();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var rows = new StackPanel();
        foreach (var detail in details)
        {
            rows.Children.Add(CreateDiagnosticRow(detail));
        }

        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = rows
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(CreateButton("Copy diagnostics", (_, _) => CopyToClipboard(BuildDiagnosticsText(), "Diagnostics copied"), primary: true));
        actions.Children.Add(CreateButton("Open product page", (_, _) => OpenProductSite()));
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);
        return root;
    }

    private void RefreshDeviceDetails()
    {
        if (_deviceDetailsPanel is null)
        {
            return;
        }

        _deviceDetailsPanel.Children.Clear();
        if (_devicesList?.SelectedItem is not ListBoxItem { Tag: DeviceListItem selected })
        {
            _deviceDetailsPanel.Children.Add(CreateSectionHeading("Device details"));
            _deviceDetailsPanel.Children.Add(CreateMutedText("Select a device to manage connection and permissions."));
            return;
        }

        var device = _pairingManager.GetDevices().FirstOrDefault(item => item.ClientId == selected.ClientId);
        if (device is null)
        {
            return;
        }

        _deviceDetailsPanel.Children.Add(CreateSectionHeading(device.DeviceName));
        _deviceDetailsPanel.Children.Add(CreateMutedText(selected.Status));
        _deviceDetailsPanel.Children.Add(CreateCardText("Activity", selected.Activity));
        _deviceDetailsPanel.Children.Add(CreateCardText("Details", selected.Metadata.Length == 0 ? "No device metadata" : selected.Metadata));

        _deviceDetailsPanel.Children.Add(CreateSectionHeading("Permissions"));
        AddPermissionChoices(_deviceDetailsPanel, device, "PC sleep", PermissionKind.PcSleep);
        AddPermissionChoices(_deviceDetailsPanel, device, "Volume control", PermissionKind.VolumeControl);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        actions.Children.Add(CreateButton(device.IsActive ? "Disconnect" : "Remove", (_, _) =>
        {
            _pairingManager.DisconnectDevice(device.ClientId);
            SelectPage(HostPage.Devices);
        }, danger: true));
        _deviceDetailsPanel.Children.Add(actions);
    }

    private void AddPermissionChoices(StackPanel parent, PairedDeviceStatus device, string title, PermissionKind kind)
    {
        parent.Children.Add(CreateLabel(title));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var current = kind == PermissionKind.PcSleep ? device.PermissionOverrides.AllowPcSleep : device.PermissionOverrides.AllowVolumeControl;
        row.Children.Add(CreateButton("Use global", (_, _) => SetDevicePermission(device.ClientId, kind, null), primary: current is null));
        row.Children.Add(CreateButton("Allow", (_, _) => SetDevicePermission(device.ClientId, kind, true), primary: current == true));
        row.Children.Add(CreateButton("Block", (_, _) => SetDevicePermission(device.ClientId, kind, false), primary: current == false, danger: current == false));
        parent.Children.Add(row);
    }

    private void SetDevicePermission(string clientId, PermissionKind kind, bool? value)
    {
        var current = _pairingManager.GetDevicePermissionOverrides(clientId);
        var updated = kind == PermissionKind.PcSleep
            ? current with { AllowPcSleep = value }
            : current with { AllowVolumeControl = value };
        _pairingManager.SetDevicePermissionOverrides(clientId, updated);
        SelectPage(HostPage.Devices);
        if (_devicesList is not null)
        {
            _devicesList.SelectedItem = _devicesList.Items
                .OfType<ListBoxItem>()
                .FirstOrDefault(item => item.Tag is DeviceListItem device && device.ClientId == clientId);
        }
    }

    private void SaveGlobalPermissions(CheckBox sleep, CheckBox volume)
    {
        if (_isLoadingPreferences)
        {
            return;
        }

        AppPermissionSettings.Save(new HostPermissionSet(
            AllowPcSleep: sleep.IsChecked == true,
            AllowVolumeControl: volume.IsChecked == true));
    }

    private void SaveConnectionSettings()
    {
        if (_networkCandidateList is null ||
            _networkAutomaticButton is null ||
            _portAutomaticButton is null ||
            _manualPortTextBox is null ||
            _connectionStatusText is null)
        {
            return;
        }

        var current = AppNetworkSettings.Load();
        var networkMode = _networkAutomaticButton.IsChecked == true ? NetworkSelectionMode.Automatic : NetworkSelectionMode.Manual;
        var portMode = _portAutomaticButton.IsChecked == true ? PortSelectionMode.Automatic : PortSelectionMode.Manual;
        string? manualAddress = null;
        string? manualAdapterId = null;
        string? manualAdapterName = null;
        if (networkMode == NetworkSelectionMode.Manual)
        {
            if (_networkCandidateList.SelectedItem is not ListBoxItem { Tag: CandidateListItem selected })
            {
                ShowConnectionStatus("Choose a network address before saving manual mode.", isError: true);
                return;
            }

            manualAddress = selected.Candidate.Address.ToString();
            manualAdapterId = selected.Candidate.AdapterId;
            manualAdapterName = LanAddressSelector.GetAdapterDisplayName(selected.Candidate);
        }

        int? manualPort = null;
        if (portMode == PortSelectionMode.Manual)
        {
            if (!ValidateManualPortText(showEmptyWarning: true))
            {
                return;
            }

            if (!int.TryParse(_manualPortTextBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort))
            {
                ShowConnectionStatus(PortSelector.ManualPortRangeMessage, isError: true);
                return;
            }

            var portValidationError = PortSelector.GetManualPortValidationError(parsedPort);
            if (portValidationError is not null)
            {
                ShowConnectionStatus(portValidationError, isError: true);
                return;
            }

            if (parsedPort != _webHost.Port && !WebHostService.IsPortAvailable(parsedPort))
            {
                ShowConnectionStatus($"Port {parsedPort} is already in use.", isError: true);
                return;
            }

            manualPort = parsedPort;
        }

        var updated = current with
        {
            NetworkMode = networkMode,
            ManualHostAddress = manualAddress,
            ManualAdapterId = manualAdapterId,
            ManualAdapterName = manualAdapterName,
            PortMode = portMode,
            ManualPort = manualPort
        };
        AppNetworkSettings.Save(updated);

        var selection = LanAddressSelector.Select(LanAddressSelector.GetCandidates(), updated);
        var hostAddress = selection?.Address.ToString() ?? WebHostService.GetDnsLanAddressFallback() ?? "127.0.0.1";
        _webHost.UpdateAdvertisedHostAddress(hostAddress, selection?.Candidate);
        UpdateServerUrl(_webHost.ServerUrl);
        if (networkMode == NetworkSelectionMode.Automatic)
        {
            AppNetworkSettings.SetLastAutomaticHostAddress(hostAddress);
        }

        var status = selection?.Warning ?? "Connection settings saved.";
        if (portMode == PortSelectionMode.Manual && manualPort != _webHost.Port)
        {
            status = $"{status} Port change will apply after restarting Voltura Air.";
        }

        ShowConnectionStatus(status, selection?.Warning is not null);
    }

    private void ShowConnectionStatus(string message, bool isError)
    {
        if (_connectionStatusText is null)
        {
            return;
        }

        _connectionStatusText.Text = message;
        _connectionStatusText.Foreground = isError ? (Brush)Resources["DangerBrush"] : (Brush)Resources["AccentBrush"];
    }

    private void NewPairing()
    {
        _pairingUrl = CreatePairingUrl();
        if (_activePage == HostPage.Connect)
        {
            SelectPage(HostPage.Connect);
        }
    }

    private string CreatePairingUrl()
    {
        var token = _pairingManager.CreatePairingToken();
        var url = new UriBuilder(_clientUrl)
        {
            Query = $"t={Uri.EscapeDataString(token)}&v={Uri.EscapeDataString(AppVersion.Display)}"
        };

        if (!string.Equals(_clientUrl, _serverUrl, StringComparison.OrdinalIgnoreCase))
        {
            url.Query = $"{url.Query.TrimStart('?')}&h={Uri.EscapeDataString(CreateHostHint(_clientUrl, _serverUrl))}";
        }

        return url.Uri.ToString();
    }

    private string GetVisiblePairingUrl()
    {
        return _usePublicScreenshotPairingUrl ? ProductSiteUrl : _pairingUrl;
    }

    internal static string CreateHostHint(string clientUrl, string serverUrl)
    {
        if (Uri.TryCreate(clientUrl, UriKind.Absolute, out var clientUri) &&
            Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri) &&
            string.Equals(clientUri.Scheme, serverUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(clientUri.Host, serverUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return serverUri.Port.ToString(CultureInfo.InvariantCulture);
        }

        return serverUrl;
    }

    private void CleanUpDuplicates()
    {
        var candidates = _pairingManager.GetDuplicateCleanupCandidates();
        if (candidates.Count == 0)
        {
            SelectPage(HostPage.Devices);
            return;
        }

        var confirmed = ThemedConfirmationDialog.Show(
            this,
            "Clean up duplicates",
            $"Remove {candidates.Count} older disconnected duplicate pairing{(candidates.Count == 1 ? string.Empty : "s")}? Connected devices are kept.",
            "Clean up",
            "Cancel",
            ConfirmationTone.Question);
        if (confirmed)
        {
            _pairingManager.CleanUpDuplicateDevices();
            SelectPage(HostPage.Devices);
        }
    }

    private void DisconnectAllDevices()
    {
        if (_pairingManager.PairedDeviceCount == 0)
        {
            return;
        }

        var confirmed = ThemedConfirmationDialog.Show(
            this,
            "Disconnect all",
            "Disconnect and remove all paired devices?",
            "Disconnect all",
            "Cancel",
            ConfirmationTone.Warning);
        if (confirmed)
        {
            _pairingManager.ClearPairing();
            SelectPage(HostPage.Devices);
        }
    }

    private void RefreshStatusText()
    {
        NavStatusText.Text = _pairingManager.HasActiveController
            ? $"Connected: {_pairingManager.ActiveDeviceSummary}"
            : _pairingManager.IsPaired
                ? $"{_pairingManager.PairedDeviceCount} paired device{Plural(_pairingManager.PairedDeviceCount)}"
                : "Ready to pair";
    }

    private void RefreshNavigationTheme()
    {
        foreach (var button in _navigationButtons)
        {
            var isActive = button == GetButtonForPage(_activePage);
            button.Background = isActive ? (Brush)Resources["AccentBrush"] : (Brush)Resources["SurfaceRaisedBrush"];
            button.Foreground = isActive ? (Brush)Resources["AccentTextBrush"] : (Brush)Resources["TextBrush"];
            button.BorderBrush = isActive ? (Brush)Resources["AccentBrush"] : (Brush)Resources["BorderBrush"];
        }
    }

    private Button GetButtonForPage(HostPage page)
    {
        return page switch
        {
            HostPage.Connect => ConnectNavButton,
            HostPage.Devices => DevicesNavButton,
            HostPage.Connection => ConnectionNavButton,
            HostPage.Preferences => PreferencesNavButton,
            HostPage.Diagnostics => DiagnosticsNavButton,
            _ => ConnectNavButton
        };
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var pairedDeviceCount = _pairingManager.PairedDeviceCount;
            if (pairedDeviceCount != _lastPairedDeviceCount)
            {
                _lastPairedDeviceCount = pairedDeviceCount;
                NewPairing();
                return;
            }

            RefreshStatusText();
            if (_activePage is HostPage.Connect or HostPage.Devices or HostPage.Diagnostics)
            {
                SelectPage(_activePage);
            }
        });
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            WpfTheme.Apply(this);
            SelectPage(_activePage);
        });
    }

    private IReadOnlyList<DeviceListItem> GetDeviceItems()
    {
        return _pairingManager.GetDevices()
            .Select(device => new DeviceListItem(
                device.ClientId,
                device.DeviceName,
                device.IsActive ? "Connected" : "Not connected",
                GetDeviceActivityText(device),
                GetDeviceMetadataText(device)))
            .ToArray();
    }

    private IReadOnlyList<CandidateListItem> GetCandidateItems(IReadOnlyList<LanAddressCandidate> candidates, LanAddressCandidate? selectedCandidate)
    {
        var recommended = candidates.OrderByDescending(candidate => candidate.Score).FirstOrDefault();
        return candidates.Select(candidate =>
        {
            var status = candidate == recommended ? "Recommended" : candidate.IsLikelyVpnOrVirtual ? "Not recommended" : string.Empty;
            return new CandidateListItem(
                candidate,
                $"{GetAdapterTypeDisplayName(candidate)} - {GetAdapterDescription(candidate)}",
                candidate.Address.ToString(),
                candidate.Address.Equals(selectedCandidate?.Address) ? $"{status} selected".Trim() : status);
        }).ToArray();
    }

    private IReadOnlyList<DiagnosticItem> GetDiagnostics()
    {
        return
        [
            new("Voltura Air host version", AppVersion.Display),
            new("Voltura Air web client version", "copy mobile diagnostics for web client version"),
            new("PC name", Environment.MachineName),
            new("Selected adapter", _webHost.SelectedAdapterName),
            new("Selected IP", _webHost.AdvertisedHostAddress),
            new("Selected port", _webHost.Port.ToString(CultureInfo.InvariantCulture)),
            new("Host URL", _webHost.ServerUrl),
            new("Current WebSocket URL", _webHost.WebSocketUrl),
            new("Pairing state", GetPairingState()),
            new("Last error code", GetLastErrorCode()),
            new("Last error message", GetLastErrorMessage()),
            new("Paired device count", _pairingManager.PairedDeviceCount.ToString(CultureInfo.InvariantCulture)),
            new("Connected device count", _pairingManager.ActiveControllerCount.ToString(CultureInfo.InvariantCulture)),
            new("Paired devices", _pairingManager.PairedDeviceSummary),
            new("Active devices", _pairingManager.HasActiveController ? _pairingManager.ActiveDeviceSummary : "none"),
            new("Data folder", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltura Air")),
            new("Executable", Environment.ProcessPath ?? string.Empty)
        ];
    }

    private string BuildDiagnosticsText()
    {
        var lines = new List<string>
        {
            "Voltura Air diagnostics",
            $"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}"
        };
        lines.AddRange(GetDiagnostics().Select(detail => $"{detail.Name}: {RedactDiagnosticValue(detail.Value)}"));
        return string.Join(Environment.NewLine, lines);
    }

    private string GetPairingState()
    {
        if (_pairingManager.HasActiveController)
        {
            return "connected";
        }

        return _pairingManager.IsPaired ? "paired-not-connected" : "ready-to-pair";
    }

    private string GetLastErrorCode()
    {
        if (!string.IsNullOrWhiteSpace(_webHost.PortSelectionWarning))
        {
            return "VAIR-HOST-PORT-WARNING";
        }

        if (!string.IsNullOrWhiteSpace(_webHost.AddressSelectionWarning))
        {
            return "VAIR-HOST-NETWORK-WARNING";
        }

        return "none";
    }

    private string GetLastErrorMessage()
    {
        var messages = new[]
        {
            _webHost.AddressSelectionWarning,
            _webHost.PortSelectionWarning
        }.Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();

        return messages.Length == 0 ? "none" : string.Join(" ", messages);
    }

    private static string RedactDiagnosticValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.Contains("t=", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("pairToken", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("hash", StringComparison.OrdinalIgnoreCase))
        {
            return "[redacted]";
        }

        return value;
    }

    private string GetConnectionStatus()
    {
        return _pairingManager.IsPaired
            ? _pairingManager.HasActiveController
                ? $"Connected to {_pairingManager.ActiveDeviceSummary}"
                : $"{_pairingManager.PairedDeviceCount} paired device{Plural(_pairingManager.PairedDeviceCount)}. Ready for another."
            : "Waiting for a phone or tablet on the same network";
    }

    private ListBox CreateModernList<T>(IEnumerable<T> items, Func<T, UIElement> createRow)
    {
        var list = new ListBox
        {
            Style = (Style)Resources["ModernListBoxStyle"],
            ItemContainerStyle = (Style)Resources["ModernListBoxItemStyle"]
        };

        foreach (var item in items)
        {
            list.Items.Add(new ListBoxItem
            {
                Tag = item,
                Style = (Style)Resources["ModernListBoxItemStyle"],
                Content = createRow(item)
            });
        }

        return list;
    }

    private UIElement CreateDeviceListRow(DeviceListItem device)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.4, GridUnitType.Star) });

        grid.Children.Add(CreateListCell("Device", device.Name, 0, strong: true));
        grid.Children.Add(CreateListCell("Status", device.Status, 1));
        grid.Children.Add(CreateListCell("Last activity", device.Activity, 2));
        grid.Children.Add(CreateListCell("Details", string.IsNullOrWhiteSpace(device.Metadata) ? "No metadata" : device.Metadata, 3));
        return CreateListRowShell(grid);
    }

    private UIElement CreateCandidateListRow(CandidateListItem candidate)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.5, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(CreateListCell("Adapter", candidate.Adapter, 0, strong: true));
        grid.Children.Add(CreateListCell("Address", candidate.Address, 1, monospace: true));
        grid.Children.Add(CreateListCell("Status", string.IsNullOrWhiteSpace(candidate.Status) ? "Available" : candidate.Status, 2));
        return CreateListRowShell(grid);
    }

    private UIElement CreateDiagnosticRow(DiagnosticItem detail)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(CreateListCell("Name", detail.Name, 0, strong: true));
        grid.Children.Add(CreateListCell("Value", detail.Value, 1, monospace: true));
        var copy = CreateButton("Copy", (_, _) => CopyToClipboard(detail.Value, "Copied"));
        copy.Margin = new Thickness(12, 8, 0, 8);
        Grid.SetColumn(copy, 2);
        grid.Children.Add(copy);
        return CreateListRowShell(grid);
    }

    private UIElement CreateListCell(string label, string value, int column, bool strong = false, bool monospace = false)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["MutedTextBrush"]
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
            FontFamily = monospace ? new FontFamily("Cascadia Mono, Consolas") : SystemFonts.MessageFontFamily,
            Foreground = (Brush)Resources["TextBrush"]
        });
        Grid.SetColumn(stack, column);
        return stack;
    }

    private UIElement CreateListRowShell(UIElement content)
    {
        return new Border
        {
            Background = (Brush)Resources["SurfaceBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = content
        };
    }

    private ToggleButton CreateSegmentButton(string text, bool isChecked)
    {
        return new ToggleButton
        {
            Content = text,
            IsChecked = isChecked,
            Style = (Style)Resources["SegmentButtonStyle"]
        };
    }

    private StackPanel CreateSegmentRow(params ToggleButton[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 12)
        };
        foreach (var button in buttons)
        {
            row.Children.Add(button);
        }

        return row;
    }

    private static void WireSegmentPair(ToggleButton first, ToggleButton second)
    {
        WireSegmentGroup(first, second);
    }

    private static void WireSegmentGroup(params ToggleButton[] buttons)
    {
        foreach (var button in buttons)
        {
            button.Click += (_, _) =>
            {
                foreach (var candidate in buttons)
                {
                    candidate.IsChecked = ReferenceEquals(candidate, button);
                }
            };
        }
    }

    private void SetThemeMode(AppThemeMode mode)
    {
        if (!_isLoadingPreferences)
        {
            AppThemeSettings.SetMode(mode);
        }
    }

    private void SetDefaultRemoteMode(AppRemoteMode mode)
    {
        if (!_isLoadingPreferences)
        {
            AppRemoteSettings.SetDefaultRemoteMode(mode);
        }
    }

    private void UpdatePortInputState()
    {
        if (_manualPortTextBox is null)
        {
            return;
        }

        var enabled = _portManualButton?.IsChecked == true;
        _manualPortTextBox.IsEnabled = enabled;
        _manualPortTextBox.Opacity = enabled ? 1 : 0.62;
        if (enabled)
        {
            ValidateManualPortText(showEmptyWarning: false);
        }
        else
        {
            ShowManualPortValidation(string.Empty, isError: false);
        }
    }

    private void OnManualPortPreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        if (_manualPortTextBox is null)
        {
            return;
        }

        var proposed = GetProposedText(_manualPortTextBox, e.Text);
        if (proposed.Any(character => !char.IsDigit(character)) || proposed.Length > 5)
        {
            e.Handled = true;
            ShowManualPortValidation(proposed.Length > 5 ? PortSelector.ManualPortRangeMessage : "Port must use numbers only.", isError: true);
        }
    }

    private void OnManualPortPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(WpfDataFormats.Text) ||
            e.DataObject.GetData(WpfDataFormats.Text) is not string text ||
            text.Any(character => !char.IsDigit(character)) ||
            text.Length > 5)
        {
            e.CancelCommand();
            ShowManualPortValidation("Port must use numbers only and be between 49152 and 65535.", isError: true);
        }
    }

    private void OnManualPortTextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateManualPortText(showEmptyWarning: false);
    }

    private bool ValidateManualPortText(bool showEmptyWarning)
    {
        if (_manualPortTextBox is null)
        {
            return true;
        }

        var text = _manualPortTextBox.Text.Trim();
        if (text.Length == 0)
        {
            if (showEmptyWarning)
            {
                ShowManualPortValidation(PortSelector.ManualPortRangeMessage, isError: true);
            }

            return !showEmptyWarning;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
        {
            ShowManualPortValidation("Port must use numbers only.", isError: true);
            return false;
        }

        var message = PortSelector.GetManualPortValidationError(port);
        if (message is not null)
        {
            ShowManualPortValidation(message, isError: true);
            return false;
        }

        ShowManualPortValidation("Manual port looks valid.", isError: false);
        return true;
    }

    private void ShowManualPortValidation(string message, bool isError)
    {
        if (_manualPortValidationText is null)
        {
            return;
        }

        _manualPortValidationText.Text = message;
        _manualPortValidationText.Foreground = isError ? (Brush)Resources["DangerBrush"] : (Brush)Resources["MutedTextBrush"];
    }

    private static string GetProposedText(TextBox textBox, string replacement)
    {
        var text = textBox.Text;
        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;
        return text.Remove(selectionStart, selectionLength).Insert(selectionStart, replacement);
    }
    private StackPanel CreateSectionPanel()
    {
        return new StackPanel
        {
            Background = (Brush)Resources["WindowBrush"]
        };
    }

    private TextBlock CreateSectionHeading(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Resources["TextBrush"],
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private TextBlock CreateMutedText(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Resources["MutedTextBrush"],
            Margin = new Thickness(0, 0, 0, 10)
        };
    }

    private TextBlock CreateLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["TextBrush"],
            Margin = new Thickness(0, 12, 0, 0)
        };
    }

    private Border CreateCardText(string title, string text, bool emphasize = false, bool monospace = false)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["MutedTextBrush"]
        });
        stack.Children.Add(new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = emphasize ? 18 : 13,
            FontWeight = emphasize ? FontWeights.Bold : FontWeights.Normal,
            FontFamily = monospace ? new FontFamily("Cascadia Mono, Consolas") : SystemFonts.MessageFontFamily,
            Foreground = (Brush)Resources["TextBrush"]
        });

        return new Border
        {
            Background = (Brush)Resources["SurfaceBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
            Child = stack
        };
    }

    private Border CreateNotice(string text, bool isError)
    {
        return new Border
        {
            Background = (Brush)Resources["SurfaceBrush"],
            BorderBrush = isError ? (Brush)Resources["DangerBrush"] : (Brush)Resources["AccentBrush"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = isError ? (Brush)Resources["DangerBrush"] : (Brush)Resources["TextBrush"]
            }
        };
    }

    private Button CreateButton(string text, RoutedEventHandler handler, bool primary = false, bool danger = false)
    {
        var button = new Button
        {
            Content = text,
            Background = primary ? (Brush)Resources["AccentBrush"] : (Brush)Resources["SurfaceRaisedBrush"],
            Foreground = primary ? (Brush)Resources["AccentTextBrush"] : danger ? (Brush)Resources["DangerBrush"] : (Brush)Resources["TextBrush"],
            BorderBrush = primary ? (Brush)Resources["AccentBrush"] : (Brush)Resources["BorderBrush"]
        };
        button.Click += handler;
        return button;
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

    private static string GetDeviceActivityText(PairedDeviceStatus device)
    {
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

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private static void SetIcon(Window window)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAir.ico");
        if (File.Exists(iconPath))
        {
            window.Icon = BitmapFrame.Create(new Uri(iconPath));
        }
    }

    private void SetSidebarAppIcon()
    {
        var imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAir-256.png");
        if (File.Exists(imagePath))
        {
            SidebarAppIcon.Source = BitmapFrame.Create(new Uri(imagePath));
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private sealed record DeviceListItem(string ClientId, string Name, string Status, string Activity, string Metadata);

    private sealed record CandidateListItem(LanAddressCandidate Candidate, string Adapter, string Address, string Status);

    private sealed record DiagnosticItem(string Name, string Value);

    private enum PermissionKind
    {
        PcSleep,
        VolumeControl
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
