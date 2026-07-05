using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void StartupWindowCanTransitionToErrorState()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new StartupWindow();
            try
            {
                window.Show();
                window.ShowError("Could not bind to port.", "details");
                window.UpdateLayout();

                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Voltura Air could not start.");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Could not bind to port.");
                Assert.Contains(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "Copy details");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void MainWindowNavigatesToPrimaryPages()
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
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector));
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Connect);
                window.UpdateLayout();
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Connect");
                Assert.Contains(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "New code");

                window.ShowPage(HostPage.Connection);
                window.UpdateLayout();
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Connection");
                Assert.Contains(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "Save");

                window.ShowPage(HostPage.Devices);
                window.UpdateLayout();
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Devices");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Device details");

                window.ShowPage(HostPage.Preferences);
                window.UpdateLayout();
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkBox => checkBox.Content?.ToString() == "Start Voltura Air hidden in the tray");
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkBox => checkBox.Content?.ToString() == "Show Voltura Air when the last device disconnects");
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkBox => checkBox.Content?.ToString() == "Developer mode");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Default remote mode");
                Assert.Contains(FindWpfDescendants<ToggleButton>(window), button => button.Content?.ToString() == "Kodi");
            }
            finally
            {
                window.Close();
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void MainWindowRotatesPairingCodeAfterDeviceCountChanges()
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
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector));
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Connect);
                window.UpdateLayout();
                var initialPairingUrl = window.PairingUrl;
                var token = new Uri(initialPairingUrl).Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2))
                    .First(part => part[0] == "t")[1];

                var accepted = manager.Accept("client-a", "Phone", Uri.UnescapeDataString(token), null);
                DoWpfEvents();

                Assert.True(accepted.Accepted);
                Assert.NotEqual(initialPairingUrl, window.PairingUrl);
            }
            finally
            {
                window.Close();
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void ConfirmationDialogUsesWpfThemedControls()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var dialog = new ThemedConfirmationDialog(
                "Clean up duplicates",
                "Remove 2 older disconnected duplicate pairings? Connected devices are kept.",
                "Clean up",
                "Cancel",
                ConfirmationTone.Question);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();

                Assert.Same(dialog.Resources["WindowBrush"], dialog.Background);
                Assert.Contains(FindWpfDescendants<TextBlock>(dialog), text => text.Text == "Clean up duplicates");
                Assert.Contains(FindWpfDescendants<Button>(dialog), button => button.Content?.ToString() == "Clean up");
                Assert.Contains(FindWpfDescendants<Button>(dialog), button => button.Content?.ToString() == "Cancel");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData("http://192.168.68.51:5173", "http://192.168.68.51:51395", "51395")]
    [InlineData("http://192.168.68.51:5173", "http://10.0.0.20:51395", "http://10.0.0.20:51395")]
    public void HostHintUsesCompactPortOnlyForSameHost(string clientUrl, string serverUrl, string expectedHint)
    {
        Assert.Equal(expectedHint, MainWindow.CreateHostHint(clientUrl, serverUrl));
    }

    private static IEnumerable<T> FindWpfDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T directMatch)
            {
                yield return directMatch;
            }

            if (child is not DependencyObject childObject)
            {
                continue;
            }

            foreach (var descendant in FindWpfDescendants<T>(childObject))
            {
                yield return descendant;
            }
        }
    }

    private static void DoWpfEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            frame.Continue = false;
        }), DispatcherPriority.Background);
        Dispatcher.PushFrame(frame);
    }

    private sealed class WpfApplicationScope : IDisposable
    {
        private readonly Application? _created;

        public WpfApplicationScope()
        {
            if (Application.Current is null)
            {
                _created = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }
        }

        public void Dispose()
        {
            _created?.Shutdown();
        }
    }
}
