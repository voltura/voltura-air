using System.Text.Json;
using System.Text.Json.Serialization;

namespace VolturaAir.Host;

[JsonConverter(typeof(DeviceAccessProfileJsonConverter))]
public enum DeviceAccessProfile
{
    Invalid,
    MyDevice,
    RemoteControls,
    Custom
}

public enum DevicePermissionKind
{
    RemoteInput,
    PcSleep,
    VolumeControl,
    PresentationControl,
    RemoteAppLaunch,
    UrlOpen,
    PcLock,
    BlackoutDisplay,
    DisplayControl,
    ScreenSaver,
    AwakeControl,
    ClipboardRead,
    ScreenViewing,
    PhoneWebcam,
    FileBrowsing,
    FileChanges,
    FileTransfer,
    Diagnostics,
    Terminal,
    SignOut,
    Restart,
    Shutdown
}

public sealed record DevicePermissionDefinition(
    DevicePermissionKind Kind,
    string PersistedKey,
    string DisplayName,
    bool RemoteControlsAllowed,
    Func<HostPermissionSet, bool> Read,
    Func<HostPermissionSet, bool, HostPermissionSet> Write,
    Func<DevicePermissionOverrides, bool?> ReadOverride,
    Func<DevicePermissionOverrides, bool?, DevicePermissionOverrides> WriteOverride);

public static class DeviceAccessProfiles
{
    public static IReadOnlyList<DevicePermissionDefinition> Permissions { get; } =
    [
        Define(DevicePermissionKind.RemoteInput, "allowRemoteInput", "Pointer and keyboard", true,
            value => value.AllowRemoteInput, (value, allowed) => value with { AllowRemoteInput = allowed },
            value => value.AllowRemoteInput, (value, allowed) => value with { AllowRemoteInput = allowed }),
        Define(DevicePermissionKind.PcSleep, "allowPcSleep", "PC sleep", false,
            value => value.AllowPcSleep, (value, allowed) => value with { AllowPcSleep = allowed },
            value => value.AllowPcSleep, (value, allowed) => value with { AllowPcSleep = allowed }),
        Define(DevicePermissionKind.VolumeControl, "allowVolumeControl", "Volume control", true,
            value => value.AllowVolumeControl, (value, allowed) => value with { AllowVolumeControl = allowed },
            value => value.AllowVolumeControl, (value, allowed) => value with { AllowVolumeControl = allowed }),
        Define(DevicePermissionKind.PresentationControl, "allowPresentationControl", "Presentation control", true,
            value => value.AllowPresentationControl, (value, allowed) => value with { AllowPresentationControl = allowed },
            value => value.AllowPresentationControl, (value, allowed) => value with { AllowPresentationControl = allowed }),
        Define(DevicePermissionKind.RemoteAppLaunch, "allowRemoteAppLaunch", "Application launch", true,
            value => value.AllowRemoteAppLaunch, (value, allowed) => value with { AllowRemoteAppLaunch = allowed },
            value => value.AllowRemoteAppLaunch, (value, allowed) => value with { AllowRemoteAppLaunch = allowed }),
        Define(DevicePermissionKind.UrlOpen, "allowUrlOpen", "Open web addresses", false,
            value => value.AllowUrlOpen, (value, allowed) => value with { AllowUrlOpen = allowed },
            value => value.AllowUrlOpen, (value, allowed) => value with { AllowUrlOpen = allowed }),
        Define(DevicePermissionKind.PcLock, "allowPcLock", "Lock PC", true,
            value => value.AllowPcLock, (value, allowed) => value with { AllowPcLock = allowed },
            value => value.AllowPcLock, (value, allowed) => value with { AllowPcLock = allowed }),
        Define(DevicePermissionKind.BlackoutDisplay, "allowBlackoutDisplay", "Blackout display", true,
            value => value.AllowBlackoutDisplay, (value, allowed) => value with { AllowBlackoutDisplay = allowed },
            value => value.AllowBlackoutDisplay, (value, allowed) => value with { AllowBlackoutDisplay = allowed }),
        Define(DevicePermissionKind.DisplayControl, "allowDisplayControl", "Control displays", false,
            value => value.AllowDisplayControl, (value, allowed) => value with { AllowDisplayControl = allowed },
            value => value.AllowDisplayControl, (value, allowed) => value with { AllowDisplayControl = allowed }),
        Define(DevicePermissionKind.ScreenSaver, "allowScreenSaver", "Screen saver", true,
            value => value.AllowScreenSaver, (value, allowed) => value with { AllowScreenSaver = allowed },
            value => value.AllowScreenSaver, (value, allowed) => value with { AllowScreenSaver = allowed }),
        Define(DevicePermissionKind.AwakeControl, "allowAwakeControl", "Keep awake", false,
            value => value.AllowAwakeControl, (value, allowed) => value with { AllowAwakeControl = allowed },
            value => value.AllowAwakeControl, (value, allowed) => value with { AllowAwakeControl = allowed }),
        Define(DevicePermissionKind.ClipboardRead, "allowClipboardRead", "Read PC clipboard", false,
            value => value.AllowClipboardRead, (value, allowed) => value with { AllowClipboardRead = allowed },
            value => value.AllowClipboardRead, (value, allowed) => value with { AllowClipboardRead = allowed }),
        Define(DevicePermissionKind.ScreenViewing, "allowScreenViewing", "View PC screen", false,
            value => value.AllowScreenViewing, (value, allowed) => value with { AllowScreenViewing = allowed },
            value => value.AllowScreenViewing, (value, allowed) => value with { AllowScreenViewing = allowed }),
        Define(DevicePermissionKind.PhoneWebcam, "allowPhoneWebcam", "Use phone as webcam", false,
            value => value.AllowPhoneWebcam, (value, allowed) => value with { AllowPhoneWebcam = allowed },
            value => value.AllowPhoneWebcam, (value, allowed) => value with { AllowPhoneWebcam = allowed }),
        Define(DevicePermissionKind.FileBrowsing, "allowFileBrowsing", "Browse and open files", false,
            value => value.AllowFileBrowsing, (value, allowed) => value with { AllowFileBrowsing = allowed },
            value => value.AllowFileBrowsing, (value, allowed) => value with { AllowFileBrowsing = allowed }),
        Define(DevicePermissionKind.FileChanges, "allowFileChanges", "Change files", false,
            value => value.AllowFileChanges, (value, allowed) => value with { AllowFileChanges = allowed },
            value => value.AllowFileChanges, (value, allowed) => value with { AllowFileChanges = allowed }),
        Define(DevicePermissionKind.FileTransfer, "allowFileTransfer", "Transfer files", false,
            value => value.AllowFileTransfer, (value, allowed) => value with { AllowFileTransfer = allowed },
            value => value.AllowFileTransfer, (value, allowed) => value with { AllowFileTransfer = allowed }),
        Define(DevicePermissionKind.Diagnostics, "allowDiagnostics", "View diagnostics", false,
            value => value.AllowDiagnostics, (value, allowed) => value with { AllowDiagnostics = allowed },
            value => value.AllowDiagnostics, (value, allowed) => value with { AllowDiagnostics = allowed }),
        Define(DevicePermissionKind.Terminal, "allowTerminal", "Terminal", false,
            value => value.AllowTerminal, (value, allowed) => value with { AllowTerminal = allowed },
            value => value.AllowTerminal, (value, allowed) => value with { AllowTerminal = allowed }),
        Define(DevicePermissionKind.SignOut, "allowSignOut", "Sign out", false,
            value => value.AllowSignOut, (value, allowed) => value with { AllowSignOut = allowed },
            value => value.AllowSignOut, (value, allowed) => value with { AllowSignOut = allowed }),
        Define(DevicePermissionKind.Restart, "allowRestart", "Restart PC", false,
            value => value.AllowRestart, (value, allowed) => value with { AllowRestart = allowed },
            value => value.AllowRestart, (value, allowed) => value with { AllowRestart = allowed }),
        Define(DevicePermissionKind.Shutdown, "allowShutdown", "Shut down PC", false,
            value => value.AllowShutdown, (value, allowed) => value with { AllowShutdown = allowed },
            value => value.AllowShutdown, (value, allowed) => value with { AllowShutdown = allowed })
    ];

    public static HostPermissionSet MyDevice { get; } = CreateMatrix(static _ => true);

    public static HostPermissionSet RemoteControls { get; } = CreateMatrix(static permission => permission.RemoteControlsAllowed);

    public static HostPermissionSet AllBlocked { get; } = CreateMatrix(static _ => false);

    public static string GetDisplayName(DeviceAccessProfile profile) => profile switch
    {
        DeviceAccessProfile.MyDevice => "My device",
        DeviceAccessProfile.RemoteControls => "Remote controls",
        _ => "Custom"
    };

    public static bool IsBuiltIn(DeviceAccessProfile profile) =>
        profile is DeviceAccessProfile.MyDevice or DeviceAccessProfile.RemoteControls;

    public static HostPermissionSet GetBuiltInMatrix(DeviceAccessProfile profile) => profile switch
    {
        DeviceAccessProfile.MyDevice => MyDevice,
        DeviceAccessProfile.RemoteControls => RemoteControls,
        _ => AllBlocked
    };

    public static DevicePermissionOverrides ToCompleteOverrides(HostPermissionSet permissions)
    {
        var result = new DevicePermissionOverrides(
            HideProtectedFileSystemItems: permissions.HideProtectedFileSystemItems);
        foreach (var permission in Permissions)
        {
            result = permission.WriteOverride(result, permission.Read(permissions));
        }

        return result;
    }

    public static DevicePermissionOverrides ClearManagedValues(DevicePermissionOverrides? values)
    {
        var result = values ?? new DevicePermissionOverrides();
        foreach (var permission in Permissions)
        {
            result = permission.WriteOverride(result, null);
        }

        return result;
    }

    public static bool TryResolveCustom(DevicePermissionOverrides? values, bool hideProtected, out HostPermissionSet permissions)
    {
        permissions = AllBlocked.HideProtectedFileSystemItems == hideProtected
            ? AllBlocked
            : AllBlocked with { HideProtectedFileSystemItems = hideProtected };
        if (values is null || Permissions.Any(permission => permission.ReadOverride(values) is null))
        {
            return false;
        }

        permissions = new HostPermissionSet(
            AllowRemoteInput: values.AllowRemoteInput!.Value,
            AllowPcSleep: values.AllowPcSleep!.Value,
            AllowVolumeControl: values.AllowVolumeControl!.Value,
            AllowPresentationControl: values.AllowPresentationControl!.Value,
            AllowRemoteAppLaunch: values.AllowRemoteAppLaunch!.Value,
            AllowUrlOpen: values.AllowUrlOpen!.Value,
            AllowPcLock: values.AllowPcLock!.Value,
            AllowBlackoutDisplay: values.AllowBlackoutDisplay!.Value,
            AllowDisplayControl: values.AllowDisplayControl!.Value,
            AllowScreenSaver: values.AllowScreenSaver!.Value,
            AllowAwakeControl: values.AllowAwakeControl!.Value,
            AllowClipboardRead: values.AllowClipboardRead!.Value,
            AllowScreenViewing: values.AllowScreenViewing!.Value,
            AllowPhoneWebcam: values.AllowPhoneWebcam!.Value,
            AllowSignOut: values.AllowSignOut!.Value,
            AllowRestart: values.AllowRestart!.Value,
            AllowShutdown: values.AllowShutdown!.Value,
            AllowFileBrowsing: values.AllowFileBrowsing!.Value,
            AllowFileChanges: values.AllowFileChanges!.Value,
            AllowFileTransfer: values.AllowFileTransfer!.Value,
            AllowDiagnostics: values.AllowDiagnostics!.Value,
            AllowTerminal: values.AllowTerminal!.Value,
            HideProtectedFileSystemItems: hideProtected);

        return true;
    }

    public static DevicePermissionOverrides Set(
        DevicePermissionOverrides values,
        DevicePermissionKind kind,
        bool allowed)
    {
        var permission = Permissions.FirstOrDefault(item => item.Kind == kind);
        return permission is null ? values : permission.WriteOverride(values, allowed);
    }

    public static bool Read(HostPermissionSet permissions, DevicePermissionKind kind) =>
        Permissions.FirstOrDefault(item => item.Kind == kind)?.Read(permissions) == true;

    private static DevicePermissionDefinition Define(
        DevicePermissionKind kind,
        string persistedKey,
        string displayName,
        bool remoteControlsAllowed,
        Func<HostPermissionSet, bool> read,
        Func<HostPermissionSet, bool, HostPermissionSet> write,
        Func<DevicePermissionOverrides, bool?> readOverride,
        Func<DevicePermissionOverrides, bool?, DevicePermissionOverrides> writeOverride) =>
        new(kind, persistedKey, displayName, remoteControlsAllowed, read, write, readOverride, writeOverride);

    private static HostPermissionSet CreateMatrix(Func<DevicePermissionDefinition, bool> isAllowed)
    {
        var result = new HostPermissionSet(
            AllowRemoteInput: false,
            AllowPcSleep: false,
            AllowVolumeControl: false,
            AllowPresentationControl: false,
            AllowRemoteAppLaunch: false,
            AllowUrlOpen: false,
            AllowPcLock: false,
            AllowBlackoutDisplay: false,
            AllowDisplayControl: false,
            AllowScreenSaver: false,
            AllowAwakeControl: false,
            AllowClipboardRead: false,
            AllowScreenViewing: false,
            AllowPhoneWebcam: false,
            AllowSignOut: false,
            AllowRestart: false,
            AllowShutdown: false,
            AllowFileBrowsing: false,
            AllowFileChanges: false,
            AllowFileTransfer: false,
            AllowDiagnostics: false,
            AllowTerminal: false,
            HideProtectedFileSystemItems: true);
        foreach (var permission in Permissions)
        {
            result = permission.Write(result, isAllowed(permission));
        }

        return result;
    }
}

internal sealed class DeviceAccessProfileJsonConverter : JsonConverter<DeviceAccessProfile>
{
    public override DeviceAccessProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return DeviceAccessProfile.Invalid;
        }

        return reader.GetString() switch
        {
            "my-device" => DeviceAccessProfile.MyDevice,
            "remote-controls" => DeviceAccessProfile.RemoteControls,
            "custom" => DeviceAccessProfile.Custom,
            _ => DeviceAccessProfile.Invalid
        };
    }

    public override void Write(Utf8JsonWriter writer, DeviceAccessProfile value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            DeviceAccessProfile.MyDevice => "my-device",
            DeviceAccessProfile.RemoteControls => "remote-controls",
            DeviceAccessProfile.Custom => "custom",
            _ => "invalid"
        });
}

internal sealed class NullableDeviceAccessProfileJsonConverter : JsonConverter<DeviceAccessProfile?>
{
    public override bool HandleNull => true;

    public override DeviceAccessProfile? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return DeviceAccessProfile.Invalid;
        }

        return new DeviceAccessProfileJsonConverter().Read(
            ref reader,
            typeof(DeviceAccessProfile),
            options);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DeviceAccessProfile? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        new DeviceAccessProfileJsonConverter().Write(writer, value.Value, options);
    }
}

internal sealed class DevicePermissionOverridesJsonConverter : JsonConverter<DevicePermissionOverrides>
{
    public override DevicePermissionOverrides Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return new DevicePermissionOverrides();
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var result = new DevicePermissionOverrides();
        foreach (var permission in DeviceAccessProfiles.Permissions)
        {
            if (document.RootElement.TryGetProperty(permission.PersistedKey, out var value))
            {
                result = permission.WriteOverride(result, ReadOptionalBoolean(value));
            }
        }

        if (document.RootElement.TryGetProperty("hideProtectedFileSystemItems", out var hideProtected))
        {
            result = result with
            {
                HideProtectedFileSystemItems = ReadOptionalBoolean(hideProtected)
            };
        }

        return result;
    }

    public override void Write(
        Utf8JsonWriter writer,
        DevicePermissionOverrides value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var permission in DeviceAccessProfiles.Permissions)
        {
            if (permission.ReadOverride(value) is { } allowed)
            {
                writer.WriteBoolean(permission.PersistedKey, allowed);
            }
        }

        if (value.HideProtectedFileSystemItems is { } hideProtected)
        {
            writer.WriteBoolean("hideProtectedFileSystemItems", hideProtected);
        }

        writer.WriteEndObject();
    }

    private static bool? ReadOptionalBoolean(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}
