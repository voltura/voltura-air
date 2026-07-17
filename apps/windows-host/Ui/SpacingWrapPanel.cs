using System.Windows;
using System.Windows.Controls;
using WpfPanel = System.Windows.Controls.Panel;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;

namespace VolturaAir.Host.Ui;

public sealed class SpacingWrapPanel : WpfPanel
{
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing),
        typeof(double),
        typeof(SpacingWrapPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure),
        IsValidSpacing);

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing),
        typeof(double),
        typeof(SpacingWrapPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure),
        IsValidSpacing);

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        var lineWidth = 0d;
        var lineHeight = 0d;
        var desiredWidth = 0d;
        var desiredHeight = 0d;
        var hasLine = false;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(availableSize);
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var proposedWidth = hasLine ? lineWidth + HorizontalSpacing + child.DesiredSize.Width : child.DesiredSize.Width;
            if (hasLine && proposedWidth > availableSize.Width)
            {
                desiredWidth = Math.Max(desiredWidth, lineWidth);
                desiredHeight += lineHeight + (desiredHeight > 0d ? VerticalSpacing : 0d);
                lineWidth = child.DesiredSize.Width;
                lineHeight = child.DesiredSize.Height;
            }
            else
            {
                lineWidth = proposedWidth;
                lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
            }

            hasLine = true;
        }

        if (hasLine)
        {
            desiredWidth = Math.Max(desiredWidth, lineWidth);
            desiredHeight += lineHeight + (desiredHeight > 0d ? VerticalSpacing : 0d);
        }

        return new WpfSize(desiredWidth, desiredHeight);
    }

    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        var line = new List<UIElement>();
        var lineWidth = 0d;
        var lineHeight = 0d;
        var y = 0d;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(WpfRect.Empty);
                continue;
            }

            var proposedWidth = line.Count > 0 ? lineWidth + HorizontalSpacing + child.DesiredSize.Width : child.DesiredSize.Width;
            if (line.Count > 0 && proposedWidth > finalSize.Width)
            {
                ArrangeLine(line, y, lineHeight);
                y += lineHeight + VerticalSpacing;
                line.Clear();
                lineWidth = 0d;
                lineHeight = 0d;
                proposedWidth = child.DesiredSize.Width;
            }

            line.Add(child);
            lineWidth = proposedWidth;
            lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
        }

        if (line.Count > 0)
        {
            ArrangeLine(line, y, lineHeight);
        }

        return finalSize;
    }

    private void ArrangeLine(IEnumerable<UIElement> line, double y, double lineHeight)
    {
        var x = 0d;
        foreach (var child in line)
        {
            child.Arrange(new WpfRect(x, y, child.DesiredSize.Width, lineHeight));
            x += child.DesiredSize.Width + HorizontalSpacing;
        }
    }

    private static bool IsValidSpacing(object value)
    {
        return value is double spacing && double.IsFinite(spacing) && spacing >= 0d;
    }
}
