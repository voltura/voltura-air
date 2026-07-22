namespace VolturaAir.Host.Features.Connection;

internal sealed record ConnectionConfiguration(
    NetworkSelectionMode NetworkMode,
    string? ManualHostAddress,
    string? ManualAdapterId,
    string? ManualAdapterName,
    PortSelectionMode PortMode,
    int? ManualPort)
{
    public static ConnectionConfiguration FromSnapshot(NetworkSettingsSnapshot snapshot) => new(
        snapshot.NetworkMode,
        snapshot.ManualHostAddress,
        snapshot.ManualAdapterId,
        snapshot.ManualAdapterName,
        snapshot.PortMode,
        snapshot.ManualPort);

    public NetworkSettingsSnapshot ApplyTo(NetworkSettingsSnapshot snapshot) => snapshot with
    {
        NetworkMode = NetworkMode,
        ManualHostAddress = ManualHostAddress,
        ManualAdapterId = ManualAdapterId,
        ManualAdapterName = ManualAdapterName,
        PortMode = PortMode,
        ManualPort = ManualPort
    };
}

internal sealed class ConnectionPageState(
    ConnectionConfiguration activeConfiguration,
    ConnectionConfiguration savedConfiguration,
    string activeAdapterName,
    string activeAddress,
    int activePort,
    IReadOnlyList<LanAddressCandidate> candidates)
{
    public ConnectionPageState(
        ConnectionConfiguration activeConfiguration,
        ConnectionConfiguration savedConfiguration,
        string activeAdapterName,
        string activeAddress,
        int activePort,
        IReadOnlyList<LanAddressCandidate> candidates,
        bool detectExistingRestartRequirement)
        : this(activeConfiguration, savedConfiguration, activeAdapterName, activeAddress, activePort, candidates)
    {
        NeedsRestartRetry = detectExistingRestartRequirement && savedConfiguration != activeConfiguration;
    }

    public ConnectionConfiguration ActiveConfiguration { get; } = activeConfiguration;

    public ConnectionConfiguration SavedConfiguration { get; private set; } = savedConfiguration;

    public ConnectionConfiguration PendingConfiguration { get; private set; } = savedConfiguration;

    public string ActiveAdapterName { get; } = activeAdapterName;

    public string ActiveAddress { get; } = activeAddress;

    public int ActivePort { get; } = activePort;

    public IReadOnlyList<LanAddressCandidate> Candidates { get; private set; } = candidates;

    public bool IsAdapterChooserOpen { get; set; }

    public bool IsAdvancedExpanded { get; set; }

    public bool IsRestartPending { get; private set; }

    public bool NeedsRestartRetry { get; private set; }

    public bool SaveRetryRequired { get; private set; }

    public string ManualPortText { get; set; } = (savedConfiguration.ManualPort ?? activePort).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string? FeedbackMessage { get; set; }

    public bool FeedbackIsError { get; set; }

    public bool HasPendingChanges => PendingConfiguration != SavedConfiguration || SaveRetryRequired;

    public bool UsesCustomPort => PendingConfiguration.PortMode == PortSelectionMode.Manual;

    public LanAddressCandidate? PendingAdapter => PendingConfiguration.NetworkMode == NetworkSelectionMode.Manual
        ? FindCandidate(PendingConfiguration)
        : null;

    public bool IsPendingAdapterAvailable => PendingConfiguration.NetworkMode == NetworkSelectionMode.Automatic || PendingAdapter is not null;

    public void SetCandidates(IReadOnlyList<LanAddressCandidate> candidates) => Candidates = candidates;

    public void SelectAdapter(LanAddressCandidate candidate)
    {
        PendingConfiguration = PendingConfiguration with
        {
            NetworkMode = NetworkSelectionMode.Manual,
            ManualHostAddress = candidate.Address.ToString(),
            ManualAdapterId = candidate.AdapterId,
            ManualAdapterName = LanAddressSelector.GetAdapterDisplayName(candidate)
        };
        IsAdapterChooserOpen = false;
        ClearFeedback();
    }

    public void UseAutomaticAdapter()
    {
        PendingConfiguration = PendingConfiguration with
        {
            NetworkMode = NetworkSelectionMode.Automatic,
            ManualHostAddress = null,
            ManualAdapterId = null,
            ManualAdapterName = null
        };
        IsAdapterChooserOpen = false;
        ClearFeedback();
    }

    public void SetUseCustomPort(bool useCustomPort)
    {
        PendingConfiguration = PendingConfiguration with
        {
            PortMode = useCustomPort ? PortSelectionMode.Manual : PortSelectionMode.Automatic,
            ManualPort = useCustomPort && int.TryParse(ManualPortText, out var port) ? port : null
        };
        ClearFeedback();
    }

    public void SetManualPort(int? port)
    {
        PendingConfiguration = PendingConfiguration with { ManualPort = port };
        ClearFeedback();
    }

    public void DiscardPendingChanges()
    {
        PendingConfiguration = SavedConfiguration;
        ManualPortText = (SavedConfiguration.ManualPort ?? ActivePort).ToString(System.Globalization.CultureInfo.InvariantCulture);
        IsAdapterChooserOpen = false;
        IsRestartPending = false;
        NeedsRestartRetry = SavedConfiguration != ActiveConfiguration;
        SaveRetryRequired = false;
        ClearFeedback();
    }

    public void MarkSaved(ConnectionConfiguration configuration)
    {
        SavedConfiguration = configuration;
        PendingConfiguration = configuration;
        IsAdapterChooserOpen = false;
        IsRestartPending = true;
        NeedsRestartRetry = false;
        SaveRetryRequired = false;
        ClearFeedback();
    }

    public void ReconcileSavedAfterFailure(ConnectionConfiguration savedConfiguration)
    {
        SavedConfiguration = savedConfiguration;
        IsRestartPending = false;
        NeedsRestartRetry = false;
        SaveRetryRequired = true;
    }

    public void MarkRestartRequestFailed()
    {
        IsRestartPending = false;
        NeedsRestartRetry = true;
    }

    public void MarkRestartRetryPending()
    {
        IsRestartPending = true;
        NeedsRestartRetry = false;
        ClearFeedback();
    }

    private LanAddressCandidate? FindCandidate(ConnectionConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.ManualAdapterId))
        {
            var adapter = Candidates.FirstOrDefault(candidate => string.Equals(
                candidate.AdapterId,
                configuration.ManualAdapterId,
                StringComparison.OrdinalIgnoreCase));
            if (adapter is not null)
            {
                return adapter;
            }
        }

        return Candidates.FirstOrDefault(candidate => string.Equals(
            candidate.Address.ToString(),
            configuration.ManualHostAddress,
            StringComparison.OrdinalIgnoreCase));
    }

    private void ClearFeedback()
    {
        FeedbackMessage = null;
        FeedbackIsError = false;
    }
}
