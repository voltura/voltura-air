using System.Text.Json;

namespace VolturaAir.Host;

public sealed class PairingStore
{
    private readonly string _filePath;

    public PairingStore(string? rootFolder = null)
    {
        var folder = Path.Combine(rootFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltura Air");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "pairing.json");
    }

    public IReadOnlyList<PairingRecord> Load()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<PairingRecord>();
        }

        try
        {
            var data = JsonSerializer.Deserialize<PairingData>(File.ReadAllText(_filePath), JsonOptions.Default);
            return data?.Devices ?? Array.Empty<PairingRecord>();
        }
        catch
        {
            return Array.Empty<PairingRecord>();
        }
    }

    public void Save(IReadOnlyCollection<PairingRecord> records)
    {
        File.WriteAllText(_filePath, JsonSerializer.Serialize(new PairingData(records.ToArray()), JsonOptions.Default));
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    private sealed record PairingData(IReadOnlyList<PairingRecord> Devices);
}
