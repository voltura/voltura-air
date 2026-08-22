using Microsoft.Win32;
using VolturaAir.Host.Features.UsageTelemetry;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class UsageStatisticsSettingsTests : IsolatedHostSettingsTest
{
    [Fact]
    public void UnsetAndMalformedConsentFailClosed()
    {
        var settings = Create(UsageStatisticsDistribution.Installed);

        Assert.Equal(UsageStatisticsConsent.Unset, settings.Read().Consent);

        using (var key = Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true))
        {
            key.SetValue(UsageStatisticsSettings.InstalledConsentValueName, 99, RegistryValueKind.DWord);
            key.SetValue(UsageStatisticsSettings.InstalledIdValueName, "NOT-A-UUID", RegistryValueKind.String);
        }

        var malformed = settings.Read();
        Assert.Equal(UsageStatisticsConsent.Unset, malformed.Consent);
        Assert.Null(malformed.InstallationId);
    }

    [Fact]
    public void InstalledAndPortableProfilesStaySeparate()
    {
        var installedId = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab");
        var portableId = Guid.Parse("11234567-89ab-4cde-8fab-0123456789ab");
        var installed = Create(UsageStatisticsDistribution.Installed, () => installedId);
        var portable = Create(UsageStatisticsDistribution.Portable, () => portableId);

        Assert.True(installed.AllowWithNewIdentity().Succeeded);
        Assert.Equal(UsageStatisticsConsent.Unset, portable.Read().Consent);
        Assert.True(portable.AllowWithNewIdentity().Succeeded);

        Assert.Equal(installedId, installed.Read().InstallationId);
        Assert.Equal(portableId, portable.Read().InstallationId);
        Assert.True(installed.DenyAndDeleteIdentity().Succeeded);
        Assert.Equal(UsageStatisticsConsent.Allowed, portable.Read().Consent);
        Assert.Equal(portableId, portable.Read().InstallationId);
    }

    [Fact]
    public void DenyReportsConsentAndIdentityCleanupSeparately()
    {
        var id = Guid.Parse("41234567-89ab-4cde-8fab-0123456789ab");
        var settings = Create(UsageStatisticsDistribution.Installed, () => id);
        Assert.True(settings.AllowWithNewIdentity().Succeeded);

        var writeOpenCount = 0;
        var cleanupBlocked = new UsageStatisticsSettings(
            UsageStatisticsDistribution.Installed,
            () => Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false),
            () => Registry.CurrentUser.OpenSubKey(
                HostSettingsRegistry.SettingsKeyPath,
                writable: Interlocked.Increment(ref writeOpenCount) == 1),
            () => Guid.NewGuid());

        var result = cleanupBlocked.DenyAndDeleteIdentity();

        Assert.True(result.Succeeded);
        Assert.False(result.IdentityRemoved);
        Assert.Equal(UsageStatisticsConsent.Denied, settings.Read().Consent);
        Assert.Equal(id, settings.Read().InstallationId);
    }

    [Fact]
    public void ReenableDeletesTheOldIdentityBeforeCreatingAnUnlinkableReplacement()
    {
        var ids = new Queue<Guid>(
        [
            Guid.Parse("21234567-89ab-4cde-8fab-0123456789ab"),
            Guid.Parse("31234567-89ab-4cde-8fab-0123456789ab")
        ]);
        var settings = Create(UsageStatisticsDistribution.Installed, ids.Dequeue);

        var first = settings.AllowWithNewIdentity();
        Assert.True(first.Succeeded);
        Assert.True(settings.DenyAndDeleteIdentity().Succeeded);
        Assert.Null(settings.Read().InstallationId);
        var second = settings.AllowWithNewIdentity();

        Assert.True(second.Succeeded);
        Assert.NotEqual(first.InstallationId, second.InstallationId);
    }

    [Fact]
    public void FailedEnableRemainsDeniedAndDoesNotRetainAnIdentifier()
    {
        var settings = new UsageStatisticsSettings(
            UsageStatisticsDistribution.Installed,
            () => Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false),
            () => Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false),
            Guid.NewGuid);

        var result = settings.AllowWithNewIdentity();

        Assert.False(result.Succeeded);
        var state = settings.Read();
        Assert.NotEqual(UsageStatisticsConsent.Allowed, state.Consent);
        Assert.Null(state.InstallationId);
    }

    [Theory]
    [InlineData(@"C:\Apps\Voltura Air\VolturaAir.Host.exe", @"C:\Apps\Voltura Air", "Installed")]
    [InlineData(@"C:\Apps\Voltura Air\VolturaAir.Host.exe", @"C:\Apps\Voltura Air\", "Installed")]
    [InlineData(@"D:\Portable\VolturaAir.Host.exe", @"C:\Apps\Voltura Air", "Portable")]
    [InlineData(@"C:\Apps\Voltura Air Backup\VolturaAir.Host.exe", @"C:\Apps\Voltura Air", "Portable")]
    public void DistributionRequiresAnExactNormalizedInstallDirectory(
        string processPath,
        string installLocation,
        string expected)
    {
        Assert.Equal(expected, UsageStatisticsSettings.DetectDistribution(processPath, installLocation).ToString());
    }

    [Theory]
    [InlineData("0c99c983-09f8-42af-879c-42b51d625c69", true)]
    [InlineData("0C99C983-09F8-42AF-879C-42B51D625C69", false)]
    [InlineData("{0c99c983-09f8-42af-879c-42b51d625c69}", false)]
    [InlineData("invalid", false)]
    public void IdentityParserRequiresCanonicalLowercaseUuid(string value, bool expected)
    {
        Assert.Equal(expected, UsageStatisticsSettings.TryParseCanonicalId(value, out _));
    }

    [Fact]
    public void IsolatedRuntimeUsesOnlyInMemoryUsageStatisticsSettings()
    {
        var settings = WpfHostRuntime.CreateUsageStatisticsSettings(isolatedTestMode: true);

        Assert.IsType<IsolatedUsageStatisticsSettings>(settings);
        Assert.Equal(UsageStatisticsConsent.Denied, settings.Read().Consent);
        var allowed = settings.AllowWithNewIdentity();
        Assert.True(allowed.Succeeded);
        Assert.Equal(UsageStatisticsConsent.Allowed, settings.Read().Consent);
        Assert.True(settings.DenyAndDeleteIdentity().IdentityRemoved);
        Assert.Null(settings.Read().InstallationId);
    }

    private static UsageStatisticsSettings Create(
        UsageStatisticsDistribution distribution,
        Func<Guid>? createId = null) =>
        new(
            distribution,
            () => Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false),
            () => Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true) ??
                Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true),
            createId);
}
