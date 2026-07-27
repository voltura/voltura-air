using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using VolturaAir.Host.Features.Presentations;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void PresentationPendingReviewPanelUsesThicknessPadding()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var injector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(injector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Presentations);
                window.UpdateLayout();

                var view = Assert.Single(FindWpfDescendants<PresentationsPageView>(window));
                var pendingReviewPanel = Assert.IsType<Border>(view.FindName("PendingReviewPanel"));
                Assert.Equal(new Thickness(UiTokens.SpaceMd), pendingReviewPanel.Padding);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void PresentationArchiveStatisticsStayOnOneRowAtTheDefaultWindowSize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var injector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(
                manager,
                new InputDispatcher(injector),
                isolatedTestMode: true);
            var reportStore = Assert.IsType<InMemoryPresentationReportStore>(
                webHost.PresentationReportStore);
            PresentationReportDemoData.AddTo(reportStore);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Presentations);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                window.UpdateLayout();

                var summary = Assert.Single(
                    FindWpfDescendants<ItemsControl>(window),
                    control => string.Equals(
                        AutomationProperties.GetName(control),
                        "Presentation archive statistics",
                        StringComparison.Ordinal));
                Assert.InRange(summary.Items.Count, 6, 7);
                Assert.InRange(summary.ActualHeight, 1, 70);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void ReplacingPresentationsPageUnsubscribesTheOldView()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var injector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(injector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Presentations);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                var first = Assert.Single(FindWpfDescendants<PresentationsPageView>(window));
                Assert.True(first.IsSessionEventsSubscribed);

                window.ShowPage(HostPage.Devices);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                Assert.False(first.IsSessionEventsSubscribed);

                window.ShowPage(HostPage.Presentations);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                var second = Assert.Single(FindWpfDescendants<PresentationsPageView>(window));
                Assert.NotSame(first, second);
                Assert.True(second.IsSessionEventsSubscribed);
                Assert.False(first.IsSessionEventsSubscribed);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void PresentationDetailsKeepInformationAndScrollerOutOfTabNavigation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var injector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(injector), isolatedTestMode: true);
            var reportStore = Assert.IsType<InMemoryPresentationReportStore>(webHost.PresentationReportStore);
            PresentationReportDemoData.AddTo(reportStore);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Presentations);
                window.UpdateLayout();

                var view = Assert.Single(FindWpfDescendants<PresentationsPageView>(window));
                view.ShowReport(reportStore.ReadAll().Reports[0]);
                window.UpdateLayout();

                var detailScroller = Assert.Single(
                    FindWpfDescendants<ScrollViewer>(window),
                    viewer => string.Equals(
                        AutomationProperties.GetName(viewer),
                        "Presentation details",
                        StringComparison.Ordinal));
                Assert.False(detailScroller.Focusable);
                Assert.False(detailScroller.IsTabStop);

                foreach (var name in new[] { "Presentation statistics", "Session and break breakdown" })
                {
                    var information = Assert.Single(
                        FindWpfDescendants<ItemsControl>(window),
                        control => string.Equals(AutomationProperties.GetName(control), name, StringComparison.Ordinal));
                    Assert.False(information.Focusable);
                    Assert.False(information.IsTabStop);
                }

                var titleFilter = Assert.Single(FindWpfDescendants<WatermarkedTextBox>(window));
                Assert.False(titleFilter.IsVisible);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }
}
