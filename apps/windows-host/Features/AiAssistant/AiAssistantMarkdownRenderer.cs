using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownInline = Markdig.Syntax.Inlines.Inline;
using MarkdownBlock = Markdig.Syntax.Block;
using WpfFontStyle = System.Windows.FontStyle;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfPanel = System.Windows.Controls.Panel;

namespace VolturaAir.Host.Features.AiAssistant;

internal static class AiAssistantMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UsePipeTables()
        .Build();

    internal static FrameworkElement Create(string markdown, IUrlOpenService urlOpenService)
    {
        var panel = new VolturaAir.Host.Ui.SpacingStackPanel { Spacing = 8 };
        MarkdownDocument document = Markdown.Parse(markdown, Pipeline);
        foreach (MarkdownBlock block in document)
        {
            AddBlock(panel, block, urlOpenService, 0);
        }
        return panel;
    }

    private static void AddBlock(WpfPanel panel, MarkdownBlock block, IUrlOpenService urlOpenService, int listDepth)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                panel.Children.Add(CreateTextBlock(paragraph.Inline, urlOpenService));
                break;
            case HeadingBlock heading:
                TextBlock headingText = CreateTextBlock(heading.Inline, urlOpenService);
                headingText.FontWeight = FontWeights.SemiBold;
                headingText.FontSize = heading.Level <= 2 ? 18 : 15;
                panel.Children.Add(headingText);
                break;
            case ListBlock list:
                int index = int.TryParse(list.OrderedStart, out int orderedStart) ? orderedStart : 1;
                foreach (ListItemBlock item in list.OfType<ListItemBlock>())
                {
                    var row = new Grid { Margin = new Thickness(listDepth * 12, 0, 0, 0) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var marker = new TextBlock
                    {
                        Text = list.IsOrdered ? $"{index++}." : "•"
                    };
                    marker.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                    row.Children.Add(marker);
                    var content = new VolturaAir.Host.Ui.SpacingStackPanel { Spacing = 6 };
                    Grid.SetColumn(content, 2);
                    row.Children.Add(content);
                    foreach (MarkdownBlock child in item) AddBlock(content, child, urlOpenService, listDepth + 1);
                    panel.Children.Add(row);
                }
                break;
            case FencedCodeBlock fenced:
                panel.Children.Add(CreateCodeBlock(fenced.Lines.ToString()));
                break;
            case CodeBlock code:
                panel.Children.Add(CreateCodeBlock(code.Lines.ToString()));
                break;
            case QuoteBlock quote:
                var quoteBorder = new Border
                {
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(10, 0, 0, 0)
                };
                quoteBorder.SetResourceReference(Border.BorderBrushProperty, "MutedTextBrush");
                var quotePanel = new VolturaAir.Host.Ui.SpacingStackPanel { Spacing = 6 };
                quoteBorder.Child = quotePanel;
                foreach (MarkdownBlock child in quote) AddBlock(quotePanel, child, urlOpenService, listDepth);
                panel.Children.Add(quoteBorder);
                break;
            case ContainerBlock container when block is not HtmlBlock:
                foreach (MarkdownBlock child in container) AddBlock(panel, child, urlOpenService, listDepth);
                break;
        }
    }

    private static TextBlock CreateTextBlock(ContainerInline? inline, IUrlOpenService urlOpenService)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        if (inline is not null) AddInlines(text.Inlines, inline, urlOpenService, FontWeights.Normal, FontStyles.Normal);
        return text;
    }

    private static void AddInlines(
        InlineCollection destination,
        ContainerInline container,
        IUrlOpenService urlOpenService,
        FontWeight weight,
        WpfFontStyle style)
    {
        for (MarkdownInline? current = container.FirstChild; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    destination.Add(new Run(literal.Content.ToString()) { FontWeight = weight, FontStyle = style });
                    break;
                case CodeInline code:
                    destination.Add(new Run(code.Content)
                    {
                        FontFamily = new WpfFontFamily("Cascadia Mono, Consolas"),
                    });
                    break;
                case LineBreakInline:
                    destination.Add(new LineBreak());
                    break;
                case EmphasisInline emphasis:
                    AddInlines(
                        destination,
                        emphasis,
                        urlOpenService,
                        emphasis.DelimiterCount >= 2 ? FontWeights.SemiBold : weight,
                        emphasis.DelimiterCount == 1 ? FontStyles.Italic : style);
                    break;
                case LinkInline link when link.IsImage:
                    string alt = GetPlainText(link);
                    destination.Add(new Run(string.IsNullOrWhiteSpace(alt) ? "Image omitted" : $"Image omitted: {alt}")
                    {
                        FontStyle = FontStyles.Italic
                    });
                    break;
                case LinkInline link:
                    string? target = link.GetDynamicUrl?.Invoke() ?? link.Url;
                    if (target is not null && UrlOpenService.TryNormalize(target, out _, out _, out _))
                    {
                        var hyperlink = new Hyperlink();
                        AddInlines(hyperlink.Inlines, link, urlOpenService, weight, style);
                        hyperlink.Cursor = System.Windows.Input.Cursors.Hand;
                        hyperlink.Click += (_, _) => _ = urlOpenService.Execute(target);
                        destination.Add(hyperlink);
                    }
                    else
                    {
                        AddInlines(destination, link, urlOpenService, weight, style);
                    }
                    break;
            }
        }
    }

    private static string GetPlainText(ContainerInline container)
    {
        var text = new System.Text.StringBuilder();
        for (MarkdownInline? current = container.FirstChild; current is not null; current = current.NextSibling)
        {
            if (current is LiteralInline literal) text.Append(literal.Content);
            else if (current is ContainerInline nested) text.Append(GetPlainText(nested));
        }
        return text.ToString();
    }

    private static Border CreateCodeBlock(string code)
    {
        var text = new TextBlock
        {
            Text = code,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new WpfFontFamily("Cascadia Mono, Consolas")
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Child = text
        };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        return border;
    }
}
