using Microsoft.Win32;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class HostSettingsRegistryTests : IsolatedHostSettingsTest
{
    [Fact]
    public void CustomScreenDeleteConfirmationsDefaultOnAndCanBeDisabled()
    {
        Assert.True(CustomScreenEditorSettings.ConfirmDeletes());
        Assert.True(CustomScreenEditorSettings.ConfirmHides());

        CustomScreenEditorSettings.SetConfirmDeletes(false);
        Assert.False(CustomScreenEditorSettings.ConfirmDeletes());
        Assert.True(CustomScreenEditorSettings.ConfirmHides());

        CustomScreenEditorSettings.SetConfirmHides(false);
        Assert.False(CustomScreenEditorSettings.ConfirmHides());
        Assert.False(CustomScreenEditorSettings.ConfirmDeletes());

        CustomScreenEditorSettings.SetConfirmDeletes(true);
        CustomScreenEditorSettings.SetConfirmHides(true);
        Assert.True(CustomScreenEditorSettings.ConfirmDeletes());
        Assert.True(CustomScreenEditorSettings.ConfirmHides());
    }

    [Fact]
    public void CustomScreenEditorPanelWidthsDefaultClampAndPersist()
    {
        Assert.Equal(
            (
                (double)CustomScreenEditorSettings.DefaultComponentPaletteWidth,
                (double)CustomScreenEditorSettings.DefaultPropertiesPanelWidth
            ),
            CustomScreenEditorSettings.PanelWidths());

        CustomScreenEditorSettings.SetPanelWidths(287.6, 401.2);
        Assert.Equal((288d, 401d), CustomScreenEditorSettings.PanelWidths());

        CustomScreenEditorSettings.SetPanelWidths(1, double.NaN);
        Assert.Equal(
            (
                (double)CustomScreenEditorSettings.DefaultComponentPaletteWidth,
                (double)CustomScreenEditorSettings.DefaultPropertiesPanelWidth
            ),
            CustomScreenEditorSettings.PanelWidths());
    }

    [Fact]
    public void ActiveIsolatedScopeRefreshesCachedHotPathSettings()
    {
        AppClientControlSettings.SetEnabled(false);
        AppLoggingSettings.SetEnabled(true);
        AppPermissionSettings.Save(HostPermissions.DefaultGlobal with { AllowRemoteInput = false });

        Assert.False(AppClientControlSettings.IsEnabled());
        Assert.True(AppLoggingSettings.IsEnabled());
        Assert.False(AppPermissionSettings.Load().AllowRemoteInput);
    }

    [Fact]
    public void RemoteControlsProfileCannotControlTheHostApplication()
    {
        AppClientControlSettings.SetEnabled(true);

        Assert.True(AppClientControlSettings.AllowsDevice(DeviceAccessProfile.MyDevice));
        Assert.True(AppClientControlSettings.AllowsDevice(DeviceAccessProfile.Custom));
        Assert.False(AppClientControlSettings.AllowsDevice(DeviceAccessProfile.RemoteControls));
        Assert.False(AppClientControlSettings.AllowsDevice(DeviceAccessProfile.Invalid));
        Assert.False(AppClientControlSettings.AllowsDevice((DeviceAccessProfile)999));

        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        Assert.Equal(DeviceAccessProfile.Invalid, manager.GetDeviceAccessProfile("missing"));
        Assert.False(AppClientControlSettings.AllowsDevice(manager.GetDeviceAccessProfile("missing")));
    }

    [Fact]
    public void PermissionHotPathUsesWriteThroughCache()
    {
        var blocked = HostPermissions.DefaultGlobal with { AllowRemoteInput = false };
        AppPermissionSettings.Save(blocked);

        using (var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true))
        {
            Assert.NotNull(key);
            key.SetValue("AllowRemoteInput", 1, RegistryValueKind.DWord);
        }

        Assert.Same(blocked, AppPermissionSettings.Load());

        var allowed = blocked with { AllowRemoteInput = true };
        AppPermissionSettings.Save(allowed);
        Assert.Same(allowed, AppPermissionSettings.Load());
    }

    [Fact]
    public void PermissionSaveUsesOneExactJsonValueAndIgnoresLegacyFields()
    {
        var expected = HostPermissions.DefaultGlobal with { AllowRemoteInput = false, AllowPhoneWebcam = true };
        AppPermissionSettings.Save(expected);
        using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true);
        Assert.NotNull(key);
        Assert.IsType<string>(key.GetValue(AppPermissionSettings.ValueName));
        key.SetValue("AllowRemoteInput", 1, RegistryValueKind.DWord);
        AppPermissionSettings.RefreshForTests();
        Assert.Equal(expected, AppPermissionSettings.Load());
    }

    [Fact]
    public void MalformedPermissionJsonFailsClosed()
    {
        using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true);
        Assert.NotNull(key);
        key.SetValue(AppPermissionSettings.ValueName, "{\"allowRemoteInput\":true}", RegistryValueKind.String);
        AppPermissionSettings.RefreshForTests();
        HostPermissionSet permissions = AppPermissionSettings.Load();
        Assert.False(permissions.AllowRemoteInput);
        Assert.False(permissions.AllowPhoneWebcam);
        Assert.True(permissions.HideProtectedFileSystemItems);
    }

    [Fact]
    public void DefaultDeviceAccessIsMyDeviceAndMalformedOrCustomValuesFallBack()
    {
        Assert.Equal(DeviceAccessProfile.MyDevice, AppPermissionSettings.LoadDefaultAccessProfile());

        AppPermissionSettings.SaveDefaultAccessProfile(DeviceAccessProfile.RemoteControls);
        AppPermissionSettings.RefreshForTests();
        Assert.Equal(DeviceAccessProfile.RemoteControls, AppPermissionSettings.LoadDefaultAccessProfile());

        using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true);
        Assert.NotNull(key);
        key.SetValue(
            AppPermissionSettings.DefaultAccessProfileValueName,
            "{\"profile\":\"custom\"}",
            RegistryValueKind.String);
        AppPermissionSettings.RefreshForTests();
        Assert.Equal(DeviceAccessProfile.MyDevice, AppPermissionSettings.LoadDefaultAccessProfile());

        key.SetValue(
            AppPermissionSettings.DefaultAccessProfileValueName,
            "{\"profile\":\"unknown\"}",
            RegistryValueKind.String);
        AppPermissionSettings.RefreshForTests();
        Assert.Equal(DeviceAccessProfile.MyDevice, AppPermissionSettings.LoadDefaultAccessProfile());
    }

    [Fact]
    public void FailedAtomicPermissionWritePreservesRegistryAndCache()
    {
        var original = HostPermissions.DefaultGlobal with { AllowRemoteInput = false };
        AppPermissionSettings.Save(original);
        HostSettingsJsonValue.BeforeWriteForTests = (_, _) => throw new IOException("injected write failure");
        try
        {
            Assert.Throws<IOException>(() => AppPermissionSettings.Save(original with { AllowRemoteInput = true }));
        }
        finally
        {
            HostSettingsJsonValue.BeforeWriteForTests = null;
        }
        AppPermissionSettings.RefreshForTests();
        Assert.Equal(original, AppPermissionSettings.Load());
    }

    [Fact]
    public void AwakeStateUsesOneAtomicJsonValueAndMalformedStateIsOff()
    {
        var expected = new AwakeState(AwakeMode.Timed, true, 30, DateTimeOffset.Now.AddMinutes(30));
        AppAwakeSettings.Save(expected);
        Assert.Equal(expected, AppAwakeSettings.Load());
        using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true);
        Assert.NotNull(key);
        Assert.IsType<string>(key.GetValue(AppAwakeSettings.ValueName));
        key.SetValue(AppAwakeSettings.ValueName, "{\"mode\":2}", RegistryValueKind.String);
        Assert.Equal(AwakeMode.Off, AppAwakeSettings.Load().Mode);
    }

    [Fact]
    public void PermissionChangeNotifiesEverySubscriberWhenOneFails()
    {
        var original = AppPermissionSettings.Load();
        var laterSubscriberCalled = false;
        EventHandler failing = (_, _) => throw new InvalidOperationException("injected observer failure");
        EventHandler later = (_, _) => laterSubscriberCalled = true;
        AppPermissionSettings.Changed += failing;
        AppPermissionSettings.Changed += later;

        try
        {
            AppPermissionSettings.Save(original with { AllowRemoteInput = !original.AllowRemoteInput });
            Assert.True(laterSubscriberCalled);
        }
        finally
        {
            AppPermissionSettings.Changed -= failing;
            AppPermissionSettings.Changed -= later;
            AppPermissionSettings.Save(original);
        }
    }

    [Fact]
    public void LegacyCursorRecoverySettingIsRemoved()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true))
        {
            Assert.NotNull(key);
            key.SetValue("UseCursorRecoveryWatchdog", 0, RegistryValueKind.DWord);
        }

        AppPointerSettings.RemoveLegacyCursorRecoverySetting();

        using var refreshed = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false);
        Assert.NotNull(refreshed);
        Assert.Null(refreshed.GetValue("UseCursorRecoveryWatchdog"));
    }

    [Fact]
    public void PointerCommunicationSettingsUseAWriteThroughCache()
    {
        var expectedLaser = new PresentationLaserPointerSettings(9, PresentationLaserColor.Blue);
        AppPointerSettings.SetPresentationLaserPointer(expectedLaser);

        using (var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true))
        {
            Assert.NotNull(key);
            key.SetValue("PresentationLaserSize", 1, RegistryValueKind.DWord);
            key.SetValue("PresentationLaserColor", (int)PresentationLaserColor.Red, RegistryValueKind.DWord);
        }

        Assert.Equal(expectedLaser, AppPointerSettings.GetPresentationLaserPointer());

        var updatedLaser = new PresentationLaserPointerSettings(5, PresentationLaserColor.Green);
        AppPointerSettings.SetPresentationLaserPointer(updatedLaser);
        Assert.Equal(updatedLaser, AppPointerSettings.GetPresentationLaserPointer());
    }

    [Fact]
    public void ModeButtonsAreEnabledByDefaultAndCanBeDisabled()
    {
        Assert.True(AppAppearanceSettings.ShowModeButtons());

        AppAppearanceSettings.SetShowModeButtons(false);
        Assert.False(AppAppearanceSettings.ShowModeButtons());

        AppAppearanceSettings.SetShowModeButtons(true);
        Assert.True(AppAppearanceSettings.ShowModeButtons());
    }

    [Fact]
    public void ControlDepthUsesSeparateHostAndDeviceDefaults()
    {
        Assert.False(AppAppearanceSettings.HostControlDepth());
        Assert.True(AppAppearanceSettings.DeviceControlDepth());

        AppAppearanceSettings.SetHostControlDepth(true);
        AppAppearanceSettings.SetDeviceControlDepth(false);

        Assert.True(AppAppearanceSettings.HostControlDepth());
        Assert.False(AppAppearanceSettings.DeviceControlDepth());
    }

    [Fact]
    public void DeviceAccentColorIsOptionalAndCanBeReset()
    {
        Assert.Null(AppAppearanceSettings.DeviceAccentColor());

        AppAppearanceSettings.SetDeviceAccentColor("#5FC8B4");
        Assert.Equal("#5FC8B4", AppAppearanceSettings.DeviceAccentColor());

        AppAppearanceSettings.SetDeviceAccentColor(null);
        Assert.Null(AppAppearanceSettings.DeviceAccentColor());
    }

    [Fact]
    public void CloseToTrayNotificationIsOnlyMarkedOnce()
    {
        Assert.True(AppWindowSettings.TryMarkCloseToTrayNotificationShown());
        Assert.False(AppWindowSettings.TryMarkCloseToTrayNotificationShown());
    }
}
