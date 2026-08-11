namespace VolturaAir.Host;

internal enum LaserPointerChangeOutcome
{
    Changed,
    Unchanged,
    OwnerConflict
}

internal sealed class PresentationLaserPointerController(
    Action<bool, PresentationLaserColor?>? apply,
    Action<string>? restorePowerPointPointer = null) : IDisposable
{
    private readonly Lock _gate = new();
    private string? _ownerClientId;
    private string? _runtimePresentationId;
    private PresentationLaserColor? _colorOverride;
    private bool _enabled;
    private bool _disposed;

    public bool IsEnabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
    }

    public PresentationLaserColor? ActiveColor
    {
        get
        {
            lock (_gate)
            {
                return _enabled ? ResolveColor(_colorOverride) : null;
            }
        }
    }

    public event EventHandler? StateChanged;

    internal string? RuntimePresentationId
    {
        get
        {
            lock (_gate)
            {
                return _runtimePresentationId;
            }
        }
    }

    internal bool IsOwnedBy(string clientId)
    {
        lock (_gate)
        {
            return _enabled &&
                string.Equals(_ownerClientId, clientId, StringComparison.Ordinal);
        }
    }

    public LaserPointerChangeOutcome SetEnabled(
        string clientId,
        bool enabled,
        string? runtimePresentationId = null,
        PresentationLaserColor? colorOverride = null) =>
        Change(clientId, enabled, runtimePresentationId, colorOverride);

    public LaserPointerChangeOutcome Toggle(
        string clientId,
        PresentationLaserColor? colorOverride = null) =>
        Change(clientId, enabled: null, runtimePresentationId: null, colorOverride);

    public void DisableForClient(string clientId, bool restorePowerPoint = true)
    {
        if (restorePowerPoint)
        {
            _ = SetEnabled(clientId, enabled: false);
            return;
        }

        SetEnabledWithoutPowerPointRestore(clientId);
    }

    internal void DisableForPresentation(string runtimePresentationId)
    {
        string? owner;
        lock (_gate)
        {
            if (_disposed ||
                !_enabled ||
                !string.Equals(
                    _runtimePresentationId,
                    runtimePresentationId,
                    StringComparison.Ordinal))
            {
                return;
            }

            owner = _ownerClientId;
        }

        if (owner is not null)
        {
            _ = SetEnabled(owner, enabled: false);
        }
    }

    internal void DisableForTakeover()
    {
        string? owner;
        lock (_gate)
        {
            if (_disposed || !_enabled)
            {
                return;
            }

            owner = _ownerClientId;
        }

        if (owner is not null)
        {
            _ = SetEnabled(owner, enabled: false);
        }
    }

    public void DisableIfOwnerCannotControl(Func<string, bool> canControl)
    {
        ArgumentNullException.ThrowIfNull(canControl);
        string? ownerClientId;
        lock (_gate)
        {
            if (_disposed || !_enabled)
            {
                return;
            }

            ownerClientId = _ownerClientId;
        }

        if (ownerClientId is not null && !canControl(ownerClientId))
        {
            _ = SetEnabled(ownerClientId, enabled: false);
        }
    }

    public void Revoke(bool restorePowerPoint = true)
    {
        string? presentationToRestore;
        lock (_gate)
        {
            if (_disposed || !_enabled)
            {
                return;
            }

            presentationToRestore = _runtimePresentationId;
            ClearState();
        }

        if (restorePowerPoint)
        {
            RestorePowerPointPointer(presentationToRestore);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        var changed = false;
        string? presentationToRestore = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_enabled)
            {
                apply?.Invoke(false, null);
                presentationToRestore = _runtimePresentationId;
                changed = true;
            }

            ClearState();
            _disposed = true;
        }

        RestorePowerPointPointer(presentationToRestore);
        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private LaserPointerChangeOutcome Change(
        string clientId,
        bool? enabled,
        string? runtimePresentationId,
        PresentationLaserColor? colorOverride)
    {
        bool changed;
        string? presentationToRestore = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_enabled &&
                !string.Equals(_ownerClientId, clientId, StringComparison.Ordinal))
            {
                return LaserPointerChangeOutcome.OwnerConflict;
            }

            var desiredEnabled = enabled ?? !(_enabled &&
                ResolveColor(_colorOverride) == ResolveColor(colorOverride));
            if (!desiredEnabled)
            {
                if (!_enabled)
                {
                    return LaserPointerChangeOutcome.Unchanged;
                }

                apply?.Invoke(false, null);
                presentationToRestore = _runtimePresentationId;
                ClearState();
                changed = true;
            }
            else if (!_enabled)
            {
                apply?.Invoke(true, colorOverride);
                _enabled = true;
                _ownerClientId = clientId;
                _runtimePresentationId = runtimePresentationId;
                _colorOverride = colorOverride;
                changed = true;
            }
            else
            {
                var nextRuntimePresentationId = runtimePresentationId ?? _runtimePresentationId;
                if (_colorOverride == colorOverride &&
                    string.Equals(
                        _runtimePresentationId,
                        nextRuntimePresentationId,
                        StringComparison.Ordinal))
                {
                    return LaserPointerChangeOutcome.Unchanged;
                }

                apply?.Invoke(true, colorOverride);
                _runtimePresentationId = nextRuntimePresentationId;
                _colorOverride = colorOverride;
                changed = true;
            }
        }

        RestorePowerPointPointer(presentationToRestore);
        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        return LaserPointerChangeOutcome.Changed;
    }

    private void SetEnabledWithoutPowerPointRestore(string clientId)
    {
        var changed = false;
        lock (_gate)
        {
            if (_disposed ||
                !_enabled ||
                !string.Equals(_ownerClientId, clientId, StringComparison.Ordinal))
            {
                return;
            }

            apply?.Invoke(false, null);
            ClearState();
            changed = true;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ClearState()
    {
        _enabled = false;
        _ownerClientId = null;
        _runtimePresentationId = null;
        _colorOverride = null;
    }

    private static PresentationLaserColor ResolveColor(
        PresentationLaserColor? colorOverride) =>
        colorOverride ?? AppPointerSettings.GetPresentationLaserPointer().Color;

    private void RestorePowerPointPointer(string? runtimePresentationId)
    {
        if (!string.IsNullOrEmpty(runtimePresentationId))
        {
            restorePowerPointPointer?.Invoke(runtimePresentationId);
        }
    }
}
