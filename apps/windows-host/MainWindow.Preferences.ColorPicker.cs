using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using Button = System.Windows.Controls.Button;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using MediaColors = System.Windows.Media.Colors;
using MediaLinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using MediaPoint = System.Windows.Point;
using WpfCursors = System.Windows.Input.Cursors;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private void SaveCustomPointer(bool enabled, int size, uint color)
    {
        var settings = new CustomPointerSettings(enabled, size, color);
        try
        {
            _customPointerService.Apply(settings);
            AppPointerSettings.SetCustomPointer(settings);
            _appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "custom_pointer",
                Outcome: enabled ? "enabled" : "disabled",
                Detail: $"size={settings.Size};color=#{settings.Color:X6}"));
        }
        catch (Exception exception)
        {
            _appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "custom_pointer",
                Outcome: "failed",
                Detail: exception.Message));
            ShowToast("Custom pointer could not be applied. Your Windows cursor scheme was restored.");
        }
    }

    private Popup CreateCustomPointerColorPopup(Button owner, uint initialColor, Action<uint> apply)
    {
        var (hue, saturation, value) = RgbToHsv(initialColor);
        var draft = initialColor;
        var synchronizing = false;
        var input = new TextBox { Text = $"#{draft:X6}", Width = 104 };
        var swatchBrush = CreateCustomPointerBrush(draft);
        var swatch = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(5), Background = swatchBrush, BorderBrush = (Brush)Resources["BorderBrush"], BorderThickness = new Thickness(1) };
        var surface = new Canvas { Width = 184, Height = 116, Cursor = WpfCursors.Cross };
        var saturationBrush = new MediaLinearGradientBrush(MediaColors.White, CreateCustomPointerColor(HsvToRgb(hue, 1, 1)), 0);
        var saturationLayer = new Border { Width = 184, Height = 116, Background = saturationBrush };
        var valueLayer = new Border { Width = 184, Height = 116, Background = new MediaLinearGradientBrush(MediaColor.FromArgb(0, 0, 0, 0), MediaColors.Black, 90) };
        var marker = new Ellipse { Width = 12, Height = 12, Stroke = MediaBrushes.White, StrokeThickness = 2, Fill = MediaBrushes.Transparent, IsHitTestVisible = false };
        surface.Children.Add(saturationLayer);
        surface.Children.Add(valueLayer);
        surface.Children.Add(marker);
        var hueSlider = new Slider
        {
            Style = (Style)Resources["ModernSliderStyle"],
            Minimum = 0,
            Maximum = 360,
            Width = 184,
            Value = hue
        };
        var popup = new Popup
        {
            PlacementTarget = owner,
            Placement = PlacementMode.Bottom,
            StaysOpen = true,
            AllowsTransparency = true
        };
        void RefreshHue()
        {
            saturationBrush.GradientStops[1].Color = CreateCustomPointerColor(HsvToRgb(hue, 1, 1));
        }

        void RefreshMarker()
        {
            Canvas.SetLeft(marker, saturation * surface.Width - marker.Width / 2);
            Canvas.SetTop(marker, (1 - value) * surface.Height - marker.Height / 2);
        }

        void UpdateDraft(uint color, bool updateInput = true)
        {
            draft = color;
            swatchBrush.Color = CreateCustomPointerColor(draft);
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
        void PickSurface(MediaPoint point)
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
        input.TextChanged += (_, _) =>
        {
            if (!synchronizing && TryParseCustomPointerColor(input.Text, out var color))
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
        var applyButton = CreateButton("Apply", (_, _) =>
        {
            apply(draft);
            popup.IsOpen = false;
        }, primary: true);
        var cancelButton = CreateButton("Cancel", (_, _) => popup.IsOpen = false);
        popup.Opened += (_, _) =>
        {
            draft = GetCustomPointerColor(owner);
            (hue, saturation, value) = RgbToHsv(draft);
            synchronizing = true;
            hueSlider.Value = hue;
            input.Text = $"#{draft:X6}";
            synchronizing = false;
            swatchBrush.Color = CreateCustomPointerColor(draft);
            RefreshHue();
            RefreshMarker();
        };
        RefreshHue();
        RefreshMarker();
        var colorInputs = CreateHorizontalStack(UiTokens.SpaceSm);
        colorInputs.Children.Add(swatch);
        colorInputs.Children.Add(input);
        var popupActions = CreateHorizontalStack(UiTokens.SpaceSm);
        popupActions.Children.Add(applyButton);
        popupActions.Children.Add(cancelButton);
        var popupContent = CreateVerticalStack(UiTokens.SpaceSm);
        popupContent.Children.Add(new TextBlock { Text = "Custom color", FontWeight = FontWeights.SemiBold, Foreground = (Brush)Resources["TextBrush"] });
        popupContent.Children.Add(colorInputs);
        popupContent.Children.Add(surface);
        popupContent.Children.Add(new TextBlock { Text = "Hue", Foreground = (Brush)Resources["MutedTextBrush"] });
        popupContent.Children.Add(hueSlider);
        popupContent.Children.Add(popupActions);
        popup.Child = new Border
        {
            Background = (Brush)Resources["SurfaceRaisedBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = popupContent
        };
        return popup;
    }

    private static bool TryParseCustomPointerColor(string value, out uint color)
    {
        var hex = value.Trim().TrimStart('#');
        return uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out color) && color <= 0xFFFFFF;
    }

    private static uint GetCustomPointerColor(Button button) => button.Tag is uint color ? color : AppPointerSettings.DefaultCustomPointerColor;

    private static (double Hue, double Saturation, double Value) RgbToHsv(uint color)
    {
        var red = ((color >> 16) & 0xFF) / 255d;
        var green = ((color >> 8) & 0xFF) / 255d;
        var blue = (color & 0xFF) / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        var hue = delta == 0 ? 0 : max == red ? 60 * ((green - blue) / delta % 6) : max == green ? 60 * ((blue - red) / delta + 2) : 60 * ((red - green) / delta + 4);
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

    private static MediaColor CreateCustomPointerColor(uint color) => MediaColor.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color);

    private static SolidColorBrush CreateCustomPointerBrush(uint color) => new(CreateCustomPointerColor(color));

    private static void SetCustomPointerColorButton(Button button, uint color)
    {
        button.Tag = color;
        button.Content = $"#{color:X6}";
        button.Background = CreateCustomPointerBrush(color);
        button.Foreground = color > 0x808080 ? MediaBrushes.Black : MediaBrushes.White;
    }

}
