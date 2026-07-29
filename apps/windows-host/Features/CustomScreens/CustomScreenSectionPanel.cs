using System.Windows;
using System.Windows.Controls;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace VolturaAir.Host.Features.CustomScreens;

public sealed class CustomScreenSectionPanel : Panel
{
    private const int ColumnCount = 12;
    private const double Gap = 8;

    public static readonly DependencyProperty WidthColumnsProperty =
        DependencyProperty.RegisterAttached(
            "WidthColumns",
            typeof(int),
            typeof(CustomScreenSectionPanel),
            new FrameworkPropertyMetadata(
                ColumnCount,
                FrameworkPropertyMetadataOptions.AffectsParentMeasure |
                FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty HeightModeProperty =
        DependencyProperty.RegisterAttached(
            "HeightMode",
            typeof(string),
            typeof(CustomScreenSectionPanel),
            new FrameworkPropertyMetadata(
                "content",
                FrameworkPropertyMetadataOptions.AffectsParentMeasure |
                FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty FillWeightProperty =
        DependencyProperty.RegisterAttached(
            "FillWeight",
            typeof(int),
            typeof(CustomScreenSectionPanel),
            new FrameworkPropertyMetadata(
                1,
                FrameworkPropertyMetadataOptions.AffectsParentMeasure |
                FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static int GetWidthColumns(DependencyObject element) =>
        (int)element.GetValue(WidthColumnsProperty);

    public static void SetWidthColumns(DependencyObject element, int value) =>
        element.SetValue(WidthColumnsProperty, value);

    public static string GetHeightMode(DependencyObject element) =>
        (string)element.GetValue(HeightModeProperty);

    public static void SetHeightMode(DependencyObject element, string value) =>
        element.SetValue(HeightModeProperty, value);

    public static int GetFillWeight(DependencyObject element) =>
        (int)element.GetValue(FillWeightProperty);

    public static void SetFillWeight(DependencyObject element, int value) =>
        element.SetValue(FillWeightProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = ResolveWidth(availableSize.Width);
        var rows = MeasureRows(width);
        var naturalHeight = rows.Sum(row => row.MinimumHeight) +
            Math.Max(0, rows.Count - 1) * Gap;
        var targetHeight = ResolveTargetHeight(availableSize.Height, naturalHeight);
        return new Size(width, targetHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rows = MeasureRows(finalSize.Width);
        var rowHeights = AllocateRowHeights(rows, finalSize.Height);
        var y = 0d;

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var rowHeight = rowHeights[rowIndex];
            var columns = 0;
            foreach (var child in row.Children)
            {
                var span = Span(child);
                var x = finalSize.Width * columns / ColumnCount;
                var width = Math.Max(
                    0,
                    finalSize.Width * span / ColumnCount - Gap);
                child.Arrange(new Rect(
                    x,
                    y,
                    width,
                    row.IsFill ? rowHeight : child.DesiredSize.Height));
                columns += span;
            }
            y += rowHeight + Gap;
        }

        return finalSize;
    }

    private List<LayoutRow> MeasureRows(double width)
    {
        var rows = new List<LayoutRow>();
        var current = new LayoutRow();

        foreach (UIElement child in InternalChildren)
        {
            var span = Span(child);
            if (current.Columns > 0 && current.Columns + span > ColumnCount)
            {
                rows.Add(current);
                current = new LayoutRow();
            }

            child.Measure(new Size(
                Math.Max(0, width * span / ColumnCount - Gap),
                double.PositiveInfinity));
            current.Children.Add(child);
            current.Columns += span;
            current.MinimumHeight = Math.Max(
                current.MinimumHeight,
                child.DesiredSize.Height);
            if (string.Equals(
                    GetHeightMode(child),
                    "fill",
                    StringComparison.Ordinal))
            {
                current.IsFill = true;
                current.FillWeight = Math.Max(
                    current.FillWeight,
                    Math.Clamp(GetFillWeight(child), 1, 4));
            }

            if (current.Columns == ColumnCount)
            {
                rows.Add(current);
                current = new LayoutRow();
            }
        }

        if (current.Children.Count > 0)
        {
            rows.Add(current);
        }
        return rows;
    }

    private static double[] AllocateRowHeights(
        IReadOnlyList<LayoutRow> rows,
        double finalHeight)
    {
        var heights = rows.Select(row => row.MinimumHeight).ToArray();
        var gaps = Math.Max(0, rows.Count - 1) * Gap;
        var contentHeight = rows
            .Where(row => !row.IsFill)
            .Sum(row => row.MinimumHeight);
        var fillRows = rows
            .Select((row, index) => (row, index))
            .Where(item => item.row.IsFill)
            .ToArray();
        if (fillRows.Length == 0)
        {
            return heights;
        }

        var availableForFill = Math.Max(
            fillRows.Sum(item => item.row.MinimumHeight),
            finalHeight - contentHeight - gaps);
        var totalWeight = fillRows.Sum(item => item.row.FillWeight);
        foreach (var item in fillRows)
        {
            heights[item.index] = Math.Max(
                item.row.MinimumHeight,
                availableForFill * item.row.FillWeight / totalWeight);
        }
        return heights;
    }

    private double ResolveTargetHeight(double availableHeight, double naturalHeight)
    {
        if (!double.IsInfinity(availableHeight) && availableHeight > 0)
        {
            return Math.Max(naturalHeight, availableHeight);
        }
        return Math.Max(naturalHeight, MinHeight);
    }

    private double ResolveWidth(double availableWidth)
    {
        if (!double.IsInfinity(availableWidth) && availableWidth > 0)
        {
            return availableWidth;
        }
        return ActualWidth > 0 ? ActualWidth : 320;
    }

    private static int Span(UIElement child) =>
        Math.Clamp(GetWidthColumns(child), 1, ColumnCount);

    private sealed class LayoutRow
    {
        public List<UIElement> Children { get; } = [];

        public int Columns { get; set; }

        public double MinimumHeight { get; set; }

        public bool IsFill { get; set; }

        public int FillWeight { get; set; } = 1;
    }
}
