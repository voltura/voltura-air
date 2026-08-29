using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using VolturaAir.Host.Features.AiAssistant;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace VolturaAir.Host.Tests;

public sealed class AiAssistantMarkdownRendererTests
{
    [Fact]
    public void KeepsImagesInertAndOpensLinksOnlyAfterClick()
    {
        RunOnStaThread(() =>
        {
            var urls = new RecordingUrlOpenService();
            FrameworkElement rendered = AiAssistantMarkdownRenderer.Create(
                "![private image](https://example.test/image.png) [Documentation](https://example.test/docs)",
                urls);
            Inline[] inlines = Descendants(rendered)
                .OfType<WpfTextBlock>()
                .SelectMany(text => text.Inlines.Cast<Inline>())
                .ToArray();

            Assert.Contains(inlines.OfType<Run>(), run => run.Text == "Image omitted: private image");
            Hyperlink link = Assert.Single(inlines.OfType<Hyperlink>());
            Assert.Empty(urls.Opened);

            link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

            Assert.Equal(["https://example.test/docs"], urls.Opened);
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        if (root is not Panel panel) yield break;
        foreach (UIElement child in panel.Children)
        {
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private sealed class RecordingUrlOpenService : IUrlOpenService
    {
        internal List<string> Opened { get; } = [];
        public UrlOpenExecutionResult Execute(string value)
        {
            Opened.Add(value);
            return new(true, "accepted", "Open request sent.", value);
        }
    }
}
