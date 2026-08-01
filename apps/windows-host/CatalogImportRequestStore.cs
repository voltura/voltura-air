using System.Security;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed record CatalogImportRequest(string Id, string CatalogBaseUrl);

internal static class CatalogImportRequestStore
{
    internal const string ProductionCatalogBaseUrl =
        "https://voltura.se/air/screens";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Voltura Air",
        "pending-catalog-import.txt");

    public static bool IsValidId(string? id) =>
        id is { Length: 36 } &&
        Guid.TryParseExact(id, "D", out _);

    public static CatalogImportRequest? Find(string[] args)
    {
        foreach (var argument in args)
        {
            if (TryCreate(argument, null, out var directRequest))
            {
                return directRequest;
            }

            if (!Uri.TryCreate(argument, UriKind.Absolute, out var uri) ||
                !string.Equals(
                    uri.Scheme,
                    "voltura-air",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    uri.Host,
                    "import",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var query = ParseQuery(uri.Query);
            if (query.TryGetValue("id", out var id) &&
                TryCreate(
                    id,
                    query.GetValueOrDefault("source"),
                    out var request))
            {
                return request;
            }
        }

        return null;
    }

    public static void EnqueueIfPresent(string[] args)
    {
        var request = Find(args);
        if (request is not null)
        {
            Enqueue(request);
        }
    }

    public static void Enqueue(CatalogImportRequest request)
    {
        if (!TryCreate(request.Id, request.CatalogBaseUrl, out var normalized))
        {
            return;
        }

        try
        {
            var folder = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(folder);
            var temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(normalized));
            File.Move(temporary, FilePath, overwrite: true);
            TryDelete(temporary);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException)
        {
        }
    }

    public static CatalogImportRequest? TryTake()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var content = File.ReadAllText(FilePath).Trim();
            File.Delete(FilePath);

            // Compatibility with requests written by versions that persisted
            // only the catalog GUID.
            if (TryCreate(content, null, out var legacyRequest))
            {
                return legacyRequest;
            }

            var stored = JsonSerializer.Deserialize<CatalogImportRequest>(content);
            return stored is not null &&
                TryCreate(stored.Id, stored.CatalogBaseUrl, out var request)
                    ? request
                    : null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or
                SecurityException or JsonException)
        {
            return null;
        }
    }

    internal static bool TryCreate(
        string? id,
        string? source,
        out CatalogImportRequest? request)
    {
        request = null;
        if (!IsValidId(id) || !TryNormalizeCatalogBaseUrl(source, out var baseUrl))
        {
            return false;
        }

        request = new CatalogImportRequest(id!, baseUrl);
        return true;
    }

    internal static bool TryNormalizeCatalogBaseUrl(
        string? source,
        out string normalized)
    {
        normalized = ProductionCatalogBaseUrl;
        if (string.IsNullOrWhiteSpace(source))
        {
            return true;
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(uri.Host, "voltura.se", StringComparison.OrdinalIgnoreCase) &&
            uri.IsDefaultPort &&
            string.Equals(path, "/air/screens", StringComparison.Ordinal))
        {
            normalized = ProductionCatalogBaseUrl;
            return true;
        }

#if DEBUG
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            uri.IsLoopback &&
            string.Equals(path, "/screens", StringComparison.Ordinal))
        {
            normalized = $"{uri.Scheme}://{uri.Authority}{path}";
            return true;
        }
#endif

        return false;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            try
            {
                values[Uri.UnescapeDataString(pair[0])] =
                    Uri.UnescapeDataString(pair[1]);
            }
            catch (UriFormatException)
            {
            }
        }

        return values;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException)
        {
        }
    }
}
