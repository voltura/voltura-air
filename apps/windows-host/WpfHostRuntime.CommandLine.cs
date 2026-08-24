namespace VolturaAir.Host;

internal static class HostCommandLine
{
#if DEBUG
    public static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index += 1)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) continue;
            return index + 1 < args.Length ? args[index + 1] : null;
        }
        return null;
    }
#endif

    public static bool HasOption(string[] args, string name) =>
        args.Contains(name, StringComparer.OrdinalIgnoreCase);

#if DEBUG
    public static string ResolveIsolatedAutomationPath(
        string[] args,
        string requestedPath,
        string leafName)
    {
        if (!HasOption(args, "--isolated-test-mode"))
        {
            throw new InvalidOperationException("Isolated automation paths require --isolated-test-mode.");
        }

        var automationDirectoryName = HasOption(args, "--site-screenshot-mode")
            ? "voltura-air-site-screenshots"
            : "voltura-air-dev-ui";
        var expectedPath = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            automationDirectoryName,
            leafName));
        if (!string.Equals(
            Path.GetFullPath(requestedPath),
            expectedPath,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Isolated automation files must use the Voltura Air temporary workspace.");
        }

        return expectedPath;
    }

    public static void WritePairingUrlIfRequested(string[] args, string pairingUrl)
    {
        var requestedPairingUrlFile = GetOption(args, "--pairing-url-file");
        if (string.IsNullOrWhiteSpace(requestedPairingUrlFile)) return;

        var fullPath = ResolveIsolatedAutomationPath(args, requestedPairingUrlFile, "pairing-url.txt");
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, pairingUrl);
    }
#endif
}
