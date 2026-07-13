using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBlock = System.Windows.Controls.TextBlock;
using Thickness = System.Windows.Thickness;
using UserControl = System.Windows.Controls.UserControl;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace VolturaAir.Host;

public sealed class ModernDatePicker : UserControl
{
    private readonly DateTime? _minimumDate;
    private readonly Button _trigger;
    private readonly Popup _popup;
    private readonly Grid _calendar;
    private readonly TextBlock _monthCaption;
    private DateTime _displayMonth;
    private DateTime _workingDate;

    public ModernDatePicker(DateTime selectedDate, DateTime? minimumDate = null)
    {
        _minimumDate = minimumDate?.Date;
        SelectedDate = NormalizeDate(selectedDate);
        _workingDate = SelectedDate;
        _displayMonth = FirstOfMonth(SelectedDate);
        _trigger = CreateTriggerButton();
        _calendar = CreateCalendarGrid();
        _monthCaption = CreateMonthCaption();
        _popup = CreatePopup();

        var root = new Grid();
        root.Children.Add(_trigger);
        root.Children.Add(_popup);
        Content = root;
        UpdateTrigger();
    }

    public DateTime SelectedDate { get; private set; }

    public event EventHandler? DateChanged;

    public void SetDate(DateTime date)
    {
        SelectedDate = NormalizeDate(date);
        _workingDate = SelectedDate;
        _displayMonth = FirstOfMonth(SelectedDate);
        UpdateTrigger();
        DateChanged?.Invoke(this, EventArgs.Empty);
    }

    private Button CreateTriggerButton()
    {
        var icon = new TextBlock
        {
            Text = "\u25A6",
            FontSize = 18,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var label = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var chevron = new TextBlock
        {
            Text = "\u2304",
            FontSize = 16,
            Margin = new Thickness(12, -2, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(label, 1);
        Grid.SetColumn(chevron, 2);
        content.Children.Add(icon);
        content.Children.Add(label);
        content.Children.Add(chevron);

        var button = new Button
        {
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = label
        };
        button.Click += (_, _) => Open();
        AutomationProperties.SetName(button, "Choose expiration date");
        return button;
    }

    private Popup CreatePopup()
    {
        var title = new TextBlock
        {
            Text = "Expiration date",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var previous = CreateNavigationButton("\u2039", -1, "Previous month");
        var next = CreateNavigationButton("\u203A", 1, "Next month");
        var navigation = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        Grid.SetColumn(_monthCaption, 1);
        Grid.SetColumn(next, 2);
        navigation.Children.Add(previous);
        navigation.Children.Add(_monthCaption);
        navigation.Children.Add(next);

        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => _popup.IsOpen = false;
        var apply = new Button { Content = "Apply" };
        apply.SetResourceReference(Button.BackgroundProperty, "AccentBrush");
        apply.SetResourceReference(Button.ForegroundProperty, "AccentTextBrush");
        apply.SetResourceReference(Button.BorderBrushProperty, "AccentBrush");
        apply.Click += (_, _) => Apply();
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        footer.Children.Add(cancel);
        footer.Children.Add(apply);

        var panel = new StackPanel();
        panel.Children.Add(title);
        panel.Children.Add(navigation);
        panel.Children.Add(_calendar);
        panel.Children.Add(footer);
        var chrome = new Border
        {
            Width = 322,
            Padding = new Thickness(18),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = panel
        };
        chrome.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        chrome.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        chrome.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
            {
                return;
            }

            _popup.IsOpen = false;
            args.Handled = true;
        };

        return new Popup
        {
            AllowsTransparency = true,
            Child = chrome,
            Placement = PlacementMode.Bottom,
            PlacementTarget = _trigger,
            PopupAnimation = PopupAnimation.Fade,
            StaysOpen = false
        };
    }

    private static Grid CreateCalendarGrid()
    {
        var grid = new Grid();
        for (var column = 0; column < 7; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        }

        for (var row = 0; row < 7; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = row == 0 ? new GridLength(28) : new GridLength(36) });
        }

        return grid;
    }

    private static TextBlock CreateMonthCaption()
    {
        var caption = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return caption;
    }

    private Button CreateNavigationButton(string content, int months, string accessibleName)
    {
        var button = new Button { Content = content };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ModernCalendarNavigationButtonStyle");
        button.Click += (_, _) =>
        {
            _displayMonth = _displayMonth.AddMonths(months);
            RebuildCalendar();
        };
        AutomationProperties.SetName(button, accessibleName);
        return button;
    }

    private void Open()
    {
        _workingDate = SelectedDate;
        _displayMonth = FirstOfMonth(SelectedDate);
        RebuildCalendar();
        _popup.IsOpen = true;
    }

    private void Apply()
    {
        SelectedDate = _workingDate;
        _popup.IsOpen = false;
        UpdateTrigger();
        DateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildCalendar()
    {
        _calendar.Children.Clear();
        _monthCaption.Text = _displayMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        AddDayHeadings();

        var firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var offset = ((int)_displayMonth.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        for (var day = 1; day <= DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month); day++)
        {
            var date = new DateTime(_displayMonth.Year, _displayMonth.Month, day);
            var position = offset + day - 1;
            var button = CreateDayButton(date);
            Grid.SetRow(button, (position / 7) + 1);
            Grid.SetColumn(button, position % 7);
            _calendar.Children.Add(button);
        }
    }

    private void AddDayHeadings()
    {
        var firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        for (var column = 0; column < 7; column++)
        {
            var dayOfWeek = (DayOfWeek)(((int)firstDayOfWeek + column) % 7);
            var dayName = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(int)dayOfWeek];
            var label = new TextBlock
            {
                Text = dayName.Length <= 2 ? dayName : dayName[..2],
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            Grid.SetColumn(label, column);
            _calendar.Children.Add(label);
        }
    }

    private Button CreateDayButton(DateTime date)
    {
        var button = new Button
        {
            Content = date.Day.ToString(CultureInfo.CurrentCulture),
            IsEnabled = _minimumDate is null || date >= _minimumDate.Value
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ModernCalendarDayButtonStyle");
        ToolTipService.SetToolTip(button, date.ToString("D", CultureInfo.CurrentCulture));
        AutomationProperties.SetName(button, date.ToString("D", CultureInfo.CurrentCulture));
        button.Click += (_, _) =>
        {
            _workingDate = date;
            RebuildCalendar();
        };

        if (date == _workingDate)
        {
            button.SetResourceReference(Button.BackgroundProperty, "AccentBrush");
            button.SetResourceReference(Button.ForegroundProperty, "AccentTextBrush");
        }
        else if (date == DateTime.Today)
        {
            button.SetResourceReference(Button.BorderBrushProperty, "AccentBrush");
            button.BorderThickness = new Thickness(1);
        }

        return button;
    }

    private void UpdateTrigger()
    {
        var text = SelectedDate.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
        if (_trigger.Tag is TextBlock label)
        {
            label.Text = text;
        }

        AutomationProperties.SetHelpText(_trigger, $"Current date: {text}");
    }

    private DateTime NormalizeDate(DateTime date) =>
        _minimumDate is { } minimum && date.Date < minimum ? minimum : date.Date;

    private static DateTime FirstOfMonth(DateTime date) => new(date.Year, date.Month, 1);
}
