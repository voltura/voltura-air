using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Panel = System.Windows.Controls.Panel;
using Path = System.Windows.Shapes.Path;
using RotateTransform = System.Windows.Media.RotateTransform;
using Stretch = System.Windows.Media.Stretch;
using VisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenPreviewController(
    CustomScreenSectionPanel previewSections,
    FrameworkElement previewWorkspace,
    FrameworkElement dragSurface,
    Func<string, Brush> brush,
    Action<string, string?, int?> select,
    Action<CustomScreenDefinition, string, string?, int?> applyDraft,
    Action<string, string?> deleteComponent,
    Action<string, string?> deleteComponentEverywhere)
{
    private CustomScreenDefinition? _draft;
    private string? _selectedSectionId;
    private string? _selectedButtonId;
    private int? _selectedRow;
    private string _orientation = "portrait";
    private bool _surfaceAttached;
    private readonly CustomScreenPreviewDragController _drag =
        new(previewSections, previewWorkspace, dragSurface, brush, applyDraft);
    public void Render(
        CustomScreenDefinition? draft,
        string? selectedSectionId,
        string? selectedButtonId,
        int? selectedRow,
        string orientation)
    {
        if (!_surfaceAttached)
        {
            _drag.AttachSurface();
            previewWorkspace.SizeChanged += (_, _) =>
            {
                previewSections.MinHeight = Math.Max(
                    0,
                    previewWorkspace.ActualHeight);
            };
            _surfaceAttached = true;
        }
        previewSections.MinHeight = Math.Max(0, previewWorkspace.ActualHeight);
        _draft = draft;
        _selectedSectionId = selectedSectionId;
        _selectedButtonId = selectedButtonId;
        _selectedRow = selectedRow;
        _orientation = orientation;
        _drag.SetContext(draft, selectedButtonId, orientation);
        previewSections.Children.Clear();
        if (draft is null)
        {
            return;
        }

        foreach (var section in draft.Sections
            .OrderBy(section => GetOverride(section, orientation)?.Order ??
                IndexOf(draft.Sections, section)))
        {
            var sectionOverride = GetOverride(section, orientation);
            if (sectionOverride?.Visible == false)
            {
                continue;
            }

            var widthColumns = sectionOverride?.WidthColumns ?? section.WidthColumns;
            var sectionPanel = new Grid();
            var collapsible = CustomScreenSectionKinds.IsCollapsible(section.Kind);
            var expanded = !collapsible || section.InitiallyExpanded;
            var contentRow = 0;
            if (collapsible)
            {
                var header = new DockPanel
                {
                    LastChildFill = true
                };
                var chevron = new Path
                {
                    Width = 12,
                    Height = 7,
                    Margin = new Thickness(10, 0, 2, 0),
                    Data = System.Windows.Media.Geometry.Parse(
                        "M 1 1 L 6 6 L 11 1"),
                    Stroke = brush("MutedTextBrush"),
                    StrokeThickness = 1.8,
                    StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                    StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
                    Stretch = Stretch.None,
                    VerticalAlignment = VerticalAlignment.Center,
                    RenderTransform = section.InitiallyExpanded
                        ? null
                        : new RotateTransform(-90, 6, 3.5)
                };
                DockPanel.SetDock(chevron, Dock.Right);
                header.Children.Add(chevron);
                header.Children.Add(new TextBlock
                {
                    Text = section.Name,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = brush("TextBrush")
                });
                var toggle = new Button
                {
                    Content = header,
                    Margin = section.InitiallyExpanded
                        ? new Thickness(0, 0, 0, 8)
                        : new Thickness(0)
                };
                toggle.SetResourceReference(
                    FrameworkElement.StyleProperty,
                    "CustomScreenCollapsibleHeaderButtonStyle");
                AutomationProperties.SetName(
                    toggle,
                    section.InitiallyExpanded
                        ? $"Collapse panel {section.Name}"
                        : $"Expand panel {section.Name}");
                AutomationProperties.SetHelpText(
                    toggle,
                    "This preview state becomes the device default after Save.");
                toggle.Click += (_, _) =>
                {
                    if (_draft is null)
                    {
                        return;
                    }

                    applyDraft(
                        _draft with
                        {
                            Sections =
                            [
                                .. _draft.Sections.Select(candidate =>
                                    candidate.Id == section.Id
                                        ? candidate with
                                        {
                                            InitiallyExpanded =
                                                !section.InitiallyExpanded
                                        }
                                        : candidate)
                            ]
                        },
                        section.Id,
                        null,
                        null);
                };
                sectionPanel.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });
                sectionPanel.Children.Add(toggle);
                contentRow = 1;
            }
            else if (section.ShowHeader)
            {
                sectionPanel.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });
                var header = new TextBlock
                {
                    Text = section.Name,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8),
                    Foreground = brush("TextBrush")
                };
                sectionPanel.Children.Add(header);
                contentRow = 1;
            }

            if (expanded)
            {
                sectionPanel.RowDefinitions.Add(new RowDefinition
                {
                    Height = section.HeightMode == "fill"
                        ? new GridLength(1, GridUnitType.Star)
                        : GridLength.Auto
                });
                var content = CustomScreenSectionKinds.IsTrackpad(section.Kind)
                    ? CustomScreenTrackpadPreviewFactory.Create(section, brush)
                    : CustomScreenSectionKinds.IsVolume(section.Kind)
                        ? CustomScreenVolumePreviewFactory.Create(brush)
                    : CreateButtonPanel(section, orientation);
                Grid.SetRow(content, contentRow);
                sectionPanel.Children.Add(content);
            }
            var card = CreateSectionCard(section, sectionPanel);
            CustomScreenSectionPanel.SetWidthColumns(card, widthColumns);
            CustomScreenSectionPanel.SetHeightMode(
                card,
                expanded ? section.HeightMode : "content");
            CustomScreenSectionPanel.SetFillWeight(card, section.FillWeight);
            previewSections.Children.Add(card);
        }
    }

    private Panel CreateButtonPanel(CustomScreenSection section, string orientation)
    {
        var visibleButtons = section.Buttons
            .OrderBy(button => GetOverride(button, orientation)?.Order ??
                IndexOf(section.Buttons, button))
            .Where(button => GetOverride(button, orientation)?.Visible != false)
            .ToArray();
        if (section.RowLimit == 0)
        {
            var automatic = new CustomScreenButtonFlowPanel
            {
                ButtonAlignment = section.ButtonAlignment
            };
            foreach (var button in visibleButtons)
            {
                automatic.Children.Add(CreateButton(
                    section,
                    button,
                    null,
                    GetOverride(button, orientation)?.Size ?? button.Size));
            }
            return automatic;
        }

        var rows = new Grid();
        var rowPanels = new CustomScreenButtonFlowPanel[section.RowLimit];
        for (var row = 0; row < section.RowLimit; row++)
        {
            var rowNumber = row + 1;
            rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var rowPanel = new CustomScreenButtonFlowPanel
            {
                AllowDrop = true,
                Background = Brushes.Transparent,
                MinHeight = 58,
                Tag = rowNumber,
                ButtonAlignment = section.ButtonAlignment
            };
            _drag.AttachRow(rowPanel, section.Id, rowNumber);

            var rowContent = new DockPanel();
            var rowLabel = new TextBlock
            {
                Text = $"Row {rowNumber}",
                Width = 44,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = brush("MutedTextBrush"),
                FontSize = 11
            };
            DockPanel.SetDock(rowLabel, Dock.Left);
            rowContent.Children.Add(rowLabel);
            rowContent.Children.Add(rowPanel);

            var selected = section.Id == _selectedSectionId && _selectedRow == rowNumber;
            var rowTarget = new Border
            {
                AllowDrop = true,
                Background = selected ? brush("SurfaceRaisedBrush") : Brushes.Transparent,
                BorderBrush = brush(selected ? "AccentBrush" : "BorderBrush"),
                BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 4, 2, 0),
                Margin = new Thickness(0, 0, 0, row + 1 < section.RowLimit ? 6 : 0),
                Child = rowContent,
                Tag = rowNumber
            };
            AutomationProperties.SetName(rowTarget, $"Button row {rowNumber}");
            _drag.AttachRow(rowTarget, section.Id, rowNumber);
            rowTarget.PreviewMouseLeftButtonDown += (_, eventArgs) =>
            {
                if (eventArgs.OriginalSource is DependencyObject source &&
                    FindAncestor<Button>(source) is not null)
                {
                    return;
                }
                select(section.Id, null, rowNumber);
                eventArgs.Handled = true;
            };
            Grid.SetRow(rowTarget, row);
            rows.Children.Add(rowTarget);
            rowPanels[row] = rowPanel;
        }

        var automaticIndex = 0;
        foreach (var button in visibleButtons)
        {
            var effectiveRow = GetOverride(button, orientation)?.Row ?? button.Row;
            var rowIndex = effectiveRow > 0
                ? Math.Min(effectiveRow, section.RowLimit) - 1
                : automaticIndex++ % section.RowLimit;
            rowPanels[rowIndex].Children.Add(CreateButton(
                section,
                button,
                rowIndex + 1,
                GetOverride(button, orientation)?.Size ?? button.Size));
        }
        return rows;
    }

    private Button CreateButton(
        CustomScreenSection section,
        CustomScreenButton button,
        int? visualRow,
        string size)
    {
        var selected = button.Id == _selectedButtonId;
        var control = new Button
        {
            Content = ButtonPreviewContent(button),
            MinHeight = 52,
            MinWidth = size switch
            {
                "compact" => 72,
                "wide" => 150,
                "fill" => 220,
                _ => 104
            },
            Margin = new Thickness(0, 0, 8, 8),
            Tag = button.Id,
            AllowDrop = true,
            BorderBrush = brush(selected ? "AccentBrush" : "BorderBrush"),
            BorderThickness = new Thickness(selected ? 2 : 1)
        };
        AutomationProperties.SetName(control, $"Select button {button.Name}");
        control.Click += (_, _) => select(section.Id, button.Id, visualRow);
        CustomScreenComponentContextMenu.Attach(
            control,
            $"button {button.Name}",
            ComponentActionLabel(),
            () => deleteComponent(section.Id, button.Id),
            _draft?.OrientationLayoutsEnabled == true
                ? () => deleteComponentEverywhere(section.Id, button.Id)
                : null);
        _drag.AttachButton(control, section.Id, button.Id, visualRow);
        return control;
    }
    private Border CreateSectionCard(
        CustomScreenSection section,
        UIElement sectionPanel)
    {
        var selected = section.Id == _selectedSectionId;
        var card = new Border
        {
            MinHeight = CustomScreenSectionKinds.IsCollapsible(section.Kind) &&
                !section.InitiallyExpanded
                ? 0
                : section.HeightMode == "fill" ? 150 : 84,
            Background = brush("SurfaceBrush"),
            BorderBrush = brush(selected ? "AccentBrush" : "BorderBrush"),
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = sectionPanel,
            Tag = section.Id,
            AllowDrop = true
        };
        CustomScreenComponentContextMenu.Attach(
            card,
            $"panel {section.Name}",
            ComponentActionLabel(),
            () => deleteComponent(section.Id, null),
            _draft?.OrientationLayoutsEnabled == true
                ? () => deleteComponentEverywhere(section.Id, null)
                : null);
        _drag.AttachSection(card, section.Id);
        card.MouseLeftButtonDown += (_, _) => select(section.Id, null, null);
        return card;
    }

    private string ComponentActionLabel() =>
        _draft?.OrientationLayoutsEnabled == true
            ? $"Hide in {OrientationTitle(_orientation)}"
            : "Delete";

    private static string OrientationTitle(string orientation) =>
        orientation == "landscape" ? "Landscape" : "Portrait";

    private static CustomScreenLayoutOverride? GetOverride(
        CustomScreenSection section,
        string orientation) =>
        orientation == "landscape" ? section.Landscape : section.Portrait;

    private static CustomScreenLayoutOverride? GetOverride(
        CustomScreenButton button,
        string orientation) =>
        orientation == "landscape" ? button.Landscape : button.Portrait;

    private static string ButtonPreviewContent(CustomScreenButton button) =>
        button.Presentation switch
        {
            "icon" => $"[{button.Icon}]",
            "label" => button.Label,
            _ => $"[{button.Icon}]  {button.Label}"
        };

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

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

}
