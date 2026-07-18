using System.Globalization;
using VolturaAir.Host.Features.Diagnostics;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private IReadOnlyList<DiagnosticItem> GetDiagnostics()
    {
        return
        [
            new("Voltura Air version", AppVersion.Display),
            new("Voltura Air web client version", "copy mobile diagnostics for web client version"),
            new("PC name", Environment.MachineName),
            new("Selected adapter", _webHost.SelectedAdapterName),
            new("Selected IP", _webHost.AdvertisedHostAddress),
            new("Selected port", _webHost.Port.ToString(CultureInfo.InvariantCulture)),
            new("Host URL", _webHost.ServerUrl),
            new("Current WebSocket URL", _webHost.WebSocketUrl),
            new("Windows lock policy", _workstationLockPolicy.GetStatus().State.ToString().ToLowerInvariant()),
            new("Application logging", AppLoggingSettings.IsEnabled() ? "enabled" : "disabled"),
            new("Application log retention", $"{AppLoggingSettings.GetMaxAgeDays().ToString(CultureInfo.InvariantCulture)} days"),
            new("Application log folder", _appLog.LogDirectory),
            new("Pairing state", GetPairingState()),
            new("Last error code", GetLastErrorCode()),
            new("Last error message", GetLastErrorMessage()),
            new("Paired device count", _pairingManager.PairedDeviceCount.ToString(CultureInfo.InvariantCulture)),
            new("Connected device count", _pairingManager.ActiveControllerCount.ToString(CultureInfo.InvariantCulture)),
            new("Paired devices", _pairingManager.PairedDeviceSummary),
            new("Active devices", _pairingManager.HasActiveController ? _pairingManager.ActiveDeviceSummary : "none"),
            new("Data folder", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltura Air")),
            new("Executable", Environment.ProcessPath ?? string.Empty)
        ];
    }

    private string BuildDiagnosticsText()
    {
        var lines = new List<string>
        {
            "Voltura Air diagnostics",
            $"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}"
        };
        lines.AddRange(GetDiagnostics().Select(detail => $"{detail.Name}: {RedactDiagnosticValue(detail.Value)}"));
        return string.Join(Environment.NewLine, lines);
    }

    private string GetPairingState()
    {
        if (_pairingManager.HasActiveController)
        {
            return "connected";
        }

        return _pairingManager.IsPaired ? "paired-not-connected" : "ready-to-pair";
    }

    private string GetLastErrorCode()
    {
        if (!string.IsNullOrWhiteSpace(_webHost.PortSelectionWarning))
        {
            return "VAIR-HOST-PORT-WARNING";
        }

        if (!string.IsNullOrWhiteSpace(_webHost.AddressSelectionWarning))
        {
            return "VAIR-HOST-NETWORK-WARNING";
        }

        return "none";
    }

    private string GetLastErrorMessage()
    {
        var messages = new[]
        {
            _webHost.AddressSelectionWarning,
            _webHost.PortSelectionWarning
        }.Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();

        return messages.Length == 0 ? "none" : string.Join(" ", messages);
    }

    private static string RedactDiagnosticValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.Contains("t=", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("pairToken", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("hash", StringComparison.OrdinalIgnoreCase))
        {
            return "[redacted]";
        }

        return value;
    }

    private DiagnosticsPageView BuildDiagnosticsPage()
    {
        return new DiagnosticsPageView(
            () =>
            {
                SetDiagnosticsTitle("Application log");
                return CreateApplicationLogViewer();
            },
            () =>
            {
                SetDiagnosticsTitle("System details");
                return BuildSystemDiagnosticsView();
            });
    }

    private void SetDiagnosticsTitle(string viewTitle)
    {
        if (_activePage == HostPage.Diagnostics)
        {
            PageTitleText.Text = $"Diagnostics > {viewTitle}";
        }
    }

    private SystemDiagnosticsView BuildSystemDiagnosticsView()
    {
        return new SystemDiagnosticsView(
            GetDiagnostics(),
            detail => CopyToClipboard($"{detail.Name}: {detail.Value}", "Copied"),
            () => CopyToClipboard(BuildDiagnosticsText(), "Diagnostics copied"),
            OpenProductSite);
    }
}
