namespace VolturaAir.Host.Features.CustomScreens;

internal sealed record CustomScreenDraftEdit(
    CustomScreenDefinition Draft,
    string SelectedSectionId,
    string? SelectedButtonId = null,
    int? SelectedRow = null);

internal static class CustomScreenPreviewDraftEditing
{
    public static CustomScreenDraftEdit? ReorderSection(
        CustomScreenDefinition draft,
        string draggedSectionId,
        string targetSectionId,
        string orientation,
        bool insertAfter)
    {
        if (draggedSectionId == targetSectionId)
        {
            return null;
        }

        if (draft.OrientationLayoutsEnabled)
        {
            return new(
                CustomScreenOrientationEditing.ReorderSection(
                    draft,
                    draggedSectionId,
                    targetSectionId,
                    orientation,
                    insertAfter),
                draggedSectionId);
        }

        var sections = draft.Sections.ToList();
        var dragged = sections.FirstOrDefault(section =>
            section.Id == draggedSectionId);
        if (dragged is null)
        {
            return null;
        }

        sections.Remove(dragged);
        var targetIndex = sections.FindIndex(section =>
            section.Id == targetSectionId);
        if (targetIndex < 0)
        {
            return null;
        }

        sections.Insert(targetIndex + (insertAfter ? 1 : 0), dragged);
        return new(draft with { Sections = sections }, draggedSectionId);
    }

    public static CustomScreenDraftEdit? ReorderButton(
        CustomScreenDefinition draft,
        string draggedButtonId,
        string targetSectionId,
        string targetButtonId,
        bool insertAfter,
        int? targetVisualRow,
        string orientation)
    {
        var next = draft.OrientationLayoutsEnabled
            ? CustomScreenOrientationEditing.ReorderButton(
                draft,
                draggedButtonId,
                targetSectionId,
                targetButtonId,
                insertAfter,
                targetVisualRow,
                orientation)
            : CustomScreenLayoutEditing.ReorderButton(
                draft,
                draggedButtonId,
                targetSectionId,
                targetButtonId,
                insertAfter,
                targetVisualRow);
        return next is null || Equals(next, draft)
            ? null
            : new(next, targetSectionId, draggedButtonId, targetVisualRow);
    }

    public static CustomScreenDraftEdit? MoveButtonToSection(
        CustomScreenDefinition draft,
        string buttonId,
        string targetSectionId,
        int? targetRow)
    {
        var button = draft.Sections
            .SelectMany(section => section.Buttons)
            .FirstOrDefault(candidate => candidate.Id == buttonId);
        if (button is null)
        {
            return null;
        }

        var sections = draft.Sections.Select(section =>
        {
            var buttons = section.Buttons
                .Where(candidate => candidate.Id != buttonId)
                .ToList();
            if (section.Id == targetSectionId)
            {
                buttons.Add(button with
                {
                    Row = targetRow ??
                        (button.Row <= section.RowLimit ? button.Row : 0)
                });
            }
            return section with { Buttons = buttons };
        }).ToArray();
        return new(
            draft with { Sections = sections },
            targetSectionId,
            buttonId,
            targetRow);
    }

    public static CustomScreenDraftEdit? CreatePanelForDroppedButton(
        CustomScreenDefinition draft,
        string? existingButtonId,
        string orientation)
    {
        var panelEdit = CreateSection(
            draft,
            "buttons",
            targetSectionId: null,
            insertAfter: true,
            orientation);
        return existingButtonId is null
            ? CreateButton(
                panelEdit.Draft,
                panelEdit.SelectedSectionId,
                targetRow: 0,
                targetButtonId: null,
                insertAfter: true,
                orientation)
            : MoveButtonToSection(
                panelEdit.Draft,
                existingButtonId,
                panelEdit.SelectedSectionId,
                targetRow: null);
    }

    public static CustomScreenDraftEdit CreateSection(
        CustomScreenDefinition draft,
        string kind,
        string? targetSectionId,
        bool insertAfter,
        string orientation)
    {
        var next = kind switch
        {
            "collapsible" => CustomScreenService.CreateCollapsibleSection(draft),
            "trackpad" => CustomScreenService.CreateTrackpad(draft),
            "collapsibleTrackpad" => CustomScreenService.CreateCollapsibleTrackpad(draft),
            "volume" => CustomScreenService.CreateVolumeSlider(draft),
            _ => CustomScreenService.CreateSection(draft)
        };
        var created = next.Sections[^1];
        if (targetSectionId is not null)
        {
            var edit = ReorderSection(
                next,
                created.Id,
                targetSectionId,
                orientation,
                insertAfter);
            next = edit?.Draft ?? next;
        }
        next = CustomScreenOrientationEditing.ScopeNewSection(
            next,
            created.Id,
            orientation);

        return new(next, created.Id);
    }

    public static CustomScreenDraftEdit? CreateButton(
        CustomScreenDefinition draft,
        string targetSectionId,
        int targetRow,
        string? targetButtonId,
        bool insertAfter,
        string orientation)
    {
        var targetSection = draft.Sections.FirstOrDefault(section =>
            section.Id == targetSectionId);
        if (targetSection is null ||
            !CustomScreenSectionKinds.AllowsButtons(targetSection.Kind))
        {
            return null;
        }

        var next = CustomScreenService.CreateButton(
            draft,
            targetSectionId,
            targetRow);
        var created = next.Sections
            .First(section => section.Id == targetSectionId)
            .Buttons[^1];
        if (targetButtonId is not null)
        {
            next = ReorderButton(
                next,
                created.Id,
                targetSectionId,
                targetButtonId,
                insertAfter,
                targetRow > 0 ? targetRow : null,
                orientation)?.Draft ?? next;
        }
        next = CustomScreenOrientationEditing.ScopeNewButton(
            next,
            targetSectionId,
            created.Id,
            orientation);

        return new(
            next,
            targetSectionId,
            created.Id,
            targetRow > 0 ? targetRow : null);
    }
}
