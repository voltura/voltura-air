using Microsoft.Win32;
using System.Windows;

namespace VolturaAir.Host;

public static class CustomScreenEditorSettings
{
    public const int DefaultComponentPaletteWidth = 210;
    public const int DefaultPropertiesPanelWidth = 290;

    private const string ConfirmDeletesValueName = "CustomScreenConfirmDeletes";
    private const string ConfirmHidesValueName = "CustomScreenConfirmHides";
    private const string ComponentPaletteWidthValueName =
        "CustomScreenComponentPaletteWidth";
    private const string PropertiesPanelWidthValueName =
        "CustomScreenPropertiesPanelWidth";

    public static bool ConfirmDeletes()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            HostSettingsRegistry.SettingsKeyPath,
            writable: false);
        return key?.GetValue(ConfirmDeletesValueName) is not int value || value != 0;
    }

    public static void SetConfirmDeletes(bool enabled)
        => SetBoolean(ConfirmDeletesValueName, enabled);

    public static bool ConfirmHides()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            HostSettingsRegistry.SettingsKeyPath,
            writable: false);
        return key?.GetValue(ConfirmHidesValueName) is not int value || value != 0;
    }

    public static void SetConfirmHides(bool enabled)
        => SetBoolean(ConfirmHidesValueName, enabled);

    public static (double ComponentPalette, double Properties) PanelWidths() =>
        (
            ReadPanelWidth(
                ComponentPaletteWidthValueName,
                DefaultComponentPaletteWidth),
            ReadPanelWidth(
                PropertiesPanelWidthValueName,
                DefaultPropertiesPanelWidth)
        );

    public static void SetPanelWidths(
        double componentPaletteWidth,
        double propertiesPanelWidth)
    {
        using var key = OpenWritableKey();
        key.SetValue(
            ComponentPaletteWidthValueName,
            NormalizePanelWidth(
                componentPaletteWidth,
                DefaultComponentPaletteWidth),
            RegistryValueKind.DWord);
        key.SetValue(
            PropertiesPanelWidthValueName,
            NormalizePanelWidth(
                propertiesPanelWidth,
                DefaultPropertiesPanelWidth),
            RegistryValueKind.DWord);
    }

    private static void SetBoolean(string valueName, bool enabled)
    {
        using var key = OpenWritableKey();
        key.SetValue(
            valueName,
            enabled ? 1 : 0,
            RegistryValueKind.DWord);
    }

    private static double ReadPanelWidth(string valueName, int defaultWidth)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            HostSettingsRegistry.SettingsKeyPath,
            writable: false);
        return key?.GetValue(valueName) is int width
            ? NormalizePanelWidth(width, defaultWidth)
            : defaultWidth;
    }

    private static int NormalizePanelWidth(double width, int minimum)
    {
        if (!double.IsFinite(width))
        {
            return minimum;
        }

        var virtualScreenWidth = SystemParameters.VirtualScreenWidth;
        var maximum = double.IsFinite(virtualScreenWidth) &&
            virtualScreenWidth >= minimum
                ? Math.Floor(virtualScreenWidth)
                : int.MaxValue;
        return (int)Math.Clamp(
            Math.Round(width, MidpointRounding.AwayFromZero),
            minimum,
            maximum);
    }

    private static RegistryKey OpenWritableKey() =>
        Registry.CurrentUser.OpenSubKey(
            HostSettingsRegistry.SettingsKeyPath,
            writable: true) ??
        Registry.CurrentUser.CreateSubKey(
            HostSettingsRegistry.SettingsKeyPath,
            writable: true);
}
