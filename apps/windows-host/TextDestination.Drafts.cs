using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace VolturaAir.Host;

internal static class ExcelDraftWorkbook
{
    public static string Create(string text, bool sendEnter)
    {
        var draft = TextDestinationDraftStore.CreateDraft(".xlsx");
        using var archive = ZipFile.Open(draft.Path, ZipArchiveMode.Create);
        Write(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
            """);
        Write(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
            """);
        Write(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>
            """);
        Write(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>
            """);
        Write(archive, "xl/worksheets/sheet1.xml", BuildWorksheet(text, sendEnter, draft));
        return draft.Path;
    }

    internal static void Write(ZipArchive archive, string name, string contents)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Fastest).Open());
        writer.Write(contents.Trim());
    }

    private static string EscapeXml(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);

    private static string BuildWorksheet(string text, bool sendEnter, TextDestinationDraft draft)
    {
        if (sendEnter) text += Environment.NewLine;
        var rows = TextDestinationDraftStore.GetNoticeLines(draft)
            .Append(string.Empty)
            .Concat(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Split('\n'));
        var sheetData = new StringBuilder();
        foreach (var (row, rowIndex) in rows.Select((row, index) => (row, index)))
        {
            var rowNumber = rowIndex + 1;
            sheetData.Append($"<row r=\"{rowNumber}\">");
            var cells = row.Split('\t');
            for (var columnIndex = 0; columnIndex < cells.Length; columnIndex++)
            {
                if (cells[columnIndex].Length == 0) continue;
                sheetData.Append($"<c r=\"{GetColumnName(columnIndex + 1)}{rowNumber}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{EscapeXml(cells[columnIndex])}</t></is></c>");
            }
            sheetData.Append("</row>");
        }
        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>{sheetData}</sheetData></worksheet>";
    }

    private static string GetColumnName(int column)
    {
        var name = new StringBuilder();
        while (column > 0)
        {
            column--;
            name.Insert(0, (char)('A' + column % 26));
            column /= 26;
        }
        return name.ToString();
    }
}

internal static class PlainTextDraft
{
    public static string Create(string text, bool sendEnter)
    {
        var draft = TextDestinationDraftStore.CreateDraft(".txt");
        File.WriteAllText(draft.Path, PrepareContents(text, sendEnter, draft), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return draft.Path;
    }

    public static bool TryOpen(string path)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    internal static string PrepareContents(string text, bool sendEnter, TextDestinationDraft? draft = null)
    {
        var content = sendEnter ? text + Environment.NewLine : text;
        return draft is null ? content : string.Join(Environment.NewLine, TextDestinationDraftStore.GetNoticeLines(draft)) + Environment.NewLine + "------------------" + Environment.NewLine + content;
    }
}

internal static class WordDraftDocument
{
    public static string Create(string text, bool sendEnter)
    {
        var draft = TextDestinationDraftStore.CreateDraft(".docx");
        using var archive = ZipFile.Open(draft.Path, ZipArchiveMode.Create);
        ExcelDraftWorkbook.Write(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
            """);
        ExcelDraftWorkbook.Write(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
            """);
        ExcelDraftWorkbook.Write(archive, "word/document.xml", BuildDocument(text, sendEnter, draft));
        return draft.Path;
    }

    private static string BuildDocument(string text, bool sendEnter, TextDestinationDraft draft)
    {
        var lines = TextDestinationDraftStore.GetNoticeLines(draft)
            .Append(string.Empty)
            .Concat(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Split('\n'))
            .ToList();
        if (sendEnter) lines.Add(string.Empty);
        var body = new StringBuilder();
        foreach (var line in lines)
        {
            body.Append("<w:p>");
            var parts = line.Split('\t');
            for (var index = 0; index < parts.Length; index++)
            {
                if (index > 0) body.Append("<w:r><w:tab/></w:r>");
                if (parts[index].Length > 0) body.Append($"<w:r><w:t xml:space=\"preserve\">{EscapeXml(parts[index])}</w:t></w:r>");
            }
            body.Append("</w:p>");
        }
        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{body}<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/></w:sectPr></w:body></w:document>";
    }

    private static string EscapeXml(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);
}

internal static class OutlookCompose
{
    private const int MailItem = 0;

    public static bool TryCreate(string text, bool sendEnter)
    {
        try
        {
            var applicationType = Type.GetTypeFromProgID("Outlook.Application");
            if (applicationType is null) return false;
            var application = Activator.CreateInstance(applicationType);
            if (application is null) return false;
            var mail = applicationType.InvokeMember("CreateItem", BindingFlags.InvokeMethod, null, application, [MailItem]);
            if (mail is null) return false;
            var body = sendEnter ? text + Environment.NewLine : text;
            mail.GetType().InvokeMember("Body", BindingFlags.SetProperty, null, mail, [body]);
            mail.GetType().InvokeMember("Display", BindingFlags.InvokeMethod, null, mail, [false]);
            var inspector = mail.GetType().InvokeMember("GetInspector", BindingFlags.GetProperty, null, mail, null);
            inspector?.GetType().InvokeMember("Activate", BindingFlags.InvokeMethod, null, inspector, null);
            return true;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidOperationException)
        {
            return false;
        }
    }
}

internal static class DefaultMailCompose
{
    private const int MaximumMailtoUriLength = 8_000;

    public static bool TryCreate(string text, bool sendEnter)
    {
        var mailtoUri = BuildMailtoUri(text, sendEnter);
        if (mailtoUri.Length > MaximumMailtoUriLength) return false;
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(mailtoUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    public static bool TryOpenDefaultAppsSettings()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    internal static string BuildMailtoUri(string text, bool sendEnter)
    {
        var body = sendEnter ? text + Environment.NewLine : text;
        return $"mailto:?body={Uri.EscapeDataString(body)}";
    }
}
