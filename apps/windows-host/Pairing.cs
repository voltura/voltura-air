namespace VolturaAir.Host;

public sealed partial class PairingManager(PairingStore store)
{
    internal static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan TokenRotationOverlap = TimeSpan.FromSeconds(15);
    private readonly PairingStore _store = store;
    private readonly Lock _gate = new();
    private PairingToken? _currentToken;
    private PairingToken? _previousToken;
    private readonly List<PairingRecord> _records = [.. store.Load().Select(NormalizeRecord)];
    private readonly Dictionary<string, int> _activeConnections = new(StringComparer.Ordinal);

    public event EventHandler? ConnectionChanged;
    public event EventHandler? PermissionsChanged;
    public event EventHandler? DeviceProfileChanged;
    public event EventHandler<PairingRevokedEventArgs>? PairingRevoked;
    internal event EventHandler? PairingCodeInvalidated;
}
