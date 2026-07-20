using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VolturaAir.Host.Features.Devices;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void DevicesPageMapsConnectionStatesToSemanticPills()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            var connected = CreateDevice("connected", "Connected device", "Connected", true, "Connected now");
            var disconnected = CreateDevice("disconnected", "Disconnected device", "Not connected", false, "Disconnected earlier");
            var page = CreatePage([connected, disconnected]);
            AssertStatusPill(page, connected, PillBadgeTone.Success);
            AssertStatusPill(page, disconnected, PillBadgeTone.Danger);
        });
    }

    [Fact]
    public void TabbingIntoDevicesMovesKeyboardFocusToTheSelectedRow()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var devices = new[]
            {
                CreateDevice("first", "First device", "Connected", true, "Connected now"),
                CreateDevice("second", "Second device", "Not connected", false, "Disconnected earlier")
            };
            var page = CreatePage(devices);
            var window = new Window
            {
                Content = page,
                Width = 900,
                Height = 600
            };
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            });
            WpfTheme.Apply(window);
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.True(page.Devices.Focus());
                DoWpfEvents();

                Assert.Equal(0, page.Devices.SelectedIndex);
                var firstRow = Assert.IsType<ListBoxItem>(page.Devices.ItemContainerGenerator.ContainerFromIndex(0));
                Assert.Same(firstRow, Keyboard.FocusedElement);
                Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(page.Devices));

                var presentationSource = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(window));
                var enter = new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource, 0, Key.Enter)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };
                firstRow.RaiseEvent(enter);
                DoWpfEvents();

                Assert.True(enter.Handled);
                Assert.True(devices[0].IsExpanded);

                var space = new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource, 0, Key.Space)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };
                firstRow.RaiseEvent(space);
                DoWpfEvents();

                Assert.True(space.Handled);
                Assert.False(devices[0].IsExpanded);

                firstRow.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource, 0, Key.Enter)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                DoWpfEvents();
                Assert.True(devices[0].IsExpanded);

                Assert.True(firstRow.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                DoWpfEvents();
                Assert.Contains(
                    FindVisualDescendants<System.Windows.Controls.Primitives.ToggleButton>(firstRow),
                    toggle => ReferenceEquals(toggle, Keyboard.FocusedElement));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DevicesUseVirtualizedPixelScrollingForExpandedContent()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            var page = CreatePage([CreateDevice("device", "Device", "Connected", true, "Connected now")]);

            Assert.True(VirtualizingPanel.GetIsVirtualizing(page.Devices));
            Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(page.Devices));
            Assert.True(ScrollViewer.GetCanContentScroll(page.Devices));
        });
    }

    [Fact]
    public void ExpandingADeviceCollapsesThePreviouslyExpandedDevice()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var page = CreatePage([
                CreateDevice("first", "First device", "Connected", true, "Connected now"),
                CreateDevice("second", "Second device", "Not connected", false, "Disconnected earlier")
            ]);
            var window = new Window
            {
                Content = page,
                Width = 900,
                Height = 600
            };
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            });
            WpfTheme.Apply(window);
            try
            {
                window.Show();
                window.UpdateLayout();
                var accordions = FindVisualDescendants<Expander>(window)
                    .Where(expander => expander.Header is DeviceListItem)
                    .ToArray();
                Assert.Equal(2, accordions.Length);

                accordions[0].IsExpanded = true;
                window.UpdateLayout();
                Assert.True(accordions[0].IsExpanded);

                accordions[1].IsExpanded = true;
                DoWpfEvents();
                window.UpdateLayout();

                Assert.False(accordions[0].IsExpanded);
                Assert.True(accordions[1].IsExpanded);
                Assert.Single(accordions, accordion => accordion.IsExpanded);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DeviceChildAccordionsAllowOnlyOneExpandedSection()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var page = CreatePage([CreateDevice("device", "Device", "Connected", true, "Connected now")]);
            var window = new Window
            {
                Content = page,
                Width = 900,
                Height = 600
            };
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            });
            WpfTheme.Apply(window);
            try
            {
                window.Show();
                window.UpdateLayout();
                var device = FindVisualDescendants<Expander>(window)
                    .Single(expander => expander.Header is DeviceListItem);
                device.IsExpanded = true;
                window.UpdateLayout();
                var trackpad = FindVisualDescendants<Expander>(device)
                    .Single(expander => string.Equals(expander.Header as string, "Trackpad profile", StringComparison.Ordinal));
                var appearance = FindVisualDescendants<Expander>(device)
                    .Single(expander => string.Equals(expander.Header as string, "Appearance", StringComparison.Ordinal));
                var permissions = FindVisualDescendants<Expander>(device)
                    .Single(expander => string.Equals(expander.Header as string, "Permissions", StringComparison.Ordinal));

                trackpad.IsExpanded = true;
                DoWpfEvents();
                Assert.True(trackpad.IsExpanded);
                Assert.False(permissions.IsExpanded);

                appearance.IsExpanded = true;
                DoWpfEvents();
                Assert.True(appearance.IsExpanded);
                Assert.False(trackpad.IsExpanded);
                Assert.False(permissions.IsExpanded);

                permissions.IsExpanded = true;
                DoWpfEvents();
                Assert.False(trackpad.IsExpanded);
                Assert.True(permissions.IsExpanded);

                trackpad.IsExpanded = true;
                DoWpfEvents();
                Assert.True(trackpad.IsExpanded);
                Assert.False(permissions.IsExpanded);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void CollapsingADeviceResetsItsChildAccordions()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var page = CreatePage([CreateDevice("device", "Device", "Connected", true, "Connected now")]);
            var window = new Window
            {
                Content = page,
                Width = 900,
                Height = 600
            };
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            });
            WpfTheme.Apply(window);
            try
            {
                window.Show();
                window.UpdateLayout();
                var device = FindVisualDescendants<Expander>(window)
                    .Single(expander => expander.Header is DeviceListItem);
                device.IsExpanded = true;
                window.UpdateLayout();
                var trackpad = FindVisualDescendants<Expander>(device)
                    .Single(expander => string.Equals(expander.Header as string, "Trackpad profile", StringComparison.Ordinal));
                var appearance = FindVisualDescendants<Expander>(device)
                    .Single(expander => string.Equals(expander.Header as string, "Appearance", StringComparison.Ordinal));
                var permissions = FindVisualDescendants<Expander>(device)
                    .Single(expander => string.Equals(expander.Header as string, "Permissions", StringComparison.Ordinal));

                trackpad.IsExpanded = true;
                DoWpfEvents();
                Assert.True(trackpad.IsExpanded);

                device.IsExpanded = false;
                DoWpfEvents();
                device.IsExpanded = true;
                DoWpfEvents();

                Assert.False(trackpad.IsExpanded);
                Assert.False(appearance.IsExpanded);
                Assert.False(permissions.IsExpanded);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void AssertStatusPill(
        DevicesPageView page,
        DeviceListItem device,
        PillBadgeTone expectedTone)
    {
        var accordion = Assert.IsType<Expander>(page.Devices.ItemTemplate.LoadContent());
        var templateRoot = Assert.IsAssignableFrom<FrameworkElement>(accordion.HeaderTemplate.LoadContent());
        templateRoot.DataContext = device;
        templateRoot.Measure(new Size(600, 200));
        templateRoot.Arrange(new Rect(0, 0, 600, 200));
        templateRoot.UpdateLayout();
        var pill = FindWpfDescendants<PillBadge>(templateRoot).Single();

        Assert.Equal("ConnectionStatusPill", pill.Name);
        Assert.Equal(device.Status, pill.Content);
        Assert.Equal(expectedTone, pill.Tone);
    }

    private static DevicesPageView CreatePage(IReadOnlyList<DeviceListItem> devices) => new(
        devices,
        static _ => { },
        static _ => { },
        static (_, value) => (value, value ?? true),
        static (_, _) => true,
        static _ => DevicePointerProfile.DefaultPointerSpeed,
        static (_, _, _) => true,
        static _ => { },
        static () => { },
        static () => { });

    private static DeviceListItem CreateDevice(string clientId, string name, string status, bool isConnected, string activity) => new(
        clientId,
        name,
        status,
        isConnected,
        activity,
        "Windows / Chrome / Browser",
        DevicePointerProfile.DefaultPointerSpeed,
        false,
        null,
        true,
        [],
        false);
}
