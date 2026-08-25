using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VolturaAir.Host.Features.Connection;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void ConnectionExplainerStartsFromPendingMethodAndSwitchesReadOnly()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var page = new ConnectionPageView(
                static () => { }, static () => { }, static () => { }, static () => { },
                static _ => { }, static _ => { }, static _ => { }, static _ => { },
                static _ => { }, static () => { }, static () => { })
            {
                TransportMode = ConnectionTransportMode.Relay,
                EnhancedCapabilitiesEnabled = true
            };
            var owner = new Window { Width = 920, Height = 620 };
            WpfTheme.Apply(owner);
            owner.Show();
            var dialog = ConnectionExplainerDialog.CreateForTest(
                owner,
                page.CurrentTransportMode,
                page.CurrentEnhancedCapabilitiesEnabled);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                Assert.Equal(ConnectionTransportMode.Relay, dialog.SelectedMethod);
                Assert.Equal(ConnectionTransportMode.Relay, page.CurrentTransportMode);
                Assert.InRange(dialog.ActualHeight, 1, 620);

                var directButton = FindWpfDescendants<System.Windows.Controls.Primitives.ToggleButton>(dialog)
                    .Single(button => button.Name == "DirectMethodButton");
                var enhancedRegion = FindWpfDescendants<StackPanel>(dialog)
                    .Single(panel => panel.Name == "EnhancedRouteTogglePanel");
                var pulse = FindWpfDescendants<Ellipse>(dialog)
                    .Single(ellipse => ellipse.Name == "InitialLoadPacketGlow");
                var enhancedRouteToggle = FindWpfDescendants<CheckBox>(dialog)
                    .Single(checkBox => checkBox.Name == "EnhancedRouteToggle");
                var playFlowButton = FindWpfDescendants<Button>(dialog)
                    .Single(button => button.Name == "PlayFlowButton");
                var closeButton = FindWpfDescendants<Button>(dialog)
                    .Single(button => Equals(button.Content, "Close"));
                var initialTrackGlow = FindWpfDescendants<System.Windows.Shapes.Path>(dialog)
                    .Single(path => path.Name == "InitialLoadTrackGlow");
                Assert.Equal(Visibility.Collapsed, enhancedRegion.Visibility);
                Assert.False(dialog.Diagram.ShowsInitialLoadStage);
                Assert.IsType<RadialGradientBrush>(pulse.Fill);
                Assert.IsType<System.Windows.Media.Effects.BlurEffect>(pulse.Effect);
                directButton.IsChecked = true;

                Assert.Equal(ConnectionTransportMode.DirectLan, dialog.SelectedMethod);
                Assert.Equal(ConnectionTransportMode.Relay, page.CurrentTransportMode);
                Assert.Equal(Visibility.Visible, enhancedRegion.Visibility);
                Assert.True(dialog.Diagram.ShowsInitialLoadStage);
                Assert.Contains("First", AutomationProperties.GetName(dialog.Diagram), StringComparison.Ordinal);
                Assert.Contains(
                    "initial secure web-app load",
                    FindNamedText(dialog, "RouteExplanationText"),
                    StringComparison.Ordinal);
                Assert.True(enhancedRouteToggle.IsChecked);

                enhancedRouteToggle.IsChecked = false;
                Assert.False(dialog.Diagram.ShowsInitialLoadStage);
                Assert.False(dialog.Diagram.ShowsEnhancedRoute);
                Assert.False(dialog.Diagram.ShowsMainStageLabel);
                Assert.Equal(ConnectionTransportMode.Relay, page.CurrentTransportMode);
                Assert.True(page.CurrentEnhancedCapabilitiesEnabled);

                enhancedRouteToggle.IsChecked = true;
                Assert.True(dialog.Diagram.ShowsInitialLoadStage);
                Assert.True(dialog.Diagram.ShowsEnhancedRoute);
                Assert.True(dialog.Diagram.ShowsMainStageLabel);
                Assert.Equal("2  NORMAL USE — LOCAL ROUTE", dialog.Diagram.MainStageLabelText);

                dialog.Diagram.StopAnimations();
                Assert.Equal(0, initialTrackGlow.Opacity);
                var controlMinHeight = Assert.IsType<double>(
                    dialog.FindResource("ControlMinHeight"));
                Assert.Equal(
                    controlMinHeight,
                    playFlowButton.ActualHeight,
                    precision: 3);
                WaitForWpf(
                    () => Math.Abs(dialog.Diagram.PlayButtonGlowAngle) > 1,
                    "Play flow button border glow");
                Assert.True(playFlowButton.Focus());
                Assert.True(playFlowButton.IsKeyboardFocused);
                playFlowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, dialog.Diagram.ActiveInitialLoadPassCount);
                Assert.Equal(2, dialog.Diagram.ActiveMainRoutePassCount);
                Assert.True(dialog.AnimationsRunning);
                Assert.False(dialog.Diagram.PlayButtonGlowRunning);
                Assert.False(dialog.Diagram.PlayButtonColorGlowVisible);
                playFlowButton.ApplyTemplate();
                closeButton.ApplyTemplate();
                var playFlowChrome = Assert.IsType<Border>(
                    playFlowButton.Template.FindName("Chrome", playFlowButton));
                var closeChrome = Assert.IsType<Border>(
                    closeButton.Template.FindName("Chrome", closeButton));
                var closeBorder = Assert.IsType<SolidColorBrush>(closeChrome.BorderBrush);
                var playFlowBorder = Assert.IsType<SolidColorBrush>(playFlowChrome.BorderBrush);
                Assert.Equal(closeBorder.Color, playFlowBorder.Color);
                Assert.False(playFlowButton.IsKeyboardFocused);
                Assert.False(playFlowButton.Focusable);
                WaitForWpf(
                    () => initialTrackGlow.Opacity > 0 &&
                          Math.Abs(dialog.Diagram.InitialGlowOffset - 95) > 2,
                    "moving connection-route glow");
                dialog.Diagram.StopAnimations();
                Assert.True(dialog.Diagram.PlayButtonGlowRunning);
                Assert.True(dialog.Diagram.PlayButtonColorGlowVisible);
                Assert.True(playFlowButton.Focusable);
                WaitForWpf(
                    () => Math.Abs(dialog.Diagram.PlayButtonGlowAngle) > 1,
                    "restored Play flow button border glow");
                var closeBottom = closeButton.TranslatePoint(
                    new System.Windows.Point(0, closeButton.ActualHeight),
                    dialog).Y;
                Assert.InRange(closeBottom, 0, dialog.ActualHeight);
            }
            finally
            {
                dialog.Close();
                Assert.False(dialog.AnimationsRunning);
                Assert.Equal(95, dialog.Diagram.InitialGlowOffset, precision: 3);
                Assert.Equal(95, dialog.Diagram.MainGlowOffset, precision: 3);
                owner.Close();
            }
        });
    }

    [Fact]
    public void DirectExplainerWithoutEnhancedFeaturesShowsOnlyLocalRoute()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var owner = new Window { Width = 920, Height = 620 };
            WpfTheme.Apply(owner);
            owner.Show();
            var dialog = ConnectionExplainerDialog.CreateForTest(
                owner,
                ConnectionTransportMode.DirectLan,
                enhancedCapabilitiesEnabled: false);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();

                Assert.False(dialog.Diagram.ShowsInitialLoadStage);
                Assert.False(dialog.Diagram.ShowsEnhancedRoute);
                Assert.False(dialog.Diagram.ShowsMainStageLabel);
                Assert.DoesNotContain(
                    "Internet",
                    FindNamedText(dialog, "RouteExplanationText"),
                    StringComparison.Ordinal);
                var playFlowButton = FindWpfDescendants<Button>(dialog)
                    .Single(button => button.Name == "PlayFlowButton");
                playFlowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, dialog.Diagram.ActiveInitialLoadPassCount);
                Assert.Equal(2, dialog.Diagram.ActiveMainRoutePassCount);
            }
            finally
            {
                dialog.Close();
                Assert.False(dialog.AnimationsRunning);
                owner.Close();
            }
        });
    }
}
