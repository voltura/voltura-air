namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenComponentMovementController(
    Func<CustomScreenDefinition?> getDraft,
    Func<string> getOrientation,
    Action<CustomScreenDefinition> applyDraft)
{
    public void MoveSection(string? sectionId, int direction)
    {
        var draft = getDraft();
        if (draft is null || sectionId is null)
        {
            return;
        }

        if (draft.OrientationLayoutsEnabled)
        {
            applyDraft(CustomScreenOrientationEditing.MoveSection(
                draft,
                sectionId,
                direction,
                getOrientation()));
            return;
        }

        var sections = draft.Sections.ToList();
        var index = sections.FindIndex(section => section.Id == sectionId);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= sections.Count)
        {
            return;
        }
        (sections[index], sections[target]) = (sections[target], sections[index]);
        applyDraft(draft with { Sections = sections });
    }

    public void MoveButton(string? sectionId, string? buttonId, int direction)
    {
        var draft = getDraft();
        if (draft is null || sectionId is null || buttonId is null)
        {
            return;
        }

        if (draft.OrientationLayoutsEnabled)
        {
            applyDraft(CustomScreenOrientationEditing.MoveButton(
                draft,
                sectionId,
                buttonId,
                direction,
                getOrientation()));
            return;
        }

        var section = draft.Sections.First(item => item.Id == sectionId);
        var buttons = section.Buttons.ToList();
        var index = buttons.FindIndex(button => button.Id == buttonId);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= buttons.Count)
        {
            return;
        }
        (buttons[index], buttons[target]) = (buttons[target], buttons[index]);
        applyDraft(draft with
        {
            Sections = [.. draft.Sections.Select(candidate =>
                candidate.Id == sectionId
                    ? section with { Buttons = buttons }
                    : candidate)]
        });
    }
}
