using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using VolturaAir.Host.Ui;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void MainWindowPagesKeepCompositionSpacingAndModernListsVirtualized()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var inputInjector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                AssertModernListBehavior(window);
                foreach (var page in Enum.GetValues<HostPage>())
                {
                    window.ShowPage(page);
                    window.UpdateLayout();

                    Assert.All(
                        FindWpfDescendants<Control>(window.PageContent),
                        control => Assert.Equal(new Thickness(), control.Margin));
                }
            }
            finally
            {
                window.Close();
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private static void AssertModernListBehavior(MainWindow window)
    {
        var list = new ListBox
        {
            Style = (Style)window.Resources["ModernListBoxStyle"],
            ItemContainerStyle = (Style)window.Resources["ModernListBoxItemStyle"]
        };

        Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
        Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
        var itemsHost = Assert.IsType<VirtualizingStackPanel>(list.ItemsPanel.LoadContent());
        Assert.IsAssignableFrom<IScrollInfo>(itemsHost);
    }

    [Fact]
    public void SpacingIsAppliedOnceBetweenVisibleChildren()
    {
        RunOnStaThread(() =>
        {
            var first = new Border { Height = 20 };
            var collapsed = new Border { Height = 100, Visibility = Visibility.Collapsed };
            var last = new Border { Height = 30 };
            var panel = new SpacingStackPanel { Spacing = 12 };
            panel.Children.Add(first);
            panel.Children.Add(collapsed);
            panel.Children.Add(last);

            panel.Measure(new WpfSize(100, double.PositiveInfinity));
            panel.Arrange(new WpfRect(0, 0, 100, panel.DesiredSize.Height));

            Assert.Equal(62, panel.DesiredSize.Height);
            Assert.Equal(12, last.TranslatePoint(new WpfPoint(), panel).Y - first.ActualHeight);
        });
    }

    [Fact]
    public void WrapSpacingIsAppliedBetweenVisibleItemsAndRows()
    {
        RunOnStaThread(() =>
        {
            var first = new Border { Width = 40, Height = 20 };
            var collapsed = new Border { Width = 100, Height = 100, Visibility = Visibility.Collapsed };
            var second = new Border { Width = 40, Height = 20 };
            var third = new Border { Width = 40, Height = 20 };
            var panel = new SpacingWrapPanel { HorizontalSpacing = 8, VerticalSpacing = 12 };
            panel.Children.Add(first);
            panel.Children.Add(collapsed);
            panel.Children.Add(second);
            panel.Children.Add(third);

            panel.Measure(new WpfSize(90, double.PositiveInfinity));
            panel.Arrange(new WpfRect(0, 0, 90, panel.DesiredSize.Height));

            Assert.Equal(52, panel.DesiredSize.Height);
            Assert.Equal(8, second.TranslatePoint(new WpfPoint(), panel).X - first.ActualWidth);
            Assert.Equal(12, third.TranslatePoint(new WpfPoint(), panel).Y - first.ActualHeight);
        });
    }

}
