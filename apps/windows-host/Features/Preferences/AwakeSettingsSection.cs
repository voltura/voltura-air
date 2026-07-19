using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Ui;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class AwakeSettingsSection(
    IAwakeService awakeService,
    HostVisualFactory visuals,
    HostToastPresenter toasts,
    Func<bool> isLoading)
{
    public void AddTo(StackPanel parent)
    {
        var state = awakeService.State;
        parent.Children.Add(visuals.CreateMutedText("Prevent automatic sleep without changing the Windows power plan. Manual sleep, lid close, and the lock screen still take precedence."));
        parent.Children.Add(visuals.CreateLabel("Mode"));

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

        var timedPanel = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceSm);
        timedPanel.Children.Add(visuals.CreateLabel("Interval"));
        var intervalRow = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceSm);
        var hours = CreateTextBox((state.IntervalMinutes / 60).ToString(CultureInfo.CurrentCulture), 70);
        var minutes = CreateTextBox((state.IntervalMinutes % 60).ToString(CultureInfo.CurrentCulture), 70);
        intervalRow.Children.Add(hours);
        intervalRow.Children.Add(CreateInlineText("hours"));
        intervalRow.Children.Add(minutes);
        intervalRow.Children.Add(CreateInlineText("minutes"));
        intervalRow.Children.Add(visuals.CreateButton("Start", (_, _) => StartInterval(hours, minutes), primary: true));
        timedPanel.Children.Add(intervalRow);
        parent.Children.Add(timedPanel);

        var expirationPanel = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceSm);
        expirationPanel.Children.Add(visuals.CreateLabel("Expiration"));
        var expirationRow = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceSm);
        var suggestedExpiration = state.ExpiresAt is { } currentExpiration && currentExpiration > DateTimeOffset.Now
            ? currentExpiration.LocalDateTime
            : DateTime.Now.AddHours(1);
        var date = new ModernDatePicker(suggestedExpiration.Date, DateTime.Today) { Width = 180 };
        var time = CreateTextBox(suggestedExpiration.ToString("t", CultureInfo.CurrentCulture), 100);
        expirationRow.Children.Add(date);
        expirationRow.Children.Add(time);
        expirationRow.Children.Add(visuals.CreateButton("Start", (_, _) => StartUntil(date, time), primary: true));
        expirationPanel.Children.Add(expirationRow);
        parent.Children.Add(expirationPanel);

        var keepScreenOn = visuals.CreateCheckBox("Keep screen on while Keep awake is active", state.KeepScreenOn);
        keepScreenOn.Checked += (_, _) => Apply(awakeService.SetKeepScreenOn(true));
        keepScreenOn.Unchecked += (_, _) => Apply(awakeService.SetKeepScreenOn(false));
        parent.Children.Add(keepScreenOn);
        parent.Children.Add(visuals.CreateMutedText("Keeping the screen on uses more power and can delay normal idle behavior; paired devices cannot change this host setting."));

        void UpdateModePanels()
        {
            var selected = mode.SelectedItem is ComboBoxItem { Tag: AwakeMode selectedMode }
                ? selectedMode
                : AwakeMode.Off;
            timedPanel.Visibility = selected == AwakeMode.Timed ? Visibility.Visible : Visibility.Collapsed;
            expirationPanel.Visibility = selected == AwakeMode.Expiration ? Visibility.Visible : Visibility.Collapsed;
        }

        mode.SelectionChanged += (_, _) =>
        {
            UpdateModePanels();
            if (isLoading() || mode.SelectedItem is not ComboBoxItem { Tag: AwakeMode selectedMode })
            {
                return;
            }

            if (selectedMode == AwakeMode.Off)
            {
                Apply(awakeService.SetOff());
            }
            else if (selectedMode == AwakeMode.Indefinite)
            {
                Apply(awakeService.SetIndefinite());
            }
        };
        UpdateModePanels();
    }

    private void StartInterval(TextBox hours, TextBox minutes)
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

        Apply(awakeService.SetTimed(TimeSpan.FromMinutes(totalMinutes)));
    }

    private void StartUntil(ModernDatePicker date, TextBox time)
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

        Apply(awakeService.SetExpiration(new DateTimeOffset(local)));
    }

    private void Apply(AwakeOperationResult result) =>
        toasts.Show(result.Succeeded ? "Keep awake updated" : result.Error ?? "Keep awake could not be updated");

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
