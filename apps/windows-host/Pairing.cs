namespace VolturaAir.Host;

public sealed partial class PairingManager
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
    private readonly PairingStore _store;
    private readonly object _gate = new();
    private PairingToken? _currentToken;
    private readonly List<PairingRecord> _records;
    private readonly Dictionary<string, int> _activeConnections = new(StringComparer.Ordinal);

    public PairingManager(PairingStore store)
    {
        _store = store;
        _records = store.Load().Select(NormalizeRecord).ToList();
    }

    public event EventHandler? ConnectionChanged;
    public event EventHandler? PermissionsChanged;
    public event EventHandler? DeviceProfileChanged;
    public event EventHandler<PairingRevokedEventArgs>? PairingRevoked;
}
