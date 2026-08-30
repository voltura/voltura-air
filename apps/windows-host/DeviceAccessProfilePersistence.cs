namespace VolturaAir.Host;

internal static class DeviceAccessProfilePersistence
{
    public static PairingRecord Normalize(
        PairingRecord record,
        HostPermissionSet legacyGlobalPermissions)
    {
        if (record.AccessProfile is null)
        {
            var legacy = HostPermissions.ResolveLegacy(legacyGlobalPermissions, record.PermissionOverrides);
            return record with
            {
                AccessProfile = DeviceAccessProfile.Custom,
                PermissionOverrides = PreserveProtectedOverride(
                    record.PermissionOverrides,
                    DeviceAccessProfiles.ToCompleteOverrides(legacy)),
                InitialAccessNoticePending = null
            };
        }

        if (record.AccessProfile == DeviceAccessProfile.Invalid)
        {
            return BlockAll(record);
        }

        if (DeviceAccessProfiles.IsBuiltIn(record.AccessProfile.Value))
        {
            return record with
            {
                PermissionOverrides = DeviceAccessProfiles.ClearManagedValues(record.PermissionOverrides)
            };
        }

        var customOverrides = MigrateLegacyCompleteCustom(record.PermissionOverrides);
        record = record with { PermissionOverrides = customOverrides };
        return DeviceAccessProfiles.TryResolveCustom(
            customOverrides,
            customOverrides?.HideProtectedFileSystemItems ?? legacyGlobalPermissions.HideProtectedFileSystemItems,
            out _)
                ? record
                : BlockAll(record);
    }

    public static PairingRecord ApplyPermissionOverrides(
        PairingRecord existing,
        DevicePermissionOverrides requested,
        HostPermissionSet globalPermissions)
    {
        var normalized = DeviceAccessProfiles.ToCompleteOverrides(
            Resolve(existing, globalPermissions));
        foreach (var permission in DeviceAccessProfiles.Permissions)
        {
            if (permission.ReadOverride(requested) is { } value)
            {
                normalized = permission.WriteOverride(normalized, value);
            }
        }

        normalized = normalized with
        {
            HideProtectedFileSystemItems = requested.HideProtectedFileSystemItems ??
                existing.PermissionOverrides?.HideProtectedFileSystemItems
        };
        return existing with
        {
            AccessProfile = DeviceAccessProfile.Custom,
            PermissionOverrides = normalized
        };
    }

    public static PairingRecord? ApplyProfile(
        PairingRecord existing,
        DeviceAccessProfile profile,
        HostPermissionSet globalPermissions)
    {
        if (profile is not (DeviceAccessProfile.MyDevice or DeviceAccessProfile.RemoteControls or DeviceAccessProfile.Custom))
        {
            return null;
        }

        var permissionOverrides = profile == DeviceAccessProfile.Custom
            ? PreserveProtectedOverride(
                existing.PermissionOverrides,
                DeviceAccessProfiles.ToCompleteOverrides(Resolve(existing, globalPermissions)))
            : DeviceAccessProfiles.ClearManagedValues(existing.PermissionOverrides);
        return existing with
        {
            AccessProfile = profile,
            PermissionOverrides = permissionOverrides
        };
    }

    public static PairingRecord? ApplyPermission(
        PairingRecord existing,
        DevicePermissionKind kind,
        bool allowed,
        HostPermissionSet globalPermissions)
    {
        if (DeviceAccessProfiles.Permissions.All(permission => permission.Kind != kind))
        {
            return null;
        }

        var permissionOverrides = existing.AccessProfile == DeviceAccessProfile.Custom
            ? existing.PermissionOverrides ?? DeviceAccessProfiles.ToCompleteOverrides(DeviceAccessProfiles.AllBlocked)
            : PreserveProtectedOverride(
                existing.PermissionOverrides,
                DeviceAccessProfiles.ToCompleteOverrides(Resolve(existing, globalPermissions)));
        return existing with
        {
            AccessProfile = DeviceAccessProfile.Custom,
            PermissionOverrides = DeviceAccessProfiles.Set(permissionOverrides, kind, allowed)
        };
    }

    public static PairingRecord ApplyProtectedFileFilter(
        PairingRecord existing,
        bool? hideProtected) => existing with
        {
            PermissionOverrides = (existing.PermissionOverrides ?? new DevicePermissionOverrides()) with
            {
                HideProtectedFileSystemItems = hideProtected
            }
        };

    private static PairingRecord BlockAll(PairingRecord record) => record with
    {
        AccessProfile = DeviceAccessProfile.Custom,
        PermissionOverrides = PreserveProtectedOverride(
            record.PermissionOverrides,
            DeviceAccessProfiles.ToCompleteOverrides(DeviceAccessProfiles.AllBlocked))
    };

    private static DevicePermissionOverrides PreserveProtectedOverride(
        DevicePermissionOverrides? source,
        DevicePermissionOverrides target) => target with
        {
            HideProtectedFileSystemItems = source?.HideProtectedFileSystemItems
        };

    private static HostPermissionSet Resolve(PairingRecord record, HostPermissionSet globalPermissions) =>
        HostPermissions.Resolve(
            record.AccessProfile ?? DeviceAccessProfile.Custom,
            record.PermissionOverrides,
            globalPermissions);

    private static DevicePermissionOverrides? MigrateLegacyCompleteCustom(DevicePermissionOverrides? values)
    {
        if (values is null) return null;

        if (values.AllowFileTransfer is null)
        {
            var completeBeforeFileTransfer = DeviceAccessProfiles.Permissions
                .Where(permission => permission.Kind is not (
                    DevicePermissionKind.FileTransfer or DevicePermissionKind.Diagnostics or DevicePermissionKind.Terminal))
                .All(permission => permission.ReadOverride(values) is not null);
            if (completeBeforeFileTransfer)
            {
                values = values with { AllowFileTransfer = false };
            }
        }

        if (values.AllowDiagnostics is null)
        {
            var completeBeforeDiagnostics = DeviceAccessProfiles.Permissions
                .Where(permission => permission.Kind is not (DevicePermissionKind.Diagnostics or DevicePermissionKind.Terminal))
                .All(permission => permission.ReadOverride(values) is not null);
            if (completeBeforeDiagnostics)
            {
                values = values with { AllowDiagnostics = false };
            }
        }

        if (values.AllowTerminal is null)
        {
            var completeBeforeTerminal = DeviceAccessProfiles.Permissions
                .Where(permission => permission.Kind is not (DevicePermissionKind.Terminal or DevicePermissionKind.AppsControl))
                .All(permission => permission.ReadOverride(values) is not null);
            if (completeBeforeTerminal)
            {
                values = values with { AllowTerminal = false };
            }
        }

        if (values.AllowAppsControl is null)
        {
            var completeBeforeApps = DeviceAccessProfiles.Permissions
                .Where(permission => permission.Kind != DevicePermissionKind.AppsControl)
                .All(permission => permission.ReadOverride(values) is not null);
            if (completeBeforeApps)
            {
                values = values with { AllowAppsControl = false };
            }
        }

        return values;
    }
}
