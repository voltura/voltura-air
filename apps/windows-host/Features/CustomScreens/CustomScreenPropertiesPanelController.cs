using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Brush = System.Windows.Media.Brush;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenPropertiesPanelController(
    StackPanel panel,
    TextBlock hint,
    CustomScreenService service,
    Func<string, Brush> brush,
    Action<CustomScreenSection> updateSection,
    Action<CustomScreenButton> updateButton,
    Action<int> moveSection,
    Action<int> moveButton,
    Action<string, string> moveButtonToSection,
    Action<string, string?> deleteComponent,
    Action<string, string?> deleteComponentEverywhere)
    : CustomScreenPropertyFields(brush)
{
    private readonly CustomScreenPropertyGroupPresenter _groups = new(panel);
    private readonly CustomScreenButtonPropertiesController _buttonProperties =
        new(
            new CustomScreenPropertyGroupPresenter(panel),
            service,
            brush,
            updateButton,
            moveButton,
            moveButtonToSection,
            deleteComponent,
            deleteComponentEverywhere);

    public void SetAllExpanded(bool expanded) =>
        CustomScreenPropertyGroupPresenter.SetAllExpanded(panel, expanded);

    public void Render(
        CustomScreenDefinition? draft,
        string? selectedSectionId,
        string? selectedButtonId,
        int? selectedRow,
        string orientation)
    {
        while (panel.Children.Count > 2)
        {
            panel.Children.RemoveAt(panel.Children.Count - 1);
        }

        if (draft is null || selectedSectionId is null)
        {
            hint.Text = "Select a panel or button on the preview.";
            return;
        }

        var section = draft.Sections.First(item => item.Id == selectedSectionId);
        var button = section.Buttons.FirstOrDefault(item => item.Id == selectedButtonId);
        if (button is not null)
        {
            _buttonProperties.Render(draft, section, button, orientation, hint);
            return;
        }

        RenderSection(draft, section, selectedRow, orientation);
    }

    private void RenderSection(
        CustomScreenDefinition draft,
        CustomScreenSection section,
        int? selectedRow,
        string orientation)
    {
        hint.Text = selectedRow is > 0
            ? $"Panel · {section.Name} · Row {selectedRow} target"
            : $"Panel · {section.Name}";
        _groups.BeginComponent(section.Id);
        _groups.Add(
            "name",
            "Name",
            initiallyExpanded: IsGeneratedSectionName(section.Name),
            target => AddTextProperty(
                target,
                "Name",
                section.Name,
                value => updateSection(section with { Name = value }),
                CustomScreenLimits.MaxSectionNameLength,
                showLabel: false));
        _groups.Add(
            "header",
            "Header",
            initiallyExpanded: false,
            target =>
            {
                if (CustomScreenSectionKinds.IsCollapsible(section.Kind))
                {
                    AddBooleanProperty(
                        target,
                        "Expanded by default",
                        section.InitiallyExpanded,
                        value => updateSection(section with { InitiallyExpanded = value }));
                }
                else
                {
                    AddBooleanProperty(
                        target,
                        "Show header",
                        section.ShowHeader,
                        value => updateSection(section with { ShowHeader = value }));
                }
            });
        if (draft.OrientationLayoutsEnabled)
        {
            AddSectionVisibilityGroup(section);
        }
        AddSectionLayoutGroup(draft, section, orientation);
        if (CustomScreenSectionKinds.IsTrackpad(section.Kind))
        {
            AddTrackpadGroup(section);
        }
        else if (CustomScreenSectionKinds.AllowsButtons(section.Kind))
        {
            AddButtonGridGroup(section);
        }

        var sectionIndex = draft.OrientationLayoutsEnabled
            ? draft.Sections
                .OrderBy(candidate =>
                    CustomScreenOrientationEditing.SectionOverride(candidate, orientation).Order)
                .ToList()
                .FindIndex(candidate => candidate.Id == section.Id)
            : IndexOf(draft.Sections, section);
        _groups.Add(
            "component",
            "Component",
            initiallyExpanded: false,
            target => AddComponentActions(
                target,
                () => moveSection(-1),
                () => moveSection(1),
                () => deleteComponent(section.Id, null),
                sectionIndex > 0,
                sectionIndex < draft.Sections.Count - 1,
                draft.OrientationLayoutsEnabled
                    ? $"Hide panel {section.Name} in {OrientationTitle(orientation)}"
                    : $"Delete panel {section.Name}",
                draft.OrientationLayoutsEnabled
                    ? $"Hide in {OrientationTitle(orientation)}"
                    : "Delete",
                draft.OrientationLayoutsEnabled
                    ? () => deleteComponentEverywhere(section.Id, null)
                    : null,
                $"Delete panel {section.Name} everywhere"));
    }

    private void AddSectionLayoutGroup(
        CustomScreenDefinition draft,
        CustomScreenSection section,
        string orientation)
    {
        _groups.Add(
            "layout",
            "Layout",
            initiallyExpanded: true,
            target =>
            {
                var widthChoices = SectionWidthChoices(section.Kind);
                AddTaggedChoiceProperty(
                    target,
                    "Width",
                    widthChoices,
                    section.WidthColumns.ToString(CultureInfo.InvariantCulture),
                    value => updateSection(section with
                    {
                        WidthColumns = int.Parse(value, CultureInfo.InvariantCulture)
                    }));
                if (draft.OrientationLayoutsEnabled)
                {
                    AddSectionOrientationProperties(target, section, orientation);
                }
                if (CustomScreenSectionKinds.IsVolume(section.Kind))
                {
                    return;
                }
                AddChoiceProperty(
                    target,
                    "Height",
                    ["content", "fill"],
                    section.HeightMode,
                    value => updateSection(section with { HeightMode = value }));
                if (section.HeightMode == "fill")
                {
                    AddTaggedChoiceProperty(
                        target,
                        "Fill weight",
                        [("1", "1"), ("2", "2"), ("3", "3"), ("4", "4")],
                        section.FillWeight.ToString(CultureInfo.InvariantCulture),
                        value => updateSection(section with
                        {
                            FillWeight = int.Parse(value, CultureInfo.InvariantCulture)
                        }));
                }
            });
    }

    private void AddTrackpadGroup(CustomScreenSection section)
    {
        _groups.Add(
            "trackpad",
            "Trackpad",
            initiallyExpanded: false,
            target =>
            {
                AddBooleanProperty(
                    target,
                    "Show Left click",
                    section.TrackpadLeftClick,
                    value => updateSection(section with { TrackpadLeftClick = value }));
                AddBooleanProperty(
                    target,
                    "Show Right click",
                    section.TrackpadRightClick,
                    value => updateSection(section with { TrackpadRightClick = value }));
                AddBooleanProperty(
                    target,
                    "Show fullscreen control",
                    section.TrackpadFullscreenControl,
                    value => updateSection(section with
                    {
                        TrackpadFullscreenControl = value
                    }));
                AddTaggedChoiceProperty(
                    target,
                    "Click-button order",
                    [("Left then Right", "right"), ("Right then Left", "left")],
                    section.TrackpadButtonSide,
                    value => updateSection(section with { TrackpadButtonSide = value }));
            });
    }

    private void AddButtonGridGroup(CustomScreenSection section)
    {
        _groups.Add(
            "buttons",
            "Buttons",
            initiallyExpanded: false,
            target =>
            {
                AddTaggedChoiceProperty(
                    target,
                    "Button placement",
                    [
                        ("Start", "start"),
                        ("Center", "center"),
                        ("End", "end"),
                        ("Space between", "space-between"),
                        ("Space around", "space-around"),
                        ("Space evenly", "space-evenly")
                    ],
                    section.ButtonAlignment,
                    value => updateSection(section with { ButtonAlignment = value }));
                AddTaggedChoiceProperty(
                    target,
                    "Button rows",
                    [
                        ("Automatic", "0"),
                        ("1 row", "1"),
                        ("2 rows", "2"),
                        ("3 rows", "3")
                    ],
                    section.RowLimit.ToString(CultureInfo.InvariantCulture),
                    value =>
                    {
                        var rowLimit = int.Parse(value, CultureInfo.InvariantCulture);
                        updateSection(
                            CustomScreenOrientationEditing.SetRowLimit(
                                section,
                                rowLimit));
                    });
            });
    }

    private void AddSectionOrientationProperties(
        StackPanel target,
        CustomScreenSection section,
        string orientation)
    {
        var title = char.ToUpperInvariant(orientation[0]) + orientation[1..];
        var layout = CustomScreenOrientationEditing.SectionOverride(section, orientation);
        var widthChoices = SectionWidthChoices(section.Kind);
        AddTaggedChoiceProperty(
            target,
            $"{title} width",
            widthChoices,
            (layout.WidthColumns ?? section.WidthColumns).ToString(
                CultureInfo.InvariantCulture),
            value => updateSection(CustomScreenOrientationEditing.SetOverride(
                section,
                orientation,
                layout with
                {
                    WidthColumns = int.Parse(value, CultureInfo.InvariantCulture)
                })));
    }

    private static (string Label, string Value)[] SectionWidthChoices(
        string kind) =>
        CustomScreenSectionKinds.IsVolume(kind)
            ? [("25%", "3"), ("50%", "6"), ("75%", "9"), ("100%", "12")]
            : CustomScreenSectionKinds.IsNavigationRing(kind)
                ? [("50%", "6"), ("67%", "8"), ("75%", "9"), ("100%", "12")]
                :
                [
                    ("25%", "3"),
                    ("33%", "4"),
                    ("50%", "6"),
                    ("67%", "8"),
                    ("75%", "9"),
                    ("100%", "12")
                ];

    private void AddSectionVisibilityGroup(CustomScreenSection section)
    {
        _groups.Add(
            "visibility",
            "Visibility",
            initiallyExpanded: false,
            target =>
            {
                AddBooleanProperty(
                    target,
                    "Show in Portrait",
                    CustomScreenOrientationEditing.SectionOverride(
                        section,
                        "portrait").Visible,
                    value => updateSection(CustomScreenOrientationEditing.SetOverride(
                        section,
                        "portrait",
                        CustomScreenOrientationEditing.SectionOverride(
                            section,
                            "portrait") with
                        {
                            Visible = value
                        })));
                AddBooleanProperty(
                    target,
                    "Show in Landscape",
                    CustomScreenOrientationEditing.SectionOverride(
                        section,
                        "landscape").Visible,
                    value => updateSection(CustomScreenOrientationEditing.SetOverride(
                        section,
                        "landscape",
                        CustomScreenOrientationEditing.SectionOverride(
                            section,
                            "landscape") with
                        {
                            Visible = value
                        })));
            });
    }

    private static int IndexOf<T>(IReadOnlyList<T> items, T value)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(items[index], value))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static string OrientationTitle(string orientation) =>
        orientation == "landscape" ? "Landscape" : "Portrait";

    private static bool IsGeneratedSectionName(string name) =>
        name is "New panel" or "Collapsible panel" or "Trackpad" or
            "Collapsible trackpad" or "Volume slider";
}
