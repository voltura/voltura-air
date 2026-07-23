using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class PresentationSettingsSection(
    CustomPointerService customPointerService,
    IAppLogWriter appLog,
    HostVisualFactory visuals,
    HostToastPresenter toasts,
    Func<bool> isLoading)
{
    public void AddTo(StackPanel parent)
    {
        var current = AppPointerSettings.GetPresentationLaserPointer();
        parent.Children.Add(visuals.CreateMutedText(
            "Controls the native laser pointer used by Presentation mode on this PC."));
        parent.Children.Add(visuals.CreateLabel("Laser pointer size"));

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
        parent.Children.Add(sizeRow);

        parent.Children.Add(visuals.CreateLabel("Laser pointer color"));
        var red = CreateColorButton("Red", PresentationLaserColor.Red, current.Color);
        var green = CreateColorButton("Green", PresentationLaserColor.Green, current.Color);
        var blue = CreateColorButton("Blue", PresentationLaserColor.Blue, current.Color);
        HostVisualFactory.WireSegmentGroup(red, green, blue);
        parent.Children.Add(HostVisualFactory.CreateSegmentRow(red, green, blue));

        var sizePreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        EventHandler previewTick = (_, _) =>
        {
            sizePreviewTimer.Stop();
            Save((int)Math.Round(size.Value), SelectedColor(red, green, blue));
        };
        sizePreviewTimer.Tick += previewTick;
        parent.Unloaded += (_, _) =>
        {
            sizePreviewTimer.Stop();
            sizePreviewTimer.Tick -= previewTick;
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

        red.Click += OnColorChanged;
        green.Click += OnColorChanged;
        blue.Click += OnColorChanged;

        void OnColorChanged(object sender, RoutedEventArgs eventArgs)
        {
            _ = sender;
            _ = eventArgs;
            if (!isLoading())
            {
                Save((int)Math.Round(size.Value), SelectedColor(red, green, blue));
            }
        }
    }

    private ToggleButton CreateColorButton(
        string label,
        PresentationLaserColor color,
        PresentationLaserColor current)
    {
        var button = visuals.CreateSegmentButton(label, color == current);
        button.Tag = color;
        return button;
    }

    private void Save(int size, PresentationLaserColor color)
    {
        var settings = new PresentationLaserPointerSettings(size, color);
        try
        {
            customPointerService.ApplyPresentationLaserPointerSettings(settings);
            AppPointerSettings.SetPresentationLaserPointer(settings);
            appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "presentation_laser_pointer",
                Outcome: "updated",
                Detail: $"size={settings.Size};color={settings.Color.ToString().ToLowerInvariant()}"));
        }
        catch (Exception exception)
        {
            appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "presentation_laser_pointer",
                Outcome: "failed",
                Detail: exception.Message));
            toasts.Show("Laser pointer settings could not be applied. The previous appearance was restored.");
        }
    }

    private static PresentationLaserColor SelectedColor(params ToggleButton[] buttons) =>
        buttons.FirstOrDefault(button => button.IsChecked == true)?.Tag is PresentationLaserColor color
            ? color
            : PresentationLaserColor.Red;
}
