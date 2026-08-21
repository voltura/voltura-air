using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VolturaAir.Host.Ui;
using TextBox = System.Windows.Controls.TextBox;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class PreferencesPageController
{
    private readonly HostVisualFactory _visuals;
    private readonly PreferencesSearchRegistry _searchRegistry;
    private readonly PreferencesVisualFactory _preferenceVisuals;
    private readonly ScreenViewSettingsSection _screenView;
    private readonly HostToastPresenter _toasts;
    private readonly Action _requestRefresh;
    private readonly Action<string?> _titleChanged;
    private readonly AwakeSettingsSection _awake;
    private readonly AppLaunchSettingsSection _appLaunch;
    private readonly TextDestinationSettingsSection _textDestination;
    private readonly CustomPointerSettingsSection _customPointer;
    private readonly PresentationSettingsSection _presentation;
    private readonly ApplicationSettingsSection _application;
    private readonly GlobalPermissionsSettingsSection _permissions;
    private readonly DeveloperSettingsSection _developer;
    private PreferencesPageView? _currentView;
    private bool _isLoading;
    private string? _sectionToOpen;
    private string _searchQuery = string.Empty;
    private double? _scrollOffsetToRestore;

    public PreferencesPageController(
        Window owner,
        ISystemPowerController powerController,
        IWorkstationLockPolicy workstationLockPolicy,
        IAwakeService awakeService,
        IActivitySimulationService activitySimulationService,
        ICursorOverrideController cursorOverrides,
        IAppLog appLog,
        IAppLaunchService appLaunchService,
        HostVisualFactory visuals,
        HostToastPresenter toasts,
        Action requestRefresh,
        Action<string?> titleChanged)
    {
        _visuals = visuals;
        _searchRegistry = new PreferencesSearchRegistry();
        _preferenceVisuals = new PreferencesVisualFactory(visuals, _searchRegistry);
        _toasts = toasts;
        _requestRefresh = requestRefresh;
        _titleChanged = titleChanged;
        _awake = new AwakeSettingsSection(
            owner,
            awakeService,
            activitySimulationService,
            visuals,
            _preferenceVisuals,
            toasts,
            () => _isLoading);
        _appLaunch = new AppLaunchSettingsSection(owner, appLaunchService, visuals, _preferenceVisuals, toasts, () => _isLoading, RefreshPreservingState);
        _textDestination = new TextDestinationSettingsSection(owner, visuals, _preferenceVisuals, toasts, () => _isLoading);
        _customPointer = new CustomPointerSettingsSection(cursorOverrides, appLog, visuals, _preferenceVisuals, toasts, () => _isLoading);
        _presentation = new PresentationSettingsSection(cursorOverrides, appLog, visuals, _preferenceVisuals, toasts, () => _isLoading);
        _application = new ApplicationSettingsSection(appLog, visuals, _preferenceVisuals, () => _isLoading);
        _permissions = new GlobalPermissionsSettingsSection(powerController, owner, visuals, _preferenceVisuals, () => _isLoading);
        _screenView = new ScreenViewSettingsSection(visuals, _preferenceVisuals, () => _isLoading);
        _developer = new DeveloperSettingsSection(owner, powerController, workstationLockPolicy, appLog, visuals, _preferenceVisuals, toasts, RefreshPreservingState);
    }

    public PreferencesPageView CreateView()
    {
        _isLoading = true;
        _searchRegistry.Clear();
        var root = new PreferencesPageView(
            _sectionToOpen,
            _searchQuery,
            _searchRegistry,
            _titleChanged,
            PreferencesScrollCoordinator.RevealExpandedSection,
            query => _searchQuery = query);
        _currentView = root;
        _searchRegistry.RegisterSection(root.ApplicationSection);
        _application.AddTo(root.ApplicationContent);
        _searchRegistry.RegisterSection(root.AppearanceSection);
        AddAppearanceSettings(root.AppearanceContent);
        _searchRegistry.RegisterSection(root.TrackpadSection);
        AddTrackpadSettings(root.TrackpadContent);
        _searchRegistry.RegisterSection(root.RemoteSection);
        AddRemoteSettings(root.RemoteContent);
        _searchRegistry.RegisterSection(root.PresentationSection);
        _presentation.AddTo(root.PresentationContent);
        _searchRegistry.RegisterSection(root.AwakeSection);
        _awake.AddTo(root.AwakeContent);
        _searchRegistry.RegisterSection(root.PermissionsSection);
        _permissions.AddTo(root.PermissionsContent);
        _searchRegistry.RegisterSection(root.ScreenViewSection);
        _screenView.AddTo(root.ScreenViewContent);
        _searchRegistry.RegisterSection(root.TextDestinationSection);
        _textDestination.AddTo(root.TextDestinationContent);
        _searchRegistry.RegisterSection(root.AppLaunchSection);
        _appLaunch.AddTo(root.AppLaunchContent);
        _searchRegistry.RegisterSection(root.CustomPointerSection);
        _customPointer.AddTo(root.CustomPointerContent);
        _searchRegistry.RegisterSection(root.DeveloperSection);
        _developer.AddTo(root.DeveloperContent);
        root.CompleteSearchRegistration();

        _sectionToOpen = null;
        _isLoading = false;
        return root;
    }

    public void OpenSection(string sectionTitle)
    {
        _sectionToOpen = sectionTitle;
    }

    public void RefreshPreservingState()
    {
        RememberViewState();
        _requestRefresh();
    }

    public void RememberViewState()
    {
        if (_currentView is null)
        {
            _sectionToOpen = null;
            _scrollOffsetToRestore = null;
            return;
        }

        _sectionToOpen = _currentView.ExpandedSectionTitle;
        _searchQuery = _currentView.SearchQuery;
        _scrollOffsetToRestore = _currentView.Scroller.VerticalOffset;
    }

    public void RestoreScrollPosition()
    {
        if (_scrollOffsetToRestore is not { } offset || _currentView is not { } view)
        {
            return;
        }

        _scrollOffsetToRestore = null;
        _ = view.Scroller.Dispatcher.InvokeAsync(
            () => view.Scroller.ScrollToVerticalOffset(offset),
            DispatcherPriority.Loaded);
    }

    private void AddAppearanceSettings(StackPanel parent)
    {
        var themeLabel = _visuals.CreateLabel("Theme");
        parent.Children.Add(themeLabel);
        var activeTheme = AppThemeSettings.GetMode();
        var systemTheme = _visuals.CreateSegmentButton("System", activeTheme == AppThemeMode.System);
        var lightTheme = _visuals.CreateSegmentButton("Light", activeTheme == AppThemeMode.Light);
        var darkTheme = _visuals.CreateSegmentButton("Dark", activeTheme == AppThemeMode.Dark);
        HostVisualFactory.WireSegmentGroup(systemTheme, lightTheme, darkTheme);
        systemTheme.Click += (_, _) => SetThemeMode(AppThemeMode.System);
        lightTheme.Click += (_, _) => SetThemeMode(AppThemeMode.Light);
        darkTheme.Click += (_, _) => SetThemeMode(AppThemeMode.Dark);
        parent.Children.Add(HostVisualFactory.CreateSegmentRow(systemTheme, lightTheme, darkTheme));
        _preferenceVisuals.RegisterLabel(themeLabel, systemTheme);
        var hostControlDepth = _preferenceVisuals.Register(
            _visuals.CreateCheckBox("3D effect on controls", AppAppearanceSettings.HostControlDepth()),
            "Theme");
        hostControlDepth.Checked += (_, _) => AppAppearanceSettings.SetHostControlDepth(true);
        hostControlDepth.Unchecked += (_, _) => AppAppearanceSettings.SetHostControlDepth(false);
        parent.Children.Add(hostControlDepth);
        var deviceLabel = _visuals.CreateLabel("Device");
        parent.Children.Add(deviceLabel);
        var showModeButtons = _preferenceVisuals.Register(
            _visuals.CreateCheckBox("Show mode buttons", AppAppearanceSettings.ShowModeButtons()));
        showModeButtons.Checked += (_, _) => AppAppearanceSettings.SetShowModeButtons(true);
        showModeButtons.Unchecked += (_, _) => AppAppearanceSettings.SetShowModeButtons(false);
        parent.Children.Add(showModeButtons);
        _preferenceVisuals.RegisterLabel(deviceLabel, showModeButtons);
        var deviceControlDepth = _preferenceVisuals.Register(
            _visuals.CreateCheckBox("3D effect on controls", AppAppearanceSettings.DeviceControlDepth()),
            "Device");
        deviceControlDepth.Checked += (_, _) => AppAppearanceSettings.SetDeviceControlDepth(true);
        deviceControlDepth.Unchecked += (_, _) => AppAppearanceSettings.SetDeviceControlDepth(false);
        parent.Children.Add(deviceControlDepth);
    }

    private void AddTrackpadSettings(StackPanel parent)
    {
        parent.Children.Add(_visuals.CreateMutedText("Default pointer speed for paired devices. Device-specific overrides take precedence."));
        var speedLabel = _visuals.CreateLabel("Default pointer speed");
        parent.Children.Add(speedLabel);
        var row = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceMd);
        var currentSpeed = AppPointerSettings.GetDefaultPointerSpeed();
        var slider = new Slider
        {
            Style = _visuals.Style("ModernSliderStyle"),
            Minimum = DevicePointerProfile.MinPointerSpeed,
            Maximum = DevicePointerProfile.MaxPointerSpeed,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Width = 220,
            Value = currentSpeed
        };
        var output = new TextBlock
        {
            Text = $"{currentSpeed.ToString(CultureInfo.InvariantCulture)}%",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = _visuals.Brush("TextBrush"),
            MinWidth = 48
        };
        slider.ValueChanged += (_, _) =>
        {
            var speed = (int)Math.Round(slider.Value);
            output.Text = $"{speed.ToString(CultureInfo.InvariantCulture)}%";
            if (!_isLoading)
            {
                AppPointerSettings.SetDefaultPointerSpeed(speed);
            }
        };
        row.Children.Add(slider);
        row.Children.Add(output);
        parent.Children.Add(row);
        _preferenceVisuals.RegisterLabel(speedLabel, slider);
    }

    private void AddRemoteSettings(StackPanel parent)
    {
        parent.Children.Add(_visuals.CreateMutedText("Choose the initial Remote mode for newly connected phones. Mobile settings can still override this per PC."));
        var activeMode = AppRemoteSettings.GetDefaultRemoteMode();
        var standard = _visuals.CreateSegmentButton("Standard", activeMode == AppRemoteMode.Standard);
        var youtube = _visuals.CreateSegmentButton("YouTube", activeMode == AppRemoteMode.Youtube);
        var kodi = _visuals.CreateSegmentButton("Kodi", activeMode == AppRemoteMode.Kodi);
        HostVisualFactory.WireSegmentGroup(standard, youtube, kodi);
        standard.Click += (_, _) => SetDefaultRemoteMode(AppRemoteMode.Standard);
        youtube.Click += (_, _) => SetDefaultRemoteMode(AppRemoteMode.Youtube);
        kodi.Click += (_, _) => SetDefaultRemoteMode(AppRemoteMode.Kodi);
        var modeLabel = _visuals.CreateLabel("Default remote mode");
        parent.Children.Add(modeLabel);
        parent.Children.Add(HostVisualFactory.CreateSegmentRow(standard, youtube, kodi));
        _preferenceVisuals.RegisterLabel(modeLabel, standard);
        var urlLabel = _visuals.CreateLabel("YouTube URL");
        parent.Children.Add(urlLabel);
        parent.Children.Add(_visuals.CreateMutedText("Used when a paired device triggers the YouTube remote launch action. The URL stays on this PC."));
        var row = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceMd);
        var input = new TextBox { Text = AppRemoteSettings.GetYoutubeUrl(), Width = 360 };
        row.Children.Add(input);
        row.Children.Add(_visuals.CreateButton("Save URL", (_, _) => SaveYoutubeUrl(input), primary: true));
        parent.Children.Add(row);
        _preferenceVisuals.RegisterLabel(urlLabel, input);
    }

    private void SaveYoutubeUrl(TextBox input)
    {
        if (AppRemoteSettings.TrySetYoutubeUrl(input.Text, out var normalizedUrl))
        {
            input.Text = normalizedUrl;
            _toasts.Show("YouTube URL updated");
            return;
        }
        _toasts.Show("Enter a valid http or https URL");
    }

    private void SetThemeMode(AppThemeMode mode)
    {
        if (!_isLoading)
        {
            AppThemeSettings.SetMode(mode);
        }
    }

    private void SetDefaultRemoteMode(AppRemoteMode mode)
    {
        if (!_isLoading)
        {
            AppRemoteSettings.SetDefaultRemoteMode(mode);
        }
    }

}
