using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class PreferencesVisualFactory(
    HostVisualFactory visuals,
    PreferencesSearchRegistry searchRegistry)
{
    public SpacingWrapPanel AddToggleGroup(StackPanel parent)
    {
        var group = HostVisualFactory.CreateWrap(UiTokens.SpaceSm, UiTokens.SpaceSm);
        group.Background = visuals.Brush("WindowBrush");
        parent.Children.Add(group);
        return group;
    }

    public StackPanel AddNestedSection(StackPanel parent, string title)
    {
        var content = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceMd);
        var expander = new Expander
        {
            Header = title,
            Content = content,
            IsExpanded = false,
            Style = visuals.Style("PreferencesNestedAccordionStyle")
        };
        parent.Children.Add(expander);
        searchRegistry.RegisterSection(expander);
        return content;
    }

    public SettingsCheckBox Register(SettingsCheckBox control, string? context = null) =>
        searchRegistry.Register(control, context);

    public void RegisterLabel(
        TextBlock label,
        FrameworkElement focusTarget,
        string? context = null) =>
        searchRegistry.RegisterLabel(label, focusTarget, context);
}
