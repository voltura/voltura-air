using System.Text.Json;
using System.Security;

namespace VolturaAir.Host;

public sealed class PairingStore
{
    private const long MaxStoreBytes = 1024 * 1024;
    private const int MaxRecords = 1024;
    private const int MaxClientIdLength = 128;
    private const int MaxDeviceNameLength = 120;
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
            return [];
        }

        try
        {
            if (new FileInfo(_filePath).Length > MaxStoreBytes)
            {
                return [];
            }

            var data = JsonSerializer.Deserialize<PairingData>(File.ReadAllText(_filePath), JsonOptions.Default);
            return [.. (data?.Devices ?? [])
                .Where(record => record is not null && IsValidRecord(record))
                .Take(MaxRecords)
                .GroupBy(record => record.ClientId, StringComparer.Ordinal)
                .Select(group => group.Last())];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or JsonException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyCollection<PairingRecord> records)
    {
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var persistedRecords = records.Where(IsValidRecord).TakeLast(MaxRecords).ToArray();
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new PairingData(persistedRecords), JsonOptions.Default));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    private static bool IsValidRecord(PairingRecord record)
    {
        return !string.IsNullOrWhiteSpace(record.ClientId) &&
            record.ClientId.Length <= MaxClientIdLength &&
            PairingManager.IsValidReconnectPublicKey(record.ReconnectPublicKey) &&
            record.DeviceName is not null &&
            record.DeviceName.Length <= MaxDeviceNameLength;
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
        }
    }

    private sealed record PairingData(IReadOnlyList<PairingRecord> Devices);
}
