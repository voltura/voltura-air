#if DEBUG
using System.Windows.Controls;
using VolturaAir.Host.Features.Preferences;
using WpfSize = System.Windows.Size;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private static readonly WpfSize StandardSiteScreenshotSize = new(1160, 760);
    private static readonly WpfSize EditorSiteScreenshotSize = new(1600, 900);

    internal async Task RenderSiteScreenshotAsync(string[] args, string outputPath)
    {
        var size = PrepareSiteScreenshot(args);
        await WpfPngRenderer.SaveAsync(WindowScrollViewer, Background, outputPath, size);
    }

    private WpfSize PrepareSiteScreenshot(string[] args)
    {
        var preferencesSection = GetOption(args, "--site-screenshot-preferences-section");
        if (args.Contains("--site-screenshot-custom-screens", StringComparer.OrdinalIgnoreCase))
        {
            SelectPage(HostPage.CustomScreens);
            _customScreensPage.OpenFirstForScreenshot();
            return EditorSiteScreenshotSize;
        }

        if (args.Contains("--site-screenshot-relay-connection", StringComparer.OrdinalIgnoreCase))
        {
            SelectPage(HostPage.Connection);
            _connectionPage.ShowRelayForScreenshot();
            return StandardSiteScreenshotSize;
        }

        if (args.Contains("--presentation-demo-data", StringComparer.OrdinalIgnoreCase))
        {
            SelectPage(HostPage.Presentations);
            return StandardSiteScreenshotSize;
        }

        if (!string.IsNullOrWhiteSpace(preferencesSection))
        {
            SelectPage(HostPage.Preferences);
            if (PageContent.Content is PreferencesPageView preferences)
            {
                preferences.FindSection(preferencesSection)?.SetCurrentValue(
                    Expander.IsExpandedProperty,
                    true);
            }
            return StandardSiteScreenshotSize;
        }

        SelectPage(HostPage.Connect);
        return StandardSiteScreenshotSize;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index += 1)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < args.Length ? args[index + 1] : null;
            }
        }

        return null;
    }
}
#endif
