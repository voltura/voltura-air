using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VolturaAir.Host.Ui;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Point = System.Windows.Point;
using TextBox = System.Windows.Controls.TextBox;

namespace VolturaAir.Host.Features.Preferences;

internal static class ColorPickerPopup
{
    public static Popup Create(
        HostVisualFactory visuals,
        Button popupOwner,
        Func<uint> getCurrentColor,
        Action<uint> apply)
    {
        var draft = getCurrentColor();
        var (hue, saturation, value) = RgbToHsv(draft);
        var synchronizing = false;
        var input = new TextBox { Text = $"#{draft:X6}", Width = 104 };
        var swatchBrush = CreateBrush(draft);
        var swatch = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(5),
            Background = swatchBrush,
            BorderBrush = visuals.Brush("BorderBrush"),
            BorderThickness = new Thickness(1)
        };
        var surface = new Canvas { Width = 184, Height = 116, Cursor = Cursors.Cross };
        var saturationBrush = new LinearGradientBrush(Colors.White, CreateColor(HsvToRgb(hue, 1, 1)), 0);
        var saturationLayer = new Border { Width = 184, Height = 116, Background = saturationBrush };
        var valueLayer = new Border
        {
            Width = 184,
            Height = 116,
            Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90)
        };
        var marker = new Ellipse
        {
            Width = 12,
            Height = 12,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        surface.Children.Add(saturationLayer);
        surface.Children.Add(valueLayer);
        surface.Children.Add(marker);
        var hueSlider = new Slider
        {
            Style = visuals.Style("ModernSliderStyle"),
            Minimum = 0,
            Maximum = 360,
            IsMoveToPointEnabled = true,
            Width = 184,
            Value = hue
        };
        var popup = new Popup
        {
            PlacementTarget = popupOwner,
            Placement = PlacementMode.Bottom,
            StaysOpen = true,
            AllowsTransparency = true
        };

        void RefreshHue() => saturationBrush.GradientStops[1].Color = CreateColor(HsvToRgb(hue, 1, 1));
        void RefreshMarker()
        {
            Canvas.SetLeft(marker, saturation * surface.Width - marker.Width / 2);
            Canvas.SetTop(marker, (1 - value) * surface.Height - marker.Height / 2);
        }

        void UpdateDraft(uint color, bool updateInput = true)
        {
            draft = color;
            swatchBrush.Color = CreateColor(draft);
            if (updateInput)
            {
                synchronizing = true;
                input.Text = $"#{draft:X6}";
                synchronizing = false;
            }
        }

        hueSlider.ValueChanged += (_, _) =>
        {
            if (synchronizing)
            {
                return;
            }

            hue = hueSlider.Value;
            RefreshHue();
            UpdateDraft(HsvToRgb(hue, saturation, value));
        };

        void PickSurface(Point point)
        {
            saturation = Math.Clamp(point.X / surface.Width, 0, 1);
            value = Math.Clamp(1 - point.Y / surface.Height, 0, 1);
            RefreshMarker();
            UpdateDraft(HsvToRgb(hue, saturation, value));
        }

        surface.MouseLeftButtonDown += (_, eventArgs) =>
        {
            surface.CaptureMouse();
            PickSurface(eventArgs.GetPosition(surface));
        };
        surface.MouseMove += (_, eventArgs) =>
        {
            if (surface.IsMouseCaptured)
            {
                PickSurface(eventArgs.GetPosition(surface));
            }
        };
        surface.MouseLeftButtonUp += (_, _) => surface.ReleaseMouseCapture();

        var applyButton = visuals.CreateButton("Apply", (_, _) =>
        {
            apply(draft);
            popup.IsOpen = false;
        }, primary: true);
        input.TextChanged += (_, _) =>
        {
            var valid = TryParseColor(input.Text, out var color);
            applyButton.IsEnabled = valid;
            if (!synchronizing && valid)
            {
                (hue, saturation, value) = RgbToHsv(color);
                synchronizing = true;
                hueSlider.Value = hue;
                synchronizing = false;
                RefreshHue();
                RefreshMarker();
                UpdateDraft(color, updateInput: false);
            }
        };

        var cancelButton = visuals.CreateButton("Cancel", (_, _) => popup.IsOpen = false);
        popup.Opened += (_, _) =>
        {
            draft = getCurrentColor();
            (hue, saturation, value) = RgbToHsv(draft);
            synchronizing = true;
            hueSlider.Value = hue;
            input.Text = $"#{draft:X6}";
            synchronizing = false;
            applyButton.IsEnabled = true;
            swatchBrush.Color = CreateColor(draft);
            RefreshHue();
            RefreshMarker();
        };
        RefreshHue();
        RefreshMarker();

        var colorInputs = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceSm);
        colorInputs.Children.Add(swatch);
        colorInputs.Children.Add(input);
        var popupActions = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceSm);
        popupActions.Children.Add(applyButton);
        popupActions.Children.Add(cancelButton);
        var popupContent = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceSm);
        popupContent.Children.Add(new TextBlock
        {
            Text = "Custom color",
            FontWeight = FontWeights.SemiBold,
            Foreground = visuals.Brush("TextBrush")
        });
        popupContent.Children.Add(colorInputs);
        popupContent.Children.Add(surface);
        popupContent.Children.Add(new TextBlock { Text = "Hue", Foreground = visuals.Brush("MutedTextBrush") });
        popupContent.Children.Add(hueSlider);
        popupContent.Children.Add(popupActions);
        popup.Child = new Border
        {
            Background = visuals.Brush("SurfaceRaisedBrush"),
            BorderBrush = visuals.Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = popupContent
        };
        return popup;
    }

    public static uint GetButtonColor(Button button, uint fallback) => button.Tag is uint color ? color : fallback;

    public static void SetButtonColor(Button button, uint color)
    {
        button.Tag = color;
        button.Content = $"#{color:X6}";
        button.Background = CreateBrush(color);
        button.Foreground = RelativeLuminance(color) > 0.179 ? Brushes.Black : Brushes.White;
    }

    private static bool TryParseColor(string value, out uint color)
    {
        color = 0;
        var hex = value.Trim();
        return hex.Length == 7 && hex[0] == '#' &&
            uint.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out color);
    }

    private static (double Hue, double Saturation, double Value) RgbToHsv(uint color)
    {
        var red = ((color >> 16) & 0xFF) / 255d;
        var green = ((color >> 8) & 0xFF) / 255d;
        var blue = (color & 0xFF) / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        var hue = delta == 0
            ? 0
            : max == red
                ? 60 * ((green - blue) / delta % 6)
                : max == green
                    ? 60 * ((blue - red) / delta + 2)
                    : 60 * ((red - green) / delta + 4);
        return (hue < 0 ? hue + 360 : hue, max == 0 ? 0 : delta / max, max);
    }

    private static uint HsvToRgb(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var second = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var offset = value - chroma;
        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, second, 0d),
            < 120 => (second, chroma, 0d),
            < 180 => (0d, chroma, second),
            < 240 => (0d, second, chroma),
            < 300 => (second, 0d, chroma),
            _ => (chroma, 0d, second)
        };
        var redByte = (uint)Math.Round((red + offset) * 255);
        var greenByte = (uint)Math.Round((green + offset) * 255);
        var blueByte = (uint)Math.Round((blue + offset) * 255);
        return (redByte << 16) | (greenByte << 8) | blueByte;
    }

    private static double RelativeLuminance(uint color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear((byte)(color >> 16)) +
            0.7152 * Linear((byte)(color >> 8)) +
            0.0722 * Linear((byte)color);
    }

    private static Color CreateColor(uint color) =>
        Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color);

    private static SolidColorBrush CreateBrush(uint color) => new(CreateColor(color));
}
