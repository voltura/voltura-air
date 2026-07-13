using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ComboBox = System.Windows.Controls.ComboBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private void AddAwakeSettings(StackPanel parent)
    {
        var state = _awakeService.State;
        parent.Children.Add(CreateMutedText("Keep this PC awake without changing the selected Windows power plan. Manual sleep, lid close, and the Windows lock screen still take precedence."));
        parent.Children.Add(CreateLabel("Mode"));

        var mode = new ComboBox
        {
            Width = 280,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 12)
        };
        mode.SetResourceReference(FrameworkElement.StyleProperty, "ModernComboBoxStyle");
        AddModeItem(mode, "Use selected power plan", AwakeMode.Off, state.Mode);
        AddModeItem(mode, "Keep awake indefinitely", AwakeMode.Indefinite, state.Mode);
        AddModeItem(mode, "Keep awake for an interval", AwakeMode.Timed, state.Mode);
        AddModeItem(mode, "Keep awake until a date and time", AwakeMode.Expiration, state.Mode);
        parent.Children.Add(mode);

        var timedPanel = new StackPanel();
        timedPanel.Children.Add(CreateLabel("Interval"));
        var intervalRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var hours = CreateAwakeTextBox((state.IntervalMinutes / 60).ToString(CultureInfo.CurrentCulture), 70);
        var minutes = CreateAwakeTextBox((state.IntervalMinutes % 60).ToString(CultureInfo.CurrentCulture), 70);
        intervalRow.Children.Add(hours);
        intervalRow.Children.Add(CreateInlineText("hours"));
        intervalRow.Children.Add(minutes);
        intervalRow.Children.Add(CreateInlineText("minutes"));
        intervalRow.Children.Add(CreateButton("Start", (_, _) => StartAwakeInterval(hours, minutes), primary: true));
        timedPanel.Children.Add(intervalRow);
        parent.Children.Add(timedPanel);

        var expirationPanel = new StackPanel();
        expirationPanel.Children.Add(CreateLabel("Expiration"));
        var expirationRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var suggestedExpiration = state.ExpiresAt is { } currentExpiration && currentExpiration > DateTimeOffset.Now
            ? currentExpiration.LocalDateTime
            : DateTime.Now.AddHours(1);
        var date = new ModernDatePicker(suggestedExpiration.Date, DateTime.Today)
        {
            Width = 180,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var time = CreateAwakeTextBox(suggestedExpiration.ToString("t", CultureInfo.CurrentCulture), 100);
        expirationRow.Children.Add(date);
        expirationRow.Children.Add(time);
        expirationRow.Children.Add(CreateButton("Start", (_, _) => StartAwakeUntil(date, time), primary: true));
        expirationPanel.Children.Add(expirationRow);
        parent.Children.Add(expirationPanel);

        var keepScreenOn = CreateCheckBox("Keep screen on while Keep awake is active", state.KeepScreenOn);
        keepScreenOn.Checked += (_, _) => ApplyAwakeResult(_awakeService.SetKeepScreenOn(true));
        keepScreenOn.Unchecked += (_, _) => ApplyAwakeResult(_awakeService.SetKeepScreenOn(false));
        parent.Children.Add(keepScreenOn);
        parent.Children.Add(CreateMutedText("Keeping the screen on uses more power and can delay normal idle behavior. Paired devices use this host setting and cannot change it."));

        var status = CreateMutedText(BuildAwakeStatus(state));
        status.Margin = new Thickness(0, 10, 0, 0);
        parent.Children.Add(status);

        void UpdateModePanels()
        {
            var selected = mode.SelectedItem is ComboBoxItem item && item.Tag is AwakeMode selectedMode
                ? selectedMode
                : AwakeMode.Off;
            timedPanel.Visibility = selected == AwakeMode.Timed ? Visibility.Visible : Visibility.Collapsed;
            expirationPanel.Visibility = selected == AwakeMode.Expiration ? Visibility.Visible : Visibility.Collapsed;
        }

        mode.SelectionChanged += (_, _) =>
        {
            UpdateModePanels();
            if (_isLoadingPreferences || mode.SelectedItem is not ComboBoxItem { Tag: AwakeMode selectedMode })
            {
                return;
            }

            if (selectedMode == AwakeMode.Off)
            {
                ApplyAwakeResult(_awakeService.SetOff());
            }
            else if (selectedMode == AwakeMode.Indefinite)
            {
                ApplyAwakeResult(_awakeService.SetIndefinite());
            }
        };
        UpdateModePanels();
    }

    private void StartAwakeInterval(TextBox hours, TextBox minutes)
    {
        if (!int.TryParse(hours.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var hourValue) || hourValue < 0 ||
            !int.TryParse(minutes.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var minuteValue) || minuteValue is < 0 or > 59)
        {
            ShowToast("Enter non-negative hours and 0 to 59 minutes");
            return;
        }

        var totalMinutes = (long)hourValue * 60 + minuteValue;
        if (totalMinutes is < 1 or > 525_600)
        {
            ShowToast("Choose an interval between 1 minute and 1 year");
            return;
        }

        ApplyAwakeResult(_awakeService.SetTimed(TimeSpan.FromMinutes(totalMinutes)));
    }

    private void StartAwakeUntil(ModernDatePicker date, TextBox time)
    {
        if (!DateTime.TryParse(time.Text, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out var selectedTime))
        {
            ShowToast("Choose a valid date and time");
            return;
        }

        var local = date.SelectedDate.Add(selectedTime.TimeOfDay);
        if (TimeZoneInfo.Local.IsInvalidTime(local))
        {
            ShowToast("That local time does not exist because of daylight saving time");
            return;
        }

        ApplyAwakeResult(_awakeService.SetExpiration(new DateTimeOffset(local)));
    }

    private void ApplyAwakeResult(AwakeOperationResult result)
    {
        ShowToast(result.Succeeded ? "Keep awake updated" : result.Error ?? "Keep awake could not be updated");
    }

    private static void AddModeItem(ComboBox comboBox, string text, AwakeMode mode, AwakeMode selectedMode)
    {
        comboBox.Items.Add(new ComboBoxItem { Content = text, Tag = mode, IsSelected = mode == selectedMode });
    }

    private static TextBox CreateAwakeTextBox(string text, double width) => new()
    {
        Text = text,
        Width = width,
        Margin = new Thickness(0, 0, 8, 0),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private TextBlock CreateInlineText(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 12, 0),
        Foreground = (System.Windows.Media.Brush)Resources["TextBrush"]
    };

    private static string BuildAwakeStatus(AwakeState state)
    {
        return state.Mode switch
        {
            AwakeMode.Off => "Status: using the selected Windows power plan.",
            AwakeMode.Indefinite => "Status: keeping the PC awake indefinitely.",
            AwakeMode.Timed when state.ExpiresAt is { } expires => $"Status: keeping the PC awake until {expires.LocalDateTime:g}.",
            AwakeMode.Expiration when state.ExpiresAt is { } expires => $"Status: keeping the PC awake until {expires.LocalDateTime:g}.",
            _ => "Status: using the selected Windows power plan."
        };
    }
}
