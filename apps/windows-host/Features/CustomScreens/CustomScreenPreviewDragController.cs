using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Brush = System.Windows.Media.Brush;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using GiveFeedbackEventHandler = System.Windows.GiveFeedbackEventHandler;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using QueryContinueDragEventHandler = System.Windows.QueryContinueDragEventHandler;
using VisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenPreviewDragController(
    Panel previewSections,
    FrameworkElement previewWorkspace,
    FrameworkElement dragSurface,
    Func<string, Brush> brush,
    Action<CustomScreenDefinition, string, string?, int?> applyDraft)
{
    private CustomScreenDefinition? _draft;
    private string? _selectedButtonId;
    private string _orientation = "portrait";
    private Point _dragStart;
    private Point _dragAnchor;
    private CustomScreenDragItem? _dragItem;
    private FrameworkElement? _dragSource;
    private readonly CustomScreenDropIndicatorController _dropIndicator =
        new(brush);

    public void AttachSurface()
    {
        previewWorkspace.AllowDrop = true;
        previewWorkspace.DragOver += OnSurfaceDragOver;
        previewWorkspace.DragLeave += (_, _) => _dropIndicator.Clear();
        previewWorkspace.Drop += DropOnSurface;
    }

    public void SetContext(
        CustomScreenDefinition? draft,
        string? selectedButtonId,
        string orientation)
    {
        _draft = draft;
        _selectedButtonId = selectedButtonId;
        _orientation = orientation;
    }

    public void AttachButton(
        Button control,
        string sectionId,
        string buttonId,
        int? visualRow)
    {
        control.PreviewMouseLeftButtonDown += (_, eventArgs) =>
            PrepareDrag(
                new CustomScreenDragItem("button", buttonId),
                control,
                eventArgs);
        control.PreviewMouseMove += OnDragMouseMove;
        control.DragOver += OnButtonDragOver;
        control.DragLeave += (_, _) => _dropIndicator.Clear();
        control.Drop += (_, eventArgs) =>
            DropButton(
                eventArgs,
                sectionId,
                buttonId,
                eventArgs.GetPosition(control).X >= control.ActualWidth / 2,
                visualRow);
    }

    public void AttachSection(Border card, string sectionId)
    {
        card.PreviewMouseLeftButtonDown += (_, eventArgs) =>
        {
            if (OriginatesInsideInteractiveControl(card, eventArgs))
            {
                return;
            }

            PrepareDrag(
                new CustomScreenDragItem("section", sectionId),
                card,
                eventArgs);
        };
        card.PreviewMouseMove += OnDragMouseMove;
        card.DragOver += OnSectionDragOver;
        card.DragLeave += (_, _) => _dropIndicator.Clear();
        card.Drop += (_, eventArgs) =>
            DropOnSection(
                eventArgs,
                sectionId,
                eventArgs.GetPosition(card).X >= card.ActualWidth / 2);
    }

    public void AttachRow(FrameworkElement target, string sectionId, int row)
    {
        target.DragOver += OnRowDragOver;
        target.DragLeave += (_, _) => _dropIndicator.Clear();
        target.Drop += (_, eventArgs) => DropOnButtonRow(eventArgs, sectionId, row);
    }

    private void PrepareDrag(
        CustomScreenDragItem item,
        FrameworkElement source,
        MouseButtonEventArgs eventArgs)
    {
        _dragItem = item;
        _dragSource = source;
        _dragStart = eventArgs.GetPosition(previewSections);
        _dragAnchor = eventArgs.GetPosition(source);
    }

    private void OnDragMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.LeftButton != MouseButtonState.Pressed ||
            _dragItem is null ||
            !IsPreparedDragSource(sender, _dragSource))
        {
            return;
        }

        var current = eventArgs.GetPosition(previewSections);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _dragItem;
        var anchor = _dragAnchor;
        _dragItem = null;
        _dragSource = null;
        if (sender is not FrameworkElement source)
        {
            return;
        }

        using var preview = new CustomScreenDragPreview(
            dragSurface,
            source,
            new Point(
                anchor.X / Math.Max(1, source.ActualWidth),
                anchor.Y / Math.Max(1, source.ActualHeight)));

        var previousOpacity = source.Opacity;
        source.Opacity = 0.35;
        GiveFeedbackEventHandler updatePreview = (_, _) =>
            preview.MoveToCursor();
        QueryContinueDragEventHandler continuePreview = (_, _) =>
            preview.MoveToCursor();
        source.GiveFeedback += updatePreview;
        source.QueryContinueDrag += continuePreview;
        try
        {
            DragDrop.DoDragDrop(source, item, DragDropEffects.Move);
        }
        finally
        {
            _dropIndicator.Clear();
            source.GiveFeedback -= updatePreview;
            source.QueryContinueDrag -= continuePreview;
            source.Opacity = previousOpacity;
        }
    }

    internal CustomScreenDragItem? PreparedDragItem => _dragItem;

    internal FrameworkElement? PreparedDragSource => _dragSource;

    internal static bool IsPreparedDragSource(
        object sender,
        FrameworkElement? preparedSource) =>
        ReferenceEquals(sender, preparedSource);

    private static bool OriginatesInsideInteractiveControl(
        Border card,
        MouseButtonEventArgs eventArgs)
    {
        var current = eventArgs.OriginalSource as DependencyObject;
        while (current is not null && !ReferenceEquals(current, card))
        {
            if (current is ButtonBase)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current) ??
                LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private void OnButtonDragOver(object sender, DragEventArgs eventArgs)
    {
        var item = GetDragItem(eventArgs);
        if (sender is not Button button ||
            item?.Kind is not ("button" or "new-button"))
        {
            eventArgs.Effects = DragDropEffects.None;
            eventArgs.Handled = false;
            return;
        }

        var insertAfter = eventArgs.GetPosition(button).X >= button.ActualWidth / 2;
        _dropIndicator.Show(
            button,
            insertAfter ? CustomScreenDropEdge.Right : CustomScreenDropEdge.Left);
        eventArgs.Effects = DragDropEffects.Move;
        eventArgs.Handled = true;
    }

    private void OnSectionDragOver(object sender, DragEventArgs eventArgs)
    {
        var item = GetDragItem(eventArgs);
        if (sender is not Border card ||
            item?.Kind is not ("section" or "new-section" or "new-collapsible" or "new-volume" or "new-trackpad" or "new-collapsible-trackpad" or "new-navigation-ring" or "new-dpad" or "button" or "new-button"))
        {
            eventArgs.Effects = DragDropEffects.None;
            eventArgs.Handled = true;
            return;
        }

        var edge = item.Kind is "section" or "new-section" or "new-collapsible" or "new-volume" or "new-trackpad" or "new-collapsible-trackpad" or "new-navigation-ring" or "new-dpad"
            ? eventArgs.GetPosition(card).X >= card.ActualWidth / 2
                ? CustomScreenDropEdge.Right
                : CustomScreenDropEdge.Left
            : CustomScreenDropEdge.Inside;
        _dropIndicator.Show(card, edge);
        eventArgs.Effects = DragDropEffects.Move;
        eventArgs.Handled = true;
    }

    private void OnRowDragOver(object sender, DragEventArgs eventArgs)
    {
        var item = GetDragItem(eventArgs);
        if (sender is not FrameworkElement target ||
            item?.Kind is not ("button" or "new-button"))
        {
            eventArgs.Effects = DragDropEffects.None;
            eventArgs.Handled = false;
            return;
        }

        _dropIndicator.Show(target, CustomScreenDropEdge.Inside);
        eventArgs.Effects = DragDropEffects.Move;
        eventArgs.Handled = true;
    }

    private void DropOnSection(
        DragEventArgs eventArgs,
        string targetSectionId,
        bool insertAfter)
    {
        _dropIndicator.Clear();
        var item = GetDragItem(eventArgs);
        if (item is null || _draft is null)
        {
            return;
        }

        if (item.Kind == "section")
        {
            Apply(CustomScreenPreviewDraftEditing.ReorderSection(
                _draft!,
                item.Id,
                targetSectionId,
                _orientation,
                insertAfter));
        }
        else if (item.Kind is "new-section" or "new-collapsible" or "new-volume" or "new-trackpad" or "new-collapsible-trackpad" or "new-navigation-ring" or "new-dpad")
        {
            Apply(CustomScreenPreviewDraftEditing.CreateSection(
                _draft!,
                item.Kind switch
                {
                    "new-collapsible" => "collapsible",
                    "new-volume" => "volume",
                    "new-trackpad" => "trackpad",
                    "new-collapsible-trackpad" => "collapsibleTrackpad",
                    "new-navigation-ring" => "navigationRing",
                    "new-dpad" => "dpad",
                    _ => "buttons"
                },
                targetSectionId,
                insertAfter,
                _orientation));
        }
        else if (_draft?.Sections.FirstOrDefault(section =>
            section.Id == targetSectionId) is { } targetSection &&
            CustomScreenSectionKinds.AllowsButtons(targetSection.Kind))
        {
            if (item.Kind == "new-button")
            {
                Apply(CustomScreenPreviewDraftEditing.CreateButton(
                    _draft!,
                    targetSectionId,
                    0,
                    null,
                    insertAfter: true,
                    _orientation));
            }
            else
            {
                Apply(CustomScreenPreviewDraftEditing.MoveButtonToSection(
                    _draft!,
                    item.Id,
                    targetSectionId,
                    null));
            }
        }
        eventArgs.Handled = true;
    }

    private void DropOnButtonRow(
        DragEventArgs eventArgs,
        string targetSectionId,
        int targetRow)
    {
        _dropIndicator.Clear();
        var item = GetDragItem(eventArgs);
        if (_draft is null)
        {
            return;
        }

        if (item?.Kind == "new-button")
        {
            Apply(CustomScreenPreviewDraftEditing.CreateButton(
                _draft!,
                targetSectionId,
                targetRow,
                null,
                insertAfter: true,
                _orientation));
            eventArgs.Handled = true;
        }
        else if (item?.Kind == "button")
        {
            Apply(CustomScreenPreviewDraftEditing.MoveButtonToSection(
                _draft!,
                item.Id,
                targetSectionId,
                targetRow));
            eventArgs.Handled = true;
        }
    }

    private void DropButton(
        DragEventArgs eventArgs,
        string targetSectionId,
        string targetButtonId,
        bool insertAfter,
        int? targetVisualRow)
    {
        _dropIndicator.Clear();
        var item = GetDragItem(eventArgs);
        if (item is null || _draft is null)
        {
            return;
        }

        if (item.Kind is "section" or "new-section" or "new-collapsible" or "new-volume" or "new-trackpad" or "new-collapsible-trackpad" or "new-navigation-ring" or "new-dpad")
        {
            eventArgs.Handled = false;
            return;
        }

        if (item.Kind == "new-button")
        {
            Apply(CustomScreenPreviewDraftEditing.CreateButton(
                _draft!,
                targetSectionId,
                targetVisualRow ?? 0,
                targetButtonId,
                insertAfter,
                _orientation));
        }
        else if (item.Kind == "button")
        {
            Apply(CustomScreenPreviewDraftEditing.ReorderButton(
                _draft!,
                item.Id,
                targetSectionId,
                targetButtonId,
                insertAfter,
                targetVisualRow,
                _orientation));
        }
        eventArgs.Handled = true;
    }

    private void Apply(CustomScreenDraftEdit? edit)
    {
        if (edit is null)
        {
            return;
        }

        applyDraft(
            edit.Draft,
            edit.SelectedSectionId,
            edit.SelectedButtonId,
            edit.SelectedRow);
    }

    private void OnSurfaceDragOver(object sender, DragEventArgs eventArgs)
    {
        var item = GetDragItem(eventArgs);
        if (item?.Kind is not ("new-section" or "new-collapsible" or "new-volume" or "new-trackpad" or "new-collapsible-trackpad" or "new-navigation-ring" or "new-dpad") &&
            item?.Kind is not ("new-button" or "button"))
        {
            eventArgs.Effects = DragDropEffects.None;
            return;
        }

        _dropIndicator.Show(
            previewWorkspace,
            CustomScreenDropEdge.Inside);
        eventArgs.Effects = DragDropEffects.Move;
        eventArgs.Handled = true;
    }

    private void DropOnSurface(object sender, DragEventArgs eventArgs)
    {
        _dropIndicator.Clear();
        var item = GetDragItem(eventArgs);
        if (_draft is null || item is null)
        {
            return;
        }

        if (item.Kind is "new-button" or "button")
        {
            var buttonEdit =
                CustomScreenPreviewDraftEditing.CreatePanelForDroppedButton(
                _draft,
                item.Kind == "button" ? item.Id : null,
                _orientation);
            Apply(buttonEdit);
            eventArgs.Effects = DragDropEffects.Move;
            eventArgs.Handled = buttonEdit is not null;
            return;
        }

        if (item.Kind is "new-section" or "new-collapsible" or "new-volume" or "new-trackpad" or "new-collapsible-trackpad" or "new-navigation-ring" or "new-dpad")
        {
            Apply(CustomScreenPreviewDraftEditing.CreateSection(
                _draft,
                item.Kind switch
                {
                    "new-collapsible" => "collapsible",
                    "new-volume" => "volume",
                    "new-trackpad" => "trackpad",
                    "new-collapsible-trackpad" => "collapsibleTrackpad",
                    "new-navigation-ring" => "navigationRing",
                    "new-dpad" => "dpad",
                    _ => "buttons"
                },
                null,
                insertAfter: true,
                _orientation));
            eventArgs.Effects = DragDropEffects.Move;
            eventArgs.Handled = true;
        }
    }

    private static CustomScreenDragItem? GetDragItem(DragEventArgs eventArgs) =>
        eventArgs.Data.GetDataPresent(typeof(CustomScreenDragItem))
            ? eventArgs.Data.GetData(typeof(CustomScreenDragItem)) as CustomScreenDragItem
            : null;
}
