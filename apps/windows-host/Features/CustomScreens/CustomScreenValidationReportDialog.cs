using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VolturaAir.Host.Ui;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenValidationReportDialog : Window
{
    private readonly Action<CustomScreenValidationFinding> _selectFinding;

    public CustomScreenValidationReportDialog(
        CustomScreenValidationReport report,
        Action<CustomScreenValidationFinding> selectFinding)
    {
        _selectFinding = selectFinding;
        Title = "Custom Screen validation";
        Width = 680;
        Height = 640;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        WpfTheme.Apply(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "/VolturaAir.Host;component/MainWindow.Styles.xaml",
                UriKind.Relative)
        });
        Content = CreateContent(report);
    }

    private Grid CreateContent(CustomScreenValidationReport report)
    {
        var root = new Grid { Margin = new Thickness(UiTokens.SpaceXl) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(UiTokens.SpaceLg) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(UiTokens.SpaceLg) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var blocking = report.Findings.Count(finding =>
            finding.Severity == CustomScreenValidationSeverity.CannotSave);
        var warnings = report.Findings.Count - blocking;
        var heading = new SpacingStackPanel { Spacing = UiTokens.SpaceXs };
        heading.Children.Add(Text(
            blocking == 0 ? "Validation complete" : "Review required before Save",
            22,
            FontWeights.Bold,
            "TextBrush"));
        heading.Children.Add(Text(
            blocking == 0
                ? $"Save remains available. {warnings} potential issue{(warnings == 1 ? "" : "s")} found."
                : $"{blocking} existing save-contract issue{(blocking == 1 ? "" : "s")} and {warnings} advisory warning{(warnings == 1 ? "" : "s")} found.",
            13,
            FontWeights.Normal,
            "MutedTextBrush"));
        root.Children.Add(heading);

        var content = new SpacingStackPanel { Spacing = UiTokens.SpaceMd };
        AddGroup(
            content,
            "Cannot save",
            report.Findings.Where(finding =>
                finding.Severity == CustomScreenValidationSeverity.CannotSave),
            "DangerBrush");
        AddGroup(
            content,
            "Potential issues",
            report.Findings.Where(finding =>
                finding.Severity == CustomScreenValidationSeverity.Warning),
            "AccentBrush");
        if (report.PassedChecks.Count > 0)
        {
            content.Children.Add(Text("Passed checks", 16, FontWeights.SemiBold, "TextBrush"));
            foreach (var check in report.PassedChecks)
            {
                content.Children.Add(CreatePassedCheck(check));
            }
        }

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        var close = new Button
        {
            Content = "Close",
            MinWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        close.SetResourceReference(Button.StyleProperty, "PrimaryButtonStyle");
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 4);
        root.Children.Add(close);
        return root;
    }

    private void AddGroup(
        System.Windows.Controls.Panel content,
        string title,
        IEnumerable<CustomScreenValidationFinding> source,
        string accentResource)
    {
        var findings = source.ToArray();
        if (findings.Length == 0)
        {
            return;
        }

        content.Children.Add(Text(title, 16, FontWeights.SemiBold, "TextBrush"));
        foreach (var finding in findings)
        {
            content.Children.Add(CreateFinding(finding, accentResource));
        }
    }

    private Border CreateFinding(
        CustomScreenValidationFinding finding,
        string accentResource)
    {
        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(UiTokens.SpaceMd)
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceMd) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var accent = new Border { CornerRadius = new CornerRadius(2) };
        accent.SetResourceReference(Border.BackgroundProperty, accentResource);
        body.Children.Add(accent);

        var details = new SpacingStackPanel { Spacing = UiTokens.SpaceXs };
        details.Children.Add(Text(finding.Title, 14, FontWeights.SemiBold, "TextBrush"));
        details.Children.Add(Text(finding.Message, 13, FontWeights.Normal, "MutedTextBrush"));
        details.Children.Add(Text($"Suggestion: {finding.Resolution}", 13, FontWeights.Normal, "TextBrush"));
        if (finding.SectionId is not null)
        {
            var select = new Button
            {
                Content = finding.ButtonId is null ? "Select panel" : "Select button",
                MinWidth = 104,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, UiTokens.SpaceXs, 0, 0)
            };
            select.SetResourceReference(Button.StyleProperty, "StandardButtonStyle");
            select.Click += (_, _) =>
            {
                _selectFinding(finding);
                Close();
            };
            details.Children.Add(select);
        }
        Grid.SetColumn(details, 2);
        body.Children.Add(details);
        card.Child = body;
        return card;
    }

    private static Border CreatePassedCheck(string message)
    {
        var text = Text($"✓  {message}", 13, FontWeights.Normal, "SuccessStrongBrush");
        var card = new Border
        {
            Padding = new Thickness(UiTokens.SpaceSm, UiTokens.SpaceXs,
                UiTokens.SpaceSm, UiTokens.SpaceXs),
            CornerRadius = new CornerRadius(8),
            Child = text
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
        return card;
    }

    private static TextBlock Text(
        string value,
        double size,
        FontWeight weight,
        string foregroundResource)
    {
        var text = new TextBlock
        {
            Text = value,
            FontSize = size,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, foregroundResource);
        return text;
    }
}
