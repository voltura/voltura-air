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
}
