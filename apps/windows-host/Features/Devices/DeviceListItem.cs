using System.ComponentModel;
using System.Runtime.CompilerServices;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Features.Devices;

internal sealed record DeviceAccessProfileChoice(DeviceAccessProfile Profile, string DisplayName);
internal sealed record DeviceSoundQualityChoice(ScreenViewSoundQuality? Quality, string DisplayName);

internal sealed class DeviceListItem(
    string clientId,
    string name,
    string status,
    bool isConnected,
    bool isConnectionAvailable,
    string activity,
    string metadata,
    int pointerSpeed,
    bool hasPointerSpeedOverride,
    bool? showModeButtonsOverride,
    bool showModeButtons,
    bool? controlDepthOverride,
    bool controlDepth,
    ScreenViewSoundQuality? screenSoundQualityOverride,
    ScreenViewSoundQuality screenSoundQuality,
    DeviceAccessProfile accessProfile,
    IReadOnlyList<DevicePermissionItem> permissions,
    ProtectedFileFilterItem protectedFileFilter,
    bool isExpanded) : INotifyPropertyChanged
{
    private IReadOnlyList<DeviceAccessProfileChoice> ProfileChoices { get; } =
    [
        new(DeviceAccessProfile.MyDevice, DeviceAccessProfiles.GetDisplayName(DeviceAccessProfile.MyDevice)),
        new(DeviceAccessProfile.RemoteControls, DeviceAccessProfiles.GetDisplayName(DeviceAccessProfile.RemoteControls)),
        new(DeviceAccessProfile.Custom, DeviceAccessProfiles.GetDisplayName(DeviceAccessProfile.Custom))
    ];

    private int _pointerSpeed = pointerSpeed;
    private bool _hasPointerSpeedOverride = hasPointerSpeedOverride;
    private bool? _showModeButtonsOverride = showModeButtonsOverride;
    private bool _showModeButtons = showModeButtons;
    private bool? _controlDepthOverride = controlDepthOverride;
    private bool _controlDepth = controlDepth;
    private ScreenViewSoundQuality? _screenSoundQualityOverride = screenSoundQualityOverride;
    private ScreenViewSoundQuality _screenSoundQuality = screenSoundQuality;
    private DeviceAccessProfile _accessProfile = accessProfile;
    private bool _isExpanded = isExpanded;
    private bool _isAppearanceExpanded;
    private bool _isScreenViewingExpanded;
    private bool _isTrackpadExpanded;
    private bool _isPermissionsExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ClientId { get; } = clientId;
    public string Name { get; } = name;
    public string Status { get; } = status;
    public bool IsConnected { get; } = isConnected;
    public bool IsConnectionAvailable { get; } = isConnectionAvailable;
    public string Activity { get; } = activity;
    public string Metadata { get; } = metadata;
    public IReadOnlyList<DeviceAccessProfileChoice> AccessProfileChoices => ProfileChoices;
    public DeviceAccessProfile AccessProfile => _accessProfile;
    public string AccessProfileDisplayName => DeviceAccessProfiles.GetDisplayName(AccessProfile);
    public IReadOnlyList<DevicePermissionItem> Permissions { get; } = permissions;
    public ProtectedFileFilterItem ProtectedFileFilter { get; } = protectedFileFilter;
    public IReadOnlyList<DeviceSoundQualityChoice> ScreenSoundQualityChoices { get; } =
    [
        new(null, "Use global"),
        new(ScreenViewSoundQuality.High, "High"),
        new(ScreenViewSoundQuality.Standard, "Standard"),
        new(ScreenViewSoundQuality.Low, "Low")
    ];

    public int PointerSpeed
    {
        get => _pointerSpeed;
        set
        {
            if (_pointerSpeed == value) return;
            _pointerSpeed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PointerSpeedHint));
        }
    }

    public bool HasPointerSpeedOverride => _hasPointerSpeedOverride;
    public string PointerSpeedHint => HasPointerSpeedOverride
        ? $"Override active. Effective speed: {PointerSpeed}%."
        : $"Using global default: {PointerSpeed}%.";
    public bool? ShowModeButtonsOverride => _showModeButtonsOverride;
    public bool ShowModeButtons => _showModeButtons;
    public bool IsModeButtonsInherited => ShowModeButtonsOverride is null;
    public bool IsModeButtonsExplicitlyShown => ShowModeButtonsOverride == true;
    public bool IsModeButtonsExplicitlyHidden => ShowModeButtonsOverride == false;
    public string ModeButtonsHint => IsModeButtonsInherited
        ? $"Using global default: {(ShowModeButtons ? "shown" : "hidden")}."
        : $"Override active: {(ShowModeButtons ? "shown" : "hidden")}.";
    public string UseGlobalModeButtonsVisualState => IsModeButtonsInherited ? "Selected" : "Default";
    public string ShowModeButtonsVisualState => IsModeButtonsExplicitlyShown || (IsModeButtonsInherited && ShowModeButtons) ? "Selected" : "Default";
    public string HideModeButtonsVisualState => IsModeButtonsExplicitlyHidden || (IsModeButtonsInherited && !ShowModeButtons) ? "Selected" : "Default";
    public string UseGlobalModeButtonsLabel => IsModeButtonsInherited ? "\u2713 Use global" : "Use global";
    public string ShowModeButtonsLabel => IsModeButtonsExplicitlyShown || (IsModeButtonsInherited && ShowModeButtons) ? "\u2713 Show" : "Show";
    public string HideModeButtonsLabel => IsModeButtonsExplicitlyHidden || (IsModeButtonsInherited && !ShowModeButtons) ? "\u2713 Hide" : "Hide";
    public bool? ControlDepthOverride => _controlDepthOverride;
    public bool ControlDepth => _controlDepth;
    public bool IsControlDepthInherited => ControlDepthOverride is null;
    public bool IsControlDepthExplicitlyEnabled => ControlDepthOverride == true;
    public bool IsControlDepthExplicitlyDisabled => ControlDepthOverride == false;
    public string ControlDepthHint => IsControlDepthInherited
        ? $"Using global default: {(ControlDepth ? "enabled" : "disabled")}."
        : $"Override active: {(ControlDepth ? "enabled" : "disabled")}.";
    public string UseGlobalControlDepthVisualState => IsControlDepthInherited ? "Selected" : "Default";
    public string EnableControlDepthVisualState => IsControlDepthExplicitlyEnabled || (IsControlDepthInherited && ControlDepth) ? "Selected" : "Default";
    public string DisableControlDepthVisualState => IsControlDepthExplicitlyDisabled || (IsControlDepthInherited && !ControlDepth) ? "Selected" : "Default";
    public string UseGlobalControlDepthLabel => IsControlDepthInherited ? "\u2713 Use global" : "Use global";
    public string EnableControlDepthLabel => IsControlDepthExplicitlyEnabled || (IsControlDepthInherited && ControlDepth) ? "\u2713 Enable" : "Enable";
    public string DisableControlDepthLabel => IsControlDepthExplicitlyDisabled || (IsControlDepthInherited && !ControlDepth) ? "\u2713 Disable" : "Disable";
    public ScreenViewSoundQuality? ScreenSoundQualityOverride => _screenSoundQualityOverride;
    public ScreenViewSoundQuality ScreenSoundQuality => _screenSoundQuality;
    public DeviceSoundQualityChoice SelectedScreenSoundQualityChoice =>
        ScreenSoundQualityChoices.First(choice => choice.Quality == ScreenSoundQualityOverride);
    public string ScreenSoundQualityHint => ScreenSoundQualityOverride is null
        ? $"Using global default: {ScreenViewSoundQualityProfile.DisplayName(ScreenSoundQuality)}."
        : $"Override active: {ScreenViewSoundQualityProfile.DisplayName(ScreenSoundQuality)}.";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsTrackpadExpanded
    {
        get => _isTrackpadExpanded;
        set
        {
            if (_isTrackpadExpanded == value) return;
            _isTrackpadExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsAppearanceExpanded
    {
        get => _isAppearanceExpanded;
        set
        {
            if (_isAppearanceExpanded == value) return;
            _isAppearanceExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsScreenViewingExpanded
    {
        get => _isScreenViewingExpanded;
        set
        {
            if (_isScreenViewingExpanded == value) return;
            _isScreenViewingExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsPermissionsExpanded
    {
        get => _isPermissionsExpanded;
        set
        {
            if (_isPermissionsExpanded == value) return;
            _isPermissionsExpanded = value;
            OnPropertyChanged();
        }
    }

    public PillBadgeTone StatusTone => IsConnected
        ? PillBadgeTone.Success
        : IsConnectionAvailable
            ? PillBadgeTone.Outline
            : PillBadgeTone.Danger;

    public void ApplyAccessProfile(DeviceAccessProfile profile, HostPermissionSet effectivePermissions)
    {
        if (_accessProfile != profile)
        {
            _accessProfile = profile;
            OnPropertyChanged(nameof(AccessProfile));
            OnPropertyChanged(nameof(AccessProfileDisplayName));
        }

        foreach (var permission in Permissions)
        {
            permission.SetAllowed(DeviceAccessProfiles.Read(effectivePermissions, permission.Kind));
        }
    }

    public void ApplyPointerSpeed(int pointerSpeedValue, bool hasOverride)
    {
        PointerSpeed = pointerSpeedValue;
        if (_hasPointerSpeedOverride == hasOverride) return;
        _hasPointerSpeedOverride = hasOverride;
        OnPropertyChanged(nameof(HasPointerSpeedOverride));
        OnPropertyChanged(nameof(PointerSpeedHint));
    }

    public void ApplyShowModeButtons(bool? overrideValue, bool effectiveValue)
    {
        if (_showModeButtonsOverride == overrideValue && _showModeButtons == effectiveValue) return;
        _showModeButtonsOverride = overrideValue;
        _showModeButtons = effectiveValue;
        NotifyProperties(
            nameof(ShowModeButtonsOverride), nameof(ShowModeButtons), nameof(IsModeButtonsInherited),
            nameof(IsModeButtonsExplicitlyShown), nameof(IsModeButtonsExplicitlyHidden), nameof(ModeButtonsHint),
            nameof(UseGlobalModeButtonsVisualState), nameof(ShowModeButtonsVisualState), nameof(HideModeButtonsVisualState),
            nameof(UseGlobalModeButtonsLabel), nameof(ShowModeButtonsLabel), nameof(HideModeButtonsLabel));
    }

    public void ApplyControlDepth(bool? overrideValue, bool effectiveValue)
    {
        if (_controlDepthOverride == overrideValue && _controlDepth == effectiveValue) return;
        _controlDepthOverride = overrideValue;
        _controlDepth = effectiveValue;
        NotifyProperties(
            nameof(ControlDepthOverride), nameof(ControlDepth), nameof(IsControlDepthInherited),
            nameof(IsControlDepthExplicitlyEnabled), nameof(IsControlDepthExplicitlyDisabled), nameof(ControlDepthHint),
            nameof(UseGlobalControlDepthVisualState), nameof(EnableControlDepthVisualState), nameof(DisableControlDepthVisualState),
            nameof(UseGlobalControlDepthLabel), nameof(EnableControlDepthLabel), nameof(DisableControlDepthLabel));
    }

    public void ApplyScreenSoundQuality(
        ScreenViewSoundQuality? overrideValue,
        ScreenViewSoundQuality effectiveValue)
    {
        if (_screenSoundQualityOverride == overrideValue && _screenSoundQuality == effectiveValue) return;
        _screenSoundQualityOverride = overrideValue;
        _screenSoundQuality = effectiveValue;
        NotifyProperties(
            nameof(ScreenSoundQualityOverride), nameof(ScreenSoundQuality),
            nameof(SelectedScreenSoundQualityChoice), nameof(ScreenSoundQualityHint));
    }

    public void OpenAppearance()
    {
        IsTrackpadExpanded = false;
        IsScreenViewingExpanded = false;
        IsPermissionsExpanded = false;
        IsAppearanceExpanded = true;
    }

    public void OpenTrackpad()
    {
        IsAppearanceExpanded = false;
        IsScreenViewingExpanded = false;
        IsPermissionsExpanded = false;
        IsTrackpadExpanded = true;
    }

    public void OpenPermissions()
    {
        IsAppearanceExpanded = false;
        IsTrackpadExpanded = false;
        IsScreenViewingExpanded = false;
        IsPermissionsExpanded = true;
    }

    public void OpenScreenViewing()
    {
        IsAppearanceExpanded = false;
        IsTrackpadExpanded = false;
        IsPermissionsExpanded = false;
        IsScreenViewingExpanded = true;
    }

    public void CollapseChildren()
    {
        IsAppearanceExpanded = false;
        IsTrackpadExpanded = false;
        IsScreenViewingExpanded = false;
        IsPermissionsExpanded = false;
    }

    private void NotifyProperties(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames) OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class DevicePermissionItem(
    string clientId,
    DevicePermissionKind kind,
    string title,
    bool allowed) : INotifyPropertyChanged
{
    private bool _allowed = allowed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ClientId { get; } = clientId;
    public DevicePermissionKind Kind { get; } = kind;
    public string Title { get; } = title;
    public bool Allowed => _allowed;
    public string AllowVisualState => Allowed ? "Selected" : "Default";
    public string BlockVisualState => Allowed ? "Default" : "Selected";
    public string AllowLabel => Allowed ? "\u2713 Allow" : "Allow";
    public string BlockLabel => Allowed ? "Block" : "\u2713 Block";

    public void SetAllowed(bool allowedValue)
    {
        if (_allowed == allowedValue) return;
        _allowed = allowedValue;
        foreach (var propertyName in new[]
        {
            nameof(Allowed), nameof(AllowVisualState), nameof(BlockVisualState),
            nameof(AllowLabel), nameof(BlockLabel)
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

internal sealed class ProtectedFileFilterItem(
    string clientId,
    bool? overrideValue,
    bool effectiveValue) : INotifyPropertyChanged
{
    private bool? _overrideValue = overrideValue;
    private bool _effectiveValue = effectiveValue;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ClientId { get; } = clientId;
    public bool? OverrideValue => _overrideValue;
    public bool EffectiveValue => _effectiveValue;
    public bool IsInherited => OverrideValue is null;
    public string UseGlobalVisualState => IsInherited ? "Selected" : "Default";
    public string HideVisualState => OverrideValue == true ? "Selected" : IsInherited && EffectiveValue ? "Effective" : "Default";
    public string ShowVisualState => OverrideValue == false ? "Selected" : IsInherited && !EffectiveValue ? "Effective" : "Default";
    public string UseGlobalLabel => IsInherited ? "\u2713 Use global" : "Use global";
    public string HideLabel => OverrideValue == true || IsInherited && EffectiveValue ? "\u2713 Hide" : "Hide";
    public string ShowLabel => OverrideValue == false || IsInherited && !EffectiveValue ? "\u2713 Show" : "Show";

    public void Apply(bool? nextOverride, bool nextEffective)
    {
        if (_overrideValue == nextOverride && _effectiveValue == nextEffective)
        {
            return;
        }

        _overrideValue = nextOverride;
        _effectiveValue = nextEffective;
        foreach (var propertyName in new[]
        {
            nameof(OverrideValue), nameof(EffectiveValue), nameof(IsInherited),
            nameof(UseGlobalVisualState), nameof(HideVisualState), nameof(ShowVisualState),
            nameof(UseGlobalLabel), nameof(HideLabel), nameof(ShowLabel)
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
