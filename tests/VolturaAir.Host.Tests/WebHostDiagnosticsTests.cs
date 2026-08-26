using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostDiagnosticsTests : WebHostServiceTestBase
{
    [Fact]
    public async Task AuthorizedRequestReturnsOnlyAllowlistedSnapshot()
    {
        var probe = new ComputerDiagnosticsTests.FakeComputerDiagnosticsProbe();
        await using var fixture = await WebHostFixture.StartAsync(computerDiagnosticsProbe: probe);
        using var socket = await PairAsync(fixture, "private-phone-name");

        var result = await SendAndReceiveAsync(socket, new { type = "diagnostics.get", operationId = "diagnostics-1" });

        Assert.True(result.GetProperty("succeeded").GetBoolean());
        var snapshot = result.GetProperty("snapshot");
        Assert.Equal(AppVersion.Display, snapshot.GetProperty("hostVersion").GetString());
        Assert.Equal(
            fixture.WebHost.EnhancedCapabilitiesEnabled ? "enabled" : "disabled",
            snapshot.GetProperty("enhancedCapabilities").GetString());
        Assert.Equal("Example Processor", snapshot.GetProperty("computer").GetProperty("processor").GetString());
        Assert.Equal(9, probe.CaptureCount);
        var serialized = result.GetRawText();
        foreach (var deniedName in new[] { "applicationLogFolder", "dataFolder", "executable", "userName", "hostUrl", "webSocketUrl", "pairedDevices", "activeDevices" })
        {
            Assert.DoesNotContain(deniedName, serialized, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("private-phone-name", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.UserName, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BlockedRequestReturnsNoSnapshotAndDoesNotCollectComputerData()
    {
        var probe = new ComputerDiagnosticsTests.FakeComputerDiagnosticsProbe();
        await using var fixture = await WebHostFixture.StartAsync(computerDiagnosticsProbe: probe);
        const string clientId = "remote-device";
        using var socket = await PairAsync(fixture, clientId);
        Assert.True(fixture.Manager.SetDeviceAccessProfile(clientId, DeviceAccessProfile.RemoteControls));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var pushedStatus = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));

        var result = await SendAndReceiveAsync(socket, new { type = "diagnostics.get", operationId = "diagnostics-2" });

        Assert.False(pushedStatus.RootElement.GetProperty("capabilities").GetProperty("diagnostics").GetProperty("canView").GetBoolean());
        Assert.False(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal("permission-denied", result.GetProperty("code").GetString());
        Assert.False(result.TryGetProperty("snapshot", out _));
        Assert.Equal(0, probe.CaptureCount);
    }

    private static async Task<WebSocket> PairAsync(WebHostFixture fixture, string clientId)
    {
        var socket = await ConnectAsync(fixture.WebHost);
        await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Private Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
        });
        return socket;
    }
}
