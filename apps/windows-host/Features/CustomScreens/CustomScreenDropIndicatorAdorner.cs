using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenDropIndicatorAdorner(
    UIElement adornedElement,
    Brush accent,
    CustomScreenDropEdge edge) : Adorner(adornedElement)
{
    private readonly Pen _insideOutline = CreateInsideOutline(accent);

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (edge == CustomScreenDropEdge.Inside)
        {
            var bounds = new Rect(
                1,
                1,
                Math.Max(0, ActualWidth - 2),
                Math.Max(0, ActualHeight - 2));
            drawingContext.DrawRoundedRectangle(
                null,
                _insideOutline,
                bounds,
                7,
                7);
            return;
        }

        const double marker = 5;
        var markerBounds = edge switch
        {
            CustomScreenDropEdge.Left => new Rect(0, 0, marker, ActualHeight),
            CustomScreenDropEdge.Right => new Rect(Math.Max(0, ActualWidth - marker), 0, marker, ActualHeight),
            CustomScreenDropEdge.Top => new Rect(0, 0, ActualWidth, marker),
            CustomScreenDropEdge.Bottom => new Rect(0, Math.Max(0, ActualHeight - marker), ActualWidth, marker),
            _ => Rect.Empty
        };
        drawingContext.DrawRoundedRectangle(accent, null, markerBounds, 2.5, 2.5);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;

    private static Pen CreateInsideOutline(Brush accent)
    {
        var pen = new Pen(accent, 2)
        {
            DashStyle = new DashStyle([5, 3], 0)
        };
        pen.Freeze();
        return pen;
    }
}

internal enum CustomScreenDropEdge
{
    Inside,
    Left,
    Right,
    Top,
    Bottom
}
