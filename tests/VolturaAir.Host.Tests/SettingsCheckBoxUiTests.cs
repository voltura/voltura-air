using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using VolturaAir.Host.Features.Preferences;
using VolturaAir.Host.Ui;
using WpfVisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void SettingsCheckBoxUsesItsExistingBorderForKeyboardFocus()
    {
        RunOnStaThread(() =>
        {
            var window = new Window();
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            });
            WpfTheme.Apply(window);
            var setting = new SettingsCheckBox { Label = "Focused setting" };
            window.Content = setting;

            try
            {
                window.Show();
                window.UpdateLayout();

                var toggle = Assert.Single(FindWpfDescendants<CheckBox>(setting));
                var chrome = Assert.Single(
                    FindWpfDescendants<Border>(setting),
                    border => border.Padding == new Thickness(12));
                Assert.Same(window.Resources["BorderBrush"], chrome.BorderBrush);

                Assert.True(toggle.Focus());
                Assert.Same(toggle, Keyboard.FocusedElement);
                window.UpdateLayout();

                Assert.Same(window.Resources["FocusBrush"], chrome.BorderBrush);
                Assert.Equal(new Thickness(1), chrome.BorderThickness);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void CursorRecoveryUsesSharedInformationAndVisibleWarning()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AppPointerSettings.SetUseCursorRecoveryWatchdog(false);
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

                var useWatchdog = Assert.Single(
                    FindWpfDescendants<SettingsCheckBox>(window),
                    checkbox => checkbox.Label == "Use cursor recovery watchdog");
                var info = Assert.Single(
                    FindWpfDescendants<Button>(window),
                    button => string.Equals(
                        System.Windows.Automation.AutomationProperties.GetName(button),
                        "More information about Use cursor recovery watchdog",
                        StringComparison.Ordinal));

                Assert.False(useWatchdog.IsChecked);
                Assert.Equal("ⓘ", info.Content);
                Assert.Same(window.Resources["TextBrush"], info.Foreground);
                Assert.Same(window.Resources["SettingsInformationButtonStyle"], info.Style);
                Assert.Equal(20d, info.FontSize);
                Assert.Equal(PlacementMode.Top, ToolTipService.GetPlacement(info));
                Assert.Contains(
                    FindWpfDescendants<TextBlock>(window),
                    text => text.Text.StartsWith("Without the recovery watchdog", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void PreferenceToggleGroupsSeparateSettingsWithPersistentGuidance()
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

                var settings = FindWpfDescendants<SettingsCheckBox>(window).ToArray();
                var startup = Assert.Single(settings, setting => setting.Label == "Start Voltura Air when I sign in to Windows");
                var logging = Assert.Single(settings, setting => setting.Label == "Write application log");
                var url = Assert.Single(settings, setting => setting.Label == "Allow paired devices to open web addresses");
                var shutdown = Assert.Single(settings, setting => setting.Label == "Allow paired devices to shut down the PC");
                var presetLabels = AppLaunchSettings.GetPresets()
                    .Select(preset => $"Show {AppLaunchSettings.GetPresetName(preset.Kind)}")
                    .ToHashSet(StringComparer.Ordinal);
                var presetSettings = settings.Where(setting => presetLabels.Contains(setting.Label)).ToArray();

                var applicationGroup = Assert.IsType<SpacingWrapPanel>(WpfVisualTreeHelper.GetParent(startup));
                Assert.Same(window.Resources["WindowBrush"], applicationGroup.Background);
                Assert.IsNotType<SpacingWrapPanel>(WpfVisualTreeHelper.GetParent(logging));
                Assert.IsType<SpacingWrapPanel>(WpfVisualTreeHelper.GetParent(url));
                Assert.IsNotType<SpacingWrapPanel>(WpfVisualTreeHelper.GetParent(shutdown));
                Assert.Equal(HorizontalAlignment.Left, logging.HorizontalAlignment);
                Assert.Equal(presetLabels.Count, presetSettings.Length);
                Assert.All(presetSettings, setting => Assert.Equal(180d, setting.MinWidth));
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }
}
