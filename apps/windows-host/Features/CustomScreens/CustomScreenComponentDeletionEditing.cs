namespace VolturaAir.Host.Features.CustomScreens;

internal static class CustomScreenComponentDeletionEditing
{
    public static CustomScreenDefinition Delete(
        CustomScreenDefinition draft,
        string sectionId,
        string? buttonId,
        string orientation,
        bool deleteEverywhere)
    {
        if (draft.OrientationLayoutsEnabled && !deleteEverywhere)
        {
            return CustomScreenOrientationEditing.HideComponent(
                draft,
                sectionId,
                buttonId,
                orientation);
        }

        if (buttonId is not null)
        {
            return draft with
            {
                Sections =
                [
                    .. draft.Sections.Select(section =>
                        section.Id == sectionId
                            ? section with
                            {
                                Buttons =
                                [
                                    .. section.Buttons.Where(button =>
                                        button.Id != buttonId)
                                ]
                            }
                            : section)
                ]
            };
        }

        return draft with
        {
            Sections =
            [
                .. draft.Sections.Where(section => section.Id != sectionId)
            ]
        };
    }
}
