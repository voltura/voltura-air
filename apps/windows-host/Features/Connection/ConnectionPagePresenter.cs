using System.Globalization;

namespace VolturaAir.Host.Features.Connection;

internal static class ConnectionPagePresenter
{
    public static void Apply(
        ConnectionPageView page,
        ConnectionPageState state,
        WebHostService webHost,
        ConnectionPortValidation portValidation)
    {
        var pendingAdapter = state.PendingAdapter;
        var networkChanged = HasNetworkChange(state.SavedConfiguration, state.PendingConfiguration);
        var portChanged = HasPortChange(state.SavedConfiguration, state.PendingConfiguration);
        var methodChanged = state.SavedConfiguration.TransportMode != state.PendingConfiguration.TransportMode;
        var selectedCandidate = pendingAdapter ?? state.Candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.Address.ToString(), state.ActiveAddress, StringComparison.OrdinalIgnoreCase));
        var restartOnly = state.NeedsRestartRetry && !state.HasPendingChanges;
        var directSettingsValid = state.UsesRelay || (state.IsPendingAdapterAvailable && portValidation.IsValid);
        var canSave = restartOnly || state.HasPendingChanges && directSettingsValid && state.IsRelayEndpointValid;
        var relayUsage = webHost.RelayUsage;

        page.ApplyPresentation(view =>
        {
            view.TransportMode = state.PendingConfiguration.TransportMode;
            view.EnhancedCapabilitiesEnabled = state.PendingConfiguration.EnhancedCapabilitiesEnabled;
            view.RelayScreenQuality = state.PendingConfiguration.RelayScreenQuality;
            view.CustomRelayEndpoint = state.PendingConfiguration.CustomRelayEndpoint ?? string.Empty;
            view.RelayEndpointValidation = state.IsRelayEndpointValid ? string.Empty : "Enter a complete HTTPS address without a query or fragment.";
            view.RelayConnectionStatus = FormatRelayStatus(webHost.RelayState, webHost.RelayFailureCode);
            view.RelayUsage = FormatRelayUsage(relayUsage?.Bytes, relayUsage?.CheckedAt);
            view.RelayUsagePercent = CalculateRelayUsagePercent(relayUsage?.Bytes, relayUsage?.CutoffBytes);
            view.RelayUsageSummary = FormatRelayUsageSummary(relayUsage?.Bytes, relayUsage?.CutoffBytes);
            view.RelayUsageThresholds = FormatRelayUsageThresholds(relayUsage?.WarningBytes, relayUsage?.CutoffBytes);
            view.ShowsRelayUsageMeter = HasRelayUsageMeter(relayUsage?.Bytes, relayUsage?.WarningBytes, relayUsage?.CutoffBytes);
            view.ShowsRelayRetry = state.UsesRelay && webHost.RelayState is RelayConnectionState.Failed or RelayConnectionState.Disconnected;
            view.ShowsRelayUsageRefresh = state.UsesRelay && webHost.RelayState == RelayConnectionState.Connected;
            view.ActiveAdapter = state.ActiveAdapterName;
            view.ActiveEndpoint = webHost.EnhancedCapabilitiesEnabled
                ? $"{state.ActiveAddress} · Standard Local port {state.ActivePort.ToString(CultureInfo.InvariantCulture)}"
                : $"{state.ActiveAddress}:{state.ActivePort.ToString(CultureInfo.InvariantCulture)}";
            view.ActiveSelectionMode = GetActiveSelectionMode(
                webHost.IsAdapterSelectionAutomatic,
                webHost.IsPortSelectionAutomatic,
                webHost.EnhancedCapabilitiesEnabled);
            view.ConnectionWarning = GetDisplayedConnectionWarning(
                webHost.AddressSelectionWarning,
                webHost.PortSelectionWarning);
            view.ShowsUnavailableAdapter = !state.IsPendingAdapterAvailable;
            view.ShowsReturnToAutomaticAdapter = state.PendingConfiguration.NetworkMode == NetworkSelectionMode.Manual;
            view.IsAdapterChooserOpen = state.IsAdapterChooserOpen;
            view.Candidates = ConnectionCandidateItem.Create(state.Candidates, selectedCandidate);
            view.IsAdvancedExpanded = state.IsAdvancedExpanded;
            view.UsesCustomPort = state.UsesCustomPort;
            view.ManualPort = state.ManualPortText;
            view.PortHeaderStatus = GetPortHeaderStatus(state, webHost.IsPortSelectionAutomatic);
            view.PortValidation = portValidation.Message;
            view.PortValidationIsError = !portValidation.IsValid;
            view.Feedback = state.FeedbackMessage ?? string.Empty;
            view.FeedbackIsError = state.FeedbackIsError;
            view.ShowsActionPanel = state.HasPendingChanges || state.NeedsRestartRetry || state.IsRestartPending;
            view.ConnectionMethodChange = methodChanged ? GetConnectionMethodChange(state) : string.Empty;
            view.ShowsConnectionMethodChange = methodChanged && !state.IsRestartPending;
            view.ActionHeading = GetActionHeading(state);
            view.ActionGuidance = GetActionGuidance(state);
            view.AdapterChange = networkChanged ? GetAdapterChange(state) : string.Empty;
            view.ShowsAdapterChange = networkChanged && !state.IsRestartPending;
            view.PortChange = portChanged ? GetPortChange(state) : string.Empty;
            view.ShowsPortChange = portChanged && !state.IsRestartPending;
            view.PrimaryActionText = restartOnly ? "Restart Voltura Air" : "Save and restart";
            view.PrimaryActionEnabled = canSave && !state.IsRestartPending;
            view.ShowsCancelChanges = state.HasPendingChanges && !state.IsRestartPending;
        });
    }

    internal static string GetActiveSelectionMode(
        bool adapterAutomatic,
        bool portAutomatic,
        bool secureDirect = false) => secureDirect
        ? (adapterAutomatic, portAutomatic) switch
        {
            (true, true) => "Adapter: Automatic · Standard Local port: Automatic",
            (false, true) => "Adapter: Custom · Standard Local port: Automatic",
            (true, false) => "Adapter: Automatic · Standard Local port: Custom",
            (false, false) => "Custom adapter · Standard Local port: Custom"
        }
        : (adapterAutomatic, portAutomatic) switch
        {
            (true, true) => "Automatic",
            (false, true) => "Adapter: Custom · Port: Automatic",
            (true, false) => "Adapter: Automatic · Port: Custom",
            (false, false) => "Custom adapter and port"
        };

    internal static string FormatRelayStatus(RelayConnectionState state, string? failureCode) => state switch
    {
        RelayConnectionState.Disabled => "Relay is not active until Voltura Air restarts.",
        RelayConnectionState.Connecting => "Connecting to Voltura Cloud…",
        RelayConnectionState.Connected => "Connected to Voltura Cloud.",
        RelayConnectionState.Retrying => "Connection unavailable. Retrying automatically…",
        RelayConnectionState.Failed => $"Connection failed ({failureCode ?? "unknown"}). Retrying automatically.",
        _ => "Disconnected."
    };

    internal static string FormatRelayUsage(long? bytes, DateTimeOffset? checkedAt) => bytes is null || checkedAt is null
        ? "Screen relay usage appears after the first viewing session."
        : $"Estimated monthly screen transfer: {(bytes.Value / 1_000_000_000d).ToString("N1", CultureInfo.CurrentCulture)} GB · checked {checkedAt.Value.ToLocalTime():g}";

    internal static bool HasRelayUsageMeter(long? usageBytes, long? warningBytes, long? cutoffBytes) =>
        usageBytes >= 0 && warningBytes > 0 && cutoffBytes > warningBytes;

    internal static double CalculateRelayUsagePercent(long? usageBytes, long? cutoffBytes) =>
        usageBytes is >= 0 && cutoffBytes > 0
            ? Math.Clamp(usageBytes.Value * 100d / cutoffBytes.Value, 0d, 100d)
            : 0d;

    internal static string FormatRelayUsageSummary(long? usageBytes, long? cutoffBytes)
    {
        if (usageBytes is not >= 0 || cutoffBytes is not > 0) return string.Empty;
        var used = usageBytes.Value / 1_000_000_000d;
        var remaining = Math.Max(0, cutoffBytes.Value - usageBytes.Value) / 1_000_000_000d;
        return $"{used.ToString("N1", CultureInfo.CurrentCulture)} GB used · {remaining.ToString("N1", CultureInfo.CurrentCulture)} GB remaining before screen relay stops";
    }

    internal static string FormatRelayUsageThresholds(long? warningBytes, long? cutoffBytes)
    {
        if (warningBytes is not > 0 || cutoffBytes is not > 0 || cutoffBytes.Value <= warningBytes.Value) return string.Empty;
        var warning = warningBytes.Value / 1_000_000_000d;
        var cutoff = cutoffBytes.Value / 1_000_000_000d;
        return $"Data saver starts at {warning.ToString("N0", CultureInfo.CurrentCulture)} GB · Screen relay stops at {cutoff.ToString("N0", CultureInfo.CurrentCulture)} GB";
    }

    internal static string GetPortHeaderStatus(ConnectionPageState state, bool activePortAutomatic)
    {
        if (HasPortChange(state.SavedConfiguration, state.PendingConfiguration))
        {
            return FormatPortSetting("Pending: ", state.PendingConfiguration, state.ManualPortText);
        }

        if ((state.NeedsRestartRetry || state.IsRestartPending) &&
            HasPortChange(state.ActiveConfiguration, state.SavedConfiguration))
        {
            return FormatPortSetting("Pending restart: ", state.SavedConfiguration);
        }

        var mode = activePortAutomatic ? "Automatic" : "Custom";
        return $"{mode} · {state.ActivePort.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static string GetActionHeading(ConnectionPageState state) => state.IsRestartPending
        ? "Restarting Voltura Air…"
        : "Restart required";

    internal static string GetActionGuidance(ConnectionPageState state)
    {
        if (state.IsRestartPending)
        {
            return string.Empty;
        }

        return state.NeedsRestartRetry && !state.HasPendingChanges
            ? "Connection settings are saved. Restart Voltura Air to apply them."
            : "Save changes and restart Voltura Air to apply them.";
    }

    internal static string GetAdapterChange(ConnectionPageState state) =>
        $"{FormatAdapterSetting(state.SavedConfiguration)} → {FormatAdapterSetting(state.PendingConfiguration)}";

    internal static string GetPortChange(ConnectionPageState state) =>
        $"{FormatPortSetting(string.Empty, state.SavedConfiguration)} → " +
        FormatPortSetting(string.Empty, state.PendingConfiguration, state.ManualPortText);

    internal static string GetConnectionMethodChange(ConnectionPageState state) =>
        $"{FormatTransport(state.SavedConfiguration.TransportMode)} → {FormatTransport(state.PendingConfiguration.TransportMode)}";

    private static string FormatTransport(ConnectionTransportMode mode) => mode == ConnectionTransportMode.Relay
        ? "Cloud relay through Voltura"
        : "Direct local network";

    internal static string GetDisplayedConnectionWarning(string? addressWarning, string? portWarning)
    {
        var displayedAddressWarning = string.Equals(
            addressWarning,
            LanAddressSelector.MultipleAdaptersWarning,
            StringComparison.Ordinal)
            ? null
            : addressWarning;
        return string.Join(
            Environment.NewLine,
            new[] { displayedAddressWarning, portWarning }
                .Where(message => !string.IsNullOrWhiteSpace(message)));
    }

    private static string FormatAdapterSetting(ConnectionConfiguration configuration)
    {
        if (configuration.NetworkMode == NetworkSelectionMode.Automatic)
        {
            return "Automatic";
        }

        return configuration.ManualAdapterName ?? configuration.ManualHostAddress ?? "Custom adapter";
    }

    private static string FormatPortSetting(
        string prefix,
        ConnectionConfiguration configuration,
        string? pendingManualPort = null)
    {
        if (configuration.PortMode == PortSelectionMode.Automatic)
        {
            return $"{prefix}Automatic";
        }

        var port = pendingManualPort ?? configuration.ManualPort?.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(port)
            ? $"{prefix}Custom"
            : $"{prefix}Custom · {port}";
    }

    private static bool HasNetworkChange(ConnectionConfiguration saved, ConnectionConfiguration pending) =>
        saved.NetworkMode != pending.NetworkMode ||
        !string.Equals(saved.ManualHostAddress, pending.ManualHostAddress, StringComparison.Ordinal) ||
        !string.Equals(saved.ManualAdapterId, pending.ManualAdapterId, StringComparison.Ordinal) ||
        !string.Equals(saved.ManualAdapterName, pending.ManualAdapterName, StringComparison.Ordinal);

    private static bool HasPortChange(ConnectionConfiguration saved, ConnectionConfiguration pending) =>
        saved.PortMode != pending.PortMode || saved.ManualPort != pending.ManualPort;
}
