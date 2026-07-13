using System.Windows.Controls;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void PreferencesUseIntentionalOrderAndThemedExpirationPicker()
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
                window.ShowPage(HostPage.Preferences);
                window.UpdateLayout();

                var sections = FindWpfDescendants<Expander>(window).ToArray();
                Assert.Equal(
                    "Application|Appearance|Trackpad defaults|Remote defaults|Application launch buttons|Keep awake|Global permissions|Windows locking|Developer tools",
                    string.Join('|', sections.Select(section => section.Header)));
                Assert.Single(FindWpfDescendants<ModernDatePicker>(window));
                Assert.Empty(FindWpfDescendants<DatePicker>(window));
            }
            finally
            {
                window.Close();
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }
}
