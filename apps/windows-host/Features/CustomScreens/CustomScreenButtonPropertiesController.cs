using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Brush = System.Windows.Media.Brush;
using ComboBox = System.Windows.Controls.ComboBox;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenButtonPropertiesController(
    CustomScreenPropertyGroupPresenter groups,
    CustomScreenService service,
    Func<string, Brush> brush,
    Action<CustomScreenButton> updateButton,
    Action<int> moveButton,
    Action<string, string> moveButtonToSection,
    Action<string, string?> deleteComponent,
    Action<string, string?> deleteComponentEverywhere)
    : CustomScreenPropertyFields(brush)
{
    private readonly CustomScreenActionPropertiesController _actionProperties =
        new(service, brush, updateButton);

    public void Render(
        CustomScreenDefinition draft,
        CustomScreenSection section,
        CustomScreenButton button,
        string orientation,
        TextBlock hint)
    {
        hint.Text = $"Button · {button.Name}";
        groups.BeginComponent(button.Id);
        groups.Add(
            "name",
            "Name",
            initiallyExpanded: IsGeneratedButtonText(button.Name),
            target => AddTextProperty(
                target,
                "Name",
                button.Name,
                value => updateButton(button with { Name = value }),
                CustomScreenLimits.MaxButtonNameLength,
                showLabel: false));
        groups.Add(
            "label",
            "Label",
            initiallyExpanded: IsGeneratedButtonText(button.Label),
            target => AddTextProperty(
                target,
                "Label",
                button.Label,
                value => updateButton(button with { Label = value }),
                CustomScreenLimits.MaxButtonLabelLength,
                allowEmpty: true,
                showLabel: false));
        groups.Add(
            "visual",
            "Visual",
            initiallyExpanded: false,
            target => AddChoiceProperty(
                target,
                "Visual",
                CustomScreenService.RequiresLabelOnlyPresentation(button.Action)
                    ? ["label"]
                    : ["iconLabel", "icon", "label"],
                button.Presentation,
                value => updateButton(button with { Presentation = value }),
                showLabel: false));
        groups.Add(
            "size",
            "Size",
            initiallyExpanded: false,
            target => AddChoiceProperty(
                target,
                "Size",
                ["compact", "standard", "wide", "fill"],
                draft.OrientationLayoutsEnabled
                    ? CustomScreenOrientationEditing.ButtonOverride(
                        button,
                        orientation).Size ?? button.Size
                    : button.Size,
                value => updateButton(draft.OrientationLayoutsEnabled
                    ? CustomScreenOrientationEditing.SetOverride(
                        button,
                        orientation,
                        CustomScreenOrientationEditing.ButtonOverride(
                            button,
                            orientation) with
                        {
                            Size = value
                        })
                    : button with { Size = value }),
                showLabel: false));
        if (draft.OrientationLayoutsEnabled)
        {
            AddVisibilityGroup(button);
        }
        AddLayoutGroup(draft, section, button, orientation);
        groups.Add(
            "action",
            "Action",
            initiallyExpanded: true,
            target =>
            {
                _actionProperties.Render(button, target);
                AddBooleanProperty(
                    target,
                    "Repeat while held",
                    button.Repeat,
                    value => updateButton(button with { Repeat = value }),
                    enabled: CustomScreenService.IsRepeatable(button.Action));
            });
        groups.Add(
            "panel",
            "Panel",
            initiallyExpanded: false,
            target => AddPanelProperty(target, draft, section, button));

        var buttonIndex = draft.OrientationLayoutsEnabled
            ? section.Buttons
                .OrderBy(candidate =>
                    CustomScreenOrientationEditing.ButtonOverride(candidate, orientation).Order)
                .ToList()
                .FindIndex(candidate => candidate.Id == button.Id)
            : IndexOf(section.Buttons, button);
        groups.Add(
            "component",
            "Component",
            initiallyExpanded: false,
            target => AddComponentActions(
                target,
                () => moveButton(-1),
                () => moveButton(1),
                () => deleteComponent(section.Id, button.Id),
                buttonIndex > 0,
                buttonIndex < section.Buttons.Count - 1,
                draft.OrientationLayoutsEnabled
                    ? $"Hide button {button.Name} in {OrientationTitle(orientation)}"
                    : $"Delete button {button.Name}",
                draft.OrientationLayoutsEnabled
                    ? $"Hide in {OrientationTitle(orientation)}"
                    : "Delete",
                draft.OrientationLayoutsEnabled
                    ? () => deleteComponentEverywhere(section.Id, button.Id)
                    : null,
                $"Delete button {button.Name} everywhere"));
    }

    private void AddLayoutGroup(
        CustomScreenDefinition draft,
        CustomScreenSection section,
        CustomScreenButton button,
        string orientation)
    {
        if (section.RowLimit <= 0)
        {
            return;
        }

        groups.Add(
            "layout",
            "Layout",
            initiallyExpanded: false,
            target =>
            {
                var selectedRow = draft.OrientationLayoutsEnabled
                    ? CustomScreenOrientationEditing.ButtonOverride(
                        button,
                        orientation).Row ?? button.Row
                    : button.Row;
                AddTaggedChoiceProperty(
                    target,
                    "Row",
                    [
                        ("Auto", "0"),
                        .. Enumerable.Range(1, section.RowLimit)
                            .Select(row => ($"Row {row}", row.ToString(
                                CultureInfo.InvariantCulture)))
                    ],
                    selectedRow.ToString(CultureInfo.InvariantCulture),
                    value =>
                    {
                        var row = int.Parse(value, CultureInfo.InvariantCulture);
                        updateButton(draft.OrientationLayoutsEnabled
                            ? CustomScreenOrientationEditing.SetOverride(
                                button,
                                orientation,
                                CustomScreenOrientationEditing.ButtonOverride(
                                    button,
                                    orientation) with
                                {
                                    Row = row
                                })
                            : button with { Row = row });
                    });
            });
    }

    private void AddVisibilityGroup(CustomScreenButton button)
    {
        groups.Add(
            "visibility",
            "Visibility",
            initiallyExpanded: false,
            target =>
            {
                AddBooleanProperty(
                    target,
                    "Show in Portrait",
                    CustomScreenOrientationEditing.ButtonOverride(
                        button,
                        "portrait").Visible,
                    value => updateButton(CustomScreenOrientationEditing.SetOverride(
                        button,
                        "portrait",
                        CustomScreenOrientationEditing.ButtonOverride(
                            button,
                            "portrait") with
                        {
                            Visible = value
                        })));
                AddBooleanProperty(
                    target,
                    "Show in Landscape",
                    CustomScreenOrientationEditing.ButtonOverride(
                        button,
                        "landscape").Visible,
                    value => updateButton(CustomScreenOrientationEditing.SetOverride(
                        button,
                        "landscape",
                        CustomScreenOrientationEditing.ButtonOverride(
                            button,
                            "landscape") with
                        {
                            Visible = value
                        })));
            });
    }

    private void AddPanelProperty(
        StackPanel target,
        CustomScreenDefinition draft,
        CustomScreenSection selectedSection,
        CustomScreenButton button)
    {
        var targetSections = draft.Sections
            .Where(section => CustomScreenSectionKinds.AllowsButtons(section.Kind))
            .ToArray();
        var combo = new ComboBox
        {
            IsEnabled = targetSections.Length > 1
        };
        AutomationProperties.SetName(combo, "Panel");
        combo.SetResourceReference(FrameworkElement.StyleProperty, "ModernComboBoxStyle");
        foreach (var section in targetSections)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = section.Name,
                Tag = section.Id,
                IsSelected = section.Id == selectedSection.Id
            });
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string targetSectionId } &&
                targetSectionId != selectedSection.Id)
            {
                moveButtonToSection(button.Id, targetSectionId);
            }
        };
        target.Children.Add(combo);
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

    private static bool IsGeneratedButtonText(string value) =>
        string.Equals(value, "New button", StringComparison.Ordinal);
}
