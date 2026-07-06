using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Image = System.Windows.Controls.Image;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using WpfDataObject = System.Windows.DataObject;

namespace VolturaAir.Host;

public partial class MainWindow
{
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
        var detailsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _deviceDetailsPanel
        };
        Grid.SetColumn(detailsScroll, 2);
        root.Children.Add(detailsScroll);

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

        panel.Children.Add(CreateSectionHeading("Application"));
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

        panel.Children.Add(CreateSectionHeading("Global permissions"));
        var allowClientControl = CreateCheckBox("Allow paired devices to control Voltura Air host", AppClientControlSettings.IsEnabled());
        allowClientControl.Checked += (_, _) => AppClientControlSettings.SetEnabled(true);
        allowClientControl.Unchecked += (_, _) => AppClientControlSettings.SetEnabled(false);
        panel.Children.Add(allowClientControl);
        panel.Children.Add(CreateMutedText("When this is off, paired devices can still control Windows, media, YouTube, Kodi, and other apps, but client-injected input is ignored while it would target this Voltura Air host window or tray menu. Native minimize, maximize, and close controls still work."));
        var sleep = CreateCheckBox("Allow paired devices to request PC sleep", globalPermissions.AllowPcSleep);
        var volume = CreateCheckBox("Allow paired devices to control volume", globalPermissions.AllowVolumeControl);
        var remoteLaunch = CreateCheckBox("Allow paired devices to start applications", globalPermissions.AllowRemoteAppLaunch);
        sleep.Checked += (_, _) => SaveGlobalPermissions(sleep, volume, remoteLaunch);
        sleep.Unchecked += (_, _) => SaveGlobalPermissions(sleep, volume, remoteLaunch);
        volume.Checked += (_, _) => SaveGlobalPermissions(sleep, volume, remoteLaunch);
        volume.Unchecked += (_, _) => SaveGlobalPermissions(sleep, volume, remoteLaunch);
        remoteLaunch.Checked += (_, _) => SaveGlobalPermissions(sleep, volume, remoteLaunch);
        remoteLaunch.Unchecked += (_, _) => SaveGlobalPermissions(sleep, volume, remoteLaunch);
        panel.Children.Add(sleep);
        panel.Children.Add(volume);
        panel.Children.Add(remoteLaunch);

        panel.Children.Add(CreateSectionHeading("Trackpad defaults"));
        panel.Children.Add(CreateMutedText("Default pointer speed for paired devices unless a device has its own override."));
        AddGlobalPointerSpeedSetting(panel);

        panel.Children.Add(CreateSectionHeading("Remote defaults"));
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
        AddYoutubeUrlSetting(panel);

        panel.Children.Add(CreateSectionHeading("Developer tools"));
        var developerMode = CreateCheckBox("Developer mode", AppDeveloperSettings.DeveloperMode());
        developerMode.Checked += (_, _) => AppDeveloperSettings.SetDeveloperMode(true);
        developerMode.Unchecked += (_, _) => AppDeveloperSettings.SetDeveloperMode(false);
        panel.Children.Add(developerMode);

        var gestureDebug = CreateCheckBox("Show gesture debug screen in the mobile app", AppDeveloperSettings.EnableGestureDebug());
        gestureDebug.Checked += (_, _) => AppDeveloperSettings.SetEnableGestureDebug(true);
        gestureDebug.Unchecked += (_, _) => AppDeveloperSettings.SetEnableGestureDebug(false);
        panel.Children.Add(gestureDebug);

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
}
