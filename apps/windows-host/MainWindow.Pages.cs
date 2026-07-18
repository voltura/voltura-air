using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VolturaAir.Host.Features.Connect;
using VolturaAir.Host.Features.Connection;
using VolturaAir.Host.Features.Devices;
using VolturaAir.Host.Features.Preferences;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Image = System.Windows.Controls.Image;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using WpfDataObject = System.Windows.DataObject;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private ConnectPageView BuildConnectPage()
    {
        var pairingLink = GetVisiblePairingUrl();
        return new ConnectPageView(
            CreateQrSource(pairingLink),
            GetConnectionStatus(),
            pairingLink,
            _webHost.ServerUrl,
            _webHost.AdvertisedHostAddress,
            _webHost.Port.ToString(CultureInfo.InvariantCulture),
            _webHost.AddressSelectionWarning,
            _webHost.PortSelectionWarning,
            _pairingCode.RefreshAt,
            NewPairing,
            () => CopyToClipboard(GetVisiblePairingUrl(), "Link copied"));
    }

    private DevicesPageView BuildDevicesPage()
    {
        var root = new DevicesPageView(
            GetDeviceItems(),
            RefreshDeviceDetails,
            CleanUpDuplicates,
            DisconnectAllDevices);
        _devicesList = root.Devices;
        _deviceDetailsPanel = root.Details;
        RefreshDeviceDetails();
        return root;
    }

    private ConnectionPageView BuildConnectionPage()
    {
        var settings = AppNetworkSettings.Load();
        var candidates = LanAddressSelector.GetCandidates();
        var selection = LanAddressSelector.Select(candidates, settings);
        _connectionPage = new ConnectionPageView(
            ConnectionCandidateItem.Create(candidates, selection?.Candidate),
            settings.NetworkMode == NetworkSelectionMode.Automatic,
            settings.PortMode == PortSelectionMode.Automatic,
            (settings.ManualPort ?? _webHost.Port).ToString(CultureInfo.InvariantCulture),
            _webHost.ServerUrl,
            _webHost.AdvertisedHostAddress,
            _webHost.Port.ToString(CultureInfo.InvariantCulture),
            selection?.Warning ?? _webHost.AddressSelectionWarning ?? _webHost.PortSelectionWarning ?? string.Empty,
            SaveConnectionSettings,
            () => SelectPage(HostPage.Connection));
        _connectionPage.AutomaticPortButton.Click += (_, _) => UpdatePortInputState();
        _connectionPage.ManualPortButton.Click += (_, _) => UpdatePortInputState();
        _connectionPage.PortTextBox.PreviewTextInput += OnManualPortPreviewTextInput;
        _connectionPage.PortTextBox.TextChanged += OnManualPortTextChanged;
        WpfDataObject.AddPastingHandler(_connectionPage.PortTextBox, OnManualPortPaste);
        UpdatePortInputState();
        return _connectionPage;
    }

    private PreferencesPageView BuildPreferencesPage()
    {
        _isLoadingPreferences = true;
        var root = new PreferencesPageView(
            _preferencesSectionToOpen,
            SetPreferencesTitle,
            RevealExpandedPreferencesSection);
        var globalPermissions = AppPermissionSettings.Load();

        var applicationPanel = root.ApplicationContent;
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

        var appearancePanel = root.AppearanceContent;
        var activeTheme = AppThemeSettings.GetMode();
        var systemTheme = CreateSegmentButton("System", activeTheme == AppThemeMode.System);
        var lightTheme = CreateSegmentButton("Light", activeTheme == AppThemeMode.Light);
        var darkTheme = CreateSegmentButton("Dark", activeTheme == AppThemeMode.Dark);
        WireSegmentGroup(systemTheme, lightTheme, darkTheme);
        systemTheme.Click += (_, _) => SetThemeMode(AppThemeMode.System);
        lightTheme.Click += (_, _) => SetThemeMode(AppThemeMode.Light);
        darkTheme.Click += (_, _) => SetThemeMode(AppThemeMode.Dark);
        appearancePanel.Children.Add(CreateSegmentRow(systemTheme, lightTheme, darkTheme));

        var trackpadPanel = root.TrackpadContent;
        trackpadPanel.Children.Add(CreateMutedText("Default pointer speed for paired devices. Device-specific overrides take precedence."));
        AddGlobalPointerSpeedSetting(trackpadPanel);

        var remotePanel = root.RemoteContent;
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

        var awakePanel = root.AwakeContent;
        AddAwakeSettings(awakePanel);

        var permissionsPanel = root.PermissionsContent;
        var allowClientControl = CreateCheckBox("Allow paired devices to control Voltura Air host", AppClientControlSettings.IsEnabled());
        allowClientControl.Checked += (_, _) => AppClientControlSettings.SetEnabled(true);
        allowClientControl.Unchecked += (_, _) => AppClientControlSettings.SetEnabled(false);
        permissionsPanel.Children.Add(allowClientControl);
        permissionsPanel.Children.Add(CreateMutedText("When off, paired devices cannot inject input into Voltura Air itself. They can still control Windows and other permitted apps."));

        var textDestinationPanel = root.TextDestinationContent;
        AddTextDestinationSettings(textDestinationPanel);

        var appLaunchPanel = root.AppLaunchContent;
        AddAppLaunchSettings(appLaunchPanel);

        var customPointerPanel = root.CustomPointerContent;
        AddCustomPointerSetting(customPointerPanel);

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
        if (AppDeveloperSettings.EnableAlphaFeatures())
        {
            permissionsPanel.Children.Add(presentation);
        }
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
        var globalPermissionDetailsPanel = AddNestedPreferencesSection(permissionsPanel, "More about global permissions");
        globalPermissionDetailsPanel.Children.Add(CreateMutedText("Lock and blackout are enabled by default. The screen-saver permission appears when Windows has a screen saver configured. Opening web addresses, reading the PC clipboard, display off, sign out, restart, and shut down require explicit host approval."));

        var developerPanel = root.DeveloperContent;
        var developerMode = CreateCheckBox("Developer mode", AppDeveloperSettings.DeveloperMode());
        developerMode.Checked += (_, _) => AppDeveloperSettings.SetDeveloperMode(true);
        developerMode.Unchecked += (_, _) => AppDeveloperSettings.SetDeveloperMode(false);
        developerPanel.Children.Add(developerMode);

        var alphaFeatures = CreateCheckBox("Enable alpha features", AppDeveloperSettings.EnableAlphaFeatures());
        alphaFeatures.Checked += (_, _) => SetAlphaFeatures(true);
        alphaFeatures.Unchecked += (_, _) => SetAlphaFeatures(false);
        developerPanel.Children.Add(alphaFeatures);
        developerPanel.Children.Add(CreateMutedText("Shows experimental features that are still under development. Alpha features remain unavailable to paired devices until this setting is enabled."));

        var gestureDebug = CreateCheckBox("Show gesture debug screen in the mobile app", AppDeveloperSettings.EnableGestureDebug());
        gestureDebug.Checked += (_, _) => AppDeveloperSettings.SetEnableGestureDebug(true);
        gestureDebug.Unchecked += (_, _) => AppDeveloperSettings.SetEnableGestureDebug(false);
        developerPanel.Children.Add(gestureDebug);

        var windowsLockingPanel = AddNestedPreferencesSection(developerPanel, "Windows locking");
        AddWindowsLockPolicySetting(windowsLockingPanel);

        _preferencesSectionToOpen = null;
        _isLoadingPreferences = false;
        return root;

        void SetAlphaFeatures(bool enabled)
        {
            AppDeveloperSettings.SetEnableAlphaFeatures(enabled);
            RefreshPreferencesPage();
        }
    }

}
