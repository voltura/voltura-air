using System.Globalization;
using System.Net;
using System.Text;

namespace VolturaAir.Host.Features.Presentations;

internal enum PresentationExportFormat
{
    Html,
    Excel,
    Pdf,
    Csv,
    Text
}

internal static class PresentationReportExports
{
    public static string Extension(PresentationExportFormat format) => format switch
    {
        PresentationExportFormat.Html => ".html",
        PresentationExportFormat.Excel => ".xlsx",
        PresentationExportFormat.Pdf => ".pdf",
        PresentationExportFormat.Csv => ".csv",
        PresentationExportFormat.Text => ".txt",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static void Write(string path, IReadOnlyList<PresentationReport> reports, PresentationExportFormat format)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new IOException("The presentation export path has no parent directory.");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteNew(temporaryPath, reports, format);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void WriteNew(
        string path,
        IReadOnlyList<PresentationReport> reports,
        PresentationExportFormat format)
    {
        switch (format)
        {
            case PresentationExportFormat.Html:
                File.WriteAllText(path, BuildModernHtml(reports), new UTF8Encoding(false));
                return;
            case PresentationExportFormat.Excel:
                PresentationReportOfficeExports.WriteExcel(path, reports);
                return;
            case PresentationExportFormat.Pdf:
                PresentationReportOfficeExports.WritePdf(path, reports);
                return;
            case PresentationExportFormat.Csv:
                File.WriteAllText(path, BuildCsv(reports), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                return;
            case PresentationExportFormat.Text:
                File.WriteAllText(path, BuildText(reports), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    public static string BuildEmailHtml(IReadOnlyList<PresentationReport> reports)
    {
        var body = new StringBuilder();
        body.Append("""
            <!doctype html><html><body style="margin:0;padding:20px;background:#ffffff;color:#202124;font-family:Arial,sans-serif">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:680px;margin:0 auto">
            <tr><td><h1 style="font-size:24px;margin:0 0 8px">Presentation report</h1>
            """);
        body.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 20px;color:#5f6368\">{reports.Count} presentation{(reports.Count == 1 ? string.Empty : "s")}</p></td></tr>");
        foreach (var report in reports)
        {
            body.Append("<tr><td style=\"padding:16px;border:1px solid #dadce0\">");
            body.Append(CultureInfo.InvariantCulture, $"<h2 style=\"font-size:18px;margin:0 0 10px\">{Html(report.Title)}</h2>");
            body.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"5\" cellspacing=\"0\">");
            AppendEmailRow(body, "Type", TargetLabel(report.Target));
            AppendEmailRow(body, "Device", report.DeviceName);
            AppendEmailRow(body, "Started", report.StartedAt.ToOffset(TimeSpan.FromMinutes(report.UtcOffsetMinutes)).ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
            AppendEmailRow(body, "Presenting time", FormatDuration(report.PresentationDurationSeconds));
            AppendEmailRow(body, "Break time", FormatDuration(report.BreakDurationSeconds));
            AppendEmailRow(body, "Sessions", report.SessionCount.ToString(CultureInfo.CurrentCulture));
            AppendEmailRow(body, "Slides", report.Slides.Count.ToString(CultureInfo.CurrentCulture));
            if (!string.IsNullOrWhiteSpace(report.PresentationUrl))
            {
                body.Append(CultureInfo.InvariantCulture, $"<tr><td style=\"color:#5f6368;border-bottom:1px solid #eeeeee\">Presentation link</td><td style=\"border-bottom:1px solid #eeeeee\"><a href=\"{Html(report.PresentationUrl)}\">Open presentation</a></td></tr>");
            }
            body.Append("</table></td></tr><tr><td style=\"height:14px\"></td></tr>");
        }

        body.Append("</table></body></html>");
        return body.ToString();
    }

    public static string BuildText(IReadOnlyList<PresentationReport> reports)
    {
        var text = new StringBuilder();
        text.AppendLine("Voltura Air presentation report");
        text.AppendLine(CultureInfo.CurrentCulture, $"Presentations: {reports.Count}");
        text.AppendLine();
        foreach (var report in reports)
        {
            text.AppendLine(report.Title);
            text.AppendLine(CultureInfo.CurrentCulture, $"Type: {TargetLabel(report.Target)}");
            text.AppendLine(CultureInfo.CurrentCulture, $"Device: {report.DeviceName}");
            text.AppendLine(CultureInfo.CurrentCulture, $"Started: {report.StartedAt.ToOffset(TimeSpan.FromMinutes(report.UtcOffsetMinutes)):yyyy-MM-dd HH:mm zzz}");
            text.AppendLine(CultureInfo.CurrentCulture, $"Presenting time: {FormatDuration(report.PresentationDurationSeconds)}");
            text.AppendLine(CultureInfo.CurrentCulture, $"Break time: {FormatDuration(report.BreakDurationSeconds)}");
            text.AppendLine(CultureInfo.CurrentCulture, $"Sessions: {report.SessionCount}");
            text.AppendLine(CultureInfo.CurrentCulture, $"Slides: {report.Slides.Count}");
            if (!string.IsNullOrWhiteSpace(report.PresentationFilePath))
            {
                text.AppendLine(CultureInfo.CurrentCulture, $"Presentation file: {Path.GetFileName(report.PresentationFilePath)}");
            }

            if (!string.IsNullOrWhiteSpace(report.PresentationUrl))
            {
                text.AppendLine(CultureInfo.CurrentCulture, $"Presentation URL: {report.PresentationUrl}");
            }
            foreach (var entry in BuildTimelineRows(report))
            {
                text.AppendLine(CultureInfo.CurrentCulture, $"{entry.Label}: {entry.Detail} - {FormatDuration(entry.DurationSeconds)}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    public static IEnumerable<string[]> BuildTabularRows(IReadOnlyList<PresentationReport> reports)
    {
        yield return
        [
            "Record", "Title", "Type", "Device", "Started", "Ended", "Presenting time",
            "Break time", "Sessions", "Breaks", "Slides", "Presentation file", "Presentation URL", "Detail", "Duration"
        ];
        foreach (var report in reports)
        {
            yield return
            [
                "Presentation",
                report.Title,
                TargetLabel(report.Target),
                report.DeviceName,
                report.StartedAt.ToString("O", CultureInfo.InvariantCulture),
                report.EndedAt.ToString("O", CultureInfo.InvariantCulture),
                FormatDuration(report.PresentationDurationSeconds),
                FormatDuration(report.BreakDurationSeconds),
                report.SessionCount.ToString(CultureInfo.InvariantCulture),
                report.Breaks.Count.ToString(CultureInfo.InvariantCulture),
                report.Slides.Count.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(report.PresentationFilePath) ? string.Empty : Path.GetFileName(report.PresentationFilePath),
                report.PresentationUrl ?? string.Empty,
                string.Empty,
                string.Empty
            ];
            foreach (var entry in BuildTimelineRows(report))
            {
                yield return
                [
                    entry.Kind,
                    report.Title,
                    TargetLabel(report.Target),
                    report.DeviceName,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    entry.Detail,
                    FormatDuration(entry.DurationSeconds)
                ];
            }
        }
    }

    internal static string TargetLabel(string target) => target switch
    {
        "powerpoint" => "PowerPoint",
        "google-slides" => "Google Slides",
        "pdf" => "PDF / browser",
        _ => "Presentation"
    };

    internal static string FormatDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }

    private static string BuildModernHtml(IReadOnlyList<PresentationReport> reports)
    {
        var html = new StringBuilder();
        html.Append("""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <title>Voltura Air presentation report</title><style>
            :root{color-scheme:light dark;--bg:#0d1418;--card:#151d21;--text:#f4f3ef;--muted:#aebbc1;--line:#3a4a51;--present:#25d0a7;--break:#f0a653}
            *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.45 system-ui,sans-serif}
            main{width:min(1100px,calc(100% - 32px));margin:32px auto}h1{margin-bottom:4px}.muted{color:var(--muted)}
            article{margin:18px 0;padding:20px;border:1px solid var(--line);border-radius:12px;background:var(--card)}
            .stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px}.stat{padding:10px;border:1px solid var(--line);border-radius:8px}
            .timeline{display:flex;height:24px;margin:18px 0;overflow:hidden;border-radius:6px}.session{background:var(--present)}.break{background:var(--break)}
            .breakdown{margin-top:28px}table{width:100%;border-collapse:collapse}th,td{padding:8px;text-align:left;border-bottom:1px solid var(--line)}th{color:var(--muted)}
            @media print{:root{color-scheme:light;--bg:#fff;--card:#fff;--text:#111;--muted:#555;--line:#ccc}main{width:100%;margin:0}article{break-inside:avoid}}
            </style></head><body><main><h1>Presentation report</h1>
            """);
        html.Append(CultureInfo.InvariantCulture, $"<p class=\"muted\">Exported from Voltura Air · {reports.Count} presentation{(reports.Count == 1 ? string.Empty : "s")}</p>");
        foreach (var report in reports)
        {
            html.Append(CultureInfo.InvariantCulture, $"<article><h2>{Html(report.Title)}</h2><p class=\"muted\">{Html(TargetLabel(report.Target))} · {Html(report.DeviceName)} · {report.StartedAt.ToOffset(TimeSpan.FromMinutes(report.UtcOffsetMinutes)):yyyy-MM-dd HH:mm}</p>");
            html.Append("<div class=\"stats\">");
            AppendStat(html, "Presenting time", FormatDuration(report.PresentationDurationSeconds));
            AppendStat(html, "Break time", FormatDuration(report.BreakDurationSeconds));
            AppendStat(html, "Sessions", report.SessionCount.ToString(CultureInfo.CurrentCulture));
            AppendStat(html, "Slides", report.Slides.Count.ToString(CultureInfo.CurrentCulture));
            html.Append("</div><div class=\"timeline\" aria-label=\"Presentation timeline\">");
            foreach (var entry in BuildTimelineRows(report).Reverse())
            {
                html.Append(CultureInfo.InvariantCulture, $"<span class=\"{(entry.Kind == "Session" ? "session" : "break")}\" style=\"flex:{Math.Max(0.001, entry.DurationSeconds):0.###}\" title=\"{Html(entry.Label)}: {Html(FormatDuration(entry.DurationSeconds))}\"></span>");
            }

            html.Append("</div>");
            if (!string.IsNullOrWhiteSpace(report.PresentationUrl))
            {
                html.Append(CultureInfo.InvariantCulture, $"<p><a href=\"{Html(report.PresentationUrl)}\">Open presentation</a></p>");
            }

            html.Append("<section class=\"breakdown\"><h3>Session and break breakdown</h3><table><thead><tr><th>Part</th><th>Detail</th><th>Duration</th></tr></thead><tbody>");
            foreach (var entry in BuildTimelineRows(report))
            {
                html.Append(CultureInfo.InvariantCulture, $"<tr><td>{Html(entry.Label)}</td><td>{Html(entry.Detail)}</td><td>{Html(FormatDuration(entry.DurationSeconds))}</td></tr>");
            }

            html.Append("</tbody></table></section></article>");
        }

        html.Append("</main></body></html>");
        return html.ToString();
    }

    private static string BuildCsv(IReadOnlyList<PresentationReport> reports)
    {
        var csv = new StringBuilder();
        foreach (var row in BuildTabularRows(reports))
        {
            csv.AppendLine(string.Join(",", row.Select(CsvCell)));
        }

        return csv.ToString();
    }

    internal static IEnumerable<TimelineRow> BuildTimelineRows(PresentationReport report)
    {
        var rows = new List<TimelineRow>();
        var checkpoints = report.Breaks.OrderBy(entry => entry.BreakNumber).ToList();
        var previousCheckpoint = 0d;
        for (var index = 0; index < report.SessionCount; index += 1)
        {
            var sessionEnd = index < checkpoints.Count
                ? checkpoints[index].PresentationElapsedSeconds
                : report.PresentationDurationSeconds;
            rows.Add(new(
                "Session",
                $"Session {index + 1}",
                SessionSlides(report, index),
                Math.Max(0, sessionEnd - previousCheckpoint),
                index * 2));
            previousCheckpoint = sessionEnd;
            if (index < checkpoints.Count)
            {
                var entry = checkpoints[index];
                rows.Add(new(
                    "Break",
                    $"Break {entry.BreakNumber}",
                    $"{entry.StartedAt.ToOffset(TimeSpan.FromMinutes(report.UtcOffsetMinutes)):HH:mm:ss}-{entry.EndedAt.ToOffset(TimeSpan.FromMinutes(report.UtcOffsetMinutes)):HH:mm:ss}",
                    entry.BreakDurationSeconds,
                    index * 2 + 1));
            }
        }

        return rows.OrderByDescending(row => row.Order);
    }

    private static string SessionSlides(PresentationReport report, int sessionIndex)
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
            minimum = report.Breaks.Count == 0 ? 1 : report.Breaks[^1].SessionSlideMaximum;
            maximum = report.Slides.Count == 0 ? minimum : report.Slides.Max(slide => slide.SlideNumber);
        }

        return minimum is null
            ? "No slide navigation recorded"
            : minimum == maximum ? $"Slide {minimum}" : $"Slides {minimum}-{maximum}";
    }

    private static string CsvCell(string value)
    {
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? $"'{value}"
            : value;
        return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static void AppendStat(StringBuilder html, string label, string value) =>
        html.Append(CultureInfo.InvariantCulture, $"<div class=\"stat\"><span class=\"muted\">{Html(label)}</span><br><strong>{Html(value)}</strong></div>");

    private static void AppendEmailRow(StringBuilder html, string label, string value) =>
        html.Append(CultureInfo.InvariantCulture, $"<tr><td style=\"color:#5f6368;border-bottom:1px solid #eeeeee\">{Html(label)}</td><td style=\"border-bottom:1px solid #eeeeee\"><strong>{Html(value)}</strong></td></tr>");

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    internal sealed record TimelineRow(string Kind, string Label, string Detail, double DurationSeconds, int Order);
}
