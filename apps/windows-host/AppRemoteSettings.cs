using Microsoft.Win32;

namespace VolturaAir.Host;

public enum AppRemoteMode
{
    Standard,
    Youtube,
    Kodi
}

public static class AppRemoteSettings
{
    private const string SettingsKeyPath = @"Software\VolturaAir";
    private const string DefaultRemoteModeValueName = "DefaultRemoteMode";

    public static event EventHandler? Changed;

    public static AppRemoteMode GetDefaultRemoteMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return Parse(key?.GetValue(DefaultRemoteModeValueName) as string);
    }

    public static void SetDefaultRemoteMode(AppRemoteMode mode)
    {
        var current = GetDefaultRemoteMode();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(DefaultRemoteModeValueName, mode.ToString(), RegistryValueKind.String);

        if (current != mode)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static string ToProtocolId(AppRemoteMode mode)
    {
        return mode switch
        {
            AppRemoteMode.Youtube => "youtube",
            AppRemoteMode.Kodi => "kodi",
            _ => "standard"
        };
    }

    private static AppRemoteMode Parse(string? value)
    {
        return Enum.TryParse<AppRemoteMode>(value, ignoreCase: true, out var mode)
            ? mode
            : AppRemoteMode.Standard;
    }
}
