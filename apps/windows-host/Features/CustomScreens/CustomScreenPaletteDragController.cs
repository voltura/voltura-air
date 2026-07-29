using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using Button = System.Windows.Controls.Button;
using GiveFeedbackEventHandler = System.Windows.GiveFeedbackEventHandler;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using QueryContinueDragEventHandler = System.Windows.QueryContinueDragEventHandler;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenPaletteDragController
{
    private readonly FrameworkElement _surface;
    private readonly FrameworkElement _previewWorkspace;
    private Point _start;
    private Point _anchorRatio;
    private Button? _dragHandle;
    private FrameworkElement? _anchorSurface;
    private CustomScreenDragItem? _item;

    public CustomScreenPaletteDragController(
        FrameworkElement surface,
        FrameworkElement previewWorkspace,
        Button sectionHandle,
        FrameworkElement sectionAnchor,
        Button collapsibleSectionHandle,
        FrameworkElement collapsibleSectionAnchor,
        Button buttonHandle,
        FrameworkElement buttonAnchor,
        Button volumeHandle,
        FrameworkElement volumeAnchor,
        Button trackpadHandle,
        FrameworkElement trackpadAnchor,
        Button collapsibleTrackpadHandle,
        FrameworkElement collapsibleTrackpadAnchor)
    {
        _surface = surface;
        _previewWorkspace = previewWorkspace;
        Attach(sectionHandle, sectionAnchor, "new-section");
        Attach(
            collapsibleSectionHandle,
            collapsibleSectionAnchor,
            "new-collapsible");
        Attach(buttonHandle, buttonAnchor, "new-button");
        Attach(volumeHandle, volumeAnchor, "new-volume");
        Attach(trackpadHandle, trackpadAnchor, "new-trackpad");
        Attach(
            collapsibleTrackpadHandle,
            collapsibleTrackpadAnchor,
            "new-collapsible-trackpad");
    }

    private void Attach(
        Button dragHandle,
        FrameworkElement previewSource,
        string kind)
    {
        dragHandle.PreviewMouseLeftButtonDown += (_, eventArgs) =>
        {
            _start = eventArgs.GetPosition(_surface);
            var anchor = eventArgs.GetPosition(previewSource);
            _anchorRatio = new Point(
                anchor.X / Math.Max(1, previewSource.ActualWidth),
                anchor.Y / Math.Max(1, previewSource.ActualHeight));
            _dragHandle = dragHandle;
            _anchorSurface = previewSource;
            _item = new CustomScreenDragItem(kind);
        };
        dragHandle.PreviewMouseMove += OnMouseMove;
    }

    private void OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.LeftButton != MouseButtonState.Pressed ||
            _dragHandle is null ||
            _anchorSurface is null ||
            _item is null)
        {
            return;
        }

        var position = eventArgs.GetPosition(_surface);
        if (Math.Abs(position.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragHandle = _dragHandle;
        var anchorSurface = _anchorSurface;
        var anchorRatio = _anchorRatio;
        var item = _item;
        _dragHandle = null;
        _anchorSurface = null;
        _item = null;
        var previewScale = PreviewScale();
        var destinationPreview = CustomScreenPaletteGhostFactory.Create(
            item.Kind,
            _surface,
            _previewWorkspace.ActualWidth);
        using var preview = new CustomScreenDragPreview(
            _surface,
            destinationPreview,
            anchorRatio,
            previewScale);

        var previousOpacity = anchorSurface.Opacity;
        anchorSurface.Opacity = 0.35;
        GiveFeedbackEventHandler updatePreview = (_, _) =>
            preview.MoveToCursor();
        QueryContinueDragEventHandler continuePreview = (_, _) =>
            preview.MoveToCursor();
        dragHandle.GiveFeedback += updatePreview;
        dragHandle.QueryContinueDrag += continuePreview;
        try
        {
            DragDrop.DoDragDrop(dragHandle, item, DragDropEffects.Move);
        }
        finally
        {
            dragHandle.GiveFeedback -= updatePreview;
            dragHandle.QueryContinueDrag -= continuePreview;
            anchorSurface.Opacity = previousOpacity;
        }
    }

    private double PreviewScale()
    {
        if (_previewWorkspace.ActualWidth <= 0)
        {
            return 1;
        }

        try
        {
            var bounds = _previewWorkspace.TransformToVisual(_surface)
                .TransformBounds(new Rect(_previewWorkspace.RenderSize));
            return Math.Max(0.1, bounds.Width / _previewWorkspace.ActualWidth);
        }
        catch (InvalidOperationException)
        {
            return 1;
        }
    }
}
