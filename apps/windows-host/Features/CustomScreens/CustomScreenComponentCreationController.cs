namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenComponentCreationController(
    Func<CustomScreenDefinition?> getDraft,
    Func<string?> getSelectedSectionId,
    Func<int?> getSelectedRow,
    Func<string> getOrientation,
    Action<CustomScreenDraftEdit> apply)
{
    public void AddSection(string kind)
    {
        var draft = getDraft();
        if (draft is null)
        {
            return;
        }

        apply(CustomScreenPreviewDraftEditing.CreateSection(
            draft,
            kind,
            targetSectionId: null,
            insertAfter: false,
            orientation: getOrientation()));
    }

    public void AddButton()
    {
        var draft = getDraft();
        if (draft is null)
        {
            return;
        }

        var selectedSectionId = getSelectedSectionId();
        var selectedSection = draft.Sections.FirstOrDefault(section =>
            section.Id == selectedSectionId);
        if (selectedSection is null ||
            !CustomScreenSectionKinds.AllowsButtons(selectedSection.Kind))
        {
            var sectionEdit = CustomScreenPreviewDraftEditing.CreateSection(
                draft,
                "buttons",
                targetSectionId: null,
                insertAfter: true,
                orientation: getOrientation());
            draft = sectionEdit.Draft;
            selectedSectionId = sectionEdit.SelectedSectionId;
            selectedSection = draft.Sections.First(section =>
                section.Id == selectedSectionId);
        }

        var selectedRow = getSelectedRow();
        var targetRow = selectedRow is > 0 && selectedRow <= selectedSection.RowLimit
            ? selectedRow.Value
            : 0;
        var edit = CustomScreenPreviewDraftEditing.CreateButton(
            draft,
            selectedSectionId!,
            targetRow,
            targetButtonId: null,
            insertAfter: false,
            orientation: getOrientation());
        if (edit is not null)
        {
            apply(edit);
        }
    }
}
