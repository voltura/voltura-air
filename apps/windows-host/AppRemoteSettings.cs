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
    public const string DefaultYoutubeUrl = "https://youtube.com";

    private const string SettingsKeyPath = @"Software\VolturaAir";
    private const string DefaultRemoteModeValueName = "DefaultRemoteMode";
    private const string YoutubeUrlValueName = "YoutubeUrl";

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

    public static string GetYoutubeUrl()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        var stored = key?.GetValue(YoutubeUrlValueName) as string;
        return TryNormalizeYoutubeUrl(stored, out var normalized) ? normalized : DefaultYoutubeUrl;
    }

    public static bool TrySetYoutubeUrl(string value, out string normalizedUrl)
    {
        if (!TryNormalizeYoutubeUrl(value, out normalizedUrl))
        {
            return false;
        }

        var current = GetYoutubeUrl();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(YoutubeUrlValueName, normalizedUrl, RegistryValueKind.String);

        if (!string.Equals(current, normalizedUrl, StringComparison.OrdinalIgnoreCase))
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }

        return true;
    }

    public static bool TryNormalizeYoutubeUrl(string? value, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        normalizedUrl = uri.ToString();
        return true;
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
