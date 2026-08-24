using System.Text.Json;
using System.Security;

namespace VolturaAir.Host;

public sealed class PairingStore
{
    private const int MaxStoreBytes = 1024 * 1024;
    private const int MaxRecords = 1024;
    private const int MaxClientIdLength = 128;
    private const int MaxDeviceNameLength = 120;
    private readonly string _filePath;

    internal Action? BeforeReplaceForTests { get; set; }

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
            using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            if (stream.Length > MaxStoreBytes)
            {
                return [];
            }

            var buffer = new byte[MaxStoreBytes + 1];
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (totalRead > MaxStoreBytes || stream.ReadByte() != -1)
            {
                return [];
            }

            var data = JsonSerializer.Deserialize<PairingData>(buffer.AsSpan(0, totalRead), JsonOptions.Default);
            return [.. DeduplicateValidRecords(data?.Devices ?? [])
                .Take(MaxRecords)];
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
            var persistedRecords = DeduplicateValidRecords(records)
                .TakeLast(MaxRecords)
                .ToArray();
            var data = JsonSerializer.SerializeToUtf8Bytes(new PairingData(persistedRecords), JsonOptions.Default);
            if (data.Length > MaxStoreBytes)
            {
                throw new InvalidDataException($"Pairing data exceeds the {MaxStoreBytes}-byte storage limit.");
            }

            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }

            BeforeReplaceForTests?.Invoke();
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

    private static IEnumerable<PairingRecord> DeduplicateValidRecords(
        IEnumerable<PairingRecord> records) => records
            .Where(record => record is not null && IsValidRecord(record))
            .GroupBy(record => record.ClientId, StringComparer.Ordinal)
            .Select(group => group.Last());

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
