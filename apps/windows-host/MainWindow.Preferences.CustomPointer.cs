using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private void AddCustomPointerSetting(StackPanel parent)
    {
        var current = AppPointerSettings.GetCustomPointer();
        var customPointer = CreateCheckBox("Custom pointer", current.Enabled);
        parent.Children.Add(customPointer);

        var controls = CreateVerticalStack(UiTokens.SpaceMd);
        controls.IsEnabled = current.Enabled;
        controls.Children.Add(CreateLabel("Size"));
        var sizeRow = CreateHorizontalStack(UiTokens.SpaceMd);
        var size = new Slider
        {
            Style = (Style)Resources["ModernSliderStyle"],
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
            Foreground = (Brush)Resources["TextBrush"]
        };
        sizeRow.Children.Add(size);
        sizeRow.Children.Add(sizeValue);
        controls.Children.Add(sizeRow);

        controls.Children.Add(CreateLabel("Color"));
        var colorRow = CreateHorizontalStack(UiTokens.SpaceSm);
        var colorButton = CreateButton(string.Empty, (_, _) => { }, primary: false);
        colorButton.Width = 132;
        var colorPopup = CreateCustomPointerColorPopup(colorButton, current.Color, selected =>
        {
            SaveCustomPointer(customPointer.IsChecked == true, (int)Math.Round(size.Value), selected);
            SetCustomPointerColorButton(colorButton, selected);
        });
        colorButton.Click += (_, _) => colorPopup.IsOpen = !colorPopup.IsOpen;
        SetCustomPointerColorButton(colorButton, current.Color);
        colorRow.Children.Add(colorButton);
        controls.Children.Add(colorRow);
        parent.Children.Add(controls);

        var useWatchdog = CreateCheckBox(
            "Use cursor recovery watchdog",
            AppPointerSettings.UseCursorRecoveryWatchdog());
        useWatchdog.VerticalAlignment = VerticalAlignment.Center;
        var watchdogInfo = CreateButton("ⓘ", (_, _) => ThemedConfirmationDialog.ShowInformation(
            this,
            "Cursor recovery watchdog",
            "The recovery watchdog reloads your normal Windows cursor scheme if Voltura Air crashes or is forcibly closed while Custom pointer is active. With this option off, a normal shutdown still restores your cursors, but an unexpected termination can leave the custom cursor active until the Windows cursor scheme is reloaded.",
            ConfirmationTone.Warning));
        watchdogInfo.Width = 34;
        watchdogInfo.Height = 30;
        watchdogInfo.MinWidth = 34;
        watchdogInfo.Padding = new Thickness(0);
        watchdogInfo.ToolTip = "Why the cursor recovery watchdog is recommended";
        System.Windows.Automation.AutomationProperties.SetName(watchdogInfo, "About cursor recovery watchdog");
        var watchdogRow = CreateHorizontalStack(UiTokens.SpaceSm);
        watchdogRow.Children.Add(useWatchdog);
        watchdogRow.Children.Add(watchdogInfo);
        parent.Children.Add(watchdogRow);

        var synchronizingWatchdog = false;
        void UpdateWatchdogVisual()
        {
            var enabled = useWatchdog.IsChecked == true;
            var foreground = (Brush)Resources[enabled ? "TextBrush" : "DangerBrush"];
            useWatchdog.Foreground = foreground;
            watchdogInfo.Foreground = foreground;
            watchdogInfo.BorderBrush = (Brush)Resources[enabled ? "BorderBrush" : "DangerBrush"];
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
                _customPointerService.RefreshRecoveryMonitoring();
                _appLog.Write(new AppLogEntry(
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
                _appLog.Write(new AppLogEntry(
                    Event: "host_action",
                    Source: "windows_host",
                    Action: "cursor_recovery_watchdog",
                    Outcome: "failed",
                    Detail: exception.Message));
                ShowToast("Cursor recovery watchdog could not be started");
            }

            UpdateWatchdogVisual();
        }

        useWatchdog.Checked += (_, _) => SaveWatchdogSetting(true);
        useWatchdog.Unchecked += (_, _) => SaveWatchdogSetting(false);
        UpdateWatchdogVisual();

        var sizePreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        sizePreviewTimer.Tick += (_, _) =>
        {
            sizePreviewTimer.Stop();
            SaveCustomPointer(customPointer.IsChecked == true, (int)Math.Round(size.Value), GetCustomPointerColor(colorButton));
        };

        customPointer.Checked += (_, _) =>
        {
            controls.IsEnabled = true;
            SaveCustomPointer(true, (int)Math.Round(size.Value), GetCustomPointerColor(colorButton));
        };
        customPointer.Unchecked += (_, _) =>
        {
            controls.IsEnabled = false;
            SaveCustomPointer(false, (int)Math.Round(size.Value), GetCustomPointerColor(colorButton));
        };
        size.ValueChanged += (_, _) =>
        {
            var selected = (int)Math.Round(size.Value);
            sizeValue.Text = selected.ToString(CultureInfo.InvariantCulture);
            if (!_isLoadingPreferences)
            {
                sizePreviewTimer.Stop();
                sizePreviewTimer.Start();
            }
        };
    }
}
