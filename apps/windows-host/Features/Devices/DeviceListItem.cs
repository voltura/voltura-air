using System.ComponentModel;
using System.Runtime.CompilerServices;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Features.Devices;

internal sealed class DeviceListItem(
    string clientId,
    string name,
    string status,
    bool isConnected,
    string activity,
    string metadata,
    int pointerSpeed,
    bool hasPointerSpeedOverride,
    IReadOnlyList<DevicePermissionItem> permissions,
    bool isExpanded) : INotifyPropertyChanged
{
    private int _pointerSpeed = pointerSpeed;
    private bool _hasPointerSpeedOverride = hasPointerSpeedOverride;
    private bool _isExpanded = isExpanded;
    private bool _isTrackpadExpanded;
    private bool _isPermissionsExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ClientId { get; } = clientId;
    public string Name { get; } = name;
    public string Status { get; } = status;
    public bool IsConnected { get; } = isConnected;
    public string Activity { get; } = activity;
    public string Metadata { get; } = metadata;
    public int PointerSpeed
    {
        get => _pointerSpeed;
        set
        {
            if (_pointerSpeed == value)
            {
                return;
            }

            _pointerSpeed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PointerSpeedHint));
        }
    }

    public bool HasPointerSpeedOverride => _hasPointerSpeedOverride;
    public string PointerSpeedHint => HasPointerSpeedOverride
        ? $"Override active. Effective speed: {PointerSpeed}%."
        : $"Using global default: {PointerSpeed}%.";
    public IReadOnlyList<DevicePermissionItem> Permissions { get; } = permissions;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsTrackpadExpanded
    {
        get => _isTrackpadExpanded;
        set
        {
            if (_isTrackpadExpanded == value)
            {
                return;
            }

            _isTrackpadExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsPermissionsExpanded
    {
        get => _isPermissionsExpanded;
        set
        {
            if (_isPermissionsExpanded == value)
            {
                return;
            }

            _isPermissionsExpanded = value;
            OnPropertyChanged();
        }
    }

    public PillBadgeTone StatusTone => IsConnected ? PillBadgeTone.Success : PillBadgeTone.Danger;

    public void ApplyPointerSpeed(int pointerSpeedValue, bool hasOverride)
    {
        PointerSpeed = pointerSpeedValue;
        if (_hasPointerSpeedOverride == hasOverride)
        {
            return;
        }

        _hasPointerSpeedOverride = hasOverride;
        OnPropertyChanged(nameof(HasPointerSpeedOverride));
        OnPropertyChanged(nameof(PointerSpeedHint));
    }

    public void OpenTrackpad()
    {
        IsPermissionsExpanded = false;
        IsTrackpadExpanded = true;
    }

    public void OpenPermissions()
    {
        IsTrackpadExpanded = false;
        IsPermissionsExpanded = true;
    }

    public void CollapseChildren()
    {
        IsTrackpadExpanded = false;
        IsPermissionsExpanded = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class DevicePermissionItem(
    string clientId,
    DevicePermissionKind kind,
    string title,
    bool? overrideValue,
    bool inheritedAllow) : INotifyPropertyChanged
{
    private bool? _overrideValue = overrideValue;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ClientId { get; } = clientId;
    public DevicePermissionKind Kind { get; } = kind;
    public string Title { get; } = title;
    public bool? OverrideValue => _overrideValue;
    public bool InheritedAllow { get; } = inheritedAllow;
    public bool IsInherited => OverrideValue is null;
    public bool IsExplicitAllow => OverrideValue == true;
    public bool IsExplicitBlock => OverrideValue == false;
    public bool IsInheritedAllow => IsInherited && InheritedAllow;
    public bool IsInheritedBlock => IsInherited && !InheritedAllow;
    public string UseGlobalVisualState => IsInherited ? "Selected" : "Default";
    public string AllowVisualState => IsExplicitAllow ? "Selected" : IsInheritedAllow ? "Effective" : "Default";
    public string BlockVisualState => IsExplicitBlock ? "Selected" : IsInheritedBlock ? "Effective" : "Default";
    public string UseGlobalLabel => OverrideValue is null ? "✓ Use global" : "Use global";
    public string AllowLabel => OverrideValue == true || (OverrideValue is null && InheritedAllow) ? "✓ Allow" : "Allow";
    public string BlockLabel => OverrideValue == false || (OverrideValue is null && !InheritedAllow) ? "✓ Block" : "Block";

    public void SetOverrideValue(bool? value)
    {
        if (_overrideValue == value)
        {
            return;
        }

        _overrideValue = value;
        OnPropertyChanged(nameof(OverrideValue));
        OnPropertyChanged(nameof(IsInherited));
        OnPropertyChanged(nameof(IsExplicitAllow));
        OnPropertyChanged(nameof(IsExplicitBlock));
        OnPropertyChanged(nameof(IsInheritedAllow));
        OnPropertyChanged(nameof(IsInheritedBlock));
        OnPropertyChanged(nameof(UseGlobalVisualState));
        OnPropertyChanged(nameof(AllowVisualState));
        OnPropertyChanged(nameof(BlockVisualState));
        OnPropertyChanged(nameof(UseGlobalLabel));
        OnPropertyChanged(nameof(AllowLabel));
        OnPropertyChanged(nameof(BlockLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal enum DevicePermissionKind
{
    RemoteInput,
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
