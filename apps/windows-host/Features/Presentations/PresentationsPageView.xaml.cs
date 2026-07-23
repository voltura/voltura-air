using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using WpfBrush = System.Windows.Media.Brush;
using WpfBorder = System.Windows.Controls.Border;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfGrid = System.Windows.Controls.Grid;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfSeparator = System.Windows.Controls.Separator;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace VolturaAir.Host.Features.Presentations;

public partial class PresentationsPageView : WpfUserControl
{
    private readonly IPresentationReportStore _store;
    private readonly ModernDateRangePicker _dateRange;
    private IReadOnlyList<PresentationReport> _reports = [];
    private List<PresentationReport> _filteredReports = [];
    private PresentationReport? _currentReport;
    private bool _updatingFilters;

    internal event Action<PresentationReport?>? DetailChanged;

    internal PresentationsPageView(IPresentationReportStore store)
    {
        _store = store;
        InitializeComponent();
        var result = _store.ReadAll();
        _reports = result.Succeeded ? result.Reports : [];
        var earliestDate = _reports.Count == 0
            ? DateTime.Today
            : _reports.Min(report => CapturedLocalDateTime(report).Date);
        var latestDate = _reports.Count == 0
            ? DateTime.Today
            : DateTime.Compare(DateTime.Today, _reports.Max(report => CapturedLocalDateTime(report).Date)) >= 0
                ? DateTime.Today
                : _reports.Max(report => CapturedLocalDateTime(report).Date);
        _dateRange = new ModernDateRangePicker(earliestDate, latestDate) { Width = 230 };
        _dateRange.DateRangeChanged += OnFilterChanged;
        DateRangeHost.Content = _dateRange;
        SearchBox.TextChanged += OnFilterChanged;
        RebuildDeviceFilter();
        ApplyFilters();
    }

    private void Refresh()
    {
        var result = _store.ReadAll();
        _reports = result.Succeeded ? result.Reports : [];
        RebuildDeviceFilter();
        ApplyFilters();
    }

    private void RebuildDeviceFilter()
    {
        var selected = (DeviceFilter.SelectedItem as WpfComboBoxItem)?.Tag as string ?? string.Empty;
        DeviceFilter.Items.Clear();
        DeviceFilter.Items.Add(new WpfComboBoxItem { Content = "All devices", Tag = string.Empty });
        foreach (var device in _reports
            .Select(report => report.DeviceName)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
        {
            DeviceFilter.Items.Add(new WpfComboBoxItem { Content = device, Tag = device });
        }

        DeviceFilter.SelectedItem = DeviceFilter.Items
            .OfType<WpfComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, selected, StringComparison.Ordinal))
            ?? DeviceFilter.Items[0];
    }

    private void ApplyFilters()
    {
        if (!IsInitialized)
        {
            return;
        }

        var query = SearchBox.Text.Trim();
        var target = (TypeFilter.SelectedItem as WpfComboBoxItem)?.Tag as string ?? string.Empty;
        var device = (DeviceFilter.SelectedItem as WpfComboBoxItem)?.Tag as string ?? string.Empty;
        var from = _dateRange.SelectedStartDate;
        var toExclusive = _dateRange.SelectedEndDate.AddDays(1);
        _filteredReports =
        [.. _reports.Where(report =>
            (query.Length == 0 || report.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (target.Length == 0 || string.Equals(report.Target, target, StringComparison.Ordinal)) &&
            (device.Length == 0 || string.Equals(report.DeviceName, device, StringComparison.OrdinalIgnoreCase)) &&
            CapturedLocalDateTime(report).DateTime >= from &&
            CapturedLocalDateTime(report).DateTime < toExclusive)
            .OrderByDescending(report => report.StartedAt)];

        ReportList.ItemsSource = _filteredReports.Select(ToArchiveItem).ToList();
        ReportList.Visibility = _filteredReports.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = _filteredReports.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SummaryCards.ItemsSource = CreateArchiveSummary(_filteredReports);
        ArchiveExportButton.IsEnabled = _filteredReports.Count > 0;
        ArchiveEmailButton.IsEnabled = _filteredReports.Count > 0;
        var filtersActive = HasActiveFilters();
        ArchiveDeleteButton.Content = filtersActive ? "Delete filtered" : "Delete all";
        ArchiveDeleteButton.IsEnabled = filtersActive
            ? _filteredReports.Count > 0
            : _reports.Count > 0;
    }

    internal void ShowReport(PresentationReport report)
    {
        ArchiveView.Visibility = Visibility.Collapsed;
        DetailView.Visibility = Visibility.Visible;
        _currentReport = report;
        DetailChanged?.Invoke(report);
        var hasAvailableFile = !string.IsNullOrWhiteSpace(report.PresentationFilePath) &&
            File.Exists(report.PresentationFilePath);
        PresentationFileStatus.Fill = (WpfBrush)FindResource(
            hasAvailableFile ? "SuccessStrongBrush" : "DangerBrush");
        PresentationFileButton.ToolTip = hasAvailableFile
            ? $"Selected file: {Path.GetFileName(report.PresentationFilePath)}{Environment.NewLine}{report.PresentationFilePath}"
            : string.IsNullOrWhiteSpace(report.PresentationFilePath)
                ? "No presentation file selected. Press to select a file."
                : $"The selected file is no longer available. Press to select a replacement.{Environment.NewLine}{report.PresentationFilePath}";

        var hasUrl = !string.IsNullOrWhiteSpace(report.PresentationUrl);
        PresentationUrlStatus.Fill = (WpfBrush)FindResource(hasUrl ? "SuccessStrongBrush" : "DangerBrush");
        PresentationUrlButton.ToolTip = hasUrl
            ? $"Presentation URL:{Environment.NewLine}{report.PresentationUrl}"
            : "No presentation URL selected. Press to enter a URL.";

        var breakSeconds = report.BreakDurationSeconds;
        var totalSeconds = report.PresentationDurationSeconds + breakSeconds;
        DetailSummaryCards.ItemsSource = new SummaryCard[]
        {
            new SummaryCard("Total elapsed", FormatDuration(totalSeconds)),
            new SummaryCard("Presenting time", FormatDuration(report.PresentationDurationSeconds)),
            new SummaryCard("Sessions", report.SessionCount.ToString(CultureInfo.CurrentCulture)),
            new SummaryCard("Slides presented", report.Slides.Count.ToString(CultureInfo.CurrentCulture)),
            new SummaryCard("Breaks", report.Breaks.Count.ToString(CultureInfo.CurrentCulture)),
            new SummaryCard("Break time", FormatDuration(breakSeconds))
        };

        BuildTimeline(report);
        PresentationLegend.Text = $"● Presenting  {FormatDuration(report.PresentationDurationSeconds)}";
        BreakLegend.Text = $"● Breaks  {FormatDuration(breakSeconds)}";
        DetailRows.ItemsSource = CreateDetailRows(report);
    }

    private void BuildTimeline(PresentationReport report)
    {
        TimelineSegments.ColumnDefinitions.Clear();
        TimelineSegments.Children.Clear();
        var breaks = report.Breaks.OrderBy(entry => entry.BreakNumber).ToList();
        var checkpoints = breaks.Select(entry => entry.PresentationElapsedSeconds).ToList();
        var previousCheckpoint = 0d;
        for (var index = 0; index < report.SessionCount; index += 1)
        {
            var sessionEnd = index < checkpoints.Count
                ? checkpoints[index]
                : report.PresentationDurationSeconds;
            AddTimelineSegment(
                $"Session {index + 1}, {FormatDuration(Math.Max(0, sessionEnd - previousCheckpoint))}",
                Math.Max(0, sessionEnd - previousCheckpoint),
                "PresentationSegmentBrush");
            previousCheckpoint = sessionEnd;
            if (index < report.Breaks.Count)
            {
                var entry = report.Breaks[index];
                AddTimelineSegment(
                    $"Break {entry.BreakNumber}, {FormatDuration(entry.BreakDurationSeconds)}",
                    entry.BreakDurationSeconds,
                    "PresentationBreakBrush");
            }
        }
    }

    private void AddTimelineSegment(string accessibleName, double durationSeconds, string brushResource)
    {
        var columnIndex = TimelineSegments.ColumnDefinitions.Count;
        TimelineSegments.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
        {
            Width = new GridLength(Math.Max(0.001, durationSeconds), GridUnitType.Star)
        });
        var segment = new WpfBorder
        {
            Background = (WpfBrush)FindResource(brushResource),
            ToolTip = accessibleName
        };
        AutomationProperties.SetName(segment, accessibleName);
        WpfGrid.SetColumn(segment, columnIndex);
        TimelineSegments.Children.Add(segment);
    }

    private List<PresentationDetailRow> CreateDetailRows(PresentationReport report)
    {
        var rows = new List<PresentationDetailRow>();
        var breaks = report.Breaks.OrderBy(entry => entry.BreakNumber).ToList();
        var checkpoints = breaks.Select(entry => entry.PresentationElapsedSeconds).ToList();
        var sessionStarts = new List<double> { 0 };
        sessionStarts.AddRange(checkpoints);
        var sessionEnds = new List<double>(checkpoints) { report.PresentationDurationSeconds };
        var totalElapsed = 0d;

        for (var index = 0; index < report.SessionCount; index += 1)
        {
            var duration = Math.Max(0, sessionEnds[index] - sessionStarts[index]);
            totalElapsed += duration;
            var slideDetail = SessionSlideDetail(report, index);
            rows.Add(new(
                $"Session {index + 1}",
                slideDetail,
                FormatDuration(duration),
                FormatDuration(totalElapsed),
                (WpfBrush)FindResource("PresentationSegmentBrush"),
                SortOrder: index * 2));

            if (index < breaks.Count)
            {
                var entry = breaks[index];
                totalElapsed += Math.Max(0, entry.BreakDurationSeconds);
                rows.Add(new(
                    $"Break {entry.BreakNumber}",
                    $"{CapturedLocalDateTime(report, entry.StartedAt):HH:mm:ss}–{CapturedLocalDateTime(report, entry.EndedAt):HH:mm:ss}",
                    FormatDuration(entry.BreakDurationSeconds),
                    FormatDuration(totalElapsed),
                    (WpfBrush)FindResource("PresentationBreakBrush"),
                    SortOrder: index * 2 + 1));
            }
        }

        return [.. rows.OrderBy(row => row.SortOrder)];
    }

    private static string SessionSlideDetail(PresentationReport report, int sessionIndex)
    {
        int? minimum;
        int? maximum;
        if (sessionIndex < report.Breaks.Count)
        {
            minimum = report.Breaks[sessionIndex].SessionSlideMinimum;
            maximum = report.Breaks[sessionIndex].SessionSlideMaximum;
        }
        else
        {
            var previousMaximum = report.Breaks.Count == 0
                ? null
                : report.Breaks[^1].SessionSlideMaximum;
            minimum = previousMaximum;
            maximum = report.Slides.Count == 0 ? previousMaximum : report.Slides.Max(slide => slide.SlideNumber);
        }

        return minimum is null
            ? "No slide navigation recorded"
            : minimum == maximum
                ? $"Slide {minimum}"
                : $"Slides {minimum}–{maximum}";
    }

    private static IReadOnlyList<SummaryCard> CreateArchiveSummary(List<PresentationReport> reports)
    {
        var presenting = reports.Sum(report => report.PresentationDurationSeconds);
        var breaks = reports.Sum(report => report.BreakDurationSeconds);
        return
        [
            new("Presentations", reports.Count.ToString(CultureInfo.CurrentCulture)),
            new("PowerPoint", reports.Count(report => report.Target == "powerpoint").ToString(CultureInfo.CurrentCulture)),
            new("Google Slides", reports.Count(report => report.Target == "google-slides").ToString(CultureInfo.CurrentCulture)),
            new("PDF / browser", reports.Count(report => report.Target == "pdf").ToString(CultureInfo.CurrentCulture)),
            new("Presenting time", FormatDuration(presenting)),
            new("Break time", FormatDuration(breaks)),
            new("Average duration", FormatDuration(reports.Count == 0 ? 0 : presenting / reports.Count)),
            new("Breaks", reports.Sum(report => report.Breaks.Count).ToString(CultureInfo.CurrentCulture))
        ];
    }

    private static PresentationArchiveItem ToArchiveItem(PresentationReport report) => new(
        report.ReportId,
        PresentationReportNames.DisplayName(report),
        TargetLabel(report.Target),
        report.DeviceName,
        CapturedLocalDateTime(report).ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
        $"Presenting {FormatDuration(report.PresentationDurationSeconds)}",
        $"{report.Breaks.Count} break{Plural(report.Breaks.Count)} · {FormatDuration(report.BreakDurationSeconds)}",
        report.Slides.Count == 0 ? "No slide navigation recorded" : $"{report.Slides.Count} slide{Plural(report.Slides.Count)}");

    private static string TargetLabel(string target) => target switch
    {
        "powerpoint" => "PowerPoint",
        "google-slides" => "Google Slides",
        "pdf" => "PDF / browser",
        _ => "Presentation"
    };

    private static string FormatDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }

    internal static DateTimeOffset CapturedLocalDateTime(PresentationReport report) =>
        CapturedLocalDateTime(report, report.StartedAt);

    private static DateTimeOffset CapturedLocalDateTime(
        PresentationReport report,
        DateTimeOffset timestamp) =>
        timestamp.ToOffset(TimeSpan.FromMinutes(report.UtcOffsetMinutes));

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private void OnFilterChanged(object? sender, EventArgs e)
    {
        if (!_updatingFilters)
        {
            ApplyFilters();
        }
    }

    private void OnClearFilters(object sender, RoutedEventArgs e)
    {
        ClearFilters();
    }

    private void ClearFilters()
    {
        _updatingFilters = true;
        SearchBox.Clear();
        TypeFilter.SelectedIndex = 0;
        DeviceFilter.SelectedIndex = 0;
        var earliestDate = _reports.Count == 0
            ? DateTime.Today
            : _reports.Min(report => CapturedLocalDateTime(report).Date);
        var latestDate = _reports.Count == 0
            ? DateTime.Today
            : DateTime.Compare(DateTime.Today, _reports.Max(report => CapturedLocalDateTime(report).Date)) >= 0
                ? DateTime.Today
                : _reports.Max(report => CapturedLocalDateTime(report).Date);
        _dateRange.SetRange(earliestDate, latestDate);
        _updatingFilters = false;
        ApplyFilters();
    }

    private void OnOpenReport(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string reportId })
        {
            var report = _reports.FirstOrDefault(item => string.Equals(item.ReportId, reportId, StringComparison.Ordinal));
            if (report is not null)
            {
                ShowReport(report);
            }
        }
    }

    private void OnReportRowMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 ||
            sender is not WpfBorder { DataContext: PresentationArchiveItem item })
        {
            return;
        }

        var report = _reports.FirstOrDefault(candidate =>
            string.Equals(candidate.ReportId, item.ReportId, StringComparison.Ordinal));
        if (report is not null)
        {
            e.Handled = true;
            ShowReport(report);
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        _currentReport = null;
        DetailChanged?.Invoke(null);
        DetailView.Visibility = Visibility.Collapsed;
        ArchiveView.Visibility = Visibility.Visible;
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void OnArchiveExport(object sender, RoutedEventArgs e) =>
        ShowFormatMenu((WpfButton)sender, _filteredReports, email: false);

    private void OnArchiveEmail(object sender, RoutedEventArgs e) =>
        BeginEmail((WpfButton)sender, _filteredReports);

    private void OnDetailExport(object sender, RoutedEventArgs e)
    {
        if (_currentReport is not null)
        {
            ShowFormatMenu((WpfButton)sender, [_currentReport], email: false);
        }
    }

    private void OnDetailEmail(object sender, RoutedEventArgs e)
    {
        if (_currentReport is not null)
        {
            BeginEmail((WpfButton)sender, [_currentReport]);
        }
    }

    private void ShowFormatMenu(
        WpfButton ownerButton,
        IReadOnlyList<PresentationReport> reports,
        bool email,
        IReadOnlyList<string>? presentationFiles = null)
    {
        var menu = new WpfContextMenu
        {
            Placement = PlacementMode.Bottom,
            PlacementTarget = ownerButton
        };
        menu.SetResourceReference(StyleProperty, "EventMultiSelectContextMenuStyle");
        if (email)
        {
            AddMenuItem(menu, "As email body", () => ShowShareResult(
                PresentationReportSharing.EmailBody(reports, presentationFiles ?? [])));
        }

        AddMenuItem(menu, email ? "Web page attachment (.html)" : "Web page file (.html)",
            () => Share(reports, PresentationExportFormat.Html, email, presentationFiles));
        AddMenuItem(menu, email ? "Excel attachment (.xlsx)" : "Excel workbook (.xlsx)",
            () => Share(reports, PresentationExportFormat.Excel, email, presentationFiles));
        AddMenuItem(menu, email ? "PDF attachment (.pdf)" : "Portable Document Format (.pdf)",
            () => Share(reports, PresentationExportFormat.Pdf, email, presentationFiles));
        AddMenuItem(menu, email ? "CSV attachment (.csv)" : "Comma Separated Values (.csv)",
            () => Share(reports, PresentationExportFormat.Csv, email, presentationFiles));
        AddMenuItem(menu, email ? "Text attachment (.txt)" : "Text file (.txt)",
            () => Share(reports, PresentationExportFormat.Text, email, presentationFiles));
        menu.Items.Add(new WpfSeparator());
        AddMenuItem(menu, "Cancel", () => menu.IsOpen = false);
        menu.IsOpen = true;
    }

    private static void AddMenuItem(WpfContextMenu menu, string label, Action action)
    {
        var item = new WpfMenuItem
        {
            Header = label,
            FocusVisualStyle = null
        };
        item.SetResourceReference(StyleProperty, "EventMultiSelectMenuItemStyle");
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void Share(
        IReadOnlyList<PresentationReport> reports,
        PresentationExportFormat format,
        bool email,
        IReadOnlyList<string>? presentationFiles)
    {
        var result = email
            ? PresentationReportSharing.EmailAttachment(reports, format, presentationFiles ?? [])
            : PresentationReportSharing.Export(Window.GetWindow(this), reports, format);
        ShowShareResult(result);
    }

    private void BeginEmail(WpfButton ownerButton, List<PresentationReport> reports)
    {
        var owner = Window.GetWindow(this);
        var choice = PresentationEmailFileDialog.Show(owner, reports.Count > 1);
        if (choice == PresentationEmailFileChoice.Cancel)
        {
            return;
        }

        List<string> presentationFiles = [];
        if (choice == PresentationEmailFileChoice.IncludePresentationFiles)
        {
            if (!TryResolveRequestedPresentationFiles(
                reports,
                ResolveSinglePresentationFile,
                out presentationFiles))
            {
                return;
            }

            if (reports.Count == 1)
            {
                reports =
                [.. _reports
                    .Where(report => string.Equals(report.ReportId, reports[0].ReportId, StringComparison.Ordinal))
                    .Take(1)];
            }
        }

        ShowFormatMenu(ownerButton, reports, email: true, presentationFiles);
    }

    internal static bool TryResolveRequestedPresentationFiles(
        IReadOnlyList<PresentationReport> reports,
        Func<PresentationReport, string?> resolveSingle,
        out List<string> presentationFiles)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(resolveSingle);
        presentationFiles = [];
        if (reports.Count == 1)
        {
            var path = resolveSingle(reports[0]);
            if (path is null)
            {
                return false;
            }

            presentationFiles = [path];
            return true;
        }

        presentationFiles = [.. CollectAvailablePresentationFiles(reports)];
        return true;
    }

    private string? ResolveSinglePresentationFile(PresentationReport report)
    {
        if (!string.IsNullOrWhiteSpace(report.PresentationFilePath) && File.Exists(report.PresentationFilePath))
        {
            return report.PresentationFilePath;
        }

        return SelectAndStorePresentationFile(report);
    }

    internal static IReadOnlyList<string> CollectAvailablePresentationFiles(
        IReadOnlyList<PresentationReport> reports) =>
        [.. reports
            .Select(report => report.PresentationFilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private string? SelectAndStorePresentationFile(PresentationReport report)
    {
        var dialog = new WpfOpenFileDialog
        {
            CheckFileExists = true,
            CheckPathExists = true,
            Filter = "Presentation files (*.ppt;*.pptx;*.pps;*.ppsx;*.pdf;*.html)|*.ppt;*.pptx;*.pps;*.ppsx;*.pdf;*.html|All files (*.*)|*.*",
            Multiselect = false,
            Title = "Select presentation file"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return null;
        }

        var result = _store.SetPresentationFile(report.ReportId, dialog.FileName);
        if (!result.Succeeded)
        {
            ThemedConfirmationDialog.ShowInformation(
                Window.GetWindow(this),
                "Presentation file",
                result.Message,
                ConfirmationTone.Warning);
            return null;
        }

        Refresh();
        var updated = _reports.FirstOrDefault(item => string.Equals(item.ReportId, report.ReportId, StringComparison.Ordinal));
        if (updated is not null && _currentReport?.ReportId == report.ReportId)
        {
            ShowReport(updated);
        }

        return dialog.FileName;
    }

    private void ShowShareResult(PresentationShareResult result)
    {
        var owner = Window.GetWindow(this);
        if (!result.Succeeded)
        {
            if (result.Message.Length > 0)
            {
                ThemedConfirmationDialog.ShowInformation(owner, "Presentation sharing", result.Message, ConfirmationTone.Warning);
            }

            return;
        }

        if (result.RequiresNotice)
        {
            ThemedConfirmationDialog.ShowInformation(owner, "Presentation sharing", result.Message);
        }
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        if (_currentReport is null)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        var currentName = PresentationReportNames.DisplayName(_currentReport);
        var title = PresentationRenameDialog.Show(owner, currentName);
        if (title is null || string.Equals(title, currentName, StringComparison.Ordinal))
        {
            return;
        }

        var reportId = _currentReport.ReportId;
        var result = _store.Rename(reportId, title);
        if (!result.Succeeded)
        {
            ThemedConfirmationDialog.ShowInformation(owner, "Rename presentation", result.Message, ConfirmationTone.Warning);
            return;
        }

        Refresh();
        var updated = _reports.FirstOrDefault(report => string.Equals(report.ReportId, reportId, StringComparison.Ordinal));
        if (updated is not null)
        {
            ShowReport(updated);
        }
    }

    private void OnPresentationFile(object sender, RoutedEventArgs e)
    {
        if (_currentReport is not null)
        {
            _ = SelectAndStorePresentationFile(_currentReport);
        }
    }

    private void OnPresentationUrl(object sender, RoutedEventArgs e)
    {
        if (_currentReport is null)
        {
            return;
        }

        var url = PresentationUrlDialog.Show(Window.GetWindow(this), _currentReport.PresentationUrl);
        if (url is null || string.Equals(url, _currentReport.PresentationUrl, StringComparison.Ordinal))
        {
            return;
        }

        var reportId = _currentReport.ReportId;
        var result = _store.SetPresentationUrl(reportId, url);
        if (!result.Succeeded)
        {
            ThemedConfirmationDialog.ShowInformation(
                Window.GetWindow(this),
                "Presentation URL",
                result.Message,
                ConfirmationTone.Warning);
            return;
        }

        Refresh();
        var updated = _reports.FirstOrDefault(report => string.Equals(report.ReportId, reportId, StringComparison.Ordinal));
        if (updated is not null)
        {
            ShowReport(updated);
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_currentReport is null)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        if (!ThemedConfirmationDialog.Show(
            owner,
            "Delete presentation?",
            "This removes the saved presentation report from this PC.",
            "Delete",
            "Cancel",
            ConfirmationTone.Warning))
        {
            return;
        }

        var result = _store.Delete(_currentReport.ReportId);
        if (!result.Succeeded)
        {
            ThemedConfirmationDialog.ShowInformation(owner, "Delete presentation", result.Message, ConfirmationTone.Warning);
            return;
        }

        _currentReport = null;
        DetailChanged?.Invoke(null);
        DetailView.Visibility = Visibility.Collapsed;
        ArchiveView.Visibility = Visibility.Visible;
        Refresh();
    }

    private void OnDeleteAll(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var filtersActive = HasActiveFilters();
        var reportsToDelete = filtersActive ? _filteredReports : [.. _reports];
        var count = reportsToDelete.Count;
        if (count == 0)
        {
            return;
        }

        var title = filtersActive ? "Delete filtered presentations?" : "Delete all presentations?";
        var message = filtersActive
            ? $"This permanently removes {count} presentation report{Plural(count)} matching the current filters. Other reports stay saved."
            : $"This permanently removes all {count} saved presentation report{Plural(count)} from this PC.";
        var actionLabel = filtersActive ? "Delete filtered" : "Delete all";
        if (!ThemedConfirmationDialog.Show(
            owner,
            title,
            message,
            actionLabel,
            "Cancel",
            ConfirmationTone.Warning))
        {
            return;
        }

        var result = filtersActive
            ? _store.DeleteMany([.. reportsToDelete.Select(report => report.ReportId)])
            : _store.DeleteAll();
        if (!result.Succeeded)
        {
            ThemedConfirmationDialog.ShowInformation(owner, actionLabel, result.Message, ConfirmationTone.Warning);
            return;
        }

        if (filtersActive)
        {
            Refresh();
            ClearFilters();
        }
        else
        {
            Refresh();
        }
    }

    private bool HasActiveFilters()
    {
        var target = (TypeFilter.SelectedItem as WpfComboBoxItem)?.Tag as string ?? string.Empty;
        var device = (DeviceFilter.SelectedItem as WpfComboBoxItem)?.Tag as string ?? string.Empty;
        var (earliestDate, latestDate) = ArchiveDateBounds();
        return SearchBox.Text.Trim().Length > 0 ||
            target.Length > 0 ||
            device.Length > 0 ||
            _dateRange.SelectedStartDate.Date != earliestDate ||
            _dateRange.SelectedEndDate.Date != latestDate;
    }

    private (DateTime Earliest, DateTime Latest) ArchiveDateBounds()
    {
        var earliest = _reports.Count == 0
            ? DateTime.Today
            : _reports.Min(report => CapturedLocalDateTime(report).Date);
        var latestReportDate = _reports.Count == 0
            ? DateTime.Today
            : _reports.Max(report => CapturedLocalDateTime(report).Date);
        var latest = DateTime.Compare(DateTime.Today, latestReportDate) >= 0
            ? DateTime.Today
            : latestReportDate;
        return (earliest, latest);
    }

    private sealed record SummaryCard(string Label, string Value);
    private sealed record PresentationArchiveItem(
        string ReportId,
        string Title,
        string TypeLabel,
        string DeviceName,
        string DateLabel,
        string PresentationLabel,
        string BreakLabel,
        string SlideSummary);
    private sealed record PresentationDetailRow(
        string Label,
        string Detail,
        string Duration,
        string Elapsed,
        WpfBrush ValueBrush,
        int SortOrder);
}
