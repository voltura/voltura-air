namespace VolturaAir.Host.Features.CustomScreens;

internal static class CustomScreenLayoutEditing
{
    public static CustomScreenDefinition? ReorderButton(
        CustomScreenDefinition draft,
        string draggedButtonId,
        string targetSectionId,
        string targetButtonId,
        bool insertAfter,
        int? targetVisualRow)
    {
        if (draggedButtonId == targetButtonId)
        {
            return null;
        }

        var dragged = draft.Sections
            .SelectMany(section => section.Buttons)
            .FirstOrDefault(button => button.Id == draggedButtonId);
        var target = draft.Sections
            .SelectMany(section => section.Buttons)
            .FirstOrDefault(button => button.Id == targetButtonId);
        var targetSection = draft.Sections.FirstOrDefault(section =>
            section.Id == targetSectionId &&
            CustomScreenSectionKinds.AllowsButtons(section.Kind));
        if (dragged is null || target is null || targetSection is null)
        {
            return null;
        }

        var targetRow = targetVisualRow is > 0
            ? targetVisualRow.Value
            : target.Row;
        var sections = draft.Sections.Select(section =>
        {
            var buttons = section.Buttons
                .Where(button => button.Id != draggedButtonId)
                .Select(button =>
                    section.Id == targetSectionId &&
                    button.Id == targetButtonId &&
                    targetRow > 0
                        ? button with { Row = targetRow }
                        : button)
                .ToList();
            if (section.Id == targetSectionId)
            {
                var targetIndex = buttons.FindIndex(button =>
                    button.Id == targetButtonId);
                if (targetIndex >= 0 && insertAfter)
                {
                    targetIndex++;
                }
                buttons.Insert(
                    targetIndex < 0 ? buttons.Count : targetIndex,
                    dragged with { Row = targetRow });
            }
            return section with { Buttons = buttons };
        }).ToArray();

        return draft with { Sections = sections };
    }
}
