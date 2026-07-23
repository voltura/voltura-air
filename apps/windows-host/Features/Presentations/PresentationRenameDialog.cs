using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using VolturaAir.Host.Ui;
using WpfButton = System.Windows.Controls.Button;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfSystemFonts = System.Windows.SystemFonts;

namespace VolturaAir.Host.Features.Presentations;

internal sealed class PresentationRenameDialog : Window
{
    private readonly string _currentTitle;
    private readonly WatermarkedTextBox _titleInput;
    private readonly TextBlock _errorText;

    private PresentationRenameDialog(Window owner, string currentTitle)
    {
        _currentTitle = currentTitle;
        Owner = owner;
        Title = "Rename presentation";
        Width = 460;
        MinWidth = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = WpfSystemFonts.MessageFontFamily;
        WpfTheme.Apply(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("MainWindow.Styles.xaml", UriKind.Relative)
        });

        _titleInput = new WatermarkedTextBox
        {
            Text = currentTitle,
            Placeholder = currentTitle,
            MaxLength = PresentationReportTitle.MaxLength,
            FocusVisualStyle = null
        };
        _titleInput.SetResourceReference(
            WatermarkedTextBox.PlaceholderForegroundProperty,
            "MutedTextBrush");
        AutomationProperties.SetName(_titleInput, "Presentation name");
        AutomationProperties.SetHelpText(_titleInput, $"Original name: {currentTitle}");
        _titleInput.TextChanged += (_, _) => ClearError();

        _errorText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        _errorText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        AutomationProperties.SetLiveSetting(_errorText, AutomationLiveSetting.Polite);

        Content = CreateContent();
        Loaded += (_, _) =>
        {
            _titleInput.Focus();
            _titleInput.SelectAll();
        };
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                DialogResult = false;
            }
        };
    }

    public string ResultTitle { get; private set; } = string.Empty;

    public static string? Show(Window owner, string currentTitle)
    {
        var dialog = new PresentationRenameDialog(owner, currentTitle);
        return dialog.ShowDialog() == true ? dialog.ResultTitle : null;
    }

    private Grid CreateContent()
    {
        var root = new Grid
        {
            Margin = new Thickness(UiTokens.SpaceXl)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(UiTokens.SpaceXl) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new SpacingStackPanel { Spacing = UiTokens.SpaceSm };
        body.Children.Add(new TextBlock
        {
            Text = "Rename presentation",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = "The name is also used as the suggested export filename.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = "Presentation name",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        });
        body.Children.Add(_titleInput);
        body.Children.Add(_errorText);
        root.Children.Add(body);

        var actions = new SpacingStackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Spacing = UiTokens.SpaceSm
        };
        Grid.SetRow(actions, 2);
        var cancel = new WpfButton { Content = "Cancel", IsCancel = true, FocusVisualStyle = null };
        cancel.Click += (_, _) => DialogResult = false;
        var save = new WpfButton
        {
            Content = "Save",
            IsDefault = true,
            FocusVisualStyle = null
        };
        save.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButtonStyle");
        save.Click += (_, _) => Save();
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        root.Children.Add(actions);
        return root;
    }

    private void Save()
    {
        if (!PresentationReportTitle.TryNormalize(_titleInput.Text, _currentTitle, out var title, out var error))
        {
            _errorText.Text = error;
            _errorText.Visibility = Visibility.Visible;
            _titleInput.Focus();
            return;
        }

        ResultTitle = title;
        DialogResult = true;
    }

    private void ClearError()
    {
        _errorText.Text = string.Empty;
        _errorText.Visibility = Visibility.Collapsed;
    }

}
