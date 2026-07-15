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
            store.Save([new PairingRecord("client-a", new string('A', 64), "First name")]);
            store.Save([new PairingRecord("client-a", new string('B', 64), "Updated name")]);

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
            var pairingPath = Path.Combine(root.FullName, "Voltura Air", "pairing.json");
            File.WriteAllText(pairingPath, $$"""
                {
                  "devices": [
                    { "clientId": null, "secretHash": "{{new string('A', 64)}}", "deviceName": "Invalid" },
                    { "clientId": "client-a", "secretHash": "not-a-hash", "deviceName": "Invalid" },
                    { "clientId": "client-b", "secretHash": "{{new string('B', 64)}}", "deviceName": "Phone" }
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
