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
    private readonly PreferencesVisualFactory _preferenceVisuals;
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
    private double? _scrollOffsetToRestore;

    public PreferencesPageController(
        Window owner,
        ISystemPowerController powerController,
        IWorkstationLockPolicy workstationLockPolicy,
        IAwakeService awakeService,
        ICursorOverrideController cursorOverrides,
        IAppLog appLog,
        IAppLaunchService appLaunchService,
        HostVisualFactory visuals,
        HostToastPresenter toasts,
        Action requestRefresh,
        Action<string?> titleChanged)
    {
        _visuals = visuals;
        _preferenceVisuals = new PreferencesVisualFactory(visuals);
        _toasts = toasts;
        _requestRefresh = requestRefresh;
        _titleChanged = titleChanged;
        _awake = new AwakeSettingsSection(owner, awakeService, visuals, toasts, () => _isLoading);
        _appLaunch = new AppLaunchSettingsSection(owner, appLaunchService, visuals, _preferenceVisuals, toasts, () => _isLoading, RefreshPreservingState);
        _textDestination = new TextDestinationSettingsSection(owner, visuals, _preferenceVisuals, toasts, () => _isLoading);
        _customPointer = new CustomPointerSettingsSection(cursorOverrides, appLog, visuals, toasts, () => _isLoading);
        _presentation = new PresentationSettingsSection(cursorOverrides, appLog, visuals, toasts, () => _isLoading);
        _application = new ApplicationSettingsSection(appLog, visuals, _preferenceVisuals, () => _isLoading);
        _permissions = new GlobalPermissionsSettingsSection(powerController, owner, visuals, _preferenceVisuals, () => _isLoading);
        _developer = new DeveloperSettingsSection(owner, powerController, workstationLockPolicy, appLog, visuals, _preferenceVisuals, toasts, RefreshPreservingState);
    }

    public PreferencesPageView CreateView()
    {
        _isLoading = true;
        var root = new PreferencesPageView(
            _sectionToOpen,
            _titleChanged,
            PreferencesScrollCoordinator.RevealExpandedSection);
        _currentView = root;
        _application.AddTo(root.ApplicationContent);
        AddAppearanceSettings(root.AppearanceContent);
        AddTrackpadSettings(root.TrackpadContent);
        AddRemoteSettings(root.RemoteContent);
        _presentation.AddTo(root.PresentationContent);
        _awake.AddTo(root.AwakeContent);
        _permissions.AddTo(root.PermissionsContent);
        _textDestination.AddTo(root.TextDestinationContent);
        _appLaunch.AddTo(root.AppLaunchContent);
        _customPointer.AddTo(root.CustomPointerContent);
        _developer.AddTo(root.DeveloperContent);

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
        parent.Children.Add(_visuals.CreateLabel("Theme"));
        var activeTheme = AppThemeSettings.GetMode();
        var systemTheme = _visuals.CreateSegmentButton("System", activeTheme == AppThemeMode.System);
        var lightTheme = _visuals.CreateSegmentButton("Light", activeTheme == AppThemeMode.Light);
        var darkTheme = _visuals.CreateSegmentButton("Dark", activeTheme == AppThemeMode.Dark);
        HostVisualFactory.WireSegmentGroup(systemTheme, lightTheme, darkTheme);
        systemTheme.Click += (_, _) => SetThemeMode(AppThemeMode.System);
        lightTheme.Click += (_, _) => SetThemeMode(AppThemeMode.Light);
        darkTheme.Click += (_, _) => SetThemeMode(AppThemeMode.Dark);
        parent.Children.Add(HostVisualFactory.CreateSegmentRow(systemTheme, lightTheme, darkTheme));
        parent.Children.Add(_visuals.CreateLabel("Device"));
        var showModeButtons = _visuals.CreateCheckBox("Show mode buttons", AppAppearanceSettings.ShowModeButtons());
        showModeButtons.Checked += (_, _) => AppAppearanceSettings.SetShowModeButtons(true);
        showModeButtons.Unchecked += (_, _) => AppAppearanceSettings.SetShowModeButtons(false);
        parent.Children.Add(showModeButtons);
    }

    private void AddTrackpadSettings(StackPanel parent)
    {
        parent.Children.Add(_visuals.CreateMutedText("Default pointer speed for paired devices. Device-specific overrides take precedence."));
        parent.Children.Add(_visuals.CreateLabel("Default pointer speed"));
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
        parent.Children.Add(_visuals.CreateLabel("Default remote mode"));
        parent.Children.Add(HostVisualFactory.CreateSegmentRow(standard, youtube, kodi));
        parent.Children.Add(_visuals.CreateLabel("YouTube URL"));
        parent.Children.Add(_visuals.CreateMutedText("Used when a paired device triggers the YouTube remote launch action. The URL stays on this PC."));
        var row = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceMd);
        var input = new TextBox { Text = AppRemoteSettings.GetYoutubeUrl(), Width = 360 };
        row.Children.Add(input);
        row.Children.Add(_visuals.CreateButton("Save URL", (_, _) => SaveYoutubeUrl(input), primary: true));
        parent.Children.Add(row);
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
