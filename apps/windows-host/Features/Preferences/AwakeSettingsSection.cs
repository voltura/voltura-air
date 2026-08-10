using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Ui;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class AwakeSettingsSection(
    Window owner,
    IAwakeService awakeService,
    IActivitySimulationService activitySimulationService,
    HostVisualFactory visuals,
    PreferencesVisualFactory preferenceVisuals,
    HostToastPresenter toasts,
    Func<bool> isLoading)
{
    private int _updateRunning;
    private int _activityUpdateRunning;

    public void AddTo(StackPanel parent)
    {
        var state = awakeService.State;
        parent.Children.Add(visuals.CreateMutedText("Prevent automatic sleep without changing the Windows power plan. Manual sleep, lid close, and the lock screen still take precedence."));
        var modeLabel = visuals.CreateLabel("Mode");
        parent.Children.Add(modeLabel);

        var mode = new ComboBox
        {
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        mode.SetResourceReference(FrameworkElement.StyleProperty, "ModernComboBoxStyle");
        AddModeItem(mode, "Use selected power plan", AwakeMode.Off, state.Mode);
        AddModeItem(mode, "Keep awake indefinitely", AwakeMode.Indefinite, state.Mode);
        AddModeItem(mode, "Keep awake for an interval", AwakeMode.Timed, state.Mode);
        AddModeItem(mode, "Keep awake until a date and time", AwakeMode.Expiration, state.Mode);
        parent.Children.Add(mode);
        preferenceVisuals.RegisterLabel(modeLabel, mode);

        var timedPanel = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceSm);
        var intervalLabel = visuals.CreateLabel("Interval");
        timedPanel.Children.Add(intervalLabel);
        var intervalRow = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceSm);
        var hours = CreateTextBox((state.IntervalMinutes / 60).ToString(CultureInfo.CurrentCulture), 70);
        var minutes = CreateTextBox((state.IntervalMinutes % 60).ToString(CultureInfo.CurrentCulture), 70);
        intervalRow.Children.Add(hours);
        intervalRow.Children.Add(CreateInlineText("hours"));
        intervalRow.Children.Add(minutes);
        intervalRow.Children.Add(CreateInlineText("minutes"));
        intervalRow.Children.Add(visuals.CreateButton("Start", async (_, _) => await StartIntervalAsync(hours, minutes), primary: true));
        timedPanel.Children.Add(intervalRow);
        preferenceVisuals.RegisterLabel(intervalLabel, hours);
        parent.Children.Add(timedPanel);

        var expirationPanel = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceSm);
        var expirationLabel = visuals.CreateLabel("Expiration");
        expirationPanel.Children.Add(expirationLabel);
        var expirationRow = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceSm);
        var suggestedExpiration = state.ExpiresAt is { } currentExpiration && currentExpiration > DateTimeOffset.Now
            ? currentExpiration.LocalDateTime
            : DateTime.Now.AddHours(1);
        var date = new ModernDatePicker(suggestedExpiration.Date, DateTime.Today) { Width = 180 };
        var time = CreateTextBox(suggestedExpiration.ToString("t", CultureInfo.CurrentCulture), 100);
        expirationRow.Children.Add(date);
        expirationRow.Children.Add(time);
        expirationRow.Children.Add(visuals.CreateButton("Start", async (_, _) => await StartUntilAsync(date, time), primary: true));
        expirationPanel.Children.Add(expirationRow);
        preferenceVisuals.RegisterLabel(expirationLabel, date);
        parent.Children.Add(expirationPanel);

        var keepScreenOn = preferenceVisuals.Register(visuals.CreateCheckBox(
            "Keep screen on while Keep awake is active",
            state.KeepScreenOn,
            showInformation: () => ThemedConfirmationDialog.ShowInformation(
                owner,
                "Keep the screen on",
                "Keeping the screen on uses more power and can delay normal idle behavior. Paired devices cannot change this host setting.")));
        keepScreenOn.Checked += async (_, _) => await ApplyAsync(() => awakeService.SetKeepScreenOnAsync(true));
        keepScreenOn.Unchecked += async (_, _) => await ApplyAsync(() => awakeService.SetKeepScreenOnAsync(false));
        parent.Children.Add(keepScreenOn);

        var simulateActivity = preferenceVisuals.Register(visuals.CreateCheckBox(
            "Simulate activity every 59 seconds",
            activitySimulationService.Enabled,
            showInformation: () => ThemedConfirmationDialog.ShowInformation(
                owner,
                "Simulate activity",
                "Voltura Air sends an F15 key release every 59 seconds. Some applications may react, and presence results can vary.")));
        var synchronizingActivity = false;
        var subscribedToActivityState = false;

        void ApplyActivityState()
        {
            synchronizingActivity = true;
            try
            {
                simulateActivity.IsChecked = activitySimulationService.Enabled;
            }
            finally
            {
                synchronizingActivity = false;
            }
        }

        void OnActivityStateChanged(object? sender, EventArgs eventArgs)
        {
            _ = simulateActivity.Dispatcher.BeginInvoke(ApplyActivityState);
        }

        simulateActivity.Loaded += (_, _) =>
        {
            if (!subscribedToActivityState)
            {
                activitySimulationService.StateChanged += OnActivityStateChanged;
                subscribedToActivityState = true;
            }
            ApplyActivityState();
        };
        simulateActivity.Unloaded += (_, _) =>
        {
            if (subscribedToActivityState)
            {
                activitySimulationService.StateChanged -= OnActivityStateChanged;
                subscribedToActivityState = false;
            }
        };
        simulateActivity.Checked += async (_, _) =>
        {
            if (!synchronizingActivity && !isLoading())
            {
                await ApplyActivitySimulationAsync(true, ApplyActivityState);
            }
        };
        simulateActivity.Unchecked += async (_, _) =>
        {
            if (!synchronizingActivity && !isLoading())
            {
                await ApplyActivitySimulationAsync(false, ApplyActivityState);
            }
        };
        parent.Children.Add(simulateActivity);

        void UpdateModePanels()
        {
            var selected = mode.SelectedItem is ComboBoxItem { Tag: AwakeMode selectedMode }
                ? selectedMode
                : AwakeMode.Off;
            timedPanel.Visibility = selected == AwakeMode.Timed ? Visibility.Visible : Visibility.Collapsed;
            expirationPanel.Visibility = selected == AwakeMode.Expiration ? Visibility.Visible : Visibility.Collapsed;
        }

        mode.SelectionChanged += async (_, _) =>
        {
            UpdateModePanels();
            if (isLoading() || mode.SelectedItem is not ComboBoxItem { Tag: AwakeMode selectedMode })
            {
                return;
            }

            if (selectedMode == AwakeMode.Off)
            {
                await ApplyAsync(() => awakeService.SetOffAsync());
            }
            else if (selectedMode == AwakeMode.Indefinite)
            {
                await ApplyAsync(() => awakeService.SetIndefiniteAsync());
            }
        };
        UpdateModePanels();
    }

    private async Task StartIntervalAsync(TextBox hours, TextBox minutes)
    {
        if (!int.TryParse(hours.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var hourValue) || hourValue < 0 ||
            !int.TryParse(minutes.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var minuteValue) || minuteValue is < 0 or > 59)
        {
            toasts.Show("Enter non-negative hours and 0 to 59 minutes");
            return;
        }

        var totalMinutes = (long)hourValue * 60 + minuteValue;
        if (totalMinutes is < 1 or > 525_600)
        {
            toasts.Show("Choose an interval between 1 minute and 1 year");
            return;
        }

        await ApplyAsync(() => awakeService.SetTimedAsync(TimeSpan.FromMinutes(totalMinutes)));
    }

    private async Task StartUntilAsync(ModernDatePicker date, TextBox time)
    {
        if (!DateTime.TryParse(time.Text, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out var selectedTime))
        {
            toasts.Show("Choose a valid date and time");
            return;
        }

        var local = date.SelectedDate.Add(selectedTime.TimeOfDay);
        if (TimeZoneInfo.Local.IsInvalidTime(local))
        {
            toasts.Show("That local time does not exist because of daylight saving time");
            return;
        }

        await ApplyAsync(() => awakeService.SetExpirationAsync(new DateTimeOffset(local)));
    }

    private async Task ApplyAsync(Func<Task<AwakeOperationResult>> operation)
    {
        if (Interlocked.Exchange(ref _updateRunning, 1) != 0)
        {
            return;
        }

        try
        {
            var result = await operation();
            toasts.Show(result.Succeeded ? "Keep awake updated" : result.Error ?? "Keep awake could not be updated");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            toasts.Show($"Keep awake could not be updated: {exception.Message}");
        }
        finally
        {
            Volatile.Write(ref _updateRunning, 0);
        }
    }

    private async Task ApplyActivitySimulationAsync(bool enabled, Action restoreState)
    {
        if (Interlocked.Exchange(ref _activityUpdateRunning, 1) != 0)
        {
            restoreState();
            return;
        }

        try
        {
            var result = await activitySimulationService.SetEnabledAsync(enabled);
            toasts.Show(result.Succeeded
                ? "Simulated activity updated"
                : result.Error ?? "Simulated activity could not be updated");
            if (!result.Succeeded)
            {
                restoreState();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            toasts.Show($"Simulated activity could not be updated: {exception.Message}");
            restoreState();
        }
        finally
        {
            Volatile.Write(ref _activityUpdateRunning, 0);
        }
    }

    private static void AddModeItem(ComboBox comboBox, string text, AwakeMode mode, AwakeMode selectedMode) =>
        comboBox.Items.Add(new ComboBoxItem { Content = text, Tag = mode, IsSelected = mode == selectedMode });

    private static TextBox CreateTextBox(string text, double width) => new()
    {
        Text = text,
        Width = width,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private TextBlock CreateInlineText(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = visuals.Brush("TextBrush")
    };
}
