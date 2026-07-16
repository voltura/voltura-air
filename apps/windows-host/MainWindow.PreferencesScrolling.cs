using System.Windows;
using System.Windows.Controls;
using MediaPoint = System.Windows.Point;
using VisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private const double PreferencesAssistedScrollPadding = 16;

    private static void RevealExpandedPreferencesSection(Expander expander, StackPanel content)
    {
        if (FindVisualAncestor<ScrollViewer>(expander) is not { } scroller)
        {
            return;
        }

        RevealExpandedPreferencesSection(scroller, expander, content);
    }

    internal static void RevealExpandedPreferencesSection(ScrollViewer scroller, Expander expander, StackPanel content)
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

        var targetTop = target.TransformToAncestor(scroller).Transform(new MediaPoint()).Y;
        var sectionTop = expander.TransformToAncestor(scroller).Transform(new MediaPoint()).Y;
        var hiddenBy = targetTop + target.RenderSize.Height - (scroller.ViewportHeight - PreferencesAssistedScrollPadding);
        var availableScroll = sectionTop - PreferencesAssistedScrollPadding;
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
