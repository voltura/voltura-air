using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class TempPairingStore : IDisposable
{
    private const int DeleteAttempts = 5;
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
        for (var attempt = 1; attempt <= DeleteAttempts; attempt++)
        {
            if (!Directory.Exists(_tempRoot))
            {
                return;
            }

            try
            {
                Directory.Delete(_tempRoot, recursive: true);
                return;
            }
            catch (IOException) when (attempt < DeleteAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
            }
            catch (UnauthorizedAccessException) when (attempt < DeleteAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
            }
        }
    }
}
