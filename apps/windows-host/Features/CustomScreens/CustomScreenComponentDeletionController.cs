using System.Windows;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenComponentDeletionController(
    Window owner,
    Func<CustomScreenDefinition?> getDraft,
    Func<string> getOrientation,
    Action<string, string?, bool> delete,
    Action<string> showToast)
{
    public void Request(string sectionId, string? buttonId) =>
        Request(sectionId, buttonId, deleteEverywhere: false);

    public void RequestEverywhere(string sectionId, string? buttonId) =>
        Request(sectionId, buttonId, deleteEverywhere: true);

    private void Request(
        string sectionId,
        string? buttonId,
        bool deleteEverywhere)
    {
        var draft = getDraft();
        var section = draft?.Sections.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, sectionId, StringComparison.Ordinal));
        if (section is null)
        {
            return;
        }

        var description = $"panel \"{section.Name}\"";
        if (buttonId is not null)
        {
            var button = section.Buttons.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, buttonId, StringComparison.Ordinal));
            if (button is null)
            {
                return;
            }
            description = $"button \"{button.Name}\"";
        }

        var scopedToOrientation = draft!.OrientationLayoutsEnabled &&
            !deleteEverywhere;
        var orientation = OrientationTitle(getOrientation());
        var title = scopedToOrientation ? "Hide component" : "Delete component";
        var message = scopedToOrientation
            ? $"Hide {description} in {orientation}? It remains available in the other orientation."
            : $"Delete {description} from both orientations? You can undo this change until the screen is saved.";
        var confirmationEnabled = scopedToOrientation
            ? CustomScreenEditorSettings.ConfirmHides()
            : CustomScreenEditorSettings.ConfirmDeletes();
        if (confirmationEnabled &&
            !ThemedConfirmationDialog.Show(
                owner,
                title,
                message,
                scopedToOrientation ? "Hide" : "Delete",
                "Cancel",
                ConfirmationTone.Warning))
        {
            return;
        }

        delete(sectionId, buttonId, deleteEverywhere);
        showToast(scopedToOrientation
            ? $"Component hidden in {orientation}. Undo is available until Save."
            : "Component deleted from both orientations. Undo is available until Save.");
    }

    private static string OrientationTitle(string orientation) =>
        string.Equals(orientation, "landscape", StringComparison.OrdinalIgnoreCase)
            ? "Landscape"
            : "Portrait";
}
