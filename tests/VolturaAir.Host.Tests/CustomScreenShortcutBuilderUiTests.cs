using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VolturaAir.Host;
using VolturaAir.Host.Features.CustomScreens;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void ShortcutBuilderStagesModifiersAndRequiresAFinalKey()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var pairingStore = new TempPairingStore();
            var owner = new Window();
            WpfTheme.Apply(owner);
            owner.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/VolturaAir.Host;component/MainWindow.Styles.xaml",
                    UriKind.Relative)
            });
            var service = new CustomScreenService(
                new InMemoryCustomScreenStore(),
                new FakeAppLaunchService());
            var page = new CustomScreensPageView(
                owner,
                service,
                new PairingManager(pairingStore.Store));
            owner.Content = page;

            try
            {
                owner.Show();
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "New screen"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();
                FindVisualDescendants<Button>(page)
                    .First(button => AutomationProperties.GetName(button)
                        .StartsWith("Select button ", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();

                SelectShortcutAction(page);
                owner.UpdateLayout();
                var visualGroup = FindVisualDescendants<Expander>(page)
                    .Single(group =>
                        AutomationProperties.GetName(group) ==
                            "Visual property group");
                visualGroup.IsExpanded = true;
                owner.UpdateLayout();
                var visual = Combo(page, "Visual");
                Assert.Equal(["label"], visual.Items.Cast<string>().ToArray());
                Assert.Equal("label", visual.SelectedItem);
                var propertyGroups = FindVisualDescendants<Expander>(page)
                    .Where(expander => AutomationProperties.GetName(expander)
                        .EndsWith(" property group", StringComparison.Ordinal))
                    .ToArray();
                Assert.Contains(propertyGroups, group =>
                    AutomationProperties.GetName(group) == "Name property group" &&
                    !group.IsExpanded);
                Assert.Contains(propertyGroups, group =>
                    AutomationProperties.GetName(group) == "Action property group" &&
                    group.IsExpanded);
                Assert.All(propertyGroups, group => Assert.Same(
                    page.FindResource("CustomScreenCompactPropertyGroupStyle"),
                    group.Style));
                var optionsFrame = FindVisualDescendants<Border>(page)
                    .Single(border =>
                        border.Child is StackPanel &&
                        FindVisualDescendants<TextBlock>(border).Any(text =>
                            AutomationProperties.GetName(text) == "Command preview"));
                Assert.Same(
                    owner.FindResource("SurfaceRaisedBrush"),
                    optionsFrame.Background);
                Assert.Equal(new Thickness(1), optionsFrame.BorderThickness);
                ResetCommand(page);

                AddModifier(page, "ALT GR");
                Assert.DoesNotContain(
                    FindVisualDescendants<Button>(page),
                    button => AutomationProperties.GetName(button) is
                        "Add CTRL modifier" or "Add ALT modifier");
                ResetCommand(page);

                AddModifier(page, "CTRL");
                Assert.DoesNotContain(
                    FindVisualDescendants<Button>(page),
                    button =>
                        AutomationProperties.GetName(button) ==
                            "Add CTRL modifier");
                Assert.DoesNotContain(
                    FindVisualDescendants<Button>(page),
                    button =>
                        AutomationProperties.GetName(button) ==
                            "Add ALT GR modifier");
                AssertCommand(page, "CTRL");

                AddModifier(page, "ALT");
                var functionKey = Combo(page, "Function key");
                functionKey.SelectedItem = "F5";
                AssertCommand(page, "CTRL + ALT + F5");

                var specialKey = Combo(page, "Special key");
                SelectTagged(specialKey, "Insert");
                AssertCommand(page, "CTRL + ALT + Insert");
                Assert.Null(functionKey.SelectedItem);

                SelectTagged(specialKey, "Escape");
                AssertCommand(page, "CTRL + ALT + Escape");
                Assert.True(ButtonNamed(page, "Save command").IsEnabled);

                var symbolKey = Combo(page, "Symbol key");
                SelectTagged(symbolKey, ";");
                AssertCommand(page, "CTRL + ALT + ;");
                Assert.Null(specialKey.SelectedItem);

                var key = Combo(page, "Command letter or number");
                key.SelectedItem = "7";
                AssertCommand(page, "CTRL + ALT + 7");
                Assert.Null(symbolKey.SelectedItem);

                key.SelectedItem = "V";
                owner.UpdateLayout();
                AssertCommand(page, "CTRL + ALT + V");
                Assert.Null(functionKey.SelectedItem);
                Assert.Null(specialKey.SelectedItem);
                Assert.Null(symbolKey.SelectedItem);

                var saveCommand = ButtonNamed(page, "Save command");
                Assert.True(saveCommand.IsEnabled);
                saveCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();
                Assert.Equal(
                    "V",
                    Combo(page, "Command letter or number").SelectedItem);
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "Save"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var action = Assert.Single(service.GetAll())
                    .Sections[0].Buttons[0].Action;
                Assert.Equal("shortcut", action.Kind);
                Assert.Equal(["Control", "Alt"], action.Modifiers);
                Assert.Equal("V", action.Key);
                Assert.Equal("label", Assert.Single(service.GetAll())
                    .Sections[0].Buttons[0].Presentation);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    private static void SelectShortcutAction(DependencyObject page)
    {
        var actionType = Combo(page, "Action type");
        SelectTagged(actionType, "shortcut");
    }

    private static void ResetCommand(DependencyObject page)
    {
        ButtonNamed(page, "Reset command")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(ButtonNamed(page, "Save command").IsEnabled);
    }

    private static void AddModifier(DependencyObject page, string modifier)
    {
        ButtonNamed(page, $"Add {modifier} modifier")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void AssertCommand(DependencyObject page, string expected)
    {
        Assert.Contains(
            FindVisualDescendants<TextBlock>(page),
            text => AutomationProperties.GetName(text) == "Command preview" &&
                text.Text == expected);
    }

    private static ComboBox Combo(DependencyObject page, string name) =>
        FindVisualDescendants<ComboBox>(page).Single(combo =>
            AutomationProperties.GetName(combo) == name);

    private static Button ButtonNamed(DependencyObject page, string name) =>
        FindVisualDescendants<Button>(page).Single(button =>
            AutomationProperties.GetName(button) == name);

    private static void SelectTagged(ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items
            .OfType<ComboBoxItem>()
            .Single(item => Equals(item.Tag, tag));
    }
}
