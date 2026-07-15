using System.Net;
using System.Net.Sockets;

namespace VolturaAir.Host;

internal sealed record PortSelectionResult(
    bool Succeeded,
    int Port,
    bool IsAutomatic,
    string? ErrorMessage,
    string? Warning = null);

internal static class PortSelector
{
    public const int PreferredPort = 51395;
    public const int AutomaticPortSearchCount = 31;
    public const int MinimumUserPort = 49152;
    public const int MaximumPort = 65535;
    public const string ManualPortRangeMessage = "Manual port must be between 49152 and 65535.";
    private static readonly HashSet<int> ReservedManualPorts =
    [
        80,
        443,
        1080,
        1194,
        1433,
        1521,
        1723,
        1883,
        2049,
        2375,
        2376,
        2483,
        2484,
        3000,
        3001,
        3306,
        3389,
        4443,
        5000,
        5001,
        5173,
        5174,
        5353,
        5432,
        5672,
        5900,
        5985,
        5986,
        6379,
        6443,
        6667,
        8000,
        8008,
        8080,
        8081,
        8088,
        8443,
        8888,
        9000,
        9001,
        9042,
        9090,
        9200,
        9300,
        11211,
        15672,
        25565,
        27015,
        27017
    ];

    public static PortSelectionResult Select(
        NetworkSettingsSnapshot settings,
        Func<int, bool> isPortAvailable,
        Func<int> findFreePort)
    {
        if (settings.PortMode == PortSelectionMode.Manual)
        {
            if (settings.ManualPort is not { } manualPort || !IsValidPort(manualPort))
            {
                return new PortSelectionResult(false, 0, IsAutomatic: false, ManualPortRangeMessage);
            }

            var manualPortError = GetManualPortValidationError(manualPort);
            if (manualPortError is not null)
            {
                return new PortSelectionResult(false, 0, IsAutomatic: false, manualPortError);
            }

            if (!isPortAvailable(manualPort))
            {
                return new PortSelectionResult(false, 0, IsAutomatic: false, $"Manual port {manualPort} is already in use.");
            }

            return new PortSelectionResult(true, manualPort, IsAutomatic: false, ErrorMessage: null);
        }

        foreach (var port in GetAutomaticPortCandidates(settings.LastAutomaticPort))
        {
            if (isPortAvailable(port))
            {
                var warning = GetAutomaticPortWarning(port, settings.LastAutomaticPort);
                return new PortSelectionResult(true, port, IsAutomatic: true, ErrorMessage: null, warning);
            }
        }

        var fallbackPort = findFreePort();
        return new PortSelectionResult(
            true,
            fallbackPort,
            IsAutomatic: true,
            ErrorMessage: null,
            Warning: $"Preferred Voltura Air ports were unavailable. Using port {fallbackPort} instead. Scan a fresh QR code.");
    }

    public static bool IsValidPort(int port)
    {
        return port is >= 1 and <= MaximumPort;
    }

    public static bool IsAllowedManualPort(int port)
    {
        return GetManualPortValidationError(port) is null;
    }

    public static string? GetManualPortValidationError(int port)
    {
        if (!IsValidPort(port))
        {
            return ManualPortRangeMessage;
        }

        if (port < MinimumUserPort)
        {
            return ManualPortRangeMessage;
        }

        if (ReservedManualPorts.Contains(port))
        {
            return $"Manual port {port} is reserved for common services. Choose another port.";
        }

        return null;
    }

    public static IReadOnlyList<int> GetAutomaticPortCandidates(int? lastAutomaticPort)
    {
        var candidates = new List<int>();
        if (lastAutomaticPort is { } lastPort && IsValidPort(lastPort))
        {
            candidates.Add(lastPort);
        }

        for (var port = PreferredPort; port < PreferredPort + AutomaticPortSearchCount; port += 1)
        {
            if (!candidates.Contains(port))
            {
                candidates.Add(port);
            }
        }

        return candidates;
    }

    public static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string? GetAutomaticPortWarning(int selectedPort, int? lastAutomaticPort)
    {
        if (selectedPort == PreferredPort || selectedPort == lastAutomaticPort)
        {
            return null;
        }

        return $"Preferred port {PreferredPort} was unavailable. Using port {selectedPort} instead. Scan a fresh QR code.";
    }
}
