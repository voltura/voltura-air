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
    private Grid BuildConnectPage()
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

    private Grid BuildDevicesPage()
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

    private Grid BuildConnectionPage()
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

    private ScrollViewer BuildPreferencesPage()
    {
        _isLoadingPreferences = true;
        var root = new ScrollViewer
        {
            // Reserve the scrollbar gutter so expanding a section never
            // changes the accordion width.
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = CreateSectionPanel()
        };
        var panel = (StackPanel)root.Content;
        var sections = new List<Expander>();
        var globalPermissions = AppPermissionSettings.Load();

        var applicationPanel = AddPreferencesSection(panel, sections, "Application");
        var start = CreateCheckBox("Start Voltura Air when I sign in to Windows", AppStartupSettings.IsEnabled());
        start.Checked += (_, _) => AppStartupSettings.SetEnabled(true);
        start.Unchecked += (_, _) => AppStartupSettings.SetEnabled(false);
        applicationPanel.Children.Add(start);

        var startHidden = CreateCheckBox("Start Voltura Air hidden in the tray", AppWindowSettings.StartHiddenInTray());
        startHidden.Checked += (_, _) => AppWindowSettings.SetStartHiddenInTray(true);
        startHidden.Unchecked += (_, _) => AppWindowSettings.SetStartHiddenInTray(false);
        applicationPanel.Children.Add(startHidden);

        var notify = CreateCheckBox("Show connection status notifications", AppNotificationSettings.ShowConnectionStatusNotifications());
        notify.Checked += (_, _) => AppNotificationSettings.SetShowConnectionStatusNotifications(true);
        notify.Unchecked += (_, _) => AppNotificationSettings.SetShowConnectionStatusNotifications(false);
        applicationPanel.Children.Add(notify);

        var showOnDisconnect = CreateCheckBox("Show Voltura Air when the last device disconnects", AppNotificationSettings.ShowPairingWindowOnDisconnect());
        showOnDisconnect.Checked += (_, _) => AppNotificationSettings.SetShowPairingWindowOnDisconnect(true);
        showOnDisconnect.Unchecked += (_, _) => AppNotificationSettings.SetShowPairingWindowOnDisconnect(false);
        applicationPanel.Children.Add(showOnDisconnect);

        AddApplicationLoggingSettings(applicationPanel);

        var appearancePanel = AddPreferencesSection(panel, sections, "Appearance");
        var activeTheme = AppThemeSettings.GetMode();
        var systemTheme = CreateSegmentButton("System", activeTheme == AppThemeMode.System);
        var lightTheme = CreateSegmentButton("Light", activeTheme == AppThemeMode.Light);
        var darkTheme = CreateSegmentButton("Dark", activeTheme == AppThemeMode.Dark);
        WireSegmentGroup(systemTheme, lightTheme, darkTheme);
        systemTheme.Click += (_, _) => SetThemeMode(AppThemeMode.System);
        lightTheme.Click += (_, _) => SetThemeMode(AppThemeMode.Light);
        darkTheme.Click += (_, _) => SetThemeMode(AppThemeMode.Dark);
        appearancePanel.Children.Add(CreateSegmentRow(systemTheme, lightTheme, darkTheme));

        var trackpadPanel = AddPreferencesSection(panel, sections, "Trackpad defaults");
        trackpadPanel.Children.Add(CreateMutedText("Default pointer speed for paired devices. Device-specific overrides take precedence."));
        AddGlobalPointerSpeedSetting(trackpadPanel);

        var customPointerPanel = AddPreferencesSection(panel, sections, "Custom pointer");
        AddCustomPointerSetting(customPointerPanel);

        var remotePanel = AddPreferencesSection(panel, sections, "Remote defaults");
        remotePanel.Children.Add(CreateMutedText("Choose the initial Remote mode for newly connected phones. Mobile settings can still override this per PC."));
        var activeRemoteMode = AppRemoteSettings.GetDefaultRemoteMode();
        var standardRemote = CreateSegmentButton("Standard", activeRemoteMode == AppRemoteMode.Standard);
        var youtubeRemote = CreateSegmentButton("YouTube", activeRemoteMode == AppRemoteMode.Youtube);
        var kodiRemote = CreateSegmentButton("Kodi", activeRemoteMode == AppRemoteMode.Kodi);
        WireSegmentGroup(standardRemote, youtubeRemote, kodiRemote);
        standardRemote.Click += (_, _) => SetDefaultRemoteMode(AppRemoteMode.Standard);
        youtubeRemote.Click += (_, _) => SetDefaultRemoteMode(AppRemoteMode.Youtube);
        kodiRemote.Click += (_, _) => SetDefaultRemoteMode(AppRemoteMode.Kodi);
        remotePanel.Children.Add(CreateLabel("Default remote mode"));
        remotePanel.Children.Add(CreateSegmentRow(standardRemote, youtubeRemote, kodiRemote));
        AddYoutubeUrlSetting(remotePanel);

        var appLaunchPanel = AddPreferencesSection(panel, sections, "Application launch buttons");
        var appLaunchSection = (Expander)panel.Children[^1];
        AddAppLaunchSettings(appLaunchPanel);
        if (_openAppLaunchPreferences)
        {
            appLaunchSection.IsExpanded = true;
            _openAppLaunchPreferences = false;
        }

        var textDestinationPanel = AddPreferencesSection(panel, sections, "Text destination");
        AddTextDestinationSettings(textDestinationPanel);

        var awakePanel = AddPreferencesSection(panel, sections, "Keep awake");
        var awakeSection = (Expander)panel.Children[^1];
        AddAwakeSettings(awakePanel);
        if (_openAwakePreferences)
        {
            awakeSection.IsExpanded = true;
            _openAwakePreferences = false;
        }

        var permissionsPanel = AddPreferencesSection(panel, sections, "Global permissions");
        var allowClientControl = CreateCheckBox("Allow paired devices to control Voltura Air host", AppClientControlSettings.IsEnabled());
        allowClientControl.Checked += (_, _) => AppClientControlSettings.SetEnabled(true);
        allowClientControl.Unchecked += (_, _) => AppClientControlSettings.SetEnabled(false);
        permissionsPanel.Children.Add(allowClientControl);
        permissionsPanel.Children.Add(CreateMutedText("When off, paired devices cannot inject input into Voltura Air itself. They can still control Windows and other permitted apps."));
        var sleep = CreateCheckBox("Allow paired devices to request PC sleep", globalPermissions.AllowPcSleep);
        var volume = CreateCheckBox("Allow paired devices to control volume", globalPermissions.AllowVolumeControl);
        var presentation = CreateCheckBox("Allow paired devices to control presentations", globalPermissions.AllowPresentationControl);
        var remoteLaunch = CreateCheckBox("Allow paired devices to start applications", globalPermissions.AllowRemoteAppLaunch);
        var urlOpen = CreateCheckBox("Allow paired devices to open web addresses", globalPermissions.AllowUrlOpen);
        var pcLock = CreateCheckBox("Allow paired devices to lock the PC", globalPermissions.AllowPcLock);
        var blackoutDisplay = CreateCheckBox("Allow paired devices to blackout displays", globalPermissions.AllowBlackoutDisplay);
        var displayOff = CreateCheckBox("Allow paired devices to turn off displays", globalPermissions.AllowDisplayOff);
        var screenSaver = CreateCheckBox("Allow paired devices to start the screen saver", globalPermissions.AllowScreenSaver);
        var awakeControl = CreateCheckBox("Allow paired devices to control Keep awake", globalPermissions.AllowAwakeControl);
        var clipboardRead = CreateCheckBox("Allow paired devices to read the PC clipboard", globalPermissions.AllowClipboardRead);
        var signOut = CreateCheckBox("Allow paired devices to sign out", globalPermissions.AllowSignOut);
        var restart = CreateCheckBox("Allow paired devices to restart the PC", globalPermissions.AllowRestart);
        var shutdown = CreateCheckBox("Allow paired devices to shut down the PC", globalPermissions.AllowShutdown);
        void SavePermissions() => SaveGlobalPermissions(sleep, volume, presentation, remoteLaunch, urlOpen, pcLock, blackoutDisplay, displayOff, screenSaver, awakeControl, clipboardRead, signOut, restart, shutdown);
        foreach (var permission in new[] { sleep, volume, presentation, remoteLaunch, urlOpen, pcLock, blackoutDisplay, displayOff, screenSaver, awakeControl, clipboardRead, signOut, restart, shutdown })
        {
            permission.Checked += (_, _) => SavePermissions();
            permission.Unchecked += (_, _) => SavePermissions();
        }
        permissionsPanel.Children.Add(sleep);
        permissionsPanel.Children.Add(volume);
        permissionsPanel.Children.Add(presentation);
        permissionsPanel.Children.Add(remoteLaunch);
        permissionsPanel.Children.Add(urlOpen);
        permissionsPanel.Children.Add(pcLock);
        permissionsPanel.Children.Add(blackoutDisplay);
        permissionsPanel.Children.Add(displayOff);
        if (_powerController.IsActionAvailable(SystemPowerActions.ScreenSaver))
        {
            permissionsPanel.Children.Add(screenSaver);
        }
        permissionsPanel.Children.Add(awakeControl);
        permissionsPanel.Children.Add(clipboardRead);
        permissionsPanel.Children.Add(signOut);
        permissionsPanel.Children.Add(restart);
        permissionsPanel.Children.Add(shutdown);
        permissionsPanel.Children.Add(CreateMutedText("Display off and session-ending actions require hold-to-confirm on the mobile device."));
        permissionsPanel.Children.Add(CreateDetailsDisclosure("global permissions", "Lock and blackout are enabled by default. The screen-saver permission appears when Windows has a screen saver configured. Opening web addresses, reading the PC clipboard, display off, sign out, restart, and shut down require explicit host approval."));

        var windowsLockingPanel = AddPreferencesSection(panel, sections, "Windows locking");
        AddWindowsLockPolicySetting(windowsLockingPanel);

        var developerPanel = AddPreferencesSection(panel, sections, "Developer tools");
        var developerMode = CreateCheckBox("Developer mode", AppDeveloperSettings.DeveloperMode());
        developerMode.Checked += (_, _) => AppDeveloperSettings.SetDeveloperMode(true);
        developerMode.Unchecked += (_, _) => AppDeveloperSettings.SetDeveloperMode(false);
        developerPanel.Children.Add(developerMode);

        var gestureDebug = CreateCheckBox("Show gesture debug screen in the mobile app", AppDeveloperSettings.EnableGestureDebug());
        gestureDebug.Checked += (_, _) => AppDeveloperSettings.SetEnableGestureDebug(true);
        gestureDebug.Unchecked += (_, _) => AppDeveloperSettings.SetEnableGestureDebug(false);
        developerPanel.Children.Add(gestureDebug);

        _isLoadingPreferences = false;
        return root;
    }

}
