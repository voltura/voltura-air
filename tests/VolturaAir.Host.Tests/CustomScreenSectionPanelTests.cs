using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Features.CustomScreens;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void CustomScreenRowsWrapAndAllocateWeightedFillHeight()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var panel = new CustomScreenSectionPanel { MinHeight = 600 };
            var content = new Border { Height = 40 };
            var firstFill = new Border { MinHeight = 100 };
            var secondFill = new Border { MinHeight = 100 };
            foreach (var child in new[] { content, firstFill, secondFill })
            {
                CustomScreenSectionPanel.SetWidthColumns(child, 12);
                panel.Children.Add(child);
            }
            CustomScreenSectionPanel.SetHeightMode(firstFill, "fill");
            CustomScreenSectionPanel.SetFillWeight(firstFill, 1);
            CustomScreenSectionPanel.SetHeightMode(secondFill, "fill");
            CustomScreenSectionPanel.SetFillWeight(secondFill, 2);

            panel.Measure(new Size(360, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 360, 600));

            Assert.Equal(600, panel.DesiredSize.Height);
            Assert.InRange(firstFill.RenderSize.Height, 181, 182);
            Assert.InRange(secondFill.RenderSize.Height, 362, 363);

            var sideBySide = new CustomScreenSectionPanel();
            var left = new Border { Height = 80 };
            var right = new Border { Height = 80 };
            CustomScreenSectionPanel.SetWidthColumns(left, 6);
            CustomScreenSectionPanel.SetWidthColumns(right, 6);
            sideBySide.Children.Add(left);
            sideBySide.Children.Add(right);
            sideBySide.Measure(new Size(360, double.PositiveInfinity));
            sideBySide.Arrange(new Rect(0, 0, 360, 80));

            Assert.Equal(0, left.TranslatePoint(new Point(), sideBySide).Y);
            Assert.Equal(0, right.TranslatePoint(new Point(), sideBySide).Y);
            Assert.True(
                right.TranslatePoint(new Point(), sideBySide).X >
                left.TranslatePoint(new Point(), sideBySide).X);
        });
    }

    [Fact]
    public void CustomScreenButtonFlowUsesSelectedPlacement()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var panel = new CustomScreenButtonFlowPanel
            {
                ButtonAlignment = "space-between"
            };
            var first = new Border { Width = 50, Height = 40 };
            var second = new Border { Width = 50, Height = 40 };
            var third = new Border { Width = 50, Height = 40 };
            panel.Children.Add(first);
            panel.Children.Add(second);
            panel.Children.Add(third);

            panel.Measure(new Size(300, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 300, 40));

            Assert.Equal(0, first.TranslatePoint(new Point(), panel).X);
            Assert.Equal(125, second.TranslatePoint(new Point(), panel).X);
            Assert.Equal(250, third.TranslatePoint(new Point(), panel).X);
        });
    }
}
