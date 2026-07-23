namespace VolturaAir.Host;

internal sealed class PresentationLaserPointerController(Action<bool>? apply) : IDisposable
{
    private readonly Lock _gate = new();
    private string? _ownerClientId;
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

    public void SetEnabled(string clientId, bool enabled)
    {
        var changed = false;
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
            _enabled = enabled;
            _ownerClientId = enabled ? clientId : null;
            changed = true;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void DisableForClient(string clientId)
    {
        lock (_gate)
        {
            if (_disposed || !_enabled || !string.Equals(_ownerClientId, clientId, StringComparison.Ordinal))
            {
                return;
            }
        }

        SetEnabled(clientId, enabled: false);
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
            lock (_gate)
            {
                if (!_disposed &&
                    _enabled &&
                    string.Equals(_ownerClientId, ownerClientId, StringComparison.Ordinal))
                {
                    apply?.Invoke(false);
                    _enabled = false;
                    _ownerClientId = null;
                    changed = true;
                }
            }

            if (changed)
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void Dispose()
    {
        var changed = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_enabled)
            {
                apply?.Invoke(false);
                changed = true;
            }

            _enabled = false;
            _ownerClientId = null;
            _disposed = true;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
