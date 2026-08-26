using System.Diagnostics;
using System.Text.Json;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal sealed class PhoneWebcamSetup : IPhoneWebcamSetup
{
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(10);
    private const string ProtectedDirectoryName = "Voltura Air Webcam";
    private const string SetupFileName = "VolturaAir.WebcamSetup.exe";
    private readonly string _setupPath;
    private readonly string _packagedSetupPath;
    private readonly bool _validateProtectedPath;

    internal PhoneWebcamSetup(string? setupPath = null)
    {
        _validateProtectedPath = setupPath is null;
        _setupPath = setupPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ProtectedDirectoryName,
            SetupFileName);
        _packagedSetupPath = Path.Combine(AppContext.BaseDirectory, "PhoneWebcam", SetupFileName);
    }

    public async Task<PhoneWebcamFeatureStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_setupPath))
        {
            return new PhoneWebcamFeatureStatus(
                PhoneWebcamFeatureState.NotInstalled,
                "Phone Webcam is not installed. Run Voltura Air installer maintenance to add it.");
        }

        if (_validateProtectedPath && !IsProtectedHelperPath())
        {
            return new PhoneWebcamFeatureStatus(
                PhoneWebcamFeatureState.Unavailable,
                "Voltura Air could not verify the Phone Webcam installation. Run installer maintenance to repair it.");
        }

        ProcessResult packagedRevisionResult = await RunAsync(
            _packagedSetupPath,
            "revision",
            cancellationToken).ConfigureAwait(false);
        if (!TryReadRevision(packagedRevisionResult.Output, out string packagedRevision))
        {
            return new PhoneWebcamFeatureStatus(
                PhoneWebcamFeatureState.Unavailable,
                "Voltura Air could not verify its packaged Phone Webcam component. Reinstall Voltura Air.");
        }

        ProcessResult result = await RunAsync(_setupPath, "status", cancellationToken).ConfigureAwait(false);
        if (TryReadStatus(
                result.Output,
                out bool installed,
                out bool cleanupRequired,
                out bool updateRequired,
                out string? installedRevision))
        {
            if (installed)
            {
                if (updateRequired || !IsCurrentRevision(installedRevision, packagedRevision))
                {
                    return new PhoneWebcamFeatureStatus(
                        PhoneWebcamFeatureState.UpdateRequired,
                        "A newer Voltura Air Webcam component is available. Remove the existing camera, then enable it again.");
                }
                return new PhoneWebcamFeatureStatus(
                    PhoneWebcamFeatureState.Installed,
                    "Voltura Air Webcam is installed and ready.");
            }

            return cleanupRequired
                ? new PhoneWebcamFeatureStatus(
                    PhoneWebcamFeatureState.NeedsCleanup,
                    "An incomplete Voltura Air Webcam installation needs to be removed before enabling it again.",
                    HasError: true)
                : new PhoneWebcamFeatureStatus(
                    PhoneWebcamFeatureState.NotInstalled,
                    "Voltura Air Webcam is not installed.");
        }

        return Failed("Voltura Air could not read the Phone webcam installation state.", result);
    }

    private bool IsProtectedHelperPath()
    {
        try
        {
            string programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            string expectedDirectory = Path.Combine(programFiles, ProtectedDirectoryName);
            if (!string.Equals(Path.GetFullPath(_setupPath), Path.Combine(expectedDirectory, SetupFileName), StringComparison.OrdinalIgnoreCase))
                return false;
            return !IsReparsePoint(programFiles) && !IsReparsePoint(expectedDirectory) && !IsReparsePoint(_setupPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    public Task<PhoneWebcamFeatureStatus> InstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PhoneWebcamFeatureStatus(
            PhoneWebcamFeatureState.Unavailable,
            "Run Voltura Air installer maintenance and select Phone Webcam."));
    }

    public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PhoneWebcamFeatureStatus(
            PhoneWebcamFeatureState.Unavailable,
            "Run Voltura Air installer maintenance to remove Phone Webcam."));
    }

    private static async Task<ProcessResult> RunAsync(
        string filePath,
        string argument,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = filePath,
                    Arguments = argument,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            if (!process.Start())
            {
                return new ProcessResult(-1, string.Empty, "The native setup helper did not start.");
            }

            Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(HelperTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return new ProcessResult(-1, string.Empty, "The native setup helper timed out.");
            }
            return new ProcessResult(
                process.ExitCode,
                await output.ConfigureAwait(false),
                await error.ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            return new ProcessResult(-1, string.Empty, exception.Message);
        }
    }

    internal static bool TryReadStatus(
        string output,
        out bool installed,
        out bool cleanupRequired,
        out bool updateRequired,
        out string? revision)
    {
        installed = false;
        cleanupRequired = false;
        updateRequired = false;
        revision = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            if (!TryReadBoolean(document.RootElement, "installed", out installed) ||
                !TryReadBoolean(document.RootElement, "cleanupRequired", out cleanupRequired) ||
                !TryReadBoolean(document.RootElement, "updateRequired", out updateRequired))
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("revision", out JsonElement revisionElement))
            {
                if (revisionElement.ValueKind == JsonValueKind.String)
                {
                    string? value = revisionElement.GetString();
                    if (value is null || !TryReadRevision(value, out revision)) return false;
                }
                else if (revisionElement.ValueKind != JsonValueKind.Null)
                {
                    return false;
                }
            }

            return (!installed || cleanupRequired) && (installed || !updateRequired) &&
                (!installed || !string.IsNullOrEmpty(revision) || updateRequired);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool TryReadRevision(string output, out string revision)
    {
        revision = output.Trim();
        return revision.Length == 64 && revision.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    internal static bool IsCurrentRevision(string? installedRevision, string packagedRevision) =>
        !string.IsNullOrEmpty(installedRevision) &&
        string.Equals(installedRevision, packagedRevision, StringComparison.Ordinal);

    private static bool TryReadBoolean(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out JsonElement element) ||
            element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    private static PhoneWebcamFeatureStatus Failed(string prefix, ProcessResult result) =>
        new(PhoneWebcamFeatureState.Failed, FailedMessage(prefix, result));

    private static string FailedMessage(string prefix, ProcessResult result)
    {
        return $"{prefix} The setup helper returned exit code {result.ExitCode}.";
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
