using System.Reflection;
using System.Security;
using Microsoft.Win32;

namespace VolturaAir.Host;

internal static class CatalogProtocolRegistration
{
    private const string Scheme = "voltura-air";

    public static void TryRegisterCurrentApplication()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return;
            }

            using var classes = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes",
                writable: true);
            if (classes is null)
            {
                return;
            }

            Register(
                classes,
                BuildOpenCommand(processPath, entryAssemblyPath),
                processPath);
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or SecurityException or IOException)
        {
        }
    }

    internal static string BuildOpenCommand(
        string processPath,
        string? entryAssemblyPath)
    {
        var quotedProcess = $"\"{processPath}\"";
        if (string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            return $"{quotedProcess} \"{entryAssemblyPath}\" \"%1\"";
        }

        return $"{quotedProcess} \"%1\"";
    }

    internal static void Register(
        RegistryKey classes,
        string openCommand,
        string iconPath)
    {
        using var protocol = classes.CreateSubKey(Scheme, writable: true);
        protocol.SetValue(null, "URL:Voltura Air custom screen");
        protocol.SetValue("URL Protocol", string.Empty);

        using var icon = protocol.CreateSubKey("DefaultIcon", writable: true);
        icon.SetValue(null, $"{iconPath},0");

        using var command = protocol.CreateSubKey(
            @"shell\open\command",
            writable: true);
        command.SetValue(null, openCommand);
    }
}
