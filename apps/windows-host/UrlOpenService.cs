using System.Diagnostics;

namespace VolturaAir.Host;

public static class UrlOpenLimits
{
    public const int MaxUrlLength = 2_048;
}

public sealed record UrlOpenExecutionResult(
    bool Succeeded,
    string Code,
    string Message,
    string? NormalizedUrl = null);

public interface IUrlOpenService
{
    UrlOpenExecutionResult Execute(string value);
}

public interface IUrlShellLauncher
{
    void Open(Uri uri);
}

public sealed class WindowsUrlShellLauncher : IUrlShellLauncher
{
    public void Open(Uri uri)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }
}

public sealed class UrlOpenService(IUrlShellLauncher? launcher = null) : IUrlOpenService
{
    private readonly IUrlShellLauncher _launcher = launcher ?? new WindowsUrlShellLauncher();

    public UrlOpenExecutionResult Execute(string value)
    {
        if (!TryNormalize(value, out var uri, out var code, out var message))
        {
            return new UrlOpenExecutionResult(false, code, message);
        }

        try
        {
            _launcher.Open(uri!);
            return new UrlOpenExecutionResult(true, "accepted", "Open request sent.", uri!.AbsoluteUri);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new UrlOpenExecutionResult(
                false,
                "launch-failed",
                "Windows could not open the URL using the default browser. Check the HTTP and HTTPS assignments under Windows Settings > Apps > Default apps.",
                uri!.AbsoluteUri);
        }
    }

    internal static bool TryNormalize(string value, out Uri? uri, out string code, out string message)
    {
        uri = null;
        code = "invalid-url";
        message = "Enter a valid web address.";

        var trimmed = value.Trim();
        if (trimmed.Length is 0 or > UrlOpenLimits.MaxUrlLength || trimmed.Any(char.IsControl))
        {
            return false;
        }

        var hasScheme = HasExplicitScheme(trimmed);
        var candidate = hasScheme ? trimmed : $"https://{trimmed}";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) &&
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            code = "unsupported-scheme";
            message = "Only HTTP and HTTPS web addresses can be opened.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.Host))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool HasExplicitScheme(string value)
    {
        var colonIndex = value.IndexOf(':');
        if (colonIndex <= 0 || !char.IsAsciiLetter(value[0]) ||
            !ContainsOnlySchemeCharacters(value.AsSpan(1, colonIndex - 1)))
        {
            return false;
        }

        var remainder = value.AsSpan(colonIndex + 1);
        var portLength = 0;
        while (portLength < remainder.Length && char.IsAsciiDigit(remainder[portLength]))
        {
            portLength++;
        }

        var hostPart = value.AsSpan(0, colonIndex);
        var canBeBareHost = hostPart.Equals("localhost", StringComparison.OrdinalIgnoreCase) || hostPart.Contains('.');
        var looksLikeHostPort = canBeBareHost && portLength > 0 &&
            (portLength == remainder.Length || remainder[portLength] is '/' or '?' or '#');
        return !looksLikeHostPort;
    }

    private static bool ContainsOnlySchemeCharacters(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
