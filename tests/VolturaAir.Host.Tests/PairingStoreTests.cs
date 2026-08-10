using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class PairingStoreTests
{
    [Fact]
    public void SaveAtomicallyReplacesPairingDataWithoutLeavingTemporaryFiles()
    {
        var root = Directory.CreateTempSubdirectory("VolturaAir-PairingStore-");
        try
        {
            var store = new PairingStore(root.FullName);
            using var firstKey = new PairingTestKey();
            using var secondKey = new PairingTestKey();
            store.Save([new PairingRecord("client-a", firstKey.PublicKey, "First name")]);
            store.Save([new PairingRecord("client-a", secondKey.PublicKey, "Updated name")]);

            var record = Assert.Single(store.Load());
            Assert.Equal("Updated name", record.DeviceName);
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root.FullName, "Voltura Air"), "*.tmp"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadRejectsInvalidPersistedRecords()
    {
        var root = Directory.CreateTempSubdirectory("VolturaAir-PairingStore-");
        try
        {
            _ = new PairingStore(root.FullName);
            using var key = new PairingTestKey();
            var pairingPath = Path.Combine(root.FullName, "Voltura Air", "pairing.json");
            File.WriteAllText(pairingPath, $$"""
                {
                  "devices": [
                    { "clientId": null, "reconnectPublicKey": "{{key.PublicKey}}", "deviceName": "Invalid" },
                    { "clientId": "client-a", "reconnectPublicKey": "", "deviceName": "Invalid" },
                    { "clientId": "client-compressed", "reconnectPublicKey": "AjW9eU9yrZWu_unsupported_compressed_point", "deviceName": "Invalid" },
                    { "clientId": "client-b", "reconnectPublicKey": "{{key.PublicKey}}", "deviceName": "Phone" }
                  ]
                }
                """);

            var record = Assert.Single(new PairingStore(root.FullName).Load());
            Assert.Equal("client-b", record.ClientId);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadDeduplicatesBeforeApplyingDeviceLimit()
    {
        var root = Directory.CreateTempSubdirectory("VolturaAir-PairingStore-");
        try
        {
            _ = new PairingStore(root.FullName);
            using var key = new PairingTestKey();
            var pairingPath = Path.Combine(root.FullName, "Voltura Air", "pairing.json");
            var devices = Enumerable.Range(0, 1024)
                .Select(index => new PairingRecord("client-a", key.PublicKey, $"Phone {index}"))
                .Append(new PairingRecord("client-b", key.PublicKey, "Tablet"))
                .ToArray();
            File.WriteAllText(
                pairingPath,
                JsonSerializer.Serialize(new { devices }, JsonOptions.Default));

            var records = new PairingStore(root.FullName).Load();

            Assert.Collection(
                records,
                record =>
                {
                    Assert.Equal("client-a", record.ClientId);
                    Assert.Equal("Phone 1023", record.DeviceName);
                },
                record => Assert.Equal("client-b", record.ClientId));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void SaveDeduplicatesBeforeApplyingDeviceLimit()
    {
        var root = Directory.CreateTempSubdirectory("VolturaAir-PairingStore-");
        try
        {
            var store = new PairingStore(root.FullName);
            using var key = new PairingTestKey();
            var records = new[] { new PairingRecord("client-b", key.PublicKey, "Tablet") }
                .Concat(Enumerable.Range(0, 1024)
                    .Select(index => new PairingRecord("client-a", key.PublicKey, $"Phone {index}")))
                .ToArray();

            store.Save(records);

            Assert.Collection(
                store.Load(),
                record => Assert.Equal("client-b", record.ClientId),
                record =>
                {
                    Assert.Equal("client-a", record.ClientId);
                    Assert.Equal("Phone 1023", record.DeviceName);
                });
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void OptionalCustomScreenViewportIsBackwardCompatibleAndBounded()
    {
        var root = Directory.CreateTempSubdirectory("VolturaAir-PairingStore-");
        try
        {
            var store = new PairingStore(root.FullName);
            using var key = new PairingTestKey();
            store.Save(
            [
                new PairingRecord("legacy", key.PublicKey, "Legacy phone"),
                new PairingRecord(
                    "valid",
                    key.PublicKey,
                    "Tablet",
                    CustomScreenViewport: new CustomScreenViewport(800, 1180, "portrait")),
                new PairingRecord(
                    "invalid",
                    key.PublicKey,
                    "Invalid metadata",
                    CustomScreenViewport: new CustomScreenViewport(1, 9000, "diagonal"))
            ]);

            var manager = new PairingManager(store);

            Assert.Null(manager.GetCustomScreenViewport("legacy"));
            Assert.Equal(
                new CustomScreenViewport(800, 1180, "portrait"),
                manager.GetCustomScreenViewport("valid"));
            Assert.Null(manager.GetCustomScreenViewport("invalid"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
