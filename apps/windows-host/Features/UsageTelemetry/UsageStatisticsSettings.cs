using Microsoft.Win32;

namespace VolturaAir.Host.Features.UsageTelemetry;

internal enum UsageStatisticsConsent
{
    Unset = 0,
    Allowed = 1,
    Denied = 2
}

internal enum UsageStatisticsDistribution
{
    Installed,
    Portable
}

internal sealed record UsageStatisticsPersistentState(
    UsageStatisticsDistribution Distribution,
    UsageStatisticsConsent Consent,
    Guid? InstallationId);

internal sealed record UsageStatisticsSettingsResult(
    bool Succeeded,
    Guid? InstallationId = null,
    bool IdentityRemoved = false);

internal interface IUsageStatisticsSettings
{
    UsageStatisticsPersistentState Read();

    UsageStatisticsSettingsResult RepairAllowedIdentity();

    UsageStatisticsSettingsResult AllowWithNewIdentity();

    UsageStatisticsSettingsResult DenyAndDeleteIdentity();

    bool DeleteStaleIdentity();
}

internal sealed class IsolatedUsageStatisticsSettings : IUsageStatisticsSettings
{
    private UsageStatisticsPersistentState _state = new(
        UsageStatisticsDistribution.Portable,
        UsageStatisticsConsent.Denied,
        null);

    public UsageStatisticsPersistentState Read() => _state;

    public UsageStatisticsSettingsResult RepairAllowedIdentity()
    {
        if (_state.Consent != UsageStatisticsConsent.Allowed)
        {
            return new UsageStatisticsSettingsResult(false);
        }

        var id = _state.InstallationId ?? Guid.NewGuid();
        _state = _state with { InstallationId = id };
        return new UsageStatisticsSettingsResult(true, id);
    }

    public UsageStatisticsSettingsResult AllowWithNewIdentity()
    {
        var id = Guid.NewGuid();
        _state = _state with { Consent = UsageStatisticsConsent.Allowed, InstallationId = id };
        return new UsageStatisticsSettingsResult(true, id);
    }

    public UsageStatisticsSettingsResult DenyAndDeleteIdentity()
    {
        _state = _state with { Consent = UsageStatisticsConsent.Denied, InstallationId = null };
        return new UsageStatisticsSettingsResult(true, IdentityRemoved: true);
    }

    public bool DeleteStaleIdentity()
    {
        _state = _state with { InstallationId = null };
        return true;
    }
}

internal sealed class UsageStatisticsSettings : IUsageStatisticsSettings
{
    internal const string InstalledConsentValueName = "UsageStatisticsInstalledConsent";
    internal const string InstalledIdValueName = "UsageStatisticsInstalledId";
    internal const string PortableConsentValueName = "UsageStatisticsPortableConsent";
    internal const string PortableIdValueName = "UsageStatisticsPortableId";
    internal const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura Air";

    private readonly UsageStatisticsDistribution _distribution;
    private readonly Func<RegistryKey?> _openRead;
    private readonly Func<RegistryKey?> _openWrite;
    private readonly Func<Guid> _createId;

    public UsageStatisticsSettings()
        : this(
            DetectDistribution(Environment.ProcessPath, ReadInstalledLocation()),
            () => Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false),
            () => Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true) ??
                Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true),
            Guid.NewGuid)
    {
    }

    internal UsageStatisticsSettings(
        UsageStatisticsDistribution distribution,
        Func<RegistryKey?> openRead,
        Func<RegistryKey?> openWrite,
        Func<Guid>? createId = null)
    {
        _distribution = distribution;
        _openRead = openRead;
        _openWrite = openWrite;
        _createId = createId ?? Guid.NewGuid;
    }

    private string ConsentValueName => _distribution == UsageStatisticsDistribution.Installed
        ? InstalledConsentValueName
        : PortableConsentValueName;

    private string IdValueName => _distribution == UsageStatisticsDistribution.Installed
        ? InstalledIdValueName
        : PortableIdValueName;

    public UsageStatisticsPersistentState Read()
    {
        try
        {
            using var key = _openRead();
            var consent = key?.GetValue(ConsentValueName) is int rawConsent
                ? ParseConsent(rawConsent)
                : UsageStatisticsConsent.Unset;
            Guid? installationId = TryParseCanonicalId(key?.GetValue(IdValueName) as string, out var parsedId)
                ? parsedId
                : null;
            return new UsageStatisticsPersistentState(_distribution, consent, installationId);
        }
        catch (Exception exception) when (IsSettingsFailure(exception))
        {
            return new UsageStatisticsPersistentState(_distribution, UsageStatisticsConsent.Unset, null);
        }
    }

    public UsageStatisticsSettingsResult RepairAllowedIdentity()
    {
        var state = Read();
        if (state.Consent != UsageStatisticsConsent.Allowed)
        {
            return new UsageStatisticsSettingsResult(false);
        }

        if (state.InstallationId is { } existing)
        {
            return new UsageStatisticsSettingsResult(true, existing);
        }

        return WriteNewIdentity(writeAllowedConsent: false);
    }

    public UsageStatisticsSettingsResult AllowWithNewIdentity()
    {
        if (!DeleteStaleIdentity())
        {
            FailClosedAfterIdentityWrite(null, persistDenied: true);
            return new UsageStatisticsSettingsResult(false);
        }

        return WriteNewIdentity(writeAllowedConsent: true);
    }

    public UsageStatisticsSettingsResult DenyAndDeleteIdentity()
    {
        try
        {
            using (var key = _openWrite())
            {
                if (key is null)
                {
                    return new UsageStatisticsSettingsResult(false);
                }

                key.SetValue(ConsentValueName, (int)UsageStatisticsConsent.Denied, RegistryValueKind.DWord);
                if (key.GetValue(ConsentValueName) is not int saved || saved != (int)UsageStatisticsConsent.Denied)
                {
                    return new UsageStatisticsSettingsResult(false);
                }
            }

            return new UsageStatisticsSettingsResult(true, IdentityRemoved: DeleteStaleIdentity());
        }
        catch (Exception exception) when (IsSettingsFailure(exception))
        {
            return new UsageStatisticsSettingsResult(false);
        }
    }

    public bool DeleteStaleIdentity()
    {
        try
        {
            using var key = _openWrite();
            if (key is null)
            {
                return false;
            }

            key.DeleteValue(IdValueName, throwOnMissingValue: false);
            return key.GetValue(IdValueName) is null;
        }
        catch (Exception exception) when (IsSettingsFailure(exception))
        {
            return false;
        }
    }

    internal static UsageStatisticsDistribution DetectDistribution(string? processPath, string? installLocation)
    {
        var processDirectory = NormalizeDirectory(Path.GetDirectoryName(processPath));
        var installedDirectory = NormalizeDirectory(installLocation);
        return processDirectory is not null && installedDirectory is not null &&
            string.Equals(processDirectory, installedDirectory, StringComparison.OrdinalIgnoreCase)
                ? UsageStatisticsDistribution.Installed
                : UsageStatisticsDistribution.Portable;
    }

    internal static bool TryParseCanonicalId(string? value, out Guid id)
    {
        return Guid.TryParseExact(value, "D", out id) &&
            string.Equals(value, id.ToString("D"), StringComparison.Ordinal);
    }

    private UsageStatisticsSettingsResult WriteNewIdentity(bool writeAllowedConsent)
    {
        var installationId = _createId();
        var canonicalId = installationId.ToString("D");
        try
        {
            using var key = _openWrite();
            if (key is null)
            {
                FailClosedAfterIdentityWrite(null, writeAllowedConsent);
                return new UsageStatisticsSettingsResult(false);
            }

            key.SetValue(IdValueName, canonicalId, RegistryValueKind.String);
            if (!string.Equals(key.GetValue(IdValueName) as string, canonicalId, StringComparison.Ordinal))
            {
                FailClosedAfterIdentityWrite(key, writeAllowedConsent);
                return new UsageStatisticsSettingsResult(false);
            }

            if (writeAllowedConsent)
            {
                key.SetValue(ConsentValueName, (int)UsageStatisticsConsent.Allowed, RegistryValueKind.DWord);
                if (key.GetValue(ConsentValueName) is not int saved || saved != (int)UsageStatisticsConsent.Allowed)
                {
                    FailClosedAfterIdentityWrite(key, persistDenied: true);
                    return new UsageStatisticsSettingsResult(false);
                }
            }

            return new UsageStatisticsSettingsResult(true, installationId);
        }
        catch (Exception exception) when (IsSettingsFailure(exception))
        {
            FailClosedAfterIdentityWrite(null, writeAllowedConsent);
            return new UsageStatisticsSettingsResult(false);
        }
    }

    private void FailClosedAfterIdentityWrite(RegistryKey? existingKey, bool persistDenied)
    {
        try
        {
            using var openedKey = existingKey is null ? _openWrite() : null;
            var key = existingKey ?? openedKey;
            if (persistDenied)
            {
                key?.SetValue(ConsentValueName, (int)UsageStatisticsConsent.Denied, RegistryValueKind.DWord);
            }
            key?.DeleteValue(IdValueName, throwOnMissingValue: false);
        }
        catch (Exception cleanupException) when (IsSettingsFailure(cleanupException))
        {
            // The in-process state remains disabled. Diagnostics reports the failed transition.
        }
    }

    private static UsageStatisticsConsent ParseConsent(int value) => value switch
    {
        (int)UsageStatisticsConsent.Allowed => UsageStatisticsConsent.Allowed,
        (int)UsageStatisticsConsent.Denied => UsageStatisticsConsent.Denied,
        _ => UsageStatisticsConsent.Unset
    };

    private static string? ReadInstalledLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, writable: false);
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception exception) when (IsSettingsFailure(exception))
        {
            return null;
        }
    }

    private static string? NormalizeDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsSettingsFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ObjectDisposedException;
}
