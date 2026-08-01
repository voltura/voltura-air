using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VolturaAir.Host;
using VolturaAir.Host.Features.CustomScreens;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void NavigationRingPaletteItemCreatesASelectablePreview()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var pairingStore = new TempPairingStore();
            var owner = new Window { Width = 1000, Height = 620 };
            WpfTheme.Apply(owner);
            owner.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/VolturaAir.Host;component/MainWindow.Styles.xaml",
                    UriKind.Relative)
            });
            owner.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/VolturaAir.Host;component/PreferencesAccordion.Styles.xaml",
                    UriKind.Relative)
            });
            var page = new CustomScreensPageView(
                owner,
                new CustomScreenService(
                    new InMemoryCustomScreenStore(),
                    new FakeAppLaunchService()),
                new PairingManager(pairingStore.Store));
            owner.Content = page;

            try
            {
                owner.Show();
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "New screen"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var add = Assert.IsType<Button>(
                    page.FindName("NavigationRingPaletteButton"));
                var drag = Assert.IsType<Button>(
                    page.FindName("NavigationRingPaletteDragHandle"));
                Assert.Equal("+ Navigation ring", add.Content);
                Assert.Equal(
                    "Drag Navigation ring onto layout",
                    AutomationProperties.GetName(drag));

                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();

                Assert.Contains(
                    "Navigation ring",
                    Assert.IsType<TextBlock>(page.FindName("PropertiesHint"))
                        .Text);
                Assert.Equal(
                    4,
                    FindVisualDescendants<System.Windows.Shapes.Path>(page)
                        .Count(path => AutomationProperties.GetName(path)
                            .StartsWith("D-pad ", StringComparison.Ordinal)));
            }
            finally
            {
                owner.Close();
            }
        });
    }
}
