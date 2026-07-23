using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using VolturaAir.Host.Ui;
using WpfButton = System.Windows.Controls.Button;
using WpfClipboard = System.Windows.Clipboard;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace VolturaAir.Host.Features.Presentations;

internal sealed class PresentationUrlDialog : Window
{
    private readonly string? _currentUrl;
    private readonly WpfTextBox _urlInput;
    private readonly TextBlock _errorText;

    private PresentationUrlDialog(Window owner, string? currentUrl)
    {
        _currentUrl = currentUrl;
        Owner = owner;
        Title = "Presentation URL";
        Width = 500;
        MinWidth = 400;
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

        _urlInput = new WpfTextBox
        {
            Text = currentUrl ?? string.Empty,
            MaxLength = 2048,
            FocusVisualStyle = null
        };
        AutomationProperties.SetName(_urlInput, "Presentation URL");
        _urlInput.TextChanged += (_, _) => ClearError();
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
            _urlInput.Focus();
            _urlInput.SelectAll();
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

    public string ResultUrl { get; private set; } = string.Empty;

    public static string? Show(Window owner, string? currentUrl)
    {
        var dialog = new PresentationUrlDialog(owner, currentUrl);
        return dialog.ShowDialog() == true ? dialog.ResultUrl : null;
    }

    private Grid CreateContent()
    {
        var root = new Grid { Margin = new Thickness(UiTokens.SpaceXl) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(UiTokens.SpaceXl) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new SpacingStackPanel { Spacing = UiTokens.SpaceSm };
        body.Children.Add(new TextBlock
        {
            Text = "Presentation URL",
            FontSize = 18,
            FontWeight = FontWeights.Bold
        });
        body.Children.Add(new TextBlock
        {
            Text = "Link a Google Slides page or another browser-based presentation.",
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = "Web address",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        });
        body.Children.Add(_urlInput);
        var inputActions = new SpacingWrapPanel
        {
            HorizontalSpacing = UiTokens.SpaceSm,
            VerticalSpacing = UiTokens.SpaceSm
        };
        var paste = new WpfButton { Content = "Paste from clipboard", FocusVisualStyle = null };
        paste.Click += (_, _) => PasteFromClipboard();
        var useHttps = new WpfButton { Content = "Use https://", FocusVisualStyle = null };
        useHttps.Click += (_, _) => ApplyScheme("https://");
        var useHttp = new WpfButton { Content = "Use http://", FocusVisualStyle = null };
        useHttp.Click += (_, _) => ApplyScheme("http://");
        inputActions.Children.Add(paste);
        inputActions.Children.Add(useHttps);
        inputActions.Children.Add(useHttp);
        body.Children.Add(inputActions);
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
        var save = new WpfButton { Content = "Save", IsDefault = true, FocusVisualStyle = null };
        save.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        save.Click += (_, _) => Save();
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        root.Children.Add(actions);
        return root;
    }

    private void Save()
    {
        var candidate = _urlInput.Text.Trim();
        if (candidate.Length == 0 && !string.IsNullOrWhiteSpace(_currentUrl))
        {
            ResultUrl = _currentUrl;
            DialogResult = true;
            return;
        }

        if (candidate.Length == 0 ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
        {
            _errorText.Text = "Enter a complete http:// or https:// web address.";
            _errorText.Visibility = Visibility.Visible;
            _urlInput.Focus();
            return;
        }

        ResultUrl = uri.AbsoluteUri;
        DialogResult = true;
    }

    private void ClearError()
    {
        _errorText.Text = string.Empty;
        _errorText.Visibility = Visibility.Collapsed;
    }

    private void PasteFromClipboard()
    {
        try
        {
            if (!WpfClipboard.ContainsText())
            {
                _errorText.Text = "The clipboard does not contain text.";
                _errorText.Visibility = Visibility.Visible;
                return;
            }

            _urlInput.Text = WpfClipboard.GetText().Trim();
            _urlInput.CaretIndex = _urlInput.Text.Length;
            _urlInput.Focus();
        }
        catch (ExternalException)
        {
            _errorText.Text = "Windows could not read the clipboard. Try copying the address again.";
            _errorText.Visibility = Visibility.Visible;
        }
    }

    private void ApplyScheme(string scheme)
    {
        var value = _urlInput.Text.Trim();
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = value["https://".Length..];
        }
        else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            value = value["http://".Length..];
        }

        _urlInput.Text = scheme + value;
        _urlInput.CaretIndex = _urlInput.Text.Length;
        _urlInput.Focus();
    }
}
