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
        var selectedCandidate = pendingAdapter ?? state.Candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.Address.ToString(), state.ActiveAddress, StringComparison.OrdinalIgnoreCase));
        var restartOnly = state.NeedsRestartRetry && !state.HasPendingChanges;
        var canSave = restartOnly ||
            state.HasPendingChanges && state.IsPendingAdapterAvailable && portValidation.IsValid;

        page.ApplyPresentation(view =>
        {
            view.ActiveAdapter = state.ActiveAdapterName;
            view.ActiveEndpoint = $"{state.ActiveAddress}:{state.ActivePort.ToString(CultureInfo.InvariantCulture)}";
            view.ActiveSelectionMode = GetActiveSelectionMode(
                webHost.IsAdapterSelectionAutomatic,
                webHost.IsPortSelectionAutomatic);
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

    internal static string GetActiveSelectionMode(bool adapterAutomatic, bool portAutomatic) =>
        (adapterAutomatic, portAutomatic) switch
        {
            (true, true) => "Automatic",
            (false, true) => "Adapter: Custom · Port: Automatic",
            (true, false) => "Adapter: Automatic · Port: Custom",
            (false, false) => "Custom adapter and port"
        };

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
