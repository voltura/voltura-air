using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using VolturaAir.Host;
using VolturaAir.Host.Features.CustomScreens;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void PreviewWindowOwnsDeviceOrientationAndRotateAsFixedWpfControls()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var request = new CustomScreenPreviewWindowRequest(
                new Uri(
                    "http://127.0.0.1:51395/?customScreenPreview=screen.preview-1&controlDepth=false"),
                new CustomScreenViewport(360, 640, "portrait"),
                360,
                640,
                false,
                false,
                null,
                [
                    new CustomScreenPreviewDevice(
                        "client-mobile",
                        "Mobile device",
                        new CustomScreenViewport(366, 792, "portrait"),
                        true)
                ]);
            var window = new CustomScreenPreviewWindow(request);
            try
            {
                var toolbar = Assert.IsType<Border>(
                    window.FindName("PreviewToolbar"));
                var viewportHost = Assert.IsType<Grid>(
                    window.FindName("PreviewViewportHost"));
                _ = Assert.IsType<WebView2>(
                    window.FindName("PreviewBrowser"));
                var device = Assert.IsType<ComboBox>(
                    window.FindName("DeviceCombo"));
                var orientation = Assert.IsType<ComboBox>(
                    window.FindName("OrientationCombo"));
                var rotate = Assert.IsType<Button>(
                    window.FindName("RotateButton"));

                Assert.Equal(0, Grid.GetRow(toolbar));
                Assert.Equal(1, Grid.GetRow(viewportHost));
                Assert.Equal("Preview device", AutomationProperties.GetName(device));
                Assert.Equal(
                    "Preview orientation",
                    AutomationProperties.GetName(orientation));
                Assert.Equal("Rotate preview", AutomationProperties.GetName(rotate));
                Assert.Equal(11, device.Items.Count);
                Assert.Equal("Generic phone", device.SelectedItem?.ToString());
                Assert.Contains(
                    device.Items.Cast<object>(),
                    item => item.ToString() == "Mobile device");
                Assert.Contains(
                    device.Items.Cast<object>(),
                    item => item.ToString() ==
                        "Voltura 393x852 - iPhone Pro");
                Assert.Contains(
                    device.Items.Cast<object>(),
                    item => item.ToString() ==
                        "Voltura 820x1180 - iPad Air");
                Assert.Equal(
                    new CustomScreenViewport(360, 640, "portrait"),
                    window.Viewport);

                device.SelectedItem = device.Items.Cast<object>()
                    .Single(item => item.ToString() == "Mobile device");
                Assert.Equal(
                    new CustomScreenViewport(366, 792, "portrait"),
                    window.Viewport);
                orientation.SelectedIndex = 1;
                Assert.Equal(
                    new CustomScreenViewport(792, 366, "landscape"),
                    window.Viewport);
                rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(
                    new CustomScreenViewport(366, 792, "portrait"),
                    window.Viewport);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void LeavingCustomScreensClosesItsPreviewWindows()
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
            var closeCount = 0;
            var controller = new CustomScreensPageController(
                owner,
                new CustomScreenService(
                    new InMemoryCustomScreenStore(),
                    new FakeAppLaunchService()),
                new PairingManager(pairingStore.Store),
                static (_, _, _, _) => new(true, "accepted", "Opened."),
                () => closeCount++,
                new CustomScreenEditorActivityLog(NullAppLog.Instance),
                static _ => { });

            try
            {
                Assert.True(controller.TryLeavePage());
                Assert.Equal(1, closeCount);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void ButtonSizeEditsTheActiveOrientationAndCompactPreviewStaysIntrinsic()
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
            owner.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/VolturaAir.Host;component/MainWindow.Styles.xaml",
                    UriKind.Relative)
            });
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
                Assert.IsType<CheckBox>(page.FindName("OrientationLayoutsCheckBox"))
                    .IsChecked = true;
                owner.UpdateLayout();

                FindVisualDescendants<Button>(page)
                    .Single(button => AutomationProperties.GetName(button) ==
                        "Select button Play / pause")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var sizeGroup = FindVisualDescendants<Expander>(page)
                    .Single(group => AutomationProperties.GetName(group) ==
                        "Size property group");
                sizeGroup.IsExpanded = true;
                owner.UpdateLayout();
                Combo(page, "Size").SelectedItem = "compact";
                owner.UpdateLayout();

                var previewButton = FindVisualDescendants<Button>(page)
                    .Single(button => AutomationProperties.GetName(button) ==
                        "Select button Play / pause");
                Assert.Equal(72, previewButton.MinWidth);

                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "Save"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var savedButton = Assert.Single(service.GetAll())
                    .Sections[0].Buttons[0];
                Assert.Equal("compact", savedButton.Portrait?.Size);
                Assert.Equal("standard", savedButton.Landscape?.Size);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void CustomScreenHeaderSettingAndPreviewUseSavedStateAndApplicationLog()
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
            owner.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/VolturaAir.Host;component/MainWindow.Styles.xaml",
                    UriKind.Relative)
            });
            var service = new CustomScreenService(
                new InMemoryCustomScreenStore(),
                new FakeAppLaunchService());
            var appLog = new RecordingCustomScreenAppLog();
            var activity = new CustomScreenEditorActivityLog(appLog);
            string? previewedScreenId = null;
            CustomScreenViewport? editorViewport = null;
            bool? editorControlDepth = null;
            var page = new CustomScreensPageView(
                owner,
                service,
                new PairingManager(pairingStore.Store),
                openPreview: screenId =>
                {
                    previewedScreenId = screenId;
                    return new(true, "accepted", "Opened.");
                },
                openSizedPreview: (
                    screenId,
                    viewport,
                    controlDepth,
                    clientId) =>
                {
                    previewedScreenId = screenId;
                    editorViewport = viewport;
                    editorControlDepth = controlDepth;
                    return new(true, "accepted", "Opened.");
                },
                activityLog: activity);
            owner.Content = page;

            try
            {
                owner.Show();
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "New screen"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();
                var editorPreview = Assert.IsType<Button>(
                    page.FindName("EditorPreviewButton"));
                Assert.False(editorPreview.IsEnabled);

                FindVisualDescendants<Expander>(page)
                    .Single(expander => expander.Name == "LayoutOptionsExpander")
                    .IsExpanded = true;
                owner.UpdateLayout();
                var headerSetting = FindVisualDescendants<CheckBox>(page)
                    .Single(checkBox =>
                        checkBox.Content is TextBlock text &&
                        text.Text == "Show Back and screen title");
                Assert.True(headerSetting.IsChecked);
                headerSetting.IsChecked = false;
                Assert.IsType<ComboBox>(page.FindName("PreviewDeviceCombo"))
                    .SelectedIndex = 1;
                Assert.IsType<ComboBox>(page.FindName("PreviewOrientationCombo"))
                    .SelectedIndex = 1;
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "Save"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();
                Assert.True(editorPreview.IsEnabled);
                editorPreview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "Back"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();

                var saved = Assert.Single(service.GetAll());
                Assert.False(saved.ShowNavigationHeader);
                Assert.Equal(
                    new CustomScreenViewport(1180, 800, "landscape"),
                    editorViewport);
                Assert.Equal(
                    AppAppearanceSettings.DeviceControlDepth(),
                    editorControlDepth);
                FindVisualDescendants<Button>(page)
                    .Single(button =>
                        !ReferenceEquals(button, editorPreview) &&
                        Equals(button.Content, "Preview"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(saved.Id, previewedScreenId);
                Assert.Equal(
                    [
                        "custom_screen_save",
                        "custom_screen_preview",
                        "custom_screen_preview"
                    ],
                    appLog.Entries.Select(entry => entry.Action));
                Assert.All(appLog.Entries, entry => Assert.Null(entry.Detail));
            }
            finally
            {
                owner.Close();
            }
        });
    }

    private sealed class RecordingCustomScreenAppLog : IAppLogWriter
    {
        public List<AppLogEntry> Entries { get; } = [];

        public void Write(AppLogEntry entry) => Entries.Add(entry);
    }
}
