using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBlock = System.Windows.Controls.TextBlock;
using Thickness = System.Windows.Thickness;
using UserControl = System.Windows.Controls.UserControl;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace VolturaAir.Host;

public sealed class ModernDateRangePicker : UserControl
{
    private readonly Button _trigger;
    private readonly Popup _popup;
    private readonly StackPanel _monthPanels;
    private readonly TextBlock _selectionSummary;
    private DateTime _displayMonth;
    private DateTime _workingStart;
    private DateTime _workingEnd;
    private DateTime? _pendingStart;

    public ModernDateRangePicker(DateTime startDate, DateTime endDate)
    {
        (SelectedStartDate, SelectedEndDate) = NormalizeRange(startDate, endDate);
        _workingStart = SelectedStartDate;
        _workingEnd = SelectedEndDate;
        _displayMonth = FirstOfMonth(SelectedStartDate);

        _trigger = CreateTriggerButton();
        _selectionSummary = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        _selectionSummary.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        _monthPanels = new StackPanel { Orientation = Orientation.Horizontal };
        _popup = CreatePopup();

        var root = new Grid();
        root.Children.Add(_trigger);
        root.Children.Add(_popup);
        Content = root;
        UpdateTrigger();
    }

    public DateTime SelectedStartDate { get; private set; }

    public DateTime SelectedEndDate { get; private set; }

    public event EventHandler? DateRangeChanged;

    public void SetRange(DateTime startDate, DateTime endDate)
    {
        (SelectedStartDate, SelectedEndDate) = NormalizeRange(startDate, endDate);
        _workingStart = SelectedStartDate;
        _workingEnd = SelectedEndDate;
        _pendingStart = null;
        _displayMonth = FirstOfMonth(SelectedStartDate);
        UpdateTrigger();
        DateRangeChanged?.Invoke(this, EventArgs.Empty);
    }

    private Button CreateTriggerButton()
    {
        var label = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        var icon = new TextBlock
        {
            Text = "▦",
            FontSize = 18,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var chevron = new TextBlock
        {
            Text = "⌄",
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
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = content,
            Tag = label
        };
        button.Click += (_, _) => Open();
        AutomationProperties.SetName(button, "Choose application log date range");
        return button;
    }

    private Popup CreatePopup()
    {
        var title = new TextBlock
        {
            Text = "Date range",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var titleRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_selectionSummary, 1);
        titleRow.Children.Add(title);
        titleRow.Children.Add(_selectionSummary);

        var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        presets.Children.Add(CreatePresetButton("Today", 1));
        presets.Children.Add(CreatePresetButton("Last 2 days", 2));
        presets.Children.Add(CreatePresetButton("Last 7 days", 7));
        presets.Children.Add(CreatePresetButton("Last 30 days", 30));

        var previous = CreateNavigationButton("‹", -1, "Previous month");
        var next = CreateNavigationButton("›", 1, "Next month");
        var monthsCaption = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        monthsCaption.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var navigation = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        Grid.SetColumn(monthsCaption, 1);
        Grid.SetColumn(next, 2);
        navigation.Children.Add(previous);
        navigation.Children.Add(monthsCaption);
        navigation.Children.Add(next);
        navigation.Tag = monthsCaption;

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
        panel.Children.Add(titleRow);
        panel.Children.Add(presets);
        panel.Children.Add(navigation);
        panel.Children.Add(_monthPanels);
        panel.Children.Add(footer);

        var chrome = new Border
        {
            Width = 622,
            Padding = new Thickness(18),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = panel
        };
        chrome.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        chrome.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        chrome.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                _popup.IsOpen = false;
                args.Handled = true;
            }
        };
        chrome.Tag = navigation;

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

    private Button CreatePresetButton(string label, int days)
    {
        var button = new Button
        {
            Content = label,
            MinHeight = 32,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 7, 0)
        };
        button.Click += (_, _) =>
        {
            var today = DateTime.Today;
            _workingStart = today.AddDays(-(days - 1));
            _workingEnd = today;
            _pendingStart = null;
            _displayMonth = FirstOfMonth(_workingStart);
            RebuildCalendar();
        };
        return button;
    }

    private Button CreateNavigationButton(string content, int months, string accessibleName)
    {
        var button = new Button { Content = content };
        button.SetResourceReference(FrameworkElement.StyleProperty, "DateRangeNavigationButtonStyle");
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
        _workingStart = SelectedStartDate;
        _workingEnd = SelectedEndDate;
        _pendingStart = null;
        _displayMonth = FirstOfMonth(_workingStart);
        RebuildCalendar();
        _popup.IsOpen = true;
    }

    private void Apply()
    {
        (SelectedStartDate, SelectedEndDate) = NormalizeRange(_workingStart, _workingEnd);
        _pendingStart = null;
        _popup.IsOpen = false;
        UpdateTrigger();
        DateRangeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectDate(DateTime date)
    {
        if (_pendingStart is null)
        {
            _pendingStart = date.Date;
            _workingStart = date.Date;
            _workingEnd = date.Date;
        }
        else
        {
            (_workingStart, _workingEnd) = NormalizeRange(_pendingStart.Value, date);
            _pendingStart = null;
        }

        RebuildCalendar();
    }

    private void RebuildCalendar()
    {
        _monthPanels.Children.Clear();
        _monthPanels.Children.Add(CreateMonthPanel(_displayMonth));
        _monthPanels.Children.Add(CreateMonthPanel(_displayMonth.AddMonths(1)));

        if (_popup.Child is Border { Tag: Grid navigation } && navigation.Tag is TextBlock monthsCaption)
        {
            var secondMonth = _displayMonth.AddMonths(1);
            monthsCaption.Text = _displayMonth.Year == secondMonth.Year
                ? $"{_displayMonth:MMMM} – {secondMonth:MMMM yyyy}"
                : $"{_displayMonth:MMMM yyyy} – {secondMonth:MMMM yyyy}";
        }

        _selectionSummary.Text = _pendingStart.HasValue
            ? $"Start: {_pendingStart.Value.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} · choose an end date"
            : FormatRange(_workingStart, _workingEnd);
    }

    private Border CreateMonthPanel(DateTime month)
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
            grid.Children.Add(label);
        }

        var first = FirstOfMonth(month);
        var offset = ((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        for (var day = 1; day <= DateTime.DaysInMonth(first.Year, first.Month); day++)
        {
            var date = new DateTime(first.Year, first.Month, day);
            var position = offset + day - 1;
            var button = CreateDayButton(date);
            Grid.SetRow(button, (position / 7) + 1);
            Grid.SetColumn(button, position % 7);
            grid.Children.Add(button);
        }

        var panel = new Border
        {
            Width = 284,
            Margin = new Thickness(4, 0, 4, 0),
            Padding = new Thickness(5),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Child = grid
        };
        panel.SetResourceReference(Border.BackgroundProperty, "WindowBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return panel;
    }

    private Button CreateDayButton(DateTime date)
    {
        var button = new Button { Content = date.Day.ToString(CultureInfo.CurrentCulture) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "DateRangeDayButtonStyle");
        ToolTipService.SetToolTip(button, date.ToString("D", CultureInfo.CurrentCulture));
        AutomationProperties.SetName(button, date.ToString("D", CultureInfo.CurrentCulture));
        button.Click += (_, _) => SelectDate(date);

        var isEndpoint = date == _workingStart || date == _workingEnd;
        var isInRange = date > _workingStart && date < _workingEnd;
        if (isEndpoint)
        {
            button.SetResourceReference(Button.BackgroundProperty, "AccentBrush");
            button.SetResourceReference(Button.ForegroundProperty, "AccentTextBrush");
        }
        else if (isInRange)
        {
            button.SetResourceReference(Button.BackgroundProperty, "SurfaceRaisedBrush");
        }

        if (date == DateTime.Today && !isEndpoint)
        {
            button.SetResourceReference(Button.BorderBrushProperty, "AccentBrush");
            button.BorderThickness = new Thickness(1);
        }

        return button;
    }

    private void UpdateTrigger()
    {
        var text = FormatRange(SelectedStartDate, SelectedEndDate);
        if (_trigger.Tag is TextBlock label)
        {
            label.Text = text;
        }

        AutomationProperties.SetHelpText(_trigger, $"Current range: {text}");
    }

    private static string FormatRange(DateTime start, DateTime end)
    {
        return start.Year == end.Year
            ? $"{start:MMM d} – {end:MMM d, yyyy}"
            : $"{start:MMM d, yyyy} – {end:MMM d, yyyy}";
    }

    private static (DateTime Start, DateTime End) NormalizeRange(DateTime start, DateTime end)
    {
        start = start.Date;
        end = end.Date;
        return start <= end ? (start, end) : (end, start);
    }

    private static DateTime FirstOfMonth(DateTime date) => new(date.Year, date.Month, 1);
}
