using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace VolturaAir.Host.Features.Presentations;

internal static class PresentationReportOfficeExports
{
    public static void WriteExcel(string path, IReadOnlyList<PresentationReport> reports)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteZipEntry(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
            """);
        WriteZipEntry(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
            """);
        WriteZipEntry(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Presentations" sheetId="1" r:id="rId1"/></sheets></workbook>
            """);
        WriteZipEntry(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>
            """);
        WriteZipEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheet(reports));
    }

    public static void WritePdf(string path, IReadOnlyList<PresentationReport> reports)
    {
        var pages = BuildPdfPages(reports);

        var objects = new Dictionary<int, byte[]>();
        var pageObjectIds = new List<int>();
        var nextObjectId = 5;
        foreach (var content in pages)
        {
            var pageId = nextObjectId++;
            var contentId = nextObjectId++;
            pageObjectIds.Add(pageId);
            objects[contentId] = Encoding.ASCII.GetBytes(
                $"<< /Length {content.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n{content}\nendstream");
            objects[pageId] = Encoding.ASCII.GetBytes(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>");
        }

        objects[1] = Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>");
        objects[2] = Encoding.ASCII.GetBytes(
            $"<< /Type /Pages /Count {pageObjectIds.Count.ToString(CultureInfo.InvariantCulture)} /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] >>");
        objects[3] = Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        objects[4] = Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        WriteAscii(stream, "%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
        var offsets = new long[nextObjectId];
        for (var id = 1; id < nextObjectId; id += 1)
        {
            offsets[id] = stream.Position;
            WriteAscii(stream, $"{id} 0 obj\n");
            stream.Write(objects[id]);
            WriteAscii(stream, "\nendobj\n");
        }

        var xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {nextObjectId}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        for (var id = 1; id < nextObjectId; id += 1)
        {
            WriteAscii(stream, $"{offsets[id]:0000000000} 00000 n \n");
        }

        WriteAscii(stream, $"trailer\n<< /Size {nextObjectId} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
    }

    private static string BuildWorksheet(IReadOnlyList<PresentationReport> reports)
    {
        var sheet = new StringBuilder(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><cols><col min=\"1\" max=\"15\" width=\"20\" customWidth=\"1\"/></cols><sheetData>");
        var rowNumber = 0;
        foreach (var row in PresentationReportExports.BuildTabularRows(reports))
        {
            rowNumber += 1;
            sheet.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowNumber}\">");
            for (var columnIndex = 0; columnIndex < row.Length; columnIndex += 1)
            {
                if (row[columnIndex].Length == 0)
                {
                    continue;
                }

                sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"{ColumnName(columnIndex + 1)}{rowNumber}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{EscapeXml(row[columnIndex])}</t></is></c>");
            }

            sheet.Append("</row>");
        }

        sheet.Append("</sheetData><autoFilter ref=\"A1:O1\"/></worksheet>");
        return sheet.ToString();
    }

    private static List<string> BuildPdfPages(IReadOnlyList<PresentationReport> reports)
    {
        if (reports.Count == 0)
        {
            var empty = new StringBuilder();
            AppendPdfText(empty, "F2", 22, 42, 742, "Presentation report", 0.06, 0.10, 0.12);
            AppendPdfText(empty, "F1", 11, 42, 716, "No presentations matched the selected filters.", 0.35, 0.40, 0.42);
            return [empty.ToString()];
        }

        var pages = new List<string>();
        foreach (var report in reports)
        {
            var elapsed = 0d;
            var rows = new List<PdfTimelineRow>();
            foreach (var entry in PresentationReportExports.BuildTimelineRows(report).Reverse())
            {
                elapsed += Math.Max(0, entry.DurationSeconds);
                rows.Add(new(entry, elapsed));
            }

            var offset = 0;
            var continuation = false;
            while (offset < rows.Count)
            {
                var capacity = continuation ? 22 : 14;
                var pageRows = rows.Skip(offset).Take(capacity).ToList();
                pages.Add(BuildReportPdfPage(report, rows, pageRows, continuation));
                offset += pageRows.Count;
                continuation = true;
            }
        }

        return pages;
    }

    private static string BuildReportPdfPage(
        PresentationReport report,
        IReadOnlyList<PdfTimelineRow> allRows,
        IReadOnlyList<PdfTimelineRow> pageRows,
        bool continuation)
    {
        const double pageLeft = 42;
        const double pageWidth = 528;
        var content = new StringBuilder();
        var name = Truncate(PresentationReportNames.DisplayName(report), 62);
        AppendPdfText(
            content,
            "F2",
            continuation ? 17 : 22,
            pageLeft,
            742,
            continuation ? $"{name} - continued" : name,
            0.06,
            0.10,
            0.12);

        var localStart = report.StartedAt.ToOffset(TimeSpan.FromMinutes(report.UtcOffsetMinutes));
        AppendPdfText(
            content,
            "F1",
            10,
            pageLeft,
            718,
            $"{PresentationReportExports.TargetLabel(report.Target)}  |  {Truncate(report.DeviceName, 32)}  |  {localStart:yyyy-MM-dd HH:mm}",
            0.35,
            0.40,
            0.42);

        var tableTop = 680d;
        if (!continuation)
        {
            AppendPdfStatCard(content, pageLeft, 646, 123, "Presenting time", PresentationReportExports.FormatDuration(report.PresentationDurationSeconds));
            AppendPdfStatCard(content, 177, 646, 123, "Break time", PresentationReportExports.FormatDuration(report.BreakDurationSeconds));
            AppendPdfStatCard(content, 312, 646, 123, "Sessions", report.SessionCount.ToString(CultureInfo.CurrentCulture));
            AppendPdfStatCard(content, 447, 646, 123, "Slides", report.Slides.Count.ToString(CultureInfo.CurrentCulture));

            AppendPdfText(content, "F2", 13, pageLeft, 620, "Presentation timeline", 0.06, 0.10, 0.12);
            var total = Math.Max(0.001, allRows.Sum(row => row.Entry.DurationSeconds));
            var segmentX = pageLeft;
            foreach (var row in allRows)
            {
                var width = pageWidth * Math.Max(0, row.Entry.DurationSeconds) / total;
                var isSession = row.Entry.Kind == "Session";
                AppendPdfRect(
                    content,
                    segmentX,
                    592,
                    width,
                    18,
                    isSession ? 0.10 : 0.90,
                    isSession ? 0.68 : 0.55,
                    isSession ? 0.56 : 0.25,
                    0.24,
                    0.29,
                    0.31);
                segmentX += width;
            }

            AppendPdfText(content, "F1", 9, pageLeft, 574, $"Presenting  {PresentationReportExports.FormatDuration(report.PresentationDurationSeconds)}", 0.10, 0.58, 0.48);
            AppendPdfText(content, "F1", 9, 190, 574, $"Breaks  {PresentationReportExports.FormatDuration(report.BreakDurationSeconds)}", 0.82, 0.43, 0.14);
            tableTop = 512;
        }

        AppendPdfText(content, "F2", 13, pageLeft, tableTop + 24, "Session and break breakdown", 0.06, 0.10, 0.12);
        AppendPdfText(content, "F2", 8, pageLeft, tableTop, "PART", 0.35, 0.40, 0.42);
        AppendPdfText(content, "F2", 8, 145, tableTop, "SLIDES / TIME", 0.35, 0.40, 0.42);
        AppendPdfText(content, "F2", 8, 365, tableTop, "DURATION", 0.35, 0.40, 0.42);
        AppendPdfText(content, "F2", 8, 470, tableTop, "ELAPSED", 0.35, 0.40, 0.42);

        var rowY = tableTop - 28;
        foreach (var row in pageRows)
        {
            AppendPdfRect(content, pageLeft, rowY - 8, pageWidth, 24, 0.96, 0.97, 0.97, 0.80, 0.82, 0.83);
            var isSession = row.Entry.Kind == "Session";
            AppendPdfText(content, "F2", 9, pageLeft + 8, rowY, row.Entry.Label, isSession ? 0.06 : 0.78, isSession ? 0.54 : 0.39, isSession ? 0.44 : 0.12);
            AppendPdfText(content, "F1", 9, 145, rowY, Truncate(row.Entry.Detail, 34), 0.30, 0.35, 0.37);
            AppendPdfText(content, "F2", 9, 365, rowY, PresentationReportExports.FormatDuration(row.Entry.DurationSeconds), 0.06, 0.10, 0.12);
            AppendPdfText(content, "F2", 9, 470, rowY, PresentationReportExports.FormatDuration(row.ElapsedSeconds), 0.06, 0.10, 0.12);
            rowY -= 28;
        }

        AppendPdfText(content, "F1", 8, pageLeft, 30, "Generated by Voltura Air", 0.45, 0.49, 0.51);
        return content.ToString();
    }

    private static void AppendPdfStatCard(
        StringBuilder content,
        double x,
        double y,
        double width,
        string label,
        string value)
    {
        AppendPdfRect(content, x, y, width, 48, 0.96, 0.97, 0.97, 0.78, 0.81, 0.82);
        AppendPdfText(content, "F1", 8, x + 9, y + 31, label, 0.35, 0.40, 0.42);
        AppendPdfText(content, "F2", 13, x + 9, y + 12, value, 0.06, 0.10, 0.12);
    }

    private static void AppendPdfRect(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        double red,
        double green,
        double blue,
        double borderRed,
        double borderGreen,
        double borderBlue)
    {
        content.Append(CultureInfo.InvariantCulture, $"q {red:0.###} {green:0.###} {blue:0.###} rg {borderRed:0.###} {borderGreen:0.###} {borderBlue:0.###} RG 0.6 w {x:0.##} {y:0.##} {Math.Max(0, width):0.##} {height:0.##} re B Q\n");
    }

    private static void AppendPdfText(
        StringBuilder content,
        string font,
        double size,
        double x,
        double y,
        string value,
        double red,
        double green,
        double blue)
    {
        content.Append(CultureInfo.InvariantCulture, $"BT /{font} {size:0.##} Tf {red:0.###} {green:0.###} {blue:0.###} rg {x:0.##} {y:0.##} Td ({EscapePdfText(value)}) Tj ET\n");
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : $"{value[..(maximumLength - 3)]}...";

    private static string EscapePdfText(string value)
    {
        var safe = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            safe.Append(character is >= ' ' and <= '~' ? character : '?');
        }

        return safe.ToString()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }

    private static void WriteZipEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Fastest).Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }

    private static string EscapeXml(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);

    private static string ColumnName(int column)
    {
        var name = new StringBuilder();
        while (column > 0)
        {
            column -= 1;
            name.Insert(0, (char)('A' + column % 26));
            column /= 26;
        }

        return name.ToString();
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
    }

    private sealed record PdfTimelineRow(PresentationReportExports.TimelineRow Entry, double ElapsedSeconds);
}
