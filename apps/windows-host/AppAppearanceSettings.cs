using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppAppearanceSettings
{
    private static string SettingsKeyPath => HostSettingsRegistry.SettingsKeyPath;
    private const string ShowModeButtonsValueName = "ShowModeButtons";

    public static event EventHandler? Changed;

    public static bool ShowModeButtons()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(ShowModeButtonsValueName) is not int value || value != 0;
    }

    public static void SetShowModeButtons(bool showModeButtons)
    {
        var current = ShowModeButtons();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(ShowModeButtonsValueName, showModeButtons ? 1 : 0, RegistryValueKind.DWord);

        if (current != showModeButtons)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
