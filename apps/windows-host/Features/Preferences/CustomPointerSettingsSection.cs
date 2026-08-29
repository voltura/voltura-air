using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class CustomPointerSettingsSection(
    ICursorOverrideController cursorOverrides,
    IAppLogWriter appLog,
    HostVisualFactory visuals,
    PreferencesVisualFactory preferenceVisuals,
    HostToastPresenter toasts,
    Func<bool> isLoading)
{
    internal const string TemporarilyUnavailableMessage = "Custom pointer is temporarily unavailable.";

    public void AddTo(StackPanel parent)
    {
        var current = AppPointerSettings.GetCustomPointer();
        var customPointer = preferenceVisuals.Register(
            visuals.CreateCheckBox("Custom pointer", current.Enabled));
        parent.Children.Add(customPointer);

        var controls = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceMd);
        controls.IsEnabled = current.Enabled;
        var sizeLabel = visuals.CreateLabel("Size");
        controls.Children.Add(sizeLabel);
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
        preferenceVisuals.RegisterLabel(sizeLabel, size);

        var colorLabel = visuals.CreateLabel("Color");
        controls.Children.Add(colorLabel);
        var colorRow = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceSm);
        var colorButton = visuals.CreateButton(string.Empty, (_, _) => { });
        colorButton.Width = 132;
        var colorPopup = ColorPickerPopup.Create(visuals, colorButton, () =>
            ColorPickerPopup.GetButtonColor(colorButton, AppPointerSettings.DefaultCustomPointerColor), selected =>
        {
            Save(customPointer.IsChecked == true, (int)Math.Round(size.Value), selected);
            ColorPickerPopup.SetButtonColor(colorButton, selected);
        });
        colorButton.Click += (_, _) => colorPopup.IsOpen = !colorPopup.IsOpen;
        ColorPickerPopup.SetButtonColor(colorButton, current.Color);
        colorRow.Children.Add(colorButton);
        controls.Children.Add(colorRow);
        preferenceVisuals.RegisterLabel(colorLabel, colorButton);
        parent.Children.Add(controls);

        var sizePreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        EventHandler previewTick = (_, _) =>
        {
            sizePreviewTimer.Stop();
            Save(customPointer.IsChecked == true, (int)Math.Round(size.Value),
                ColorPickerPopup.GetButtonColor(colorButton, AppPointerSettings.DefaultCustomPointerColor));
        };
        sizePreviewTimer.Tick += previewTick;
        var pointerSettingsSubscribed = false;
        parent.Loaded += (_, _) => SubscribeToPointerSettings();
        parent.Unloaded += (_, _) =>
        {
            sizePreviewTimer.Stop();
            sizePreviewTimer.Tick -= previewTick;
            colorPopup.IsOpen = false;
            UnsubscribeFromPointerSettings();
        };

        customPointer.Checked += (_, _) =>
        {
            controls.IsEnabled = true;
            if (!Save(true, (int)Math.Round(size.Value),
                    ColorPickerPopup.GetButtonColor(colorButton, AppPointerSettings.DefaultCustomPointerColor)))
            {
                customPointer.IsChecked = false;
                controls.IsEnabled = false;
            }
        };
        customPointer.Unchecked += (_, _) =>
        {
            controls.IsEnabled = false;
            Save(false, (int)Math.Round(size.Value),
                ColorPickerPopup.GetButtonColor(colorButton, AppPointerSettings.DefaultCustomPointerColor));
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

        if (parent.IsLoaded)
        {
            SubscribeToPointerSettings();
        }

        void SubscribeToPointerSettings()
        {
            if (pointerSettingsSubscribed)
            {
                return;
            }

            AppPointerSettings.Changed += OnPointerSettingsChanged;
            pointerSettingsSubscribed = true;
        }

        void UnsubscribeFromPointerSettings()
        {
            if (!pointerSettingsSubscribed)
            {
                return;
            }

            AppPointerSettings.Changed -= OnPointerSettingsChanged;
            pointerSettingsSubscribed = false;
        }

        void OnPointerSettingsChanged(object? sender, EventArgs eventArgs)
        {
            if (AppPointerSettings.GetCustomPointer().Enabled)
            {
                return;
            }

            _ = customPointer.Dispatcher.BeginInvoke(() =>
            {
                if (customPointer.IsChecked != true)
                {
                    return;
                }

                customPointer.IsChecked = false;
                controls.IsEnabled = false;
            });
        }
    }

    private bool Save(bool enabled, int size, uint color)
    {
        var settings = new CustomPointerSettings(enabled, size, color);
        try
        {
            cursorOverrides.ApplyCustomPointer(settings);
            AppPointerSettings.SetCustomPointer(settings);
            appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "custom_pointer",
                Outcome: enabled ? "enabled" : "disabled",
                Detail: $"size={settings.Size};color=#{settings.Color:X6}"));
            return true;
        }
        catch (Exception exception)
        {
            appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "custom_pointer",
                Outcome: "failed",
                Detail: exception.Message));
            toasts.Show(exception is CursorRecoveryUnavailableException
                ? TemporarilyUnavailableMessage
                : "Custom pointer could not be applied. Your Windows cursor scheme was restored.");
            return false;
        }
    }

}
