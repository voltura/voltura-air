using System.Windows;
using System.Windows.Controls;
using Panel = System.Windows.Controls.Panel;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;
using UIElement = System.Windows.UIElement;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenButtonFlowPanel : Panel
{
    public string ButtonAlignment { get; set; } = "start";

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width)
            ? double.MaxValue
            : Math.Max(0, availableSize.Width);
        var rowWidth = 0d;
        var rowHeight = 0d;
        var desiredWidth = 0d;
        var desiredHeight = 0d;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(availableSize);
            var childSize = child.DesiredSize;
            if (rowWidth > 0 && rowWidth + childSize.Width > width)
            {
                desiredWidth = Math.Max(desiredWidth, rowWidth);
                desiredHeight += rowHeight;
                rowWidth = 0;
                rowHeight = 0;
            }

            rowWidth += childSize.Width;
            rowHeight = Math.Max(rowHeight, childSize.Height);
        }

        desiredWidth = Math.Max(desiredWidth, rowWidth);
        desiredHeight += rowHeight;
        return new(
            double.IsInfinity(availableSize.Width) ? desiredWidth : availableSize.Width,
            desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rowStart = 0;
        var rowWidth = 0d;
        var rowHeight = 0d;
        var y = 0d;

        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var childSize = InternalChildren[index].DesiredSize;
            if (rowWidth > 0 && rowWidth + childSize.Width > finalSize.Width)
            {
                ArrangeRow(rowStart, index, y, rowWidth, rowHeight, finalSize.Width);
                y += rowHeight;
                rowStart = index;
                rowWidth = 0;
                rowHeight = 0;
            }

            rowWidth += childSize.Width;
            rowHeight = Math.Max(rowHeight, childSize.Height);
        }

        ArrangeRow(
            rowStart,
            InternalChildren.Count,
            y,
            rowWidth,
            rowHeight,
            finalSize.Width);
        return finalSize;
    }

    private void ArrangeRow(
        int start,
        int end,
        double y,
        double contentWidth,
        double rowHeight,
        double availableWidth)
    {
        var count = end - start;
        if (count <= 0)
        {
            return;
        }

        var remaining = Math.Max(0, availableWidth - contentWidth);
        var (offset, gap) = ButtonAlignment switch
        {
            "center" => (remaining / 2, 0),
            "end" => (remaining, 0),
            "space-between" when count > 1 => (0, remaining / (count - 1)),
            "space-around" => (remaining / count / 2, remaining / count),
            "space-evenly" => (remaining / (count + 1), remaining / (count + 1)),
            _ => (0, 0)
        };

        var x = offset;
        for (var index = start; index < end; index++)
        {
            var child = InternalChildren[index];
            child.Arrange(new Rect(x, y, child.DesiredSize.Width, rowHeight));
            x += child.DesiredSize.Width + gap;
        }
    }
}
