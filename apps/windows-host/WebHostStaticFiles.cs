using Microsoft.AspNetCore.Http;

namespace VolturaAir.Host;

internal static class WebHostStaticFiles
{
    private const string WebBuildIdFileName = "web-build-id.txt";

    public static async Task<bool> TryServeCompressedJavaScriptAsync(HttpContext context, string staticRoot)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            return false;
        }

        var path = context.Request.Path.Value;
        if (path is null || !path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var filePath = ResolveStaticFilePath(staticRoot, path);
        if (filePath is null)
        {
            return false;
        }

        if (AcceptsEncoding(context.Request, "br") && File.Exists($"{filePath}.br"))
        {
            await ServeCompressedJavaScriptAsync(context, $"{filePath}.br", "br", path);
            return true;
        }

        if (AcceptsEncoding(context.Request, "gzip") && File.Exists($"{filePath}.gz"))
        {
            await ServeCompressedJavaScriptAsync(context, $"{filePath}.gz", "gzip", path);
            return true;
        }

        return false;
    }

    public static void SetStaticCacheHeaders(HttpResponse response, string? requestPath)
    {
        var fileName = Path.GetFileName(requestPath);
        if (string.Equals(fileName, "index.html", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "sw.js", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "manifest.webmanifest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, WebBuildIdFileName, StringComparison.OrdinalIgnoreCase))
        {
            response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            response.Headers.Pragma = "no-cache";
            response.Headers.Expires = "0";
            return;
        }

        if (requestPath?.Contains("/assets/", StringComparison.OrdinalIgnoreCase) == true)
        {
            response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    }

    public static string ResolveStaticRoot()
    {
        var devRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "mobile-web", "dist"));
        if (Directory.Exists(devRoot))
        {
            return devRoot;
        }

        return Path.Combine(AppContext.BaseDirectory, "wwwroot");
    }

    public static string? ReadWebClientBuildId(string staticRoot)
    {
        try
        {
            var buildIdPath = Path.Combine(staticRoot, WebBuildIdFileName);
            if (!File.Exists(buildIdPath))
            {
                return null;
            }

            var buildId = File.ReadAllText(buildIdPath).Trim();
            return buildId.Length > 0 ? buildId : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string? ResolveStaticFilePath(string staticRoot, string requestPath)
    {
        if (requestPath.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var rootPath = Path.GetFullPath(staticRoot);
        var relativePath = requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        return fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    private static async Task ServeCompressedJavaScriptAsync(HttpContext context, string filePath, string encoding, string requestPath)
    {
        context.Response.ContentType = "application/javascript";
        context.Response.Headers.ContentEncoding = encoding;
        context.Response.Headers.Vary = "Accept-Encoding";
        SetStaticCacheHeaders(context.Response, requestPath);
        context.Response.ContentLength = new FileInfo(filePath).Length;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await context.Response.SendFileAsync(filePath);
    }

    private static bool AcceptsEncoding(HttpRequest request, string expectedEncoding)
    {
        var acceptEncoding = request.Headers.AcceptEncoding.ToString();
        return acceptEncoding
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(encoding => encoding.Equals(expectedEncoding, StringComparison.OrdinalIgnoreCase) || encoding.StartsWith($"{expectedEncoding};", StringComparison.OrdinalIgnoreCase));
    }
}
