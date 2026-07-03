using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class TempPairingStore : IDisposable
{
    private readonly string _tempRoot;

    public TempPairingStore()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "VolturaAir.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        Store = new PairingStore(_tempRoot);
    }

    public PairingStore Store { get; }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
