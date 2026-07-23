using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfPoint = System.Windows.Point;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace VolturaAir.Host.Ui;

public sealed class WatermarkedTextBox : WpfTextBox
{
    private WatermarkAdorner? _watermarkAdorner;
    private AdornerLayer? _adornerLayer;

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder),
        typeof(string),
        typeof(WatermarkedTextBox),
        new FrameworkPropertyMetadata(string.Empty, OnPlaceholderPropertyChanged));

    public static readonly DependencyProperty PlaceholderForegroundProperty = DependencyProperty.Register(
        nameof(PlaceholderForeground),
        typeof(WpfBrush),
        typeof(WatermarkedTextBox),
        new FrameworkPropertyMetadata(
            System.Windows.SystemColors.GrayTextBrush,
            OnPlaceholderPropertyChanged));

    public WatermarkedTextBox()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => _watermarkAdorner?.InvalidateVisual();
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public WpfBrush PlaceholderForeground
    {
        get => (WpfBrush)GetValue(PlaceholderForegroundProperty);
        set => SetValue(PlaceholderForegroundProperty, value);
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        _watermarkAdorner?.InvalidateVisual();
    }

    private static void OnPlaceholderPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        _ = eventArgs;
        ((WatermarkedTextBox)dependencyObject)._watermarkAdorner?.InvalidateVisual();
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (_watermarkAdorner is not null)
        {
            return;
        }

        _adornerLayer = AdornerLayer.GetAdornerLayer(this);
        if (_adornerLayer is null)
        {
            return;
        }

        _watermarkAdorner = new WatermarkAdorner(this);
        _adornerLayer.Add(_watermarkAdorner);
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (_watermarkAdorner is null || _adornerLayer is null)
        {
            return;
        }

        _adornerLayer.Remove(_watermarkAdorner);
        _watermarkAdorner = null;
        _adornerLayer = null;
    }

    private sealed class WatermarkAdorner(WatermarkedTextBox textBox) : Adorner(textBox)
    {
        private Rect _measuredTextOrigin = Rect.Empty;

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (!textBox.IsVisible)
            {
                return;
            }

            var currentTextOrigin = textBox.GetRectFromCharacterIndex(0);
            if (!currentTextOrigin.IsEmpty)
            {
                _measuredTextOrigin = currentTextOrigin;
            }

            if (textBox.Text.Length > 0 || textBox.Placeholder.Length == 0)
            {
                return;
            }

            var textOrigin = currentTextOrigin.IsEmpty ? _measuredTextOrigin : currentTextOrigin;
            if (textOrigin.IsEmpty)
            {
                return;
            }

            var availableWidth = Math.Max(
                0,
                textBox.ActualWidth - textOrigin.Left - textBox.BorderThickness.Right - textBox.Padding.Right);
            if (availableWidth <= 0)
            {
                return;
            }

            var placeholderText = new FormattedText(
                textBox.Placeholder,
                CultureInfo.CurrentUICulture,
                textBox.FlowDirection,
                new Typeface(textBox.FontFamily, textBox.FontStyle, textBox.FontWeight, textBox.FontStretch),
                textBox.FontSize,
                textBox.PlaceholderForeground,
                VisualTreeHelper.GetDpi(textBox).PixelsPerDip)
            {
                MaxLineCount = 1,
                MaxTextWidth = availableWidth,
                Trimming = TextTrimming.CharacterEllipsis
            };
            var top = textOrigin.Top + Math.Max(0, (textOrigin.Height - placeholderText.Height) / 2);
            drawingContext.DrawText(placeholderText, new WpfPoint(textOrigin.Left, top));
        }
    }
}
