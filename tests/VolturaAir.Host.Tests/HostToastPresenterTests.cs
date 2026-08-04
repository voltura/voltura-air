using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void FailureToastUsesDangerToneAndAssertiveAccessibleStatus()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var root = new Grid();
            window.Content = root;
            using var presenter = new HostToastPresenter(root, new HostVisualFactory(window.Resources), static () => "Voltura Air");

            presenter.Show("Could not connect.", "Cloud relay", HostToastTone.Failure);

            var toast = Assert.IsType<HostToastLiveRegion>(Assert.Single(root.Children));
            Assert.NotNull(UIElementAutomationPeer.CreatePeerForElement(toast));
            var layout = Assert.IsType<Grid>(toast.Child);
            var accentStrip = Assert.IsType<Border>(layout.Children[0]);
            var badge = Assert.IsType<Border>(layout.Children[1]);
            var icon = Assert.IsType<TextBlock>(badge.Child);
            Assert.Same(window.Resources["DangerBrush"], accentStrip.Background);
            Assert.Same(window.Resources["DangerBrush"], badge.Background);
            Assert.Equal("!", icon.Text);
            Assert.Equal("Cloud relay: Could not connect.", AutomationProperties.GetName(toast));
            Assert.Equal("Error", AutomationProperties.GetItemStatus(toast));
            Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(toast));
        });
    }

    [Fact]
    public void FailureToastUsesHighContrastDangerResource()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window, highContrast: true);
            var root = new Grid();
            using var presenter = new HostToastPresenter(root, new HostVisualFactory(window.Resources), static () => "Voltura Air");

            presenter.Show("Could not connect.", "Cloud relay", HostToastTone.Failure);

            var toast = Assert.IsType<HostToastLiveRegion>(Assert.Single(root.Children));
            var layout = Assert.IsType<Grid>(toast.Child);
            Assert.Same(window.Resources["DangerBrush"], Assert.IsType<Border>(layout.Children[0]).Background);
            Assert.Equal(SystemColors.WindowTextBrush, window.Resources["DangerBrush"]);
        });
    }
}
