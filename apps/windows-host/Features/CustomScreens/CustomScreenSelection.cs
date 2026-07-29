namespace VolturaAir.Host.Features.CustomScreens;

internal static class CustomScreenSelection
{
    public static (string? SectionId, string? ButtonId, int? Row) Normalize(
        CustomScreenDefinition draft,
        string? sectionId,
        string? buttonId,
        int? row)
    {
        if (draft.Sections.Count == 0)
        {
            return (null, null, null);
        }

        var section = draft.Sections.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, sectionId, StringComparison.Ordinal));
        if (section is null)
        {
            return (draft.Sections[0].Id, null, null);
        }

        if (row is < 1 || row > section.RowLimit)
        {
            row = null;
        }
        if (buttonId is not null &&
            !section.Buttons.Any(button =>
                string.Equals(button.Id, buttonId, StringComparison.Ordinal)))
        {
            buttonId = null;
        }

        return (section.Id, buttonId, row);
    }
}
