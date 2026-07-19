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
            var connected = new DeviceListItem("connected", "Connected device", "Connected", true, "Connected now", string.Empty);
            var disconnected = new DeviceListItem("disconnected", "Disconnected device", "Not connected", false, "Disconnected earlier", string.Empty);
            var page = new DevicesPageView([connected, disconnected], static () => { }, static () => { }, static () => { });
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
                new DeviceListItem("first", "First device", "Connected", true, "Connected now", string.Empty),
                new DeviceListItem("second", "Second device", "Not connected", false, "Disconnected earlier", string.Empty)
            };
            var page = new DevicesPageView(devices, static () => { }, static () => { }, static () => { });
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
        var templateRoot = Assert.IsAssignableFrom<FrameworkElement>(page.Devices.ItemTemplate.LoadContent());
        templateRoot.DataContext = device;
        templateRoot.Measure(new Size(600, 200));
        templateRoot.Arrange(new Rect(0, 0, 600, 200));
        templateRoot.UpdateLayout();
        var pill = Assert.IsType<PillBadge>(templateRoot.FindName("ConnectionStatusPill"));

        Assert.Equal("ConnectionStatusPill", pill.Name);
        Assert.Equal(device.Status, pill.Content);
        Assert.Equal(expectedTone, pill.Tone);
    }
}
