using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ComboBox = System.Windows.Controls.ComboBox;
using Control = System.Windows.Controls.Control;
using Brush = System.Windows.Media.Brush;
using Orientation = System.Windows.Controls.Orientation;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private IReadOnlyList<DiagnosticItem> GetDiagnostics()
    {
        return
        [
            new("Voltura Air host version", AppVersion.Display),
            new("Voltura Air web client version", "copy mobile diagnostics for web client version"),
            new("PC name", Environment.MachineName),
            new("Selected adapter", _webHost.SelectedAdapterName),
            new("Selected IP", _webHost.AdvertisedHostAddress),
            new("Selected port", _webHost.Port.ToString(CultureInfo.InvariantCulture)),
            new("Host URL", _webHost.ServerUrl),
            new("Current WebSocket URL", _webHost.WebSocketUrl),
            new("Windows lock policy", _workstationLockPolicy.GetStatus().State.ToString().ToLowerInvariant()),
            new("Application logging", AppLoggingSettings.IsEnabled() ? "enabled" : "disabled"),
            new("Application log retention", $"{AppLoggingSettings.GetMaxAgeDays().ToString(CultureInfo.InvariantCulture)} days"),
            new("Application log folder", _appLog.LogDirectory),
            new("Pairing state", GetPairingState()),
            new("Last error code", GetLastErrorCode()),
            new("Last error message", GetLastErrorMessage()),
            new("Paired device count", _pairingManager.PairedDeviceCount.ToString(CultureInfo.InvariantCulture)),
            new("Connected device count", _pairingManager.ActiveControllerCount.ToString(CultureInfo.InvariantCulture)),
            new("Paired devices", _pairingManager.PairedDeviceSummary),
            new("Active devices", _pairingManager.HasActiveController ? _pairingManager.ActiveDeviceSummary : "none"),
            new("Data folder", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltura Air")),
            new("Executable", Environment.ProcessPath ?? string.Empty)
        ];
    }

    private string BuildDiagnosticsText()
    {
        var lines = new List<string>
        {
            "Voltura Air diagnostics",
            $"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}"
        };
        lines.AddRange(GetDiagnostics().Select(detail => $"{detail.Name}: {RedactDiagnosticValue(detail.Value)}"));
        return string.Join(Environment.NewLine, lines);
    }

    private string GetPairingState()
    {
        if (_pairingManager.HasActiveController)
        {
            return "connected";
        }

        return _pairingManager.IsPaired ? "paired-not-connected" : "ready-to-pair";
    }

    private string GetLastErrorCode()
    {
        if (!string.IsNullOrWhiteSpace(_webHost.PortSelectionWarning))
        {
            return "VAIR-HOST-PORT-WARNING";
        }

        if (!string.IsNullOrWhiteSpace(_webHost.AddressSelectionWarning))
        {
            return "VAIR-HOST-NETWORK-WARNING";
        }

        return "none";
    }

    private string GetLastErrorMessage()
    {
        var messages = new[]
        {
            _webHost.AddressSelectionWarning,
            _webHost.PortSelectionWarning
        }.Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();

        return messages.Length == 0 ? "none" : string.Join(" ", messages);
    }

    private static string RedactDiagnosticValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.Contains("t=", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("pairToken", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("hash", StringComparison.OrdinalIgnoreCase))
        {
            return "[redacted]";
        }

        return value;
    }

    private UIElement BuildDiagnosticsPage()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var applicationLogButton = CreateSegmentButton("Application log", isChecked: true);
        var systemDetailsButton = CreateSegmentButton("System details", isChecked: false);
        WireSegmentPair(applicationLogButton, systemDetailsButton);
        var viewSelector = CreateSegmentRow(applicationLogButton, systemDetailsButton);
        viewSelector.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(viewSelector);

        var viewContent = new ContentControl();
        Grid.SetRow(viewContent, 1);
        root.Children.Add(viewContent);

        void ShowApplicationLog()
        {
            viewContent.Content = CreateApplicationLogViewer();
        }

        applicationLogButton.Click += (_, _) => ShowApplicationLog();
        systemDetailsButton.Click += (_, _) => viewContent.Content = BuildSystemDiagnosticsView();
        ShowApplicationLog();
        return root;
    }

    private UIElement BuildSystemDiagnosticsView()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var rows = new StackPanel();
        foreach (var detail in GetDiagnostics())
        {
            rows.Children.Add(CreateDiagnosticRow(detail));
        }

        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = rows
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(CreateButton("Copy diagnostics", (_, _) => CopyToClipboard(BuildDiagnosticsText(), "Diagnostics copied"), primary: true));
        actions.Children.Add(CreateButton("Open product page", (_, _) => OpenProductSite()));
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);
        return root;
    }

    private UIElement CreateApplicationLogViewer()
    {
        var root = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var controls = new StackPanel();
        controls.Children.Add(CreateSectionHeading("Application log"));
        controls.Children.Add(CreateMutedText("View and filter sanitized remote-command and Windows-host activity. The newest 250 matching entries are shown."));
        var loggingToggle = CreateCheckBox("Write application log", AppLoggingSettings.IsEnabled());
        loggingToggle.Margin = new Thickness(0, 8, 0, 8);
        controls.Children.Add(loggingToggle);

        var today = DateTime.Today;
        var dateRange = new ModernDateRangePicker(today.AddDays(-(AppLoggingSettings.GetMaxAgeDays() - 1)), today);
        var eventFilter = new EventMultiSelectFilter(
            ("Host actions", "host_action"),
            ("Commands received", "command_received"),
            ("Command outcomes", "command_outcome"),
            ("Actions taken", "action_taken"),
            ("Responses sent", "response_sent"));
        var sourceFilter = CreateLogFilter(
            ("All sources", null),
            ("Remote client", "remote_client"),
            ("Windows host", "windows_host"));
        var actionFilter = CreateLogFilter(("All actions", null));

        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        filters.Children.Add(CreateLogFilterField("Date range", dateRange));
        filters.Children.Add(CreateLogFilterField("Event", eventFilter));
        filters.Children.Add(CreateLogFilterField("Source", sourceFilter));
        filters.Children.Add(CreateLogFilterField("Action", actionFilter));
        controls.Children.Add(filters);

        var status = CreateMutedText(string.Empty);
        controls.Children.Add(status);
        root.Children.Add(controls);
        var logRows = new StackPanel();
        var logScroller = new ScrollViewer
        {
            MinHeight = 100,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = logRows
        };
        var logFrame = new Border
        {
            Background = (Brush)Resources["WindowBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = logScroller
        };
        Grid.SetRow(logFrame, 1);
        root.Children.Add(logFrame);

        var visibleText = string.Empty;
        var updatingFilters = false;
        void RefreshLog()
        {
            var selectedAction = GetLogFilterValue(actionFilter);
            var result = _appLog.Read(new AppLogQuery(
                DateOnly.FromDateTime(dateRange.SelectedStartDate),
                DateOnly.FromDateTime(dateRange.SelectedEndDate),
                Source: GetLogFilterValue(sourceFilter),
                MaxEntries: 5000));
            if (!result.Succeeded)
            {
                SetLogViewerError(status, logRows, $"The application log could not be read: {result.Error}");
                visibleText = string.Empty;
                return;
            }

            var selectedEvents = eventFilter.SelectedValues;
            var eventEntries = result.Entries
                .Where(entry => selectedEvents.Count == 0 || selectedEvents.Contains(entry.Event))
                .ToArray();
            updatingFilters = true;
            PopulateActionFilter(actionFilter, eventEntries, selectedAction);
            updatingFilters = false;
            var effectiveAction = GetLogFilterValue(actionFilter);
            var filtered = eventEntries
                .Where(entry => string.IsNullOrWhiteSpace(effectiveAction) || string.Equals(entry.Action, effectiveAction, StringComparison.OrdinalIgnoreCase))
                .Reverse()
                .Take(250)
                .ToArray();
            visibleText = string.Join(Environment.NewLine, filtered.Select(FormatAppLogEntry));
            logRows.Children.Clear();
            if (filtered.Length == 0)
            {
                logRows.Children.Add(CreateLogEmptyState(
                    AppLoggingSettings.IsEnabled()
                        ? "No matching log entries."
                        : "Application logging is off. Enable Write application log above to record new activity."));
            }
            else
            {
                foreach (var entry in filtered)
                {
                    logRows.Children.Add(CreateAppLogRow(entry));
                }
            }

            logScroller.ScrollToTop();
            var loggingEnabled = AppLoggingSettings.IsEnabled();
            status.Foreground = (Brush)Resources[loggingEnabled ? "MutedTextBrush" : "DangerBrush"];
            var state = loggingEnabled
                ? "Logging is enabled."
                : "Logging is off. No new activity is being written; existing entries remain available.";
            var limitNote = result.Truncated || filtered.Length == 250 ? " Only the newest matching entries are shown." : string.Empty;
            status.Text = $"{state} Showing {filtered.Length.ToString(CultureInfo.InvariantCulture)} entries.{limitNote}";
        }

        void RefreshForFilterChange()
        {
            if (!updatingFilters)
            {
                RefreshLog();
            }
        }

        dateRange.DateRangeChanged += (_, _) => RefreshForFilterChange();
        eventFilter.SelectionChanged += (_, _) => RefreshForFilterChange();
        sourceFilter.SelectionChanged += (_, _) => RefreshForFilterChange();
        actionFilter.SelectionChanged += (_, _) => RefreshForFilterChange();
        loggingToggle.Checked += (_, _) =>
        {
            AppLoggingSettings.SetEnabled(true);
            _appLog.Write(new AppLogEntry("host_action", "windows_host", Action: "application_logging", Outcome: "enabled"));
            RefreshLog();
        };
        loggingToggle.Unchecked += (_, _) =>
        {
            _appLog.Write(new AppLogEntry("host_action", "windows_host", Action: "application_logging", Outcome: "disabled"));
            AppLoggingSettings.SetEnabled(false);
            RefreshLog();
        };

        var clearFilters = CreateButton(string.Empty, (_, _) =>
        {
            updatingFilters = true;
            dateRange.SetRange(today.AddDays(-(AppLoggingSettings.GetMaxAgeDays() - 1)), today);
            eventFilter.Clear();
            sourceFilter.SelectedIndex = 0;
            actionFilter.SelectedIndex = 0;
            updatingFilters = false;
            RefreshLog();
        });
        clearFilters.Style = (Style)Resources["CompactIconButtonStyle"];
        clearFilters.Content = new Grid
        {
            Width = 16,
            Height = 16,
            Children =
            {
                new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 3,3 L 13,13"),
                    Stroke = (Brush)Resources["TextBrush"],
                    StrokeThickness = 1.8,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                },
                new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 13,3 L 3,13"),
                    Stroke = (Brush)Resources["TextBrush"],
                    StrokeThickness = 1.8,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                }
            }
        };
        clearFilters.Margin = new Thickness(0, 19, 0, 8);
        clearFilters.FocusVisualStyle = null;
        clearFilters.ToolTip = "Clear filters";
        AutomationProperties.SetName(clearFilters, "Clear filters");
        filters.Children.Add(clearFilters);

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(CreateButton("Refresh", (_, _) => RefreshLog()));
        actions.Children.Add(CreateButton("Copy filtered log", (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(visibleText))
            {
                ShowToast("No log entries to copy");
                return;
            }

            CopyToClipboard(visibleText, "Filtered log copied");
        }));
        actions.Children.Add(CreateButton("Open log folder", (_, _) => OpenApplicationLogFolder()));
        actions.Children.Add(CreateButton("Delete logs", (_, _) => DeleteApplicationLogs(RefreshLog), danger: true));
        var automaticRefresh = CreateCheckBox("Automatic log refresh", isChecked: false);
        automaticRefresh.Margin = new Thickness(8, 0, 0, 0);
        automaticRefresh.VerticalAlignment = VerticalAlignment.Center;
        actions.Children.Add(automaticRefresh);

        var automaticRefreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        automaticRefreshTimer.Tick += (_, _) => RefreshLog();
        automaticRefresh.Checked += (_, _) =>
        {
            RefreshLog();
            automaticRefreshTimer.Start();
        };
        automaticRefresh.Unchecked += (_, _) => automaticRefreshTimer.Stop();
        root.Unloaded += (_, _) => automaticRefreshTimer.Stop();
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        RefreshLog();
        return root;
    }

    private ComboBox CreateLogFilter(params (string Label, string? Value)[] options)
    {
        var combo = new ComboBox { Width = 170 };
        combo.SetResourceReference(FrameworkElement.StyleProperty, "ModernComboBoxStyle");
        foreach (var (label, value) in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        combo.SelectedIndex = 0;
        return combo;
    }

    private StackPanel CreateLogFilterField(string label, Control control)
    {
        var field = new StackPanel { Margin = new Thickness(0, 0, 10, 8) };
        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["MutedTextBrush"],
            Margin = new Thickness(0, 0, 0, 4)
        });
        field.Children.Add(control);
        return field;
    }

    private static string? GetLogFilterValue(ComboBox combo)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Tag as string;
    }

    private static void PopulateActionFilter(ComboBox combo, IEnumerable<AppLogRecord> entries, string? selectedAction)
    {
        var actions = entries
            .Select(entry => entry.Action)
            .Where(action => !string.IsNullOrWhiteSpace(action))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(action => action, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        combo.Items.Clear();
        combo.Items.Add(new ComboBoxItem { Content = "All actions", Tag = null });
        foreach (var action in actions)
        {
            combo.Items.Add(new ComboBoxItem { Content = action, Tag = action });
        }

        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, selectedAction, StringComparison.OrdinalIgnoreCase)) ?? combo.Items[0];
    }

    private static string FormatAppLogEntry(AppLogRecord entry)
    {
        var fields = new List<string>
        {
            entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            entry.Source,
            entry.Event
        };
        AddLogField(fields, "type", entry.MessageType);
        AddLogField(fields, "action", entry.Action);
        AddLogField(fields, "client", entry.ClientId);
        AddLogField(fields, "outcome", entry.Outcome);
        AddLogField(fields, "code", entry.Code);
        if (entry.Win32Error.HasValue)
        {
            fields.Add($"win32={entry.Win32Error.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        AddLogField(fields, "detail", entry.Detail?.Replace('\r', ' ').Replace('\n', ' '));
        return string.Join(" | ", fields);
    }

    private static void AddLogField(ICollection<string> fields, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add($"{name}={value}");
        }
    }

    private void OpenApplicationLogFolder()
    {
        try
        {
            Directory.CreateDirectory(_appLog.LogDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _appLog.LogDirectory,
                UseShellExecute = true
            });
            _appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "open_application_log_folder",
                Outcome: "succeeded"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "open_application_log_folder",
                Outcome: "failed",
                Detail: ex.Message));
            ShowToast($"Could not open log folder: {ex.Message}");
        }
    }

    private void DeleteApplicationLogs(Action refresh)
    {
        if (!ThemedConfirmationDialog.Show(
                this,
                "Delete application logs",
                "Delete all Voltura Air application log files? If logging is enabled, a new entry will record this deletion.",
                "Delete logs",
                "Cancel",
                ConfirmationTone.Warning))
        {
            return;
        }

        var result = _appLog.DeleteAll();
        if (!result.Succeeded)
        {
            _appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "delete_application_logs",
                Outcome: "failed",
                Detail: result.Error));
            ShowToast($"Could not delete logs: {result.Error}");
            return;
        }

        _appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: "delete_application_logs",
            Outcome: "succeeded",
            Detail: $"deletedFiles={result.DeletedFiles.ToString(CultureInfo.InvariantCulture)}"));
        refresh();
        ShowToast(result.DeletedFiles == 1 ? "1 log file deleted" : $"{result.DeletedFiles.ToString(CultureInfo.InvariantCulture)} log files deleted");
    }
}
