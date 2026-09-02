using System.Text.Json;
using System.Text.Json.Nodes;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class DeviceAccessProfilePersistenceTests : IsolatedHostSettingsTest
{
    [Fact]
    public void LegacyMigrationPreservesEffectiveAccessIdentityAndTrustData()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var addedAt = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
        var connectedAt = addedAt.AddHours(1);
        var disconnectedAt = connectedAt.AddMinutes(10);
        var renamedAt = disconnectedAt.AddMinutes(5);
        var viewport = new CustomScreenViewport(390, 844, "portrait");
        var global = HostPermissions.DefaultGlobal with
        {
            AllowRemoteInput = false,
            AllowPcSleep = true,
            AllowFileBrowsing = true,
            HideProtectedFileSystemItems = false
        };
        AppPermissionSettings.Save(global);
        var legacy = new PairingRecord(
            "legacy-client",
            key.PublicKey,
            "Legacy phone",
            addedAt,
            LastConnectedAt: connectedAt,
            LastDisconnectedAt: disconnectedAt,
            LastRenamedAt: renamedAt,
            Platform: "iOS",
            Browser: "Safari",
            DisplayMode: "installed",
            HostIdentityFingerprint: "trusted-fingerprint",
            PermissionOverrides: new DevicePermissionOverrides(
                AllowRemoteInput: true,
                AllowFileBrowsing: false),
            PointerSpeedOverride: 65,
            ShowModeButtonsOverride: false,
            ControlDepthOverride: true,
            CustomScreenViewport: viewport,
            LastConnectionMethod: DeviceConnectionMethod.DebugDirect);
        store.Store.Save([legacy]);

        var manager = new PairingManager(store.Store);
        var effective = manager.GetEffectivePermissions("legacy-client", global);
        var persisted = Assert.Single(store.Store.Load());

        Assert.Equal(DeviceAccessProfile.Custom, persisted.AccessProfile);
        Assert.Equal("legacy-client", persisted.ClientId);
        Assert.Equal(key.PublicKey, persisted.ReconnectPublicKey);
        Assert.Equal("Legacy phone", persisted.DeviceName);
        Assert.Equal(addedAt, persisted.AddedAt);
        Assert.Equal(connectedAt, persisted.LastConnectedAt);
        Assert.Equal(disconnectedAt, persisted.LastDisconnectedAt);
        Assert.Equal(renamedAt, persisted.LastRenamedAt);
        Assert.Equal("trusted-fingerprint", persisted.HostIdentityFingerprint);
        Assert.Equal("iOS", persisted.Platform);
        Assert.Equal("Safari", persisted.Browser);
        Assert.Equal("installed", persisted.DisplayMode);
        Assert.Equal(65, persisted.PointerSpeedOverride);
        Assert.False(persisted.ShowModeButtonsOverride);
        Assert.True(persisted.ControlDepthOverride);
        Assert.Equal(viewport, persisted.CustomScreenViewport);
        Assert.Equal(DeviceConnectionMethod.DebugDirect, persisted.LastConnectionMethod);
        Assert.True(effective.AllowRemoteInput);
        Assert.True(effective.AllowPcSleep);
        Assert.False(effective.AllowFileBrowsing);
        Assert.False(effective.HideProtectedFileSystemItems);
        Assert.Null(persisted.PermissionOverrides!.HideProtectedFileSystemItems);
        Assert.True(manager.GetEffectivePermissions(
            "legacy-client",
            global with { HideProtectedFileSystemItems = true }).HideProtectedFileSystemItems);
        Assert.All(
            DeviceAccessProfiles.Permissions,
            permission => Assert.NotNull(permission.ReadOverride(persisted.PermissionOverrides!)));
    }

    [Fact]
    public void MixedLegacyAndNormalizedRecordsKeepTheirOwnResolutionRules()
    {
        using var store = new TempPairingStore();
        using var legacyKey = new PairingTestKey();
        using var normalizedKey = new PairingTestKey();
        AppPermissionSettings.Save(HostPermissions.DefaultGlobal with { AllowPcSleep = true });
        store.Store.Save(
        [
            new PairingRecord(
                "legacy",
                legacyKey.PublicKey,
                "Legacy",
                PermissionOverrides: new DevicePermissionOverrides(AllowPcSleep: false)),
            new PairingRecord(
                "normalized",
                normalizedKey.PublicKey,
                "Normalized",
                AccessProfile: DeviceAccessProfile.RemoteControls,
                PermissionOverrides: new DevicePermissionOverrides(AllowPcSleep: true))
        ]);

        var manager = new PairingManager(store.Store);
        var records = store.Store.Load().OrderBy(record => record.ClientId).ToArray();

        Assert.Equal(DeviceAccessProfile.Custom, records[0].AccessProfile);
        Assert.False(manager.GetEffectivePermissions("legacy", AppPermissionSettings.Load()).AllowPcSleep);
        Assert.Equal(DeviceAccessProfile.RemoteControls, records[1].AccessProfile);
        Assert.False(manager.GetEffectivePermissions("normalized", AppPermissionSettings.Load()).AllowPcSleep);
        Assert.All(
            DeviceAccessProfiles.Permissions,
            permission => Assert.Null(permission.ReadOverride(records[1].PermissionOverrides!)));
    }

    [Fact]
    public void FailedAtomicMigrationUsesNormalizedMemoryAndRetriesAfterRestart()
    {
        var root = Directory.CreateTempSubdirectory("VolturaAir-ProfileMigration-");
        try
        {
            using var key = new PairingTestKey();
            var store = new PairingStore(root.FullName);
            store.Save([new PairingRecord(
                "legacy",
                key.PublicKey,
                "Legacy",
                PermissionOverrides: new DevicePermissionOverrides(AllowPcSleep: true))]);
            store.BeforeReplaceForTests = () => throw new IOException("injected migration failure");
            var first = new PairingManager(store);
            Assert.Equal(DeviceAccessProfile.Custom, first.GetDeviceAccessProfile("legacy"));
            Assert.True(first.GetEffectivePermissions("legacy", AppPermissionSettings.Load()).AllowPcSleep);

            Assert.Null(Assert.Single(store.Load()).AccessProfile);
            store.BeforeReplaceForTests = null;
            var restarted = new PairingManager(store);
            Assert.Equal(DeviceAccessProfile.Custom, restarted.GetDeviceAccessProfile("legacy"));
            Assert.Equal(DeviceAccessProfile.Custom, Assert.Single(store.Load()).AccessProfile);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void UnknownAndMalformedProfileDataKeepsPairingsAndFailsClosed()
    {
        var root = Directory.CreateTempSubdirectory("VolturaAir-MalformedProfiles-");
        try
        {
            using var firstKey = new PairingTestKey();
            using var secondKey = new PairingTestKey();
            using var thirdKey = new PairingTestKey();
            var store = new PairingStore(root.FullName);
            store.Save(
            [
                new PairingRecord(
                    "unknown-profile",
                    firstKey.PublicKey,
                    "Unknown",
                    AccessProfile: DeviceAccessProfile.Custom,
                    PermissionOverrides: DeviceAccessProfiles.ToCompleteOverrides(DeviceAccessProfiles.MyDevice)),
                new PairingRecord(
                    "malformed-custom",
                    secondKey.PublicKey,
                    "Malformed",
                    AccessProfile: DeviceAccessProfile.Custom,
                    PermissionOverrides: DeviceAccessProfiles.ToCompleteOverrides(DeviceAccessProfiles.MyDevice)),
                new PairingRecord(
                    "null-profile",
                    thirdKey.PublicKey,
                    "Null profile",
                    AccessProfile: DeviceAccessProfile.Custom,
                    PermissionOverrides: DeviceAccessProfiles.ToCompleteOverrides(DeviceAccessProfiles.MyDevice))
            ]);
            var path = Path.Combine(root.FullName, "Voltura Air", "pairing.json");
            var rootNode = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var devices = rootNode["devices"]!.AsArray();
            devices[0]!["accessProfile"] = "unknown-value";
            devices[1]!["permissionOverrides"]!["allowRemoteInput"] = "invalid";
            devices[1]!["permissionOverrides"]!["unknownPermission"] = true;
            devices[2]!["accessProfile"] = null;
            File.WriteAllText(path, rootNode.ToJsonString(JsonOptions.Default));

            var manager = new PairingManager(store);

            Assert.Equal(3, manager.PairedDeviceCount);
            foreach (var clientId in new[] { "unknown-profile", "malformed-custom", "null-profile" })
            {
                Assert.Equal(DeviceAccessProfile.Custom, manager.GetDeviceAccessProfile(clientId));
                var effective = manager.GetEffectivePermissions(clientId, AppPermissionSettings.Load());
                Assert.All(
                    DeviceAccessProfiles.Permissions,
                    permission => Assert.False(permission.Read(effective), permission.PersistedKey));
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void CompleteCustomMatrixMissingOnlyFileTransferMigratesBlockedWithoutChangingOtherAccess()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var previous = DeviceAccessProfiles.MyDevice with { AllowPcSleep = false, AllowFileChanges = false };
        store.Store.Save([new PairingRecord(
            "legacy-custom",
            key.PublicKey,
            "Legacy custom",
            AccessProfile: DeviceAccessProfile.Custom,
            PermissionOverrides: DeviceAccessProfiles.ToCompleteOverrides(previous))]);
        var path = Path.Combine(store.RootPath, "Voltura Air", "pairing.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["devices"]!.AsArray()[0]!["permissionOverrides"]!.AsObject().Remove("allowFileTransfer");
        File.WriteAllText(path, root.ToJsonString(JsonOptions.Default));

        var manager = new PairingManager(store.Store);
        var effective = manager.GetEffectivePermissions("legacy-custom", AppPermissionSettings.Load());
        var persisted = Assert.Single(store.Store.Load());

        Assert.Equal(DeviceAccessProfile.Custom, persisted.AccessProfile);
        Assert.False(effective.AllowFileTransfer);
        Assert.False(persisted.PermissionOverrides!.AllowFileTransfer);
        foreach (var permission in DeviceAccessProfiles.Permissions.Where(permission => permission.Kind != DevicePermissionKind.FileTransfer))
            Assert.Equal(permission.Read(previous), permission.Read(effective));
    }

    [Fact]
    public void CompleteCustomMatrixMissingOnlyDiagnosticsMigratesBlockedWithoutChangingOtherAccess()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var previous = DeviceAccessProfiles.MyDevice with { AllowPcSleep = false, AllowFileChanges = false };
        store.Store.Save([new PairingRecord(
            "legacy-custom",
            key.PublicKey,
            "Legacy custom",
            AccessProfile: DeviceAccessProfile.Custom,
            PermissionOverrides: DeviceAccessProfiles.ToCompleteOverrides(previous))]);
        var path = Path.Combine(store.RootPath, "Voltura Air", "pairing.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["devices"]!.AsArray()[0]!["permissionOverrides"]!.AsObject().Remove("allowDiagnostics");
        File.WriteAllText(path, root.ToJsonString(JsonOptions.Default));

        var manager = new PairingManager(store.Store);
        var effective = manager.GetEffectivePermissions("legacy-custom", AppPermissionSettings.Load());
        var persisted = Assert.Single(store.Store.Load());

        Assert.Equal(DeviceAccessProfile.Custom, persisted.AccessProfile);
        Assert.False(effective.AllowDiagnostics);
        Assert.False(persisted.PermissionOverrides!.AllowDiagnostics);
        foreach (var permission in DeviceAccessProfiles.Permissions.Where(permission => permission.Kind != DevicePermissionKind.Diagnostics))
            Assert.Equal(permission.Read(previous), permission.Read(effective));
    }

    [Fact]
    public void CompleteCustomMatrixMissingOnlyTerminalMigratesBlockedWithoutChangingOtherAccess()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var previous = DeviceAccessProfiles.MyDevice with { AllowPcSleep = false, AllowFileChanges = false };
        store.Store.Save([new PairingRecord(
            "legacy-custom",
            key.PublicKey,
            "Legacy custom",
            AccessProfile: DeviceAccessProfile.Custom,
            PermissionOverrides: DeviceAccessProfiles.ToCompleteOverrides(previous))]);
        var path = Path.Combine(store.RootPath, "Voltura Air", "pairing.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["devices"]!.AsArray()[0]!["permissionOverrides"]!.AsObject().Remove("allowTerminal");
        File.WriteAllText(path, root.ToJsonString(JsonOptions.Default));

        var manager = new PairingManager(store.Store);
        var effective = manager.GetEffectivePermissions("legacy-custom", AppPermissionSettings.Load());
        var persisted = Assert.Single(store.Store.Load());

        Assert.Equal(DeviceAccessProfile.Custom, persisted.AccessProfile);
        Assert.False(effective.AllowTerminal);
        Assert.False(persisted.PermissionOverrides!.AllowTerminal);
        foreach (var permission in DeviceAccessProfiles.Permissions.Where(permission => permission.Kind != DevicePermissionKind.Terminal))
            Assert.Equal(permission.Read(previous), permission.Read(effective));
    }

    [Fact]
    public void CompleteCustomMatrixMissingOnlyAppsControlMigratesBlockedWithoutChangingOtherAccess()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var previous = DeviceAccessProfiles.MyDevice with { AllowPcSleep = false, AllowFileChanges = false };
        store.Store.Save([new PairingRecord(
            "legacy-custom-apps",
            key.PublicKey,
            "Legacy custom apps",
            AccessProfile: DeviceAccessProfile.Custom,
            PermissionOverrides: DeviceAccessProfiles.ToCompleteOverrides(previous))]);
        var path = Path.Combine(store.RootPath, "Voltura Air", "pairing.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["devices"]!.AsArray()[0]!["permissionOverrides"]!.AsObject().Remove("allowAppsControl");
        File.WriteAllText(path, root.ToJsonString(JsonOptions.Default));

        var manager = new PairingManager(store.Store);
        var effective = manager.GetEffectivePermissions("legacy-custom-apps", AppPermissionSettings.Load());
        var persisted = Assert.Single(store.Store.Load());

        Assert.Equal(DeviceAccessProfile.Custom, persisted.AccessProfile);
        Assert.False(effective.AllowAppsControl);
        Assert.False(persisted.PermissionOverrides!.AllowAppsControl);
        foreach (var permission in DeviceAccessProfiles.Permissions.Where(permission => permission.Kind != DevicePermissionKind.AppsControl))
            Assert.Equal(permission.Read(previous), permission.Read(effective));
    }

    [Fact]
    public void DefaultChangeAffectsOnlyLaterPairingAndDoesNotRotateCurrentToken()
    {
        using var store = new TempPairingStore();
        using var firstKey = new PairingTestKey();
        using var secondKey = new PairingTestKey();
        AppPermissionSettings.SaveDefaultAccessProfile(DeviceAccessProfile.MyDevice);
        var manager = new PairingManager(store.Store);
        var firstToken = manager.CreatePairingToken();
        Assert.True(manager.AcceptPairing(
            "first",
            "First",
            firstToken,
            reconnectPublicKey: firstKey.PublicKey).Accepted);
        var currentToken = manager.CreatePairingToken();

        AppPermissionSettings.SaveDefaultAccessProfile(DeviceAccessProfile.RemoteControls);
        Assert.True(manager.AcceptPairing(
            "second",
            "Second",
            currentToken,
            reconnectPublicKey: secondKey.PublicKey).Accepted);

        Assert.Equal(DeviceAccessProfile.MyDevice, manager.GetDeviceAccessProfile("first"));
        Assert.Equal(DeviceAccessProfile.RemoteControls, manager.GetDeviceAccessProfile("second"));
        Assert.True(manager.GetEffectivePermissions("first", AppPermissionSettings.Load()).AllowFileBrowsing);
        Assert.False(manager.GetEffectivePermissions("second", AppPermissionSettings.Load()).AllowFileBrowsing);
    }
}
