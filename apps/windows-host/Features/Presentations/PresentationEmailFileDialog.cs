using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VolturaAir.Host.Ui;
using WpfButton = System.Windows.Controls.Button;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace VolturaAir.Host.Features.Presentations;

internal enum PresentationEmailFileChoice
{
    Cancel,
    ReportOnly,
    IncludePresentationFiles
}

internal sealed class PresentationEmailFileDialog : Window
{
    private PresentationEmailFileDialog(Window owner, bool multiple)
    {
        Owner = owner;
        Title = "Email presentation";
        Width = 470;
        MinWidth = 390;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = System.Windows.SystemFonts.MessageFontFamily;
        WpfTheme.Apply(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("MainWindow.Styles.xaml", UriKind.Relative)
        });
        Content = CreateContent(multiple);
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                CloseWithChoice(PresentationEmailFileChoice.Cancel);
            }
        };
    }

    public PresentationEmailFileChoice Choice { get; private set; }

    public static PresentationEmailFileChoice Show(Window owner, bool multiple)
    {
        var dialog = new PresentationEmailFileDialog(owner, multiple);
        _ = dialog.ShowDialog();
        return dialog.Choice;
    }

    private Grid CreateContent(bool multiple)
    {
        var root = new Grid { Margin = new Thickness(UiTokens.SpaceXl) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(UiTokens.SpaceXl) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new SpacingStackPanel { Spacing = UiTokens.SpaceSm };
        body.Children.Add(new TextBlock
        {
            Text = multiple ? "Include linked presentation sources?" : "Include the presentation source?",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = multiple
                ? "Every selected statistics report will be included. Available linked presentation files will also be attached, and presentation URLs remain clickable in the email."
                : "The statistics report is always included. The linked presentation file can also be attached, and a presentation URL remains clickable in the email.",
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(body);

        var actions = new SpacingStackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Spacing = UiTokens.SpaceSm
        };
        Grid.SetRow(actions, 2);
        var cancel = CreateButton("Cancel", primary: false);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => CloseWithChoice(PresentationEmailFileChoice.Cancel);
        var reportOnly = CreateButton("Report only", primary: false);
        reportOnly.Click += (_, _) => CloseWithChoice(PresentationEmailFileChoice.ReportOnly);
        var include = CreateButton(multiple ? "Include linked sources" : "Include source", primary: true);
        include.IsDefault = true;
        include.Click += (_, _) => CloseWithChoice(PresentationEmailFileChoice.IncludePresentationFiles);
        actions.Children.Add(cancel);
        actions.Children.Add(reportOnly);
        actions.Children.Add(include);
        root.Children.Add(actions);
        return root;
    }

    private static WpfButton CreateButton(string label, bool primary)
    {
        var button = new WpfButton
        {
            Content = label,
            FocusVisualStyle = null
        };
        if (primary)
        {
            button.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        }

        return button;
    }

    private void CloseWithChoice(PresentationEmailFileChoice choice)
    {
        Choice = choice;
        try
        {
            DialogResult = choice != PresentationEmailFileChoice.Cancel;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }
}
