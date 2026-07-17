using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void ConnectionPageSwitchesAndValidatesManualPortMode()
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
                window.ShowPage(HostPage.Connection);
                window.UpdateLayout();

                var automatic = FindWpfDescendants<ToggleButton>(window).Single(button => button.Name == "PortAutomaticButton");
                var manual = FindWpfDescendants<ToggleButton>(window).Single(button => button.Name == "PortManualButton");
                var port = FindWpfDescendants<TextBox>(window).Single(textBox => textBox.Name == "ManualPortTextBox");
                var validation = FindWpfDescendants<TextBlock>(window).Single(text => text.Name == "ManualPortValidationText");

                automatic.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.False(port.IsEnabled);
                Assert.True(automatic.IsChecked);
                Assert.False(manual.IsChecked);

                manual.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(port.IsEnabled);
                Assert.False(automatic.IsChecked);
                Assert.True(manual.IsChecked);

                port.Text = "49151";
                Assert.Contains("49152", validation.Text, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }
}
