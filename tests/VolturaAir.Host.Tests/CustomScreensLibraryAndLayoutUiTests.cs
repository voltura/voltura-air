using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using VolturaAir.Host;
using VolturaAir.Host.Features.CustomScreens;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void ExportOffersFileAndCommunityDestinations()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var pairingStore = new TempPairingStore();
            var owner = new Window { Width = 1000, Height = 720 };
            WpfTheme.Apply(owner);
            var service = new CustomScreenService(
                new InMemoryCustomScreenStore(),
                new FakeAppLaunchService());
            Assert.True(
                service.TrySave(
                    CustomScreenService.CreateDraft(),
                    out _,
                    out var error),
                error);
            var page = new CustomScreensPageView(
                owner,
                service,
                new PairingManager(pairingStore.Store));
            owner.Content = page;

            try
            {
                owner.Show();
                owner.UpdateLayout();

                var export = FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "Export"));
                var menu = Assert.IsType<ContextMenu>(export.ContextMenu);
                Assert.Collection(
                    menu.Items.Cast<MenuItem>(),
                    item => Assert.Equal("Save to file", item.Header),
                    item => Assert.Equal("Share in community library", item.Header));
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void EmptyPreviewExposesAFullWorkspaceDropTarget()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var pairingStore = new TempPairingStore();
            var owner = new Window { Width = 1000, Height = 720 };
            WpfTheme.Apply(owner);
            var service = new CustomScreenService(
                new InMemoryCustomScreenStore(),
                new FakeAppLaunchService());
            Assert.True(
                service.TrySave(
                    CustomScreenService.CreateDraft() with { Sections = [] },
                    out _,
                    out var error),
                error);
            var page = new CustomScreensPageView(
                owner,
                service,
                new PairingManager(pairingStore.Store));
            owner.Content = page;

            try
            {
                owner.Show();
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "Edit"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();

                var workspace = Assert.IsType<ScrollViewer>(
                    page.FindName("PreviewWorkspace"));
                var sections = Assert.IsType<CustomScreenSectionPanel>(
                    page.FindName("PreviewSections"));
                Assert.True(workspace.AllowDrop);
                Assert.True(workspace.ActualHeight > 100);
                Assert.Empty(sections.Children);

                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "+ Button"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();

                Assert.Single(sections.Children);
                Assert.Single(
                    FindVisualDescendants<Button>(sections),
                    button => AutomationProperties.GetName(button) ==
                        "Select button New button");
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void DragPreviewUsesAnOwnedWindowAndPreservesTheGrabPoint()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var pairingStore = new TempPairingStore();
            var owner = new Window { Width = 1000, Height = 720 };
            WpfTheme.Apply(owner);
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
                owner.UpdateLayout();
                var editor = Assert.IsType<Grid>(page.FindName("EditorRoot"));
                var source = Assert.IsType<Button>(
                    page.FindName("SectionPaletteButton"));

                var preview = new CustomScreenDragPreview(
                    editor,
                    source,
                    new Point(0.75, 0.5));
                owner.UpdateLayout();

                Assert.Same(owner, preview.PreviewWindow.Owner);
                Assert.False(preview.PreviewWindow.ShowActivated);
                Assert.False(preview.PreviewWindow.ShowInTaskbar);
                Assert.False(preview.PreviewWindow.IsHitTestVisible);
                Assert.True(preview.PreviewWindow.IsVisible);
                Assert.InRange(
                    preview.PreviewWindow.Width,
                    source.ActualWidth,
                    source.ActualWidth + 4);
                Assert.InRange(
                    preview.PreviewWindow.Height,
                    source.ActualHeight,
                    source.ActualHeight + 4);

                var topLeft = CustomScreenDragPreview.CalculateTopLeft(
                    new Point(500, 400),
                    new Size(200, 100),
                    new Point(0.75, 0.5),
                    1.5);
                Assert.Equal(275, topLeft.X, precision: 3);
                Assert.Equal(325, topLeft.Y, precision: 3);

                preview.Dispose();
                Assert.False(preview.PreviewWindow.IsVisible);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void NestedButtonOwnsItsDragInsteadOfItsContainingPanel()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var owner = new Window();
            WpfTheme.Apply(owner);
            var previewSections = new StackPanel();
            var button = new Button { Content = "Button" };
            var card = new Border { Child = button };
            previewSections.Children.Add(card);
            owner.Content = previewSections;
            var controller = new CustomScreenPreviewDragController(
                previewSections,
                previewSections,
                previewSections,
                key => Assert.IsAssignableFrom<System.Windows.Media.Brush>(
                    owner.FindResource(key)),
                (_, _, _, _) => { });
            controller.AttachSection(card, "panel");
            controller.AttachButton(button, "panel", "button", null);

            try
            {
                owner.Show();
                owner.UpdateLayout();
                button.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    MouseButton.Left)
                {
                    RoutedEvent = Mouse.PreviewMouseDownEvent,
                    Source = button
                });

                Assert.Equal("button", controller.PreparedDragItem?.Kind);
                Assert.Same(button, controller.PreparedDragSource);
                Assert.True(CustomScreenPreviewDragController
                    .IsPreparedDragSource(button, controller.PreparedDragSource));
                Assert.False(CustomScreenPreviewDragController
                    .IsPreparedDragSource(card, controller.PreparedDragSource));
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void NestedInteractiveHeaderDoesNotPrepareAPanelDrag()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var owner = new Window();
            WpfTheme.Apply(owner);
            var previewSections = new StackPanel();
            var header = new Button { Content = "Header" };
            var card = new Border { Child = header };
            previewSections.Children.Add(card);
            owner.Content = previewSections;
            var controller = new CustomScreenPreviewDragController(
                previewSections,
                previewSections,
                previewSections,
                key => Assert.IsAssignableFrom<System.Windows.Media.Brush>(
                    owner.FindResource(key)),
                (_, _, _, _) => { });
            controller.AttachSection(card, "panel");

            try
            {
                owner.Show();
                owner.UpdateLayout();
                header.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    MouseButton.Left)
                {
                    RoutedEvent = Mouse.PreviewMouseDownEvent,
                    Source = header
                });

                Assert.Null(controller.PreparedDragItem);
                Assert.Null(controller.PreparedDragSource);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void PaletteDragPreviewsUseDestinationComponentShapes()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var resources = new Window();
            WpfTheme.Apply(resources);
            var cases = new[]
            {
                (Kind: "new-section", Text: "New panel", Width: 320d),
                (Kind: "new-collapsible", Text: "Collapsible panel", Width: 320d),
                (Kind: "new-button", Text: "[play]  New button", Width: 104d),
                (Kind: "new-trackpad", Text: "Trackpad", Width: 320d)
            };

            foreach (var expected in cases)
            {
                var visual = CustomScreenPaletteGhostFactory.Create(
                    expected.Kind,
                    resources,
                    320);
                visual.Measure(new Size(640, 640));
                visual.Arrange(new Rect(visual.DesiredSize));
                visual.UpdateLayout();

                Assert.Equal(expected.Width, visual.ActualWidth, precision: 3);
                Assert.Contains(
                    FindVisualDescendants<TextBlock>(visual),
                    text => Equals(text.Text, expected.Text));
            }

            foreach (var kind in new[] { "new-navigation-ring", "new-dpad" })
            {
                var visual = CustomScreenPaletteGhostFactory.Create(
                    kind,
                    resources,
                    320);
                visual.Measure(new Size(640, 640));
                visual.Arrange(new Rect(visual.DesiredSize));
                visual.UpdateLayout();

                Assert.Equal(320d, visual.ActualWidth, precision: 3);
                Assert.Contains(
                    FindVisualDescendants<System.Windows.Shapes.Path>(visual),
                    path => AutomationProperties.GetName(path) == "D-pad up preview");
            }
        });
    }

    [Theory]
    [InlineData(1.00, 120, 48)]
    [InlineData(1.25, 160, 64)]
    [InlineData(1.50, 240, 96)]
    [InlineData(2.00, 320, 128)]
    public void DragPreviewGrabPointIsScaleAndRenderedSizeIndependent(
        double dpiScale,
        double renderedWidth,
        double renderedHeight)
    {
        var cursor = new Point(1200, 900);
        var size = new Size(renderedWidth, renderedHeight);
        var anchor = new Point(0.31, 0.72);

        var topLeft = CustomScreenDragPreview.CalculateTopLeft(
            cursor,
            size,
            anchor,
            dpiScale);

        Assert.Equal(
            cursor.X,
            topLeft.X + (renderedWidth * anchor.X * dpiScale),
            precision: 8);
        Assert.Equal(
            cursor.Y,
            topLeft.Y + (renderedHeight * anchor.Y * dpiScale),
            precision: 8);
    }

    [Fact]
    public void LibraryUsesEqualActionButtonsAndOneDragHandle()
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
            var service = new CustomScreenService(
                new InMemoryCustomScreenStore(),
                new FakeAppLaunchService());
            Assert.True(
                service.TrySave(
                    CustomScreenService.CreateDraft(),
                    out _,
                    out var error),
                error);
            var page = new CustomScreensPageView(
                owner,
                service,
                new PairingManager(pairingStore.Store));
            owner.Content = page;

            try
            {
                owner.Show();
                owner.UpdateLayout();
                var buttons = FindVisualDescendants<Button>(page).ToArray();
                var actions = buttons
                    .Where(button => button.Content is "Edit" or "Duplicate" or "Delete")
                    .ToArray();

                Assert.Equal(3, actions.Length);
                Assert.Single(actions.Select(button => button.Width).Distinct());
                Assert.DoesNotContain(buttons, button => button.Content is "↑" or "↓");
                Assert.Single(buttons, button =>
                    AutomationProperties.GetName(button)
                        .StartsWith("Drag to reorder ", StringComparison.Ordinal));
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void HalfWidthSectionsShareOnePreviewRow()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var panel = new CustomScreenSectionPanel();
            var first = new Border { Height = 80 };
            var second = new Border { Height = 64 };
            CustomScreenSectionPanel.SetWidthColumns(first, 6);
            CustomScreenSectionPanel.SetWidthColumns(second, 6);
            panel.Children.Add(first);
            panel.Children.Add(second);

            panel.Measure(new Size(260, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 260, panel.DesiredSize.Height));

            var firstPosition = first.TranslatePoint(new Point(), panel);
            var secondPosition = second.TranslatePoint(new Point(), panel);
            Assert.Equal(firstPosition.Y, secondPosition.Y, precision: 3);
            Assert.True(secondPosition.X > firstPosition.X);
            Assert.Equal(122, first.ActualWidth, precision: 3);
            Assert.Equal(122, second.ActualWidth, precision: 3);
        });
    }

    [Fact]
    public void NarrowEditorKeepsBothPreviewSelectorsInsideTheirRow()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var pairingStore = new TempPairingStore();
            var owner = new Window
            {
                Width = 840,
                Height = 760
            };
            WpfTheme.Apply(owner);
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

                var device = FindVisualDescendants<ComboBox>(page).Single(combo =>
                    AutomationProperties.GetName(combo) == "Preview device");
                var orientation = FindVisualDescendants<ComboBox>(page).Single(combo =>
                    AutomationProperties.GetName(combo) == "Preview orientation");
                var row = Assert.IsType<Grid>(
                    System.Windows.Media.VisualTreeHelper.GetParent(device));
                Assert.Same(row, System.Windows.Media.VisualTreeHelper.GetParent(orientation));

                var deviceBounds = device.TransformToAncestor(row)
                    .TransformBounds(new Rect(device.RenderSize));
                var orientationBounds = orientation.TransformToAncestor(row)
                    .TransformBounds(new Rect(orientation.RenderSize));
                Assert.True(deviceBounds.Left >= 0);
                Assert.True(deviceBounds.Right <= row.ActualWidth + 0.5);
                Assert.True(orientationBounds.Right <= row.ActualWidth + 0.5);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    private static Border FindRow(DependencyObject page, int row) =>
        FindVisualDescendants<Border>(page).Single(border =>
            AutomationProperties.GetName(border) == $"Button row {row}");
}
