using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class PreferencesVisualFactory(HostVisualFactory visuals)
{
    public StackPanel AddNestedSection(StackPanel parent, string title)
    {
        var content = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceMd);
        parent.Children.Add(new Expander
        {
            Header = title,
            Content = content,
            IsExpanded = false,
            Style = visuals.Style("PreferencesNestedAccordionStyle")
        });
        return content;
    }
}
