using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace VolturaAir.Host.Features.Updates;

internal enum UpdateState { Idle, Checking, WaitingForDevices, Downloading, Ready }
internal enum UpdateNotificationKind { UpToDate, CheckFailed, WaitingForDevices, Ready, InvalidStagedUpdate, InstallFailed }
internal enum UpdateStartupOutcome { None, Updated, Failed }

internal sealed class UpdateNotificationEventArgs(UpdateNotificationKind kind, string? version = null) : EventArgs
{
    internal UpdateNotificationKind Kind { get; } = kind;
    internal string? Version { get; } = version;
}

internal sealed partial class UpdateService
{
    private sealed record DeferredCandidate(Version Version, JsonElement Assets);

    internal static UpdateStartupOutcome GetStartupOutcome(string[] args) =>
        args.Contains("--updated", StringComparer.OrdinalIgnoreCase)
            ? UpdateStartupOutcome.Updated
            : args.Contains("--update-failed", StringComparer.OrdinalIgnoreCase)
                ? UpdateStartupOutcome.Failed
                : UpdateStartupOutcome.None;

    internal static bool IsEligible(string[] args, string? processPath, out string? modifyInstaller)
    {
        modifyInstaller = null;
        if (IsSpecialExecution(args, string.Equals(
                Environment.GetEnvironmentVariable("VOLTURA_AIR_DEV_HOST"),
                "1",
                StringComparison.Ordinal)) ||
            string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura Air",
            writable: false);
        return IsInstalledHost(
            key?.GetValue("InstallLocation") as string,
            Path.GetDirectoryName(processPath),
            key?.GetValue("ModifyPath") as string,
            out modifyInstaller);
    }

    internal static bool IsSpecialExecution(string[] args, bool developmentSupervisor) =>
        developmentSupervisor || args.Any(arg =>
            arg.Equals("--isolated-test-mode", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--site-screenshot-mode", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--installer-health-check", StringComparison.OrdinalIgnoreCase));

    internal static bool IsInstalledHost(
        string? installLocation,
        string? runningDirectory,
        string? modifyPath,
        out string? modifyInstaller)
    {
        modifyInstaller = UnquotePath(modifyPath);
        if (string.IsNullOrWhiteSpace(installLocation) ||
            string.IsNullOrWhiteSpace(runningDirectory) ||
            string.IsNullOrWhiteSpace(modifyInstaller))
        {
            return false;
        }

        try
        {
            return string.Equals(
                    Path.GetFullPath(installLocation.TrimEnd(Path.DirectorySeparatorChar)),
                    Path.GetFullPath(runningDirectory.TrimEnd(Path.DirectorySeparatorChar)),
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.GetFullPath(modifyInstaller));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            modifyInstaller = null;
            return false;
        }
    }

    internal static bool TryRestoreReadyPackage(
        string pendingDirectory,
        Func<Version, string> selectInstallerName,
        Func<byte[], byte[], bool> verifyManifest,
        out UpdateReadyPackage ready)
    {
        ready = null!;
        try
        {
            var manifestPath = Path.Combine(pendingDirectory, "manifest.json");
            if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > UpdatePackageStager.MaxManifestBytes)
            {
                return false;
            }
            var manifest = File.ReadAllBytes(manifestPath);
            if (!TryReadManifestVersion(manifest, out var version)) return false;
            var signaturePath = Path.Combine(pendingDirectory, $"VolturaAir-Update-{version.ToString(3)}.sig");
            if (!File.Exists(signaturePath) || new FileInfo(signaturePath).Length > UpdatePackageStager.MaxSignatureBytes)
            {
                return false;
            }
            var signature = File.ReadAllBytes(signaturePath);
            if (!verifyManifest(manifest, signature) ||
                !UpdatePackageStager.TryReadRestoredPackage(manifest, selectInstallerName(version), out ready))
            {
                return false;
            }
            var installer = Path.Combine(pendingDirectory, ready.InstallerName);
            return File.Exists(installer) && new FileInfo(installer).Length == ready.Size;
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<string> ValidateReadyForApplyAsync(UpdateReadyPackage expected, CancellationToken cancellationToken)
    {
        if (!TryRestoreReadyPackage(_pendingDirectory, SelectInstallerName, _verifyManifest, out var restored) ||
            restored != expected)
        {
            throw new IOException("The staged update could not be verified.");
        }
        var installerPath = Path.Combine(_pendingDirectory, restored.InstallerName);
        await using var installer = File.OpenRead(installerPath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(installer, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        if (!string.Equals(hash, restored.Hash, StringComparison.Ordinal))
        {
            throw new IOException("The staged update could not be verified.");
        }
        return installerPath;
    }

    private void DeletePendingDirectory()
    {
        try
        {
            var pending = Path.GetFullPath(_pendingDirectory);
            var updates = Path.GetFullPath(Path.GetDirectoryName(pending) ?? string.Empty);
            if (!pending.StartsWith(updates.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            Directory.Delete(pending, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string SelectInstallerName(Version version)
    {
        var originalFilename = string.IsNullOrWhiteSpace(_modifyInstaller)
            ? null
            : FileVersionInfo.GetVersionInfo(_modifyInstaller).OriginalFilename;
        return SelectInstallerName(version, originalFilename);
    }

    internal static string SelectInstallerName(Version version, string? originalFilename) =>
        $"VolturaAir-Setup-{version.ToString(3)}-win-x64{(originalFilename?.Contains("-full", StringComparison.OrdinalIgnoreCase) == true ? "-full" : string.Empty)}.exe";

    private System.Net.Http.HttpClient GetClient() => _client ??= _clientFactory();

    private static System.Net.Http.HttpClient CreateClient()
    {
#pragma warning disable CA2000 // HttpClient owns and disposes the handler.
        var client = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
#pragma warning restore CA2000
        client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("VolturaAir", "1"));
        return client;
    }

    private static bool TryReadLatestRelease(JsonElement root, out Version version, out JsonElement assets)
    {
        version = new();
        assets = default;
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.False &&
            root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.False &&
            root.TryGetProperty("tag_name", out var tag) &&
            TryParseVersion(tag.GetString(), requireVPrefix: true, out version) &&
            root.TryGetProperty("assets", out assets) && assets.ValueKind == JsonValueKind.Array;
    }

    private static bool TryReadManifestVersion(byte[] manifest, out Version version)
    {
        version = new();
        using var document = JsonDocument.Parse(manifest);
        return document.RootElement.TryGetProperty("version", out var element) &&
            TryParseVersion(element.GetString(), requireVPrefix: false, out version);
    }

    private static bool VerifyManifest(byte[] manifest, byte[] signature)
    {
        using var rsa = RSA.Create();
        using var stream = typeof(UpdateService).Assembly.GetManifestResourceStream(
            "VolturaAir.Host.Features.Updates.update-signing-public.pem");
        if (stream is null) return false;
        using var reader = new StreamReader(stream);
        rsa.ImportFromPem(reader.ReadToEnd());
        return rsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }

    internal static bool TryParseVersion(string? value, bool requireVPrefix, out Version version)
    {
        version = new();
        if (string.IsNullOrWhiteSpace(value) || requireVPrefix != value.StartsWith('v')) return false;
        var numeric = requireVPrefix ? value[1..] : value;
        var parts = numeric.Split('.');
        if (parts.Length != 3 || parts.Any(part =>
                part.Length == 0 ||
                (part.Length > 1 && part[0] == '0') ||
                part.Any(character => character is < '0' or > '9')) ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch))
        {
            return false;
        }
        version = new(major, minor, patch);
        return true;
    }

    private static string? UnquotePath(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"') return trimmed[1..^1];
        return trimmed.Contains('"') ? null : trimmed;
    }

    private static Version CurrentVersion() =>
        Version.TryParse(typeof(UpdateService).Assembly.GetName().Version?.ToString(), out var version)
            ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0))
            : new Version();
}
