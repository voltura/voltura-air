namespace VolturaAir.Host;

internal sealed class PresentationLaserPointerController(
    Action<bool>? apply,
    Action<string>? restorePowerPointPointer = null) : IDisposable
{
    private readonly Lock _gate = new();
    private string? _ownerClientId;
    private string? _runtimePresentationId;
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

    public void SetEnabled(
        string clientId,
        bool enabled,
        string? runtimePresentationId = null)
    {
        bool changed;
        string? presentationToRestore = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!enabled && _ownerClientId is not null &&
                !string.Equals(_ownerClientId, clientId, StringComparison.Ordinal))
            {
                return;
            }

            if (_enabled == enabled)
            {
                return;
            }

            apply?.Invoke(enabled);
            if (!enabled)
            {
                presentationToRestore = _runtimePresentationId;
            }

            _enabled = enabled;
            _ownerClientId = enabled ? clientId : null;
            _runtimePresentationId = enabled ? runtimePresentationId : null;
            changed = true;
        }

        RestorePowerPointPointer(presentationToRestore);
        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void DisableForClient(string clientId, bool restorePowerPoint = true)
    {
        lock (_gate)
        {
            if (_disposed || !_enabled || !string.Equals(_ownerClientId, clientId, StringComparison.Ordinal))
            {
                return;
            }
        }

        if (restorePowerPoint)
        {
            SetEnabled(clientId, enabled: false);
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
            SetEnabled(owner, enabled: false);
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
            var changed = false;
            string? presentationToRestore = null;
            lock (_gate)
            {
                if (!_disposed &&
                    _enabled &&
                    string.Equals(_ownerClientId, ownerClientId, StringComparison.Ordinal))
                {
                    apply?.Invoke(false);
                    presentationToRestore = _runtimePresentationId;
                    _enabled = false;
                    _ownerClientId = null;
                    _runtimePresentationId = null;
                    changed = true;
                }
            }

            if (changed)
            {
                RestorePowerPointPointer(presentationToRestore);
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
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
            _enabled = false;
            _ownerClientId = null;
            _runtimePresentationId = null;
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
                apply?.Invoke(false);
                presentationToRestore = _runtimePresentationId;
                changed = true;
            }

            _enabled = false;
            _ownerClientId = null;
            _runtimePresentationId = null;
            _disposed = true;
        }

        RestorePowerPointPointer(presentationToRestore);
        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
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

            apply?.Invoke(false);
            _enabled = false;
            _ownerClientId = null;
            _runtimePresentationId = null;
            changed = true;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RestorePowerPointPointer(string? runtimePresentationId)
    {
        if (!string.IsNullOrEmpty(runtimePresentationId))
        {
            restorePowerPointPointer?.Invoke(runtimePresentationId);
        }
    }
}
