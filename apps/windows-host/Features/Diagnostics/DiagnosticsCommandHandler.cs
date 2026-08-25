using System.Globalization;
using System.Net.WebSockets;

namespace VolturaAir.Host.Features.Diagnostics;

internal sealed record MobileDiagnosticAdvisory(string Name, string Summary, string Details, string Code);

internal sealed record MobileHostDiagnosticsSnapshot(
    string GeneratedAt,
    string HostVersion,
    string ConnectionMethod,
    string EnhancedCapabilities,
    string RelayStatus,
    string RelayEndpointType,
    string RelayFailureCode,
    string PairingState,
    string WindowsLockPolicy,
    string ApplicationLogging,
    string ApplicationLogRetention,
    int PairedDeviceCount,
    int ConnectedDeviceCount,
    string PcName,
    string SelectedAdapter,
    string SelectedIp,
    int SelectedPort,
    IReadOnlyList<MobileDiagnosticAdvisory> Advisories,
    ComputerDiagnosticsSnapshot Computer);

internal sealed record MobileHostDiagnosticsContext(
    ConnectionTransportMode TransportMode,
    bool EnhancedCapabilitiesEnabled,
    RelayConnectionState RelayState,
    bool? RelayEndpointIsOfficial,
    string? RelayFailureCode,
    string SelectedAdapter,
    string SelectedIp,
    int SelectedPort,
    string? AddressSelectionWarning,
    string? PortSelectionWarning);

internal sealed class DiagnosticsCommandHandler(
    PairingManager pairingManager,
    HostStatusPayloadFactory statusFactory,
    IWorkstationLockPolicy workstationLockPolicy,
    ComputerDiagnosticsProvider computerDiagnostics,
    Func<MobileHostDiagnosticsContext> getContext,
    WebSocketTransport transport)
{
    public async Task HandleAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        CancellationToken cancellationToken)
    {
        if (!statusFactory.CanViewDiagnostics(clientId))
        {
            await transport.SendAsync(socket, new
            {
                type = "diagnostics.get.result",
                operationId,
                succeeded = false,
                code = "permission-denied",
                message = "Diagnostics viewing is disabled for this device."
            }, cancellationToken);
            return;
        }

        try
        {
            await transport.SendAsync(socket, new
            {
                type = "diagnostics.get.result",
                operationId,
                succeeded = true,
                message = "Diagnostics loaded.",
                snapshot = CreateSnapshot()
            }, cancellationToken);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            await transport.SendAsync(socket, new
            {
                type = "diagnostics.get.result",
                operationId,
                succeeded = false,
                code = "diagnostics-unavailable",
                message = "The PC could not generate diagnostics."
            }, cancellationToken);
        }
    }

    private MobileHostDiagnosticsSnapshot CreateSnapshot()
    {
        var context = getContext();
        var advisories = new List<MobileDiagnosticAdvisory>(2);
        if (!string.IsNullOrWhiteSpace(context.AddressSelectionWarning))
        {
            advisories.Add(new(
                "Network advisory",
                "Multiple network adapters detected",
                Limit(context.AddressSelectionWarning),
                "VAIR-HOST-NETWORK-WARNING"));
        }

        if (!string.IsNullOrWhiteSpace(context.PortSelectionWarning))
        {
            advisories.Add(new(
                "Port advisory",
                "Port configuration needs attention",
                Limit(context.PortSelectionWarning),
                "VAIR-HOST-PORT-WARNING"));
        }

        return new MobileHostDiagnosticsSnapshot(
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            AppVersion.Display,
            context.TransportMode == ConnectionTransportMode.Relay ? "cloud-relay" : "direct-lan",
            context.EnhancedCapabilitiesEnabled ? "enabled" : "disabled",
            context.RelayState.ToString().ToLowerInvariant(),
            context.RelayEndpointIsOfficial == false
                ? "custom"
                : context.TransportMode == ConnectionTransportMode.Relay ? "official" : "not-active",
            context.RelayFailureCode ?? "none",
            GetPairingState(),
            workstationLockPolicy.GetStatus().State.ToString().ToLowerInvariant(),
            AppLoggingSettings.IsEnabled() ? "enabled" : "disabled",
            $"{AppLoggingSettings.GetMaxAgeDays().ToString(CultureInfo.InvariantCulture)} days",
            pairingManager.PairedDeviceCount,
            pairingManager.ActiveControllerCount,
            Limit(Environment.MachineName),
            Limit(context.SelectedAdapter),
            Limit(context.SelectedIp),
            context.SelectedPort,
            advisories,
            computerDiagnostics.Capture());
    }

    private string GetPairingState() => pairingManager.HasActiveController
        ? "connected"
        : pairingManager.IsPaired ? "paired-not-connected" : "ready-to-pair";

    private static string Limit(string value) => value.Length <= 256 ? value : value[..256];

    private static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or AccessViolationException;
}
