namespace VolturaAir.Host.Features.CustomScreens;

internal static class CustomScreenOrientationEditing
{
    public static CustomScreenDefinition Enable(CustomScreenDefinition draft) =>
        draft with
        {
            OrientationLayoutsEnabled = true,
            Sections = [.. draft.Sections.Select((section, sectionIndex) =>
                section with
                {
                    Portrait = SectionLayout(section, sectionIndex),
                    Landscape = SectionLayout(section, sectionIndex),
                    Buttons = [.. section.Buttons.Select((button, buttonIndex) =>
                        button with
                        {
                            Portrait = ButtonLayout(button, buttonIndex),
                            Landscape = ButtonLayout(button, buttonIndex)
                        })]
                })]
        };

    public static CustomScreenDefinition MoveSection(
        CustomScreenDefinition draft,
        string sectionId,
        int direction,
        string orientation)
    {
        var ordered = OrderedSections(draft, orientation);
        var index = ordered.FindIndex(section => section.Id == sectionId);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= ordered.Count)
        {
            return draft;
        }

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        return ApplySectionOrder(draft, ordered, orientation);
    }

    public static CustomScreenDefinition ReorderSection(
        CustomScreenDefinition draft,
        string draggedSectionId,
        string targetSectionId,
        string orientation,
        bool insertAfter = false)
    {
        var ordered = OrderedSections(draft, orientation);
        var dragged = ordered.FirstOrDefault(section => section.Id == draggedSectionId);
        if (dragged is null || draggedSectionId == targetSectionId)
        {
            return draft;
        }

        ordered.Remove(dragged);
        var target = ordered.FindIndex(section => section.Id == targetSectionId);
        if (target < 0)
        {
            return draft;
        }
        ordered.Insert(target + (insertAfter ? 1 : 0), dragged);
        return ApplySectionOrder(draft, ordered, orientation);
    }

    public static CustomScreenDefinition MoveButton(
        CustomScreenDefinition draft,
        string sectionId,
        string buttonId,
        int direction,
        string orientation)
    {
        var section = draft.Sections.First(candidate => candidate.Id == sectionId);
        var ordered = OrderedButtons(section, orientation);
        var index = ordered.FindIndex(button => button.Id == buttonId);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= ordered.Count)
        {
            return draft;
        }

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        return ApplyButtonOrder(draft, sectionId, ordered, orientation);
    }

    public static CustomScreenDefinition ReorderButton(
        CustomScreenDefinition draft,
        string draggedButtonId,
        string targetSectionId,
        string targetButtonId,
        bool insertAfter,
        int? targetVisualRow,
        string orientation)
    {
        var targetButton = draft.Sections
            .SelectMany(section => section.Buttons)
            .FirstOrDefault(button => button.Id == targetButtonId);
        if (targetButton is null)
        {
            return draft;
        }
        var targetRow = targetVisualRow ??
            ButtonOverride(targetButton, orientation).Row ??
            targetButton.Row;
        var baseRows = draft.Sections
            .SelectMany(section => section.Buttons)
            .ToDictionary(button => button.Id, button => button.Row, StringComparer.Ordinal);
        var next = CustomScreenLayoutEditing.ReorderButton(
            draft,
            draggedButtonId,
            targetSectionId,
            targetButtonId,
            insertAfter,
            targetRow);
        if (next is null || !draft.OrientationLayoutsEnabled)
        {
            return next ?? draft;
        }

        next = next with
        {
            Sections = [.. next.Sections.Select(section => section with
            {
                Buttons = [.. section.Buttons.Select(button =>
                    baseRows.TryGetValue(button.Id, out var baseRow)
                        ? button with { Row = baseRow }
                        : button)]
            })]
        };
        var sourceSectionId = draft.Sections.FirstOrDefault(section =>
            section.Buttons.Any(button => button.Id == draggedButtonId))?.Id;
        var targetSection = draft.Sections.First(section => section.Id == targetSectionId);
        var orderedTarget = OrderedButtons(targetSection, orientation);
        orderedTarget.RemoveAll(button => button.Id == draggedButtonId);
        var targetIndex = orderedTarget.FindIndex(button => button.Id == targetButtonId);
        var moved = next.Sections
            .First(section => section.Id == targetSectionId)
            .Buttons
            .First(button => button.Id == draggedButtonId);
        moved = SetOverride(
            moved,
            orientation,
            ButtonOverride(moved, orientation) with { Row = targetRow });
        orderedTarget.Insert(targetIndex + (insertAfter ? 1 : 0), moved);
        next = ApplyButtonOrder(next, targetSectionId, orderedTarget, orientation);

        if (sourceSectionId is not null && sourceSectionId != targetSectionId)
        {
            var source = next.Sections.First(section => section.Id == sourceSectionId);
            next = ApplyButtonOrder(
                next,
                sourceSectionId,
                OrderedButtons(source, orientation),
                orientation);
        }
        return next with
        {
            Sections = [.. next.Sections.Select(section =>
                section.Id != targetSectionId
                    ? section
                    : section with
                    {
                        Buttons = [.. section.Buttons.Select(button =>
                            button.Id != draggedButtonId
                                ? button
                                : SetOverride(
                                    button,
                                    orientation,
                                    ButtonOverride(button, orientation) with
                                    {
                                        Row = targetRow
                                    }))]
                    })]
        };
    }

    public static CustomScreenDefinition HideComponent(
        CustomScreenDefinition draft,
        string sectionId,
        string? buttonId,
        string orientation) =>
        SetComponentVisibility(
            draft,
            sectionId,
            buttonId,
            orientation,
            visible: false);

    public static CustomScreenSection SetRowLimit(
        CustomScreenSection section,
        int rowLimit) =>
        section with
        {
            RowLimit = rowLimit,
            Buttons =
            [
                .. section.Buttons.Select(button => button with
                {
                    Row = NormalizeRow(button.Row, rowLimit),
                    Portrait = NormalizeRow(button.Portrait, rowLimit),
                    Landscape = NormalizeRow(button.Landscape, rowLimit)
                })
            ]
        };

    public static CustomScreenDefinition SetComponentVisibility(
        CustomScreenDefinition draft,
        string sectionId,
        string? buttonId,
        string orientation,
        bool visible) =>
        draft with
        {
            Sections = [.. draft.Sections.Select(section =>
            {
                if (section.Id != sectionId)
                {
                    return section;
                }
                if (buttonId is null)
                {
                    var updatedSection = SetOverride(
                        section,
                        orientation,
                        SectionOverride(section, orientation) with
                        {
                            Visible = visible
                        });
                    return !visible
                        ? updatedSection
                        : updatedSection with
                        {
                            Buttons = [.. updatedSection.Buttons.Select(button =>
                                SetOverride(
                                    button,
                                    orientation,
                                    ButtonOverride(button, orientation) with
                                    {
                                        Visible = true
                                    }))]
                        };
                }
                return section with
                {
                    Buttons = [.. section.Buttons.Select(button =>
                        button.Id != buttonId
                            ? button
                            : SetOverride(
                                button,
                                orientation,
                                ButtonOverride(button, orientation) with
                                {
                                    Visible = visible
                                }))]
                };
            })]
        };

    public static CustomScreenDefinition ScopeNewSection(
        CustomScreenDefinition draft,
        string sectionId,
        string orientation)
    {
        if (!draft.OrientationLayoutsEnabled)
        {
            return draft;
        }

        return draft with
        {
            Sections = [.. draft.Sections.Select((section, index) =>
            {
                if (section.Id != sectionId)
                {
                    return section;
                }
                var current = Get(section, orientation) ??
                    SectionLayout(section, index);
                var otherOrientation = OtherOrientation(orientation);
                var other = Get(section, otherOrientation) ??
                    SectionLayout(section, index);
                return SetOverride(
                    SetOverride(section, orientation, current with { Visible = true }),
                    otherOrientation,
                    other with { Visible = false });
            })]
        };
    }

    public static CustomScreenDefinition ScopeNewButton(
        CustomScreenDefinition draft,
        string sectionId,
        string buttonId,
        string orientation)
    {
        if (!draft.OrientationLayoutsEnabled)
        {
            return draft;
        }

        return draft with
        {
            Sections = [.. draft.Sections.Select(section =>
                section.Id != sectionId
                    ? section
                    : section with
                    {
                        Buttons = [.. section.Buttons.Select((button, index) =>
                        {
                            if (button.Id != buttonId)
                            {
                                return button;
                            }
                            var current = Get(button, orientation) ??
                                ButtonLayout(button, index);
                            var otherOrientation = OtherOrientation(orientation);
                            var other = Get(button, otherOrientation) ??
                                ButtonLayout(button, index);
                            return SetOverride(
                                SetOverride(
                                    button,
                                    orientation,
                                    current with { Visible = true }),
                                otherOrientation,
                                other with { Visible = false });
                        })]
                    })]
        };
    }

    public static CustomScreenLayoutOverride SectionOverride(
        CustomScreenSection section,
        string orientation) =>
        Get(section, orientation) ?? SectionLayout(section, 0);

    public static CustomScreenLayoutOverride ButtonOverride(
        CustomScreenButton button,
        string orientation) =>
        Get(button, orientation) ?? ButtonLayout(button, 0);

    public static CustomScreenSection SetOverride(
        CustomScreenSection section,
        string orientation,
        CustomScreenLayoutOverride value) =>
        orientation == "landscape"
            ? section with { Landscape = value }
            : section with { Portrait = value };

    public static CustomScreenButton SetOverride(
        CustomScreenButton button,
        string orientation,
        CustomScreenLayoutOverride value) =>
        orientation == "landscape"
            ? button with { Landscape = value }
            : button with { Portrait = value };

    private static List<CustomScreenSection> OrderedSections(
        CustomScreenDefinition draft,
        string orientation) =>
        [.. draft.Sections
            .Select((section, index) => (section, index))
            .OrderBy(item => Get(item.section, orientation)?.Order ?? item.index)
            .Select(item => item.section)];

    private static List<CustomScreenButton> OrderedButtons(
        CustomScreenSection section,
        string orientation) =>
        [.. section.Buttons
            .Select((button, index) => (button, index))
            .OrderBy(item => Get(item.button, orientation)?.Order ?? item.index)
            .Select(item => item.button)];

    private static CustomScreenDefinition ApplySectionOrder(
        CustomScreenDefinition draft,
        IReadOnlyList<CustomScreenSection> ordered,
        string orientation)
    {
        var orders = ordered.Select((section, index) => (section.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        return draft with
        {
            Sections = [.. draft.Sections.Select(section =>
            {
                var layout = SectionOverride(section, orientation) with
                {
                    Order = orders[section.Id]
                };
                return SetOverride(section, orientation, layout);
            })]
        };
    }

    private static CustomScreenDefinition ApplyButtonOrder(
        CustomScreenDefinition draft,
        string sectionId,
        IReadOnlyList<CustomScreenButton> ordered,
        string orientation)
    {
        var orders = ordered.Select((button, index) => (button.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        return draft with
        {
            Sections = [.. draft.Sections.Select(section =>
                section.Id != sectionId
                    ? section
                    : section with
                    {
                        Buttons = [.. section.Buttons.Select(button =>
                        {
                            var layout = ButtonOverride(button, orientation) with
                            {
                                Order = orders[button.Id]
                            };
                            return SetOverride(button, orientation, layout);
                        })]
                    })]
        };
    }

    private static CustomScreenLayoutOverride SectionLayout(
        CustomScreenSection section,
        int order) =>
        new(order, true, WidthColumns: section.WidthColumns);

    private static CustomScreenLayoutOverride ButtonLayout(
        CustomScreenButton button,
        int order) =>
        new(order, true, Size: button.Size, Row: button.Row);

    private static int NormalizeRow(int row, int rowLimit) =>
        row > rowLimit ? 0 : row;

    private static CustomScreenLayoutOverride? NormalizeRow(
        CustomScreenLayoutOverride? layout,
        int rowLimit) =>
        layout is not null && layout.Row > rowLimit
            ? layout with { Row = 0 }
            : layout;

    private static string OtherOrientation(string orientation) =>
        orientation == "landscape" ? "portrait" : "landscape";

    private static CustomScreenLayoutOverride? Get(
        CustomScreenSection section,
        string orientation) =>
        orientation == "landscape" ? section.Landscape : section.Portrait;

    private static CustomScreenLayoutOverride? Get(
        CustomScreenButton button,
        string orientation) =>
        orientation == "landscape" ? button.Landscape : button.Portrait;
}
