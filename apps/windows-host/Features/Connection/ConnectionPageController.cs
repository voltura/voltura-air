using System.Windows;

namespace VolturaAir.Host.Features.Connection;

internal sealed class ConnectionPageController
{
    private readonly PairingManager _pairingManager;
    private readonly WebHostService _webHost;
    private readonly Action _requestRestart;
    private readonly Func<NetworkSettingsSnapshot> _loadSettings;
    private readonly Action<NetworkSettingsSnapshot> _saveSettings;
    private readonly Func<IReadOnlyList<LanAddressCandidate>> _loadCandidates;
    private readonly ConnectionPageBoundaryFeedback _feedback;
    private readonly ConnectionConfiguration _activeConfiguration;
    private readonly ConnectionPortController _portInput;
    private ConnectionPageState? _state;
    private ConnectionPageView? _page;

    public ConnectionPageController(
        Window owner,
        PairingManager pairingManager,
        WebHostService webHost,
        Action requestRestart,
        IAppLogWriter appLog,
        Func<NetworkSettingsSnapshot>? loadSettings = null,
        Action<NetworkSettingsSnapshot>? saveSettings = null,
        Func<ConnectionConfirmation, bool>? confirm = null,
        Func<IReadOnlyList<LanAddressCandidate>>? loadCandidates = null)
    {
        _pairingManager = pairingManager;
        _webHost = webHost;
        _requestRestart = requestRestart;
        _loadSettings = loadSettings ?? AppNetworkSettings.Load;
        _saveSettings = saveSettings ?? AppNetworkSettings.Save;
        _loadCandidates = loadCandidates ?? LanAddressSelector.GetCandidates;
        _feedback = new ConnectionPageBoundaryFeedback(owner, appLog, confirm);
        _activeConfiguration = ConnectionConfiguration.FromSnapshot(_loadSettings());
        _portInput = new ConnectionPortController(Render);
    }

    public bool HasPendingChanges => _state?.HasPendingChanges == true;

    public ConnectionPageView CreateView(bool preserveState = false)
    {
        if (!preserveState || _state is null)
        {
            var saved = ConnectionConfiguration.FromSnapshot(_loadSettings());
            _state = new ConnectionPageState(
                _activeConfiguration,
                saved,
                _webHost.SelectedAdapterName,
                _webHost.AdvertisedHostAddress,
                _webHost.Port,
                _loadCandidates(),
                detectExistingRestartRequirement: true);
        }

        _page = new ConnectionPageView(
            OpenAdapterChooser,
            UseAutomaticAdapter,
            RefreshAdapters,
            CancelAdapterChooser,
            SetUseCustomPort,
            SetAdvancedExpanded,
            CancelPendingChanges,
            SaveAndRestart);
        _page.CandidateSelected += SelectAdapter;
        _portInput.Attach(_page, _state);
        Render();
        return _page;
    }

    public bool TryLeavePage()
    {
        if (_state?.HasPendingChanges == true && !_feedback.Confirm(ConnectionConfirmation.DiscardPendingChanges))
        {
            return false;
        }

        _state = null;
        _page = null;
        return true;
    }

    public void DiscardPendingChanges()
    {
        if (_state is null)
        {
            return;
        }

        _state.DiscardPendingChanges();
        Render();
    }

    private void OpenAdapterChooser()
    {
        if (_state is null)
        {
            return;
        }

        _state.IsAdapterChooserOpen = true;
        Render();
        _page?.FocusAdapterChooser();
    }

    private void CancelAdapterChooser()
    {
        if (_state is null)
        {
            return;
        }

        _state.IsAdapterChooserOpen = false;
        Render();
        _page?.FocusAdapterChooserButton();
    }

    private void RefreshAdapters()
    {
        if (_state is null)
        {
            return;
        }

        _state.SetCandidates(_loadCandidates());
        Render();
        _page?.FocusAdapterChooser();
    }

    internal void SelectAdapter(ConnectionCandidateItem item)
    {
        _state?.SelectAdapter(item.Candidate);
        Render();
        _page?.FocusAdapterChooserButton();
    }

    private void UseAutomaticAdapter()
    {
        _state?.UseAutomaticAdapter();
        Render();
    }

    private void SetUseCustomPort(bool useCustomPort)
    {
        if (_state is null)
        {
            return;
        }

        _state.SetUseCustomPort(useCustomPort);
        _portInput.ValidateAndStore();
        Render();
        if (useCustomPort)
        {
            _page?.FocusPortInput();
        }
    }

    private void SetAdvancedExpanded(bool isExpanded)
    {
        if (_state is { } state)
        {
            state.IsAdvancedExpanded = isExpanded;
        }
    }

    private void CancelPendingChanges()
    {
        _state?.DiscardPendingChanges();
        Render();
    }

    private void SaveAndRestart()
    {
        if (_state is null || _state.IsRestartPending)
        {
            return;
        }

        if (_state.NeedsRestartRetry && !_state.HasPendingChanges)
        {
            RequestRestartAgain();
            return;
        }

        var validation = _portInput.GetValidation();
        if (!_state.HasPendingChanges || !_state.IsPendingAdapterAvailable || !validation.IsValid)
        {
            Render();
            return;
        }

        if (_pairingManager.IsPaired && !_feedback.Confirm(ConnectionConfirmation.RestartWithPairedDevices))
        {
            return;
        }

        var pending = NormalizePendingConfiguration(_state.PendingConfiguration);
        NetworkSettingsSnapshot? originalSnapshot = null;
        try
        {
            originalSnapshot = _loadSettings();
            _saveSettings(pending.ApplyTo(originalSnapshot));
        }
        catch (Exception exception)
        {
            _feedback.LogFailure("connection_settings_save", exception);
            if (originalSnapshot is not null)
            {
                try
                {
                    _saveSettings(originalSnapshot);
                }
                catch (Exception rollbackException)
                {
                    _feedback.LogFailure("connection_settings_rollback", rollbackException);
                }
            }

            try
            {
                _state.ReconcileSavedAfterFailure(ConnectionConfiguration.FromSnapshot(_loadSettings()));
            }
            catch (Exception reloadException)
            {
                _feedback.LogFailure("connection_settings_reload", reloadException);
            }

            _state.FeedbackMessage = "Voltura Air couldn't save the connection settings. Your changes are still here; try again.";
            _state.FeedbackIsError = true;
            Render();
            return;
        }

        _state.MarkSaved(pending);
        Render();
        try
        {
            _requestRestart();
        }
        catch (Exception exception)
        {
            _feedback.LogFailure("connection_restart_request", exception);
            _state.MarkRestartRequestFailed();
            _state.FeedbackMessage = "The settings were saved, but Voltura Air couldn't restart. Restart it manually or try again.";
            _state.FeedbackIsError = true;
            Render();
        }
    }

    private void RequestRestartAgain()
    {
        if (_state is null)
        {
            return;
        }

        _state.MarkRestartRetryPending();
        Render();
        try
        {
            _requestRestart();
        }
        catch (Exception exception)
        {
            _feedback.LogFailure("connection_restart_request", exception);
            _state.MarkRestartRequestFailed();
            _state.FeedbackMessage = "Voltura Air still couldn't restart. Restart it manually or try again.";
            _state.FeedbackIsError = true;
            Render();
        }
    }

    private ConnectionConfiguration NormalizePendingConfiguration(ConnectionConfiguration pending)
    {
        if (pending.NetworkMode == NetworkSelectionMode.Manual && _state?.PendingAdapter is { } adapter)
        {
            pending = pending with
            {
                ManualHostAddress = adapter.Address.ToString(),
                ManualAdapterId = adapter.AdapterId,
                ManualAdapterName = LanAddressSelector.GetAdapterDisplayName(adapter)
            };
        }

        return pending.PortMode == PortSelectionMode.Automatic
            ? pending with { ManualPort = null }
            : pending;
    }

    private void Render()
    {
        if (_state is null || _page is null)
        {
            return;
        }

        ConnectionPagePresenter.Apply(_page, _state, _webHost, _portInput.GetValidation());
    }

}
