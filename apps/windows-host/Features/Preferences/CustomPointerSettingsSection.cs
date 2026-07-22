using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VolturaAir.Host.Ui;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using TextBox = System.Windows.Controls.TextBox;
using Cursors = System.Windows.Input.Cursors;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class CustomPointerSettingsSection(
    Window owner,
    CustomPointerService customPointerService,
    IAppLogWriter appLog,
    HostVisualFactory visuals,
    HostToastPresenter toasts,
    Func<bool> isLoading)
{
    internal const string WatchdogStartFailureMessage =
        "Cursor recovery watchdog could not be started. Reinstall Voltura Air to restore it.";

    public void AddTo(StackPanel parent)
    {
        var current = AppPointerSettings.GetCustomPointer();
        var customPointer = visuals.CreateCheckBox("Custom pointer", current.Enabled);
        parent.Children.Add(customPointer);

        var controls = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceMd);
        controls.IsEnabled = current.Enabled;
        controls.Children.Add(visuals.CreateLabel("Size"));
        var sizeRow = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceMd);
        var size = new Slider
        {
            Style = visuals.Style("ModernSliderStyle"),
            Minimum = AppPointerSettings.MinCustomPointerSize,
            Maximum = AppPointerSettings.MaxCustomPointerSize,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Width = 220,
            Value = current.Size
        };
        var sizeValue = new TextBlock
        {
            Text = current.Size.ToString(CultureInfo.InvariantCulture),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 48,
            Foreground = visuals.Brush("TextBrush")
        };
        sizeRow.Children.Add(size);
        sizeRow.Children.Add(sizeValue);
        controls.Children.Add(sizeRow);

        controls.Children.Add(visuals.CreateLabel("Color"));
        var colorRow = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceSm);
        var colorButton = visuals.CreateButton(string.Empty, (_, _) => { });
        colorButton.Width = 132;
        var colorPopup = CreateColorPopup(colorButton, current.Color, selected =>
        {
            Save(customPointer.IsChecked == true, (int)Math.Round(size.Value), selected);
            SetColorButton(colorButton, selected);
        });
        colorButton.Click += (_, _) => colorPopup.IsOpen = !colorPopup.IsOpen;
        SetColorButton(colorButton, current.Color);
        colorRow.Children.Add(colorButton);
        controls.Children.Add(colorRow);
        parent.Children.Add(controls);

        var useWatchdog = visuals.CreateCheckBox(
            "Use cursor recovery watchdog",
            AppPointerSettings.UseCursorRecoveryWatchdog(),
            showInformation: () => ThemedConfirmationDialog.ShowInformation(
                owner,
                "Cursor recovery watchdog",
                "The recovery watchdog reloads your normal Windows cursor scheme if Voltura Air crashes or is forcibly closed while Custom pointer is active. With this option off, a normal shutdown still restores your cursors, but an unexpected termination can leave the custom cursor active until the Windows cursor scheme is reloaded."));
        useWatchdog.VerticalAlignment = VerticalAlignment.Center;
        parent.Children.Add(useWatchdog);
        parent.Children.Add(visuals.CreateNotice(
            "Without the recovery watchdog, an unexpected shutdown can leave the custom cursor active until the Windows cursor scheme is reloaded.",
            isError: true));

        var synchronizingWatchdog = false;
        void UpdateWatchdogVisual()
        {
            var enabled = useWatchdog.IsChecked == true;
            useWatchdog.Opacity = enabled ? 1 : 0.92;
        }

        void SaveWatchdogSetting(bool enabled)
        {
            if (synchronizingWatchdog)
            {
                return;
            }

            AppPointerSettings.SetUseCursorRecoveryWatchdog(enabled);
            try
            {
                customPointerService.RefreshRecoveryMonitoring();
                appLog.Write(new AppLogEntry(
                    Event: "host_action",
                    Source: "windows_host",
                    Action: "cursor_recovery_watchdog",
                    Outcome: enabled ? "enabled" : "disabled"));
            }
            catch (Exception exception)
            {
                AppPointerSettings.SetUseCursorRecoveryWatchdog(false);
                synchronizingWatchdog = true;
                useWatchdog.IsChecked = false;
                synchronizingWatchdog = false;
                appLog.Write(new AppLogEntry(
                    Event: "host_action",
                    Source: "windows_host",
                    Action: "cursor_recovery_watchdog",
                    Outcome: "failed",
                    Detail: exception.Message));
                toasts.Show(WatchdogStartFailureMessage);
            }

            UpdateWatchdogVisual();
        }

        useWatchdog.Checked += (_, _) => SaveWatchdogSetting(true);
        useWatchdog.Unchecked += (_, _) => SaveWatchdogSetting(false);
        UpdateWatchdogVisual();

        var sizePreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        EventHandler previewTick = (_, _) =>
        {
            sizePreviewTimer.Stop();
            Save(customPointer.IsChecked == true, (int)Math.Round(size.Value), GetColor(colorButton));
        };
        sizePreviewTimer.Tick += previewTick;
        parent.Unloaded += (_, _) =>
        {
            sizePreviewTimer.Stop();
            sizePreviewTimer.Tick -= previewTick;
            colorPopup.IsOpen = false;
        };

        customPointer.Checked += (_, _) =>
        {
            controls.IsEnabled = true;
            Save(true, (int)Math.Round(size.Value), GetColor(colorButton));
        };
        customPointer.Unchecked += (_, _) =>
        {
            controls.IsEnabled = false;
            Save(false, (int)Math.Round(size.Value), GetColor(colorButton));
        };
        size.ValueChanged += (_, _) =>
        {
            var selected = (int)Math.Round(size.Value);
            sizeValue.Text = selected.ToString(CultureInfo.InvariantCulture);
            if (!isLoading())
            {
                sizePreviewTimer.Stop();
                sizePreviewTimer.Start();
            }
        };
    }

    private void Save(bool enabled, int size, uint color)
    {
        var settings = new CustomPointerSettings(enabled, size, color);
        try
        {
            customPointerService.Apply(settings);
            AppPointerSettings.SetCustomPointer(settings);
            appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "custom_pointer",
                Outcome: enabled ? "enabled" : "disabled",
                Detail: $"size={settings.Size};color=#{settings.Color:X6}"));
        }
        catch (Exception exception)
        {
            appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "custom_pointer",
                Outcome: "failed",
                Detail: exception.Message));
            toasts.Show("Custom pointer could not be applied. Your Windows cursor scheme was restored.");
        }
    }

    private Popup CreateColorPopup(Button popupOwner, uint initialColor, Action<uint> apply)
    {
        var (hue, saturation, value) = RgbToHsv(initialColor);
        var draft = initialColor;
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
        input.TextChanged += (_, _) =>
        {
            if (!synchronizing && TryParseColor(input.Text, out var color))
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

        var applyButton = visuals.CreateButton("Apply", (_, _) =>
        {
            apply(draft);
            popup.IsOpen = false;
        }, primary: true);
        var cancelButton = visuals.CreateButton("Cancel", (_, _) => popup.IsOpen = false);
        popup.Opened += (_, _) =>
        {
            draft = GetColor(popupOwner);
            (hue, saturation, value) = RgbToHsv(draft);
            synchronizing = true;
            hueSlider.Value = hue;
            input.Text = $"#{draft:X6}";
            synchronizing = false;
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
        popupContent.Children.Add(new TextBlock { Text = "Custom color", FontWeight = FontWeights.SemiBold, Foreground = visuals.Brush("TextBrush") });
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

    private static bool TryParseColor(string value, out uint color)
    {
        var hex = value.Trim().TrimStart('#');
        return uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out color) && color <= 0xFFFFFF;
    }

    private static uint GetColor(Button button) =>
        button.Tag is uint color ? color : AppPointerSettings.DefaultCustomPointerColor;

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

    private static Color CreateColor(uint color) =>
        Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color);

    private static SolidColorBrush CreateBrush(uint color) => new(CreateColor(color));

    private static void SetColorButton(Button button, uint color)
    {
        button.Tag = color;
        button.Content = $"#{color:X6}";
        button.Background = CreateBrush(color);
        button.Foreground = color > 0x808080 ? Brushes.Black : Brushes.White;
    }
}
