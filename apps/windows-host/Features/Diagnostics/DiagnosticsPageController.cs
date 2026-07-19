using System.Globalization;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Features.Diagnostics;

internal sealed class DiagnosticsPageController(
    PairingManager pairingManager,
    WebHostService webHost,
    IWorkstationLockPolicy workstationLockPolicy,
    IAppLog appLog,
    ApplicationLogController applicationLog,
    HostClipboardFeedback clipboard,
    Action<string> viewTitleChanged)
{
    public DiagnosticsPageView CreateView()
    {
        return new DiagnosticsPageView(
            () =>
            {
                viewTitleChanged("Application log");
                return applicationLog.CreateView();
            },
            () =>
            {
                viewTitleChanged("System details");
                return CreateSystemDetailsView();
            });
    }

    private SystemDiagnosticsView CreateSystemDetailsView()
    {
        return new SystemDiagnosticsView(
            GetDiagnostics(),
            detail => clipboard.Copy($"{detail.Name}: {detail.Value}", "Copied"),
            () => clipboard.Copy(BuildDiagnosticsText(), "Diagnostics copied"),
            ProductWebsite.Open);
    }

    private List<DiagnosticItem> GetDiagnostics()
    {
        List<DiagnosticItem> diagnostics =
        [
            new("Voltura Air version", AppVersion.Display),
            new("Voltura Air web client version", "copy mobile diagnostics for web client version"),
            new("PC name", Environment.MachineName),
            new("Selected adapter", webHost.SelectedAdapterName),
            new("Selected IP", webHost.AdvertisedHostAddress),
            new("Selected port", webHost.Port.ToString(CultureInfo.InvariantCulture)),
            new("Host URL", webHost.ServerUrl),
            new("Current WebSocket URL", webHost.WebSocketUrl),
            new("Windows lock policy", workstationLockPolicy.GetStatus().State.ToString().ToLowerInvariant()),
            new("Application logging", AppLoggingSettings.IsEnabled() ? "enabled" : "disabled"),
            new("Application log retention", $"{AppLoggingSettings.GetMaxAgeDays().ToString(CultureInfo.InvariantCulture)} days"),
            new("Application log folder", appLog.LogDirectory),
            new("Pairing state", GetPairingState()),
        ];

        foreach (var advisory in GetAdvisories())
        {
            diagnostics.Add(new(advisory.Name, advisory.Summary));
            diagnostics.Add(new($"{advisory.Name} details", advisory.Message));
        }

        diagnostics.AddRange(
        [
            new("Paired device count", pairingManager.PairedDeviceCount.ToString(CultureInfo.InvariantCulture)),
            new("Connected device count", pairingManager.ActiveControllerCount.ToString(CultureInfo.InvariantCulture)),
            new("Paired devices", pairingManager.PairedDeviceSummary),
            new("Active devices", pairingManager.HasActiveController ? pairingManager.ActiveDeviceSummary : "none"),
            new("Data folder", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltura Air")),
            new("Executable", Environment.ProcessPath ?? string.Empty)
        ]);

        return diagnostics;
    }

    private string BuildDiagnosticsText()
    {
        var lines = new List<string>
        {
            "Voltura Air diagnostics",
            $"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}"
        };
        lines.AddRange(GetDiagnostics().Select(detail => $"{detail.Name}: {RedactDiagnosticValue(detail.Value)}"));
        lines.AddRange(GetAdvisories().Select(advisory => $"{advisory.Name} code: {advisory.Code}"));
        return string.Join(Environment.NewLine, lines);
    }

    private string GetPairingState()
    {
        if (pairingManager.HasActiveController)
        {
            return "connected";
        }

        return pairingManager.IsPaired ? "paired-not-connected" : "ready-to-pair";
    }

    private IEnumerable<DiagnosticAdvisory> GetAdvisories()
    {
        if (!string.IsNullOrWhiteSpace(webHost.AddressSelectionWarning))
        {
            yield return new DiagnosticAdvisory(
                "Network advisory",
                "Multiple network adapters detected",
                webHost.AddressSelectionWarning,
                "VAIR-HOST-NETWORK-WARNING");
        }

        if (!string.IsNullOrWhiteSpace(webHost.PortSelectionWarning))
        {
            yield return new DiagnosticAdvisory(
                "Port advisory",
                "Port configuration needs attention",
                webHost.PortSelectionWarning,
                "VAIR-HOST-PORT-WARNING");
        }
    }

    private static string RedactDiagnosticValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Contains("t=", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("pairToken", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("hash", StringComparison.OrdinalIgnoreCase)
            ? "[redacted]"
            : value;
    }

    private sealed record DiagnosticAdvisory(string Name, string Summary, string Message, string Code);
}
