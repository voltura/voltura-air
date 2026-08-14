using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Features.Connect;
using VolturaAir.Host.Features.Connection;
using VolturaAir.Host.Features.CustomScreens;
using VolturaAir.Host.Features.Devices;
using VolturaAir.Host.Features.Diagnostics;
using VolturaAir.Host.Features.Preferences;
using VolturaAir.Host.Features.PhoneWebcam;
using VolturaAir.Host.Features.Presentations;
using VolturaAir.Host.Ui;
using Button = System.Windows.Controls.Button;

namespace VolturaAir.Host;

internal sealed class MainWindowNavigationController(
    HostVisualFactory visuals,
    IReadOnlyDictionary<HostPage, Button> navigationButtons,
    TextBlock pageTitle,
    TextBlock pageSubtitle,
    FrameworkElement pageTypeBadge,
    ContentControl pageContent,
    ConnectPageController connectPage,
    DevicesPageController devicesPage,
    CustomScreensPageController customScreensPage,
    PresentationsPageController presentationsPage,
    PhoneWebcamPageController phoneWebcamPage,
    ConnectionPageController connectionPage,
    PreferencesPageController preferencesPage,
    DiagnosticsPageController diagnosticsPage,
    Action refreshStatus)
{
    public HostPage ActivePage { get; private set; }

    public bool TrySelect(HostPage requestedPage)
    {
        var previousPage = ActivePage;
        var page = requestedPage;
        if (previousPage == HostPage.Connection &&
            page != HostPage.Connection &&
            !connectionPage.TryLeavePage())
        {
            return false;
        }

        if (previousPage == HostPage.CustomScreens &&
            page != HostPage.CustomScreens &&
            !customScreensPage.TryLeavePage())
        {
            return false;
        }

        if (ActivePage == HostPage.Devices && page != HostPage.Devices)
        {
            devicesPage.ResetDisclosureState();
        }

        if (previousPage == HostPage.PhoneWebcam && page != HostPage.PhoneWebcam)
        {
            phoneWebcamPage.StopPreview();
        }

        ActivePage = page;
        refreshStatus();
        RefreshTheme();
        pageTypeBadge.Visibility = Visibility.Collapsed;
        pageSubtitle.Visibility = Visibility.Visible;
        ShowPage(page, previousPage);
        return true;
    }

    public void RefreshTheme()
    {
        foreach (var (page, button) in navigationButtons)
        {
            var isActive = page == ActivePage;
            button.Tag = isActive ? "Selected" : null;
            button.Background = visuals.Brush(
                isActive ? "AccentBrush" : "SurfaceRaisedBrush");
            button.Foreground = visuals.Brush(
                isActive ? "AccentTextBrush" : "TextBrush");
            button.BorderBrush = visuals.Brush(
                isActive ? "AccentBrush" : "BorderBrush");
        }
    }

    public string GetToastTitle() => ActivePage switch
    {
        HostPage.Connect => "Connect",
        HostPage.Devices => "Devices",
        HostPage.CustomScreens => "Custom screens",
        HostPage.Presentations => "Presentations",
        HostPage.PhoneWebcam => "Phone webcam",
        HostPage.Connection => "Connection",
        HostPage.Preferences => "Preferences",
        HostPage.Diagnostics => "Diagnostics",
        _ => "Voltura Air"
    };

    private void ShowPage(HostPage page, HostPage previousPage)
    {
        switch (page)
        {
            case HostPage.Connect:
                pageTitle.Text = "Connect";
                pageSubtitle.Text = connectPage.PageSubtitle;
                pageContent.Content = connectPage.CreateView();
                break;
            case HostPage.Devices:
                pageTitle.Text = "Devices";
                pageSubtitle.Text = "Manage trusted devices, active connections, and per-device permissions.";
                pageContent.Content = devicesPage.CreateView();
                break;
            case HostPage.CustomScreens:
                pageTitle.Text = "Custom screens";
                pageSubtitle.Text = "Design reusable controls and assign them to paired devices.";
                pageContent.Content = customScreensPage.CreateView();
                break;
            case HostPage.Presentations:
                pageTitle.Text = "Presentations";
                pageSubtitle.Text = "Saved presentations";
                pageContent.Content = presentationsPage.CreateView();
                break;
            case HostPage.PhoneWebcam:
                pageTitle.Text = "Phone webcam";
                pageSubtitle.Text = "Use your phone as a camera in Windows apps.";
                pageContent.Content = phoneWebcamPage.CreateView();
                break;
            case HostPage.Connection:
                pageTitle.Text = "Connection";
                pageSubtitle.Text = "Voltura Air selects connection settings automatically. Change them only if a device cannot connect.";
                pageContent.Content = connectionPage.CreateView(
                    preserveState: previousPage == HostPage.Connection);
                break;
            case HostPage.Preferences:
                pageTitle.Text = "Preferences";
                pageSubtitle.Text = "Startup, alerts, permissions, device defaults, and theme.";
                pageContent.Content = preferencesPage.CreateView();
                preferencesPage.RestoreScrollPosition();
                break;
            case HostPage.Diagnostics:
                pageTitle.Text = "Diagnostics";
                pageSubtitle.Text = "Review application activity or inspect system details for troubleshooting.";
                pageContent.Content = diagnosticsPage.CreateView();
                break;
        }
    }
}

public enum HostPage
{
    Connect,
    Devices,
    CustomScreens,
    Presentations,
    PhoneWebcam,
    Connection,
    Preferences,
    Diagnostics
}
