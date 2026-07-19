using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VolturaAir.Host.Ui;
using Border = System.Windows.Controls.Border;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using FontFamily = System.Windows.Media.FontFamily;

namespace VolturaAir.Host.Features.Diagnostics;

internal sealed class AppLogVisualFactory(HostVisualFactory visuals)
{
    public Border CreateRow(AppLogRecord entry)
    {
        var content = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceSm);
        var header = new DockPanel();
        var isFailure = string.Equals(entry.Outcome, "failed", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(entry.Code);
        var badge = new PillBadge
        {
            Content = GetEventLabel(entry.Event),
            Tone = isFailure ? PillBadgeTone.DangerOutline : PillBadgeTone.AccentOutline
        };
        DockPanel.SetDock(badge, Dock.Left);
        header.Children.Add(badge);
        var timestamp = new TextBlock
        {
            Text = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11,
            Foreground = visuals.Brush("MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(timestamp, Dock.Right);
        header.Children.Add(timestamp);
        content.Children.Add(header);

        content.Children.Add(new TextBlock
        {
            Text = GetEntryTitle(entry),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = visuals.Brush("TextBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        var metadata = HostVisualFactory.CreateWrap(UiTokens.SpaceSm, UiTokens.SpaceXs);
        AddChip(metadata, GetSourceLabel(entry.Source));
        AddChip(metadata, entry.Outcome, isFailure);
        AddChip(metadata, string.IsNullOrWhiteSpace(entry.ClientId) ? null : $"Client {entry.ClientId}");
        AddChip(metadata, entry.Code, isFailure: true);
        AddChip(metadata, entry.Win32Error.HasValue ? $"Win32 {entry.Win32Error.Value.ToString(CultureInfo.InvariantCulture)}" : null, isFailure: true);
        content.Children.Add(metadata);

        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            content.Children.Add(new TextBlock
            {
                Text = entry.Detail,
                FontSize = 12,
                Foreground = visuals.Brush("MutedTextBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        return visuals.CreateListRowShell(content);
    }

    public Border CreateEmptyState(string message)
    {
        return new Border
        {
            Padding = new Thickness(18),
            Child = new TextBlock
            {
                Text = message,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = visuals.Brush("MutedTextBrush")
            }
        };
    }

    public void SetError(TextBlock status, StackPanel logRows, string message)
    {
        status.Text = message;
        status.Foreground = visuals.Brush("DangerBrush");
        logRows.Children.Clear();
        logRows.Children.Add(CreateEmptyState(message));
    }

    private static void AddChip(SpacingWrapPanel panel, string? text, bool isFailure = false)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            panel.Children.Add(new PillBadge
            {
                Content = text,
                Tone = isFailure ? PillBadgeTone.DangerOutline : PillBadgeTone.Outline
            });
        }
    }

    private static string GetEventLabel(string eventName)
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

    private static string GetEntryTitle(AppLogRecord entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Action))
        {
            return entry.Action.Replace('_', ' ');
        }

        return !string.IsNullOrWhiteSpace(entry.MessageType) ? entry.MessageType : GetEventLabel(entry.Event);
    }

    private static string GetSourceLabel(string source)
    {
        return source switch
        {
            "remote_client" => "Remote client",
            "windows_host" => "Windows host",
            _ => source
        };
    }
}
