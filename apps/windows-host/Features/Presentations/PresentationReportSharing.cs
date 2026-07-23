using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace VolturaAir.Host.Features.Presentations;

internal sealed record PresentationShareResult(bool Succeeded, string Message, bool RequiresNotice = false);

internal static class PresentationReportSharing
{
    private const int OutlookMailItem = 0;
    private const int MaxRetainedDraftArtifacts = 100;
    private static readonly TimeSpan DraftArtifactRetention = TimeSpan.FromDays(7);

    public static PresentationShareResult Export(
        System.Windows.Window owner,
        IReadOnlyList<PresentationReport> reports,
        PresentationExportFormat format)
    {
        if (reports.Count == 0)
        {
            return new(false, "There are no matching presentations to export.");
        }

        var extension = PresentationReportExports.Extension(format);
        var dialog = new WpfSaveFileDialog
        {
            AddExtension = true,
            DefaultExt = extension,
            FileName = SuggestedBaseName(reports) + extension,
            Filter = Filter(format),
            OverwritePrompt = true,
            Title = reports.Count == 1 ? "Export presentation" : "Export filtered presentations"
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return new(false, string.Empty);
        }

        try
        {
            PresentationReportExports.Write(dialog.FileName, reports, format);
            try
            {
                using var process = Process.Start(new ProcessStartInfo(dialog.FileName)
                {
                    UseShellExecute = true
                });
                return process is null
                    ? new(
                        true,
                        "The report was exported, but Windows could not open it.",
                        RequiresNotice: true)
                    : new(true, string.Empty);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                return new(
                    true,
                    "The report was exported, but Windows could not open it with its associated app.",
                    RequiresNotice: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(false, "Windows could not create that export. Check the destination and try again.");
        }
    }

    public static PresentationShareResult EmailBody(
        IReadOnlyList<PresentationReport> reports,
        IReadOnlyList<string> presentationFiles)
    {
        if (reports.Count == 0)
        {
            return new(false, "There are no matching presentations to email.");
        }

        if (!AllAttachmentsAvailable(presentationFiles))
        {
            return new(false, "A requested presentation file is no longer available. Relink it or email the statistics without the file.");
        }

        var subject = reports.Count == 1 ? reports[0].Title : "Voltura Air presentation reports";
        var html = PresentationReportExports.BuildEmailHtml(reports);
        if (TryCreateOutlookDraft(subject, html, presentationFiles))
        {
            return new(true, presentationFiles.Count == 0
                ? "Email draft opened."
                : "Email draft opened with the presentation file attached.");
        }

        if (TryOpenPortableEmailDraft(subject, html, presentationFiles))
        {
            return new(true, presentationFiles.Count == 0
                ? "Email draft opened."
                : "Email draft opened with the presentation file attached.");
        }

        if (presentationFiles.Count > 0)
        {
            return new(false, "The default mail app could not create a draft with attachments. Try Outlook or email the statistics without the presentation file.");
        }

        return TryOpenDefaultMail(subject, BoundedMailSummary(reports))
            ? new(true, "Your default mail app was opened with a compatible text summary because it did not expose HTML-body composition.", RequiresNotice: true)
            : new(false, "Windows could not open a mail app. Check the default email app and try again.");
    }

    public static PresentationShareResult EmailAttachment(
        IReadOnlyList<PresentationReport> reports,
        PresentationExportFormat format,
        IReadOnlyList<string> presentationFiles)
    {
        if (reports.Count == 0)
        {
            return new(false, "There are no matching presentations to email.");
        }

        if (!AllAttachmentsAvailable(presentationFiles))
        {
            return new(false, "A requested presentation file is no longer available. Relink it or email the statistics without the file.");
        }

        try
        {
            var directory = PrepareDraftDirectory(requiredSlots: 2);
            var extension = PresentationReportExports.Extension(format);
            var path = Path.Combine(directory, $"{SuggestedBaseName(reports)}-{Guid.NewGuid():N}{extension}");
            PresentationReportExports.Write(path, reports, format);
            var subject = reports.Count == 1 ? reports[0].Title : "Voltura Air presentation reports";
            var html = PresentationReportExports.BuildEmailHtml(reports);
            var attachments = new List<string> { path };
            attachments.AddRange(presentationFiles);
            if (TryCreateOutlookDraft(subject, html, attachments))
            {
                return new(true, presentationFiles.Count == 0
                    ? "Email draft opened with the report attached."
                    : "Email draft opened with the report and presentation file attached.");
            }

            if (TryOpenPortableEmailDraft(subject, html, attachments))
            {
                return new(true, presentationFiles.Count == 0
                    ? "Email draft opened with the report attached."
                    : "Email draft opened with the report and presentation file attached.");
            }

            TryDeleteUnusedArtifact(path);
            return new(false, "The default mail app could not create a draft with attachments. Try Outlook or email the statistics in the message body.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(false, "Windows could not create the email report file. Check local storage and try again.");
        }
    }

    private static bool TryCreateOutlookDraft(string subject, string htmlBody, IReadOnlyList<string> attachments)
    {
        object? application = null;
        object? mail = null;
        object? attachmentCollection = null;
        object? inspector = null;
        var displayed = false;
        try
        {
            var applicationType = Type.GetTypeFromProgID("Outlook.Application");
            if (applicationType is null)
            {
                return false;
            }

            application = Activator.CreateInstance(applicationType);
            if (application is null)
            {
                return false;
            }

            mail = applicationType.InvokeMember(
                "CreateItem",
                BindingFlags.InvokeMethod,
                null,
                application,
                [OutlookMailItem],
                CultureInfo.InvariantCulture);
            if (mail is null)
            {
                return false;
            }

            var mailType = mail.GetType();
            mailType.InvokeMember("Subject", BindingFlags.SetProperty, null, mail, [subject], CultureInfo.InvariantCulture);
            mailType.InvokeMember("HTMLBody", BindingFlags.SetProperty, null, mail, [htmlBody], CultureInfo.InvariantCulture);
            if (attachments.Count > 0)
            {
                attachmentCollection = mailType.InvokeMember("Attachments", BindingFlags.GetProperty, null, mail, null, CultureInfo.InvariantCulture);
                if (attachmentCollection is null)
                {
                    return false;
                }

                foreach (var path in attachments)
                {
                    attachmentCollection?.GetType().InvokeMember(
                        "Add",
                        BindingFlags.InvokeMethod,
                        null,
                        attachmentCollection,
                        [path, 1, Type.Missing, Path.GetFileName(path)],
                        CultureInfo.InvariantCulture);
                }
            }

            mailType.InvokeMember("Display", BindingFlags.InvokeMethod, null, mail, [false], CultureInfo.InvariantCulture);
            displayed = true;
            try
            {
                inspector = mailType.InvokeMember("GetInspector", BindingFlags.GetProperty, null, mail, null, CultureInfo.InvariantCulture);
                inspector?.GetType().InvokeMember("Activate", BindingFlags.InvokeMethod, null, inspector, null, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (exception is COMException or TargetInvocationException or InvalidOperationException)
            {
                // The draft is already visible. Activation is only a convenience.
            }

            return true;
        }
        catch (Exception exception) when (exception is COMException or TargetInvocationException or InvalidOperationException)
        {
            return displayed;
        }
        finally
        {
            ReleaseCom(inspector);
            ReleaseCom(attachmentCollection);
            ReleaseCom(mail);
            ReleaseCom(application);
        }
    }

    private static bool TryOpenPortableEmailDraft(
        string subject,
        string htmlBody,
        IReadOnlyList<string> attachments)
    {
        try
        {
            var directory = PrepareDraftDirectory(requiredSlots: 1);
            var path = Path.Combine(directory, $"Presentation-email-{Guid.NewGuid():N}.eml");
            WritePortableEmailDraft(path, subject, htmlBody, attachments);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return process is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    internal static void WritePortableEmailDraft(
        string path,
        string subject,
        string htmlBody,
        IReadOnlyList<string> attachments)
    {
        var boundary = $"VolturaAir-{Guid.NewGuid():N}";
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\r\n"
        };
        writer.WriteLine("X-Unsent: 1");
        writer.WriteLine($"Subject: =?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(subject))}?=");
        writer.WriteLine("MIME-Version: 1.0");
        writer.WriteLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
        writer.WriteLine();
        writer.WriteLine($"--{boundary}");
        writer.WriteLine("Content-Type: text/html; charset=utf-8");
        writer.WriteLine("Content-Transfer-Encoding: base64");
        writer.WriteLine();
        WriteBase64(writer, Encoding.UTF8.GetBytes(htmlBody));

        foreach (var attachmentPath in attachments)
        {
            var fileName = Path.GetFileName(attachmentPath);
            var safeFileName = string.Concat(fileName.Select(character =>
                character is >= ' ' and <= '~' && character is not '"' and not '\\' ? character : '_'));
            writer.WriteLine($"--{boundary}");
            writer.WriteLine($"Content-Type: {AttachmentContentType(attachmentPath)}; name=\"{safeFileName}\"");
            writer.WriteLine("Content-Transfer-Encoding: base64");
            writer.WriteLine($"Content-Disposition: attachment; filename=\"{safeFileName}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}");
            writer.WriteLine();
            writer.Flush();
            using var attachment = new FileStream(attachmentPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[57];
            int bytesRead;
            while ((bytesRead = attachment.Read(buffer, 0, buffer.Length)) > 0)
            {
                writer.WriteLine(Convert.ToBase64String(buffer, 0, bytesRead));
            }
        }

        writer.WriteLine($"--{boundary}--");
    }

    private static void WriteBase64(TextWriter writer, byte[] bytes)
    {
        var encoded = Convert.ToBase64String(bytes);
        for (var index = 0; index < encoded.Length; index += 76)
        {
            writer.WriteLine(encoded.AsSpan(index, Math.Min(76, encoded.Length - index)));
        }
    }

    private static string AttachmentContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html",
        ".csv" => "text/csv",
        ".txt" => "text/plain",
        ".pdf" => "application/pdf",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => "application/octet-stream"
    };

    private static bool TryOpenDefaultMail(string subject, string body)
    {
        var uri = $"mailto:?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
        if (uri.Length > 8_000)
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return process is not null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    private static string PrepareDraftDirectory(int requiredSlots)
    {
        var directory = GetDraftDirectory();
        Directory.CreateDirectory(directory);
        PruneDraftArtifacts(directory, DateTime.UtcNow, requiredSlots);
        return directory;
    }

    internal static string GetDraftDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voltura Air",
        "Presentation email drafts");

    internal static void DeleteExpiredDraftArtifacts() =>
        DeleteExpiredDraftArtifacts(GetDraftDirectory(), DateTime.UtcNow);

    internal static void DeleteExpiredDraftArtifacts(string directory, DateTime utcNow)
    {
        if (Directory.Exists(directory))
        {
            DeleteExpiredDraftArtifactsCore(directory, utcNow);
        }
    }

    private static void TryDeleteUnusedArtifact(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Bounded retention will retry after the external owner releases the file.
        }
    }

    internal static void PruneDraftArtifacts(string directory, DateTime utcNow, int requiredSlots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (requiredSlots is < 1 or > MaxRetainedDraftArtifacts)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredSlots));
        }

        Directory.CreateDirectory(directory);
        DeleteExpiredDraftArtifactsCore(directory, utcNow);

        if (Directory.EnumerateFiles(directory).Take(MaxRetainedDraftArtifacts - requiredSlots + 1).Count() >
            MaxRetainedDraftArtifacts - requiredSlots)
        {
            throw new IOException("The presentation email draft directory is full.");
        }
    }

    private static void DeleteExpiredDraftArtifactsCore(string directory, DateTime utcNow)
    {
        var cutoff = utcNow - DraftArtifactRetention;
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The mail client may still own this draft. Reconsider it during the next cleanup interval.
            }
        }
    }

    private static bool AllAttachmentsAvailable(IReadOnlyList<string> attachments) =>
        attachments.All(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));

    private static string BoundedMailSummary(IReadOnlyList<PresentationReport> reports)
    {
        var summary = reports.Count == 1
            ? PresentationReportExports.BuildText(reports)
            : $"Voltura Air presentation reports{Environment.NewLine}{reports.Count} filtered presentations{Environment.NewLine}{reports.Sum(report => report.PresentationDurationSeconds).ToString("0", CultureInfo.InvariantCulture)} presenting seconds";
        return summary.Length <= 4_000 ? summary : summary[..4_000];
    }

    private static string SuggestedBaseName(IReadOnlyList<PresentationReport> reports)
    {
        var candidate = reports.Count == 1
            ? PresentationReportNames.SuggestedExportBaseName(reports[0])
            : $"Presentations - {DateTime.Now:yyyy-MM-dd}";
        var sanitized = string.Concat(candidate.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim().TrimEnd('.');
        return sanitized.Length == 0 ? "Presentations" : sanitized[..Math.Min(sanitized.Length, 100)];
    }

    private static string Filter(PresentationExportFormat format) => format switch
    {
        PresentationExportFormat.Html => "Web page file (*.html)|*.html",
        PresentationExportFormat.Excel => "Excel workbook (*.xlsx)|*.xlsx",
        PresentationExportFormat.Pdf => "Portable Document Format (*.pdf)|*.pdf",
        PresentationExportFormat.Csv => "Comma Separated Values (*.csv)|*.csv",
        PresentationExportFormat.Text => "Text file (*.txt)|*.txt",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.ReleaseComObject(value);
        }
    }
}
