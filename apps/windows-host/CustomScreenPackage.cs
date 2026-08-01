using System.Text.Json;

namespace VolturaAir.Host;

public sealed record CustomScreenPackage(
    int PackageVersion,
    string Format,
    CustomScreenDefinition Screen);

public sealed record CustomScreenPackageInspection(
    CustomScreenPackage Package,
    CustomScreenDefinition ImportedScreen,
    int SectionCount,
    int ButtonCount,
    IReadOnlyList<string> ActionKinds,
    int HostLocalActionCount)
{
    public bool HasHostLocalActions => HostLocalActionCount > 0;
}

public static class CustomScreenPackages
{
    public const int CurrentPackageVersion = 1;
    public const string Format = "voltura-air.custom-screen";
    public const string FileExtension = ".volturascreen";

    public static byte[] Serialize(CustomScreenDefinition screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var portable = screen with { AssignedClientIds = [] };
        return JsonSerializer.SerializeToUtf8Bytes(
            new CustomScreenPackage(CurrentPackageVersion, Format, portable),
            JsonOptions.Default);
    }

    public static bool TryRead(
        ReadOnlySpan<byte> bytes,
        out CustomScreenPackageInspection? inspection,
        out string error)
    {
        inspection = null;
        if (bytes.Length == 0 || bytes.Length > CustomScreenLimits.MaxStoreBytes)
        {
            error = "The custom-screen package is empty or too large.";
            return false;
        }

        try
        {
            var package = JsonSerializer.Deserialize<CustomScreenPackage>(bytes, JsonOptions.Default);
            if (package is null ||
                package.PackageVersion != CurrentPackageVersion ||
                !string.Equals(package.Format, Format, StringComparison.Ordinal) ||
                package.Screen is null)
            {
                error = "This custom-screen package is unsupported or incomplete.";
                return false;
            }

            if (!CustomScreenValidator.TryValidate(package.Screen, out error))
            {
                error = $"The custom-screen package is invalid: {error}";
                return false;
            }

            var imported = CustomScreenDraftFactory.CloneWithNewIds(package.Screen) with
            {
                AssignedClientIds = []
            };
            var actions = package.Screen.Sections
                .SelectMany(section => section.Buttons)
                .Select(button => button.Action)
                .ToArray();
            inspection = new(
                package,
                imported,
                package.Screen.Sections.Count,
                actions.Length,
                [.. actions.Select(action => action.Kind).Distinct(StringComparer.Ordinal).Order()],
                actions.Count(action => action.Kind == "appLaunch"));
            error = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            error = "The custom-screen package is not valid JSON.";
            return false;
        }
        catch (NotSupportedException)
        {
            error = "The custom-screen package uses unsupported data.";
            return false;
        }
    }

    public static bool TryRead(
        string filePath,
        out CustomScreenPackageInspection? inspection,
        out string error)
    {
        inspection = null;
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length > CustomScreenLimits.MaxStoreBytes)
            {
                error = "The custom-screen package is missing or too large.";
                return false;
            }

            return TryRead(File.ReadAllBytes(filePath), out inspection, out error);
        }
        catch (IOException ex)
        {
            error = $"The custom-screen package could not be read: {ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = $"The custom-screen package could not be read: {ex.Message}";
            return false;
        }
    }

    public static string ReviewText(CustomScreenPackageInspection inspection)
    {
        return $"Import custom screen \"{inspection.ImportedScreen.Name}\"?\n\n" +
            ReviewDetails(inspection);
    }

    public static string DuplicateReviewText(
        CustomScreenPackageInspection inspection,
        CustomScreenDefinition existing)
    {
        return $"A matching custom screen named \"{existing.Name}\" is already in your library.\n\n" +
            "Import another copy?\n\n" +
            ReviewDetails(inspection);
    }

    public static bool HasSamePortableContent(
        CustomScreenDefinition left,
        CustomScreenDefinition right) =>
        JsonSerializer.SerializeToUtf8Bytes(
            NormalizePortableContent(left),
            JsonOptions.Default).AsSpan().SequenceEqual(
                JsonSerializer.SerializeToUtf8Bytes(
                    NormalizePortableContent(right),
                    JsonOptions.Default));

    private static string ReviewDetails(CustomScreenPackageInspection inspection)
    {
        var actionKinds = inspection.ActionKinds.Count == 0
            ? "none"
            : string.Join(", ", inspection.ActionKinds);
        var applicationActionLabel = inspection.HostLocalActionCount == 1
            ? "application action"
            : "application actions";
        var warning = inspection.HasHostLocalActions
            ? $"\nWarning: {inspection.HostLocalActionCount} {applicationActionLabel} depend on approved apps configured on this PC and may be unavailable."
            : string.Empty;
        return
            $"Panels: {inspection.SectionCount}\n" +
            $"Buttons: {inspection.ButtonCount}\n" +
            $"Action types: {actionKinds}\n" +
            "Device assignments: none\n" +
            "Assign this screen to a device after import." +
            warning;
    }

    private static CustomScreenDefinition NormalizePortableContent(
        CustomScreenDefinition screen) =>
        screen with
        {
            Id = string.Empty,
            Revision = string.Empty,
            AssignedClientIds = [],
            Sections = [.. screen.Sections.Select(section => section with
            {
                Id = string.Empty,
                Buttons = [.. section.Buttons.Select(button => button with
                {
                    Id = string.Empty
                })]
            })]
        };
}
