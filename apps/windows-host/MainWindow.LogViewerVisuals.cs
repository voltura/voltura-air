using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Border = System.Windows.Controls.Border;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private Border CreateAppLogRow(AppLogRecord entry)
    {
        var content = new StackPanel();
        var header = new DockPanel();
        var isFailure = string.Equals(entry.Outcome, "failed", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(entry.Code);
        var badge = new Border
        {
            Background = (Brush)Resources["SurfaceRaisedBrush"],
            BorderBrush = (Brush)Resources[isFailure ? "DangerBrush" : "AccentBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3, 8, 3),
            Child = new TextBlock
            {
                Text = GetLogEventLabel(entry.Event),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Resources[isFailure ? "DangerBrush" : "TextBrush"]
            }
        };
        DockPanel.SetDock(badge, Dock.Left);
        header.Children.Add(badge);
        var timestamp = new TextBlock
        {
            Text = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11,
            Foreground = (Brush)Resources["MutedTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(timestamp, Dock.Right);
        header.Children.Add(timestamp);
        content.Children.Add(header);

        content.Children.Add(new TextBlock
        {
            Text = GetLogEntryTitle(entry),
            Margin = new Thickness(0, 9, 0, 6),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["TextBrush"],
            TextWrapping = TextWrapping.Wrap
        });

        var metadata = new WrapPanel();
        AddLogChip(metadata, GetLogSourceLabel(entry.Source));
        AddLogChip(metadata, entry.Outcome, isFailure);
        AddLogChip(metadata, string.IsNullOrWhiteSpace(entry.ClientId) ? null : $"Client {entry.ClientId}");
        AddLogChip(metadata, entry.Code, isFailure: true);
        AddLogChip(metadata, entry.Win32Error.HasValue ? $"Win32 {entry.Win32Error.Value.ToString(CultureInfo.InvariantCulture)}" : null, isFailure: true);
        content.Children.Add(metadata);

        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            content.Children.Add(new TextBlock
            {
                Text = entry.Detail,
                Margin = new Thickness(0, 7, 0, 0),
                FontSize = 12,
                Foreground = (Brush)Resources["MutedTextBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        var row = (Border)CreateListRowShell(content);
        row.Margin = new Thickness(0, 0, 0, 8);
        return row;
    }

    private void AddLogChip(WrapPanel panel, string? text, bool isFailure = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        panel.Children.Add(new Border
        {
            Background = (Brush)Resources["SurfaceRaisedBrush"],
            BorderBrush = (Brush)Resources[isFailure ? "DangerBrush" : "BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 6, 4),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = (Brush)Resources[isFailure ? "DangerBrush" : "MutedTextBrush"]
            }
        });
    }

    private Border CreateLogEmptyState(string message)
    {
        return new Border
        {
            Padding = new Thickness(18),
            Child = new TextBlock
            {
                Text = message,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)Resources["MutedTextBrush"]
            }
        };
    }

    private static string GetLogEventLabel(string eventName)
    {
        return eventName switch
        {
            "host_action" => "Host action",
            "command_received" => "Command received",
            "command_outcome" => "Command outcome",
            "action_taken" => "Action taken",
            "response_sent" => "Response sent",
            _ => eventName
        };
    }

    private static string GetLogEntryTitle(AppLogRecord entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Action))
        {
            return entry.Action.Replace('_', ' ');
        }

        return !string.IsNullOrWhiteSpace(entry.MessageType) ? entry.MessageType : GetLogEventLabel(entry.Event);
    }

    private static string GetLogSourceLabel(string source)
    {
        return source switch
        {
            "remote_client" => "Remote client",
            "windows_host" => "Windows host",
            _ => source
        };
    }

    private void SetLogViewerError(TextBlock status, StackPanel logRows, string message)
    {
        status.Text = message;
        status.Foreground = (Brush)Resources["DangerBrush"];
        logRows.Children.Clear();
        logRows.Children.Add(CreateLogEmptyState(message));
    }
}
