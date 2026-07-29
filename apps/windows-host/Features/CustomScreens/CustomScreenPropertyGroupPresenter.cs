using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenPropertyGroupPresenter(StackPanel root)
{
    private readonly Dictionary<string, bool> _expandedStates =
        new(StringComparer.Ordinal);
    private string _componentId = string.Empty;

    public void BeginComponent(string componentId)
    {
        _componentId = componentId;
    }

    public static void SetAllExpanded(StackPanel root, bool expanded)
    {
        foreach (var group in root.Children.OfType<Expander>())
        {
            group.IsExpanded = expanded;
        }
    }

    public void Add(
        string id,
        string title,
        bool initiallyExpanded,
        Action<StackPanel> renderContent)
    {
        var content = new SpacingStackPanel
        {
            Spacing = 8
        };
        renderContent(content);

        var stateKey = $"{_componentId}:{id}";
        var expanded = _expandedStates.TryGetValue(stateKey, out var saved)
            ? saved
            : initiallyExpanded;
        var group = new Expander
        {
            Header = title,
            IsExpanded = expanded,
            Content = content
        };
        AutomationProperties.SetName(group, $"{title} property group");
        group.SetResourceReference(
            FrameworkElement.StyleProperty,
            "CustomScreenCompactPropertyGroupStyle");
        group.Expanded += (_, _) => _expandedStates[stateKey] = true;
        group.Collapsed += (_, _) => _expandedStates[stateKey] = false;
        root.Children.Add(group);
    }
}
