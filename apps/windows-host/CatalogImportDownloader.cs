using System.Net;
using System.Net.Http;

namespace VolturaAir.Host;

internal static class CatalogImportDownloader
{
    private static readonly HttpClient Client = new();

    public static async Task<byte[]> DownloadAsync(
        CatalogImportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!CatalogImportRequestStore.TryCreate(
                request.Id,
                request.CatalogBaseUrl,
                out var normalizedRequest))
        {
            throw new InvalidOperationException("The catalog screen request is invalid.");
        }

        var downloadUri = new Uri(
            $"{normalizedRequest!.CatalogBaseUrl}/download.php?id=" +
            Uri.EscapeDataString(normalizedRequest.Id));
        using var response = await Client.GetAsync(
            downloadUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException("The catalog screen could not be downloaded.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (buffer.Length <= CustomScreenLimits.MaxStoreBytes)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        throw new InvalidOperationException("The catalog screen package is too large.");
    }
}
