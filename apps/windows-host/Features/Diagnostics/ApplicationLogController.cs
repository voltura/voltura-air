using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Ui;
using ComboBox = System.Windows.Controls.ComboBox;
using Control = System.Windows.Controls.Control;

namespace VolturaAir.Host.Features.Diagnostics;

internal sealed class ApplicationLogController(
    Window owner,
    IAppLog appLog,
    HostVisualFactory visuals,
    AppLogVisualFactory logVisuals,
    HostClipboardFeedback clipboard,
    HostToastPresenter toasts)
{
    public ApplicationLogView CreateView()
    {
        var root = new ApplicationLogView(AppLoggingSettings.IsEnabled());
        var loggingToggle = root.LoggingToggle;

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

        root.FiltersPanel.Children.Insert(0, CreateLogFilterField("Date range", dateRange));
        root.FiltersPanel.Children.Insert(1, CreateLogFilterField("Event", eventFilter));
        root.FiltersPanel.Children.Insert(2, CreateLogFilterField("Source", sourceFilter));
        root.FiltersPanel.Children.Insert(3, CreateLogFilterField("Action", actionFilter));
        var status = root.StatusText;
        var logRows = root.LogRows;
        var logScroller = root.LogScroller;

        var visibleText = string.Empty;
        var updatingFilters = false;
        var refreshVersion = 0L;
        var refreshRunning = false;
        var unloaded = false;
        string? lastRenderSignature = null;
        async void RefreshLog()
        {
            refreshVersion += 1;
            if (refreshRunning || unloaded)
            {
                return;
            }

            refreshRunning = true;
            var requestedVersion = refreshVersion;
            var selectedAction = GetLogFilterValue(actionFilter);
            var selectedEvents = eventFilter.SelectedValues;
            var query = new AppLogQuery(
                DateOnly.FromDateTime(dateRange.SelectedStartDate),
                DateOnly.FromDateTime(dateRange.SelectedEndDate),
                Source: GetLogFilterValue(sourceFilter),
                MaxEntries: 5000);
            var result = await Task.Run(() => appLog.Read(query)).ConfigureAwait(false);
            if (owner.Dispatcher.HasShutdownStarted)
            {
                return;
            }

            _ = owner.Dispatcher.BeginInvoke(() =>
            {
                refreshRunning = false;
                if (unloaded)
                {
                    return;
                }

                if (requestedVersion != refreshVersion)
                {
                    RefreshLog();
                    return;
                }

                if (!result.Succeeded)
                {
                    var errorMessage = $"The application log could not be read: {result.Error}";
                    var errorSignature = $"error\0{errorMessage}";
                    if (!string.Equals(lastRenderSignature, errorSignature, StringComparison.Ordinal))
                    {
                        logVisuals.SetError(status, logRows, errorMessage);
                        lastRenderSignature = errorSignature;
                    }

                    visibleText = string.Empty;
                    return;
                }

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
                var loggingEnabled = AppLoggingSettings.IsEnabled();
                var renderSignature = $"success\0{loggingEnabled}\0{result.Truncated}\0{visibleText}";
                if (!string.Equals(lastRenderSignature, renderSignature, StringComparison.Ordinal))
                {
                    logRows.Children.Clear();
                    if (filtered.Length == 0)
                    {
                        logRows.Children.Add(logVisuals.CreateEmptyState(
                            loggingEnabled
                                ? "No matching log entries."
                                : "Application logging is off. Enable Write application log above to record new activity."));
                    }
                    else
                    {
                        foreach (var entry in filtered)
                        {
                            logRows.Children.Add(logVisuals.CreateRow(entry));
                        }
                    }

                    logScroller.ScrollToTop();
                    lastRenderSignature = renderSignature;
                }

                status.Foreground = visuals.Brush(loggingEnabled ? "MutedTextBrush" : "DangerBrush");
                var state = loggingEnabled
                    ? "Logging is enabled."
                    : "Logging is off. No new activity is being written; existing entries remain available.";
                var limitNote = result.Truncated || filtered.Length == 250 ? " Only the newest matching entries are shown." : string.Empty;
                status.Text = $"{state} Showing {filtered.Length.ToString(CultureInfo.InvariantCulture)} entries.{limitNote}";
            });
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
            appLog.Write(new AppLogEntry("host_action", "windows_host", Action: "application_logging", Outcome: "enabled"));
            RefreshLog();
        };
        loggingToggle.Unchecked += (_, _) =>
        {
            appLog.Write(new AppLogEntry("host_action", "windows_host", Action: "application_logging", Outcome: "disabled"));
            AppLoggingSettings.SetEnabled(false);
            RefreshLog();
        };

        root.ClearFiltersButton.Click += (_, _) =>
        {
            updatingFilters = true;
            dateRange.SetRange(today.AddDays(-(AppLoggingSettings.GetMaxAgeDays() - 1)), today);
            eventFilter.Clear();
            sourceFilter.SelectedIndex = 0;
            actionFilter.SelectedIndex = 0;
            updatingFilters = false;
            RefreshLog();
        };
        root.RefreshButton.Click += (_, _) => RefreshLog();
        root.CopyButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(visibleText))
            {
                toasts.Show("No log entries to copy");
                return;
            }

            clipboard.Copy(visibleText, "Filtered log copied");
        };
        root.OpenFolderButton.Click += (_, _) => OpenApplicationLogFolder();
        root.DeleteButton.Click += (_, _) => DeleteApplicationLogs(RefreshLog);
        var automaticRefresh = root.AutomaticRefreshToggle;
        var automaticRefreshSubscribed = false;

        void OnApplicationLogChanged(object? sender, EventArgs eventArgs)
        {
            if (!owner.Dispatcher.HasShutdownStarted)
            {
                _ = owner.Dispatcher.BeginInvoke(RefreshLog);
            }
        }

        void UpdateAutomaticRefresh()
        {
            var shouldSubscribe = automaticRefresh.IsChecked == true && owner.IsVisible && owner.WindowState != WindowState.Minimized;
            if (shouldSubscribe == automaticRefreshSubscribed)
            {
                return;
            }

            automaticRefreshSubscribed = shouldSubscribe;
            if (shouldSubscribe)
            {
                appLog.Changed += OnApplicationLogChanged;
                RefreshLog();
            }
            else
            {
                appLog.Changed -= OnApplicationLogChanged;
            }
        }

        void OnWindowStateChanged(object? sender, EventArgs eventArgs) => UpdateAutomaticRefresh();

        automaticRefresh.Checked += (_, _) => UpdateAutomaticRefresh();
        automaticRefresh.Unchecked += (_, _) => UpdateAutomaticRefresh();
        root.IsVisibleChanged += (_, _) => UpdateAutomaticRefresh();
        owner.StateChanged += OnWindowStateChanged;
        root.Unloaded += (_, _) =>
        {
            unloaded = true;
            if (automaticRefreshSubscribed)
            {
                appLog.Changed -= OnApplicationLogChanged;
            }

            owner.StateChanged -= OnWindowStateChanged;
        };
        RefreshLog();
        return root;
    }

    private static ComboBox CreateLogFilter(params (string Label, string? Value)[] options)
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

    private SpacingStackPanel CreateLogFilterField(string label, Control control)
    {
        var field = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceXs);
        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = visuals.Brush("MutedTextBrush")
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
        var existingActions = combo.Items
            .Cast<ComboBoxItem>()
            .Skip(1)
            .Select(item => item.Tag as string)
            .Where(action => action is not null)
            .Select(action => action!)
            .ToArray();
        if (actions.SequenceEqual(existingActions, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

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

    private static void AddLogField(List<string> fields, string name, string? value)
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
            Directory.CreateDirectory(appLog.LogDirectory);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = appLog.LogDirectory,
                UseShellExecute = true
            });
            appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "open_application_log_folder",
                Outcome: "succeeded"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "open_application_log_folder",
                Outcome: "failed",
                Detail: exception.Message));
            toasts.Show($"Could not open log folder: {exception.Message}");
        }
    }

    private void DeleteApplicationLogs(Action refresh)
    {
        if (!ThemedConfirmationDialog.Show(
                owner,
                "Delete application logs",
                "Delete all Voltura Air application log files? If logging is enabled, a new entry will record this deletion.",
                "Delete logs",
                "Cancel",
                ConfirmationTone.Warning))
        {
            return;
        }

        var result = appLog.DeleteAll();
        if (!result.Succeeded)
        {
            appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "delete_application_logs",
                Outcome: "failed",
                Detail: result.Error));
            toasts.Show($"Could not delete logs: {result.Error}");
            return;
        }

        appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: "delete_application_logs",
            Outcome: "succeeded",
            Detail: $"deletedFiles={result.DeletedFiles.ToString(CultureInfo.InvariantCulture)}"));
        refresh();
        toasts.Show(result.DeletedFiles == 1 ? "1 log file deleted" : $"{result.DeletedFiles.ToString(CultureInfo.InvariantCulture)} log files deleted");
    }
}
