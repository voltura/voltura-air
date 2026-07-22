using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VolturaAir.Host.Ui;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using SystemFonts = System.Windows.SystemFonts;
using WpfBinding = System.Windows.Data.Binding;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfRelativeSource = System.Windows.Data.RelativeSource;

namespace VolturaAir.Host;

public enum ConfirmationTone
{
    Information,
    Question,
    Warning
}

public sealed class ThemedConfirmationDialog : Window
{
    private const int WmMouseActivate = 0x0021;
    private const int MaActivate = 1;
    private readonly ConfirmationTone _tone;
    private HwndSource? _windowSource;

    public ThemedConfirmationDialog(
        string title,
        string message,
        string confirmText,
        string? cancelText,
        ConfirmationTone tone)
    {
        _tone = tone;

        Title = title;
        Width = 440;
        MinWidth = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = SystemFonts.MessageFontFamily;
        Padding = new Thickness(0);

        SetIcon(this);
        WpfTheme.Apply(this);

        Content = CreateContent(title, message, confirmText, cancelText);
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    public static bool Show(
        Window owner,
        string title,
        string message,
        string confirmText,
        string cancelText,
        ConfirmationTone tone)
    {
        var dialog = new ThemedConfirmationDialog(title, message, confirmText, cancelText, tone)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true;
    }

    public static void ShowInformation(
        Window owner,
        string title,
        string message,
        ConfirmationTone tone = ConfirmationTone.Information)
    {
        var dialog = new ThemedConfirmationDialog(title, message, "OK", null, tone)
        {
            Owner = owner
        };

        _ = dialog.ShowDialog();
    }

    private Grid CreateContent(string title, string message, string confirmText, string? cancelText)
    {
        var root = new Grid
        {
            Background = Brush("WindowBrush")
        };

        var panel = new Grid
        {
            Margin = new Thickness(UiTokens.SpaceXl),
        };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(UiTokens.SpaceXl) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var badge = CreateToneBadge();
        Grid.SetColumn(badge, 0);
        panel.Children.Add(badge);

        var body = new SpacingStackPanel { Spacing = UiTokens.SpaceSm };
        Grid.SetColumn(body, 2);
        body.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            LineHeight = 19,
            Foreground = Brush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(body);

        var actions = new SpacingStackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = UiTokens.SpaceSm
        };
        Grid.SetColumn(actions, 2);
        Grid.SetRow(actions, 2);

        if (!string.IsNullOrWhiteSpace(cancelText))
        {
            var cancel = CreateDialogButton(cancelText, isPrimary: false);
            cancel.IsCancel = true;
            cancel.Click += (_, _) => CloseWithResult(false);
            actions.Children.Add(cancel);
        }

        var confirm = CreateDialogButton(confirmText, isPrimary: true);
        confirm.IsDefault = true;
        confirm.Click += (_, _) => CloseWithResult(true);

        actions.Children.Add(confirm);
        panel.Children.Add(actions);

        root.Children.Add(panel);
        return root;
    }

    private Border CreateToneBadge()
    {
        var text = _tone switch
        {
            ConfirmationTone.Information => "i",
            ConfirmationTone.Warning => "!",
            _ => "?"
        };
        return new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(21),
            Background = _tone == ConfirmationTone.Warning ? Brush("DangerBrush") : Brush("AccentBrush"),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("AccentTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private Button CreateDialogButton(string text, bool isPrimary)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 38,
            MinWidth = 96,
            Padding = new Thickness(14, 6, 14, 6),
            Cursor = WpfCursors.Hand,
            BorderThickness = new Thickness(1),
            Background = isPrimary
                ? _tone == ConfirmationTone.Warning ? Brush("DangerBrush") : Brush("AccentBrush")
                : Brush("SurfaceRaisedBrush"),
            Foreground = isPrimary ? Brush("AccentTextBrush") : Brush("TextBrush"),
            BorderBrush = isPrimary
                ? _tone == ConfirmationTone.Warning ? Brush("DangerBrush") : Brush("AccentBrush")
                : Brush("BorderBrush"),
            Template = CreateButtonTemplate()
        };

        return button;
    }

    private static ControlTemplate CreateButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border))
        {
            Name = "Chrome"
        };
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty, new WpfBinding("Background") { RelativeSource = WpfRelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new WpfBinding("BorderBrush") { RelativeSource = WpfRelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new WpfBinding("BorderThickness") { RelativeSource = WpfRelativeSource.TemplatedParent });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(ContentPresenter.MarginProperty, new WpfBinding("Padding") { RelativeSource = WpfRelativeSource.TemplatedParent });
        presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button))
        {
            VisualTree = border
        };

        var mouseOver = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        mouseOver.Setters.Add(new Setter(Border.OpacityProperty, 0.92, "Chrome"));
        template.Triggers.Add(mouseOver);

        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.OpacityProperty, 0.82, "Chrome"));
        template.Triggers.Add(pressed);

        return template;
    }

    private Brush Brush(string resourceKey) => (Brush)Resources[resourceKey];

    private void CloseWithResult(bool confirmed)
    {
        try
        {
            DialogResult = confirmed;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        CloseWithResult(false);
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(OnWindowMessage);
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _windowSource?.RemoveHook(OnWindowMessage);
        _windowSource = null;
    }

    private static nint OnWindowMessage(
        nint window,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message != WmMouseActivate)
        {
            return nint.Zero;
        }

        handled = true;
        return MaActivate;
    }

    private static void SetIcon(Window window)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAir.ico");
        if (File.Exists(iconPath))
        {
            window.Icon = BitmapFrame.Create(new Uri(iconPath));
        }
    }
}
