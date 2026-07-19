using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace VolturaAir.Host.Features.Preferences;

internal static class PreferencesScrollCoordinator
{
    private const double AssistedScrollPadding = 16;

    public static void RevealExpandedSection(Expander expander, StackPanel content)
    {
        if (FindVisualAncestor<ScrollViewer>(expander) is { } scroller)
        {
            RevealExpandedSection(scroller, expander, content);
        }
    }

    internal static void RevealExpandedSection(ScrollViewer scroller, Expander expander, StackPanel content)
    {
        if (!expander.IsExpanded || expander.Visibility != Visibility.Visible ||
            scroller.ViewportHeight <= 0 || !expander.IsDescendantOf(scroller))
        {
            return;
        }

        var target = FindFirstFocusableVisualDescendant(content) ?? content;
        if (target.Visibility != Visibility.Visible || target.RenderSize.Height <= 0 || !target.IsDescendantOf(scroller))
        {
            return;
        }

        var targetTop = target.TransformToAncestor(scroller).Transform(new Point()).Y;
        var sectionTop = expander.TransformToAncestor(scroller).Transform(new Point()).Y;
        var hiddenBy = targetTop + target.RenderSize.Height - (scroller.ViewportHeight - AssistedScrollPadding);
        var availableScroll = sectionTop - AssistedScrollPadding;
        var scrollDistance = Math.Min(hiddenBy, availableScroll);

        if (!double.IsFinite(scrollDistance) || scrollDistance <= 0.5)
        {
            return;
        }

        scroller.ScrollToVerticalOffset(Math.Min(
            scroller.ScrollableHeight,
            scroller.VerticalOffset + scrollDistance));
    }

    private static UIElement? FindFirstFocusableVisualDescendant(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is UIElement { Focusable: true, IsEnabled: true, IsVisible: true } focusable)
            {
                return focusable;
            }

            if (FindFirstFocusableVisualDescendant(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject descendant)
        where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(descendant); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
