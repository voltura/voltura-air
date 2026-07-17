using System.Windows;
using System.Windows.Controls;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace VolturaAir.Host.Ui;

public sealed class SpacingStackPanel : StackPanel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(double),
        typeof(SpacingStackPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure),
        static value => value is double spacing && double.IsFinite(spacing) && spacing >= 0d);

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override WpfSize MeasureOverride(WpfSize constraint)
    {
        var childConstraint = Orientation == WpfOrientation.Vertical
            ? new WpfSize(constraint.Width, double.PositiveInfinity)
            : new WpfSize(double.PositiveInfinity, constraint.Height);
        var stackLength = 0d;
        var crossLength = 0d;
        var hasVisibleChild = false;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(childConstraint);
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            if (hasVisibleChild)
            {
                stackLength += Spacing;
            }

            if (Orientation == WpfOrientation.Vertical)
            {
                stackLength += child.DesiredSize.Height;
                crossLength = Math.Max(crossLength, child.DesiredSize.Width);
            }
            else
            {
                stackLength += child.DesiredSize.Width;
                crossLength = Math.Max(crossLength, child.DesiredSize.Height);
            }

            hasVisibleChild = true;
        }

        return Orientation == WpfOrientation.Vertical
            ? new WpfSize(crossLength, stackLength)
            : new WpfSize(stackLength, crossLength);
    }

    protected override WpfSize ArrangeOverride(WpfSize arrangeSize)
    {
        var offset = 0d;
        var hasVisibleChild = false;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(WpfRect.Empty);
                continue;
            }

            if (hasVisibleChild)
            {
                offset += Spacing;
            }

            if (Orientation == WpfOrientation.Vertical)
            {
                child.Arrange(new WpfRect(0d, offset, Math.Max(arrangeSize.Width, child.DesiredSize.Width), child.DesiredSize.Height));
                offset += child.DesiredSize.Height;
            }
            else
            {
                child.Arrange(new WpfRect(offset, 0d, child.DesiredSize.Width, Math.Max(arrangeSize.Height, child.DesiredSize.Height)));
                offset += child.DesiredSize.Width;
            }

            hasVisibleChild = true;
        }

        return arrangeSize;
    }
}
