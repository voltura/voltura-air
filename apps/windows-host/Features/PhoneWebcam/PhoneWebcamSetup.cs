using System.Diagnostics;
using System.Text.Json;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal sealed class PhoneWebcamSetup : IPhoneWebcamSetup
{
    private const string SetupRelativePath = "PhoneWebcam\\VolturaAir.WebcamSetup.exe";
    private readonly string _setupPath;

    internal PhoneWebcamSetup(string? setupPath = null)
    {
        _setupPath = setupPath ?? Path.Combine(AppContext.BaseDirectory, SetupRelativePath);
    }

    public async Task<PhoneWebcamFeatureStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_setupPath))
        {
            return new PhoneWebcamFeatureStatus(
                PhoneWebcamFeatureState.Unavailable,
                "The Phone webcam native component is not installed with this build.");
        }

        ProcessResult result = await RunAsync("status", cancellationToken).ConfigureAwait(false);
        if (TryReadStatus(result.Output, out bool installed, out bool cleanupRequired, out bool updateRequired))
        {
            if (installed)
            {
                if (updateRequired)
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

    public async Task<PhoneWebcamFeatureStatus> InstallAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_setupPath))
        {
            return new PhoneWebcamFeatureStatus(
                PhoneWebcamFeatureState.Unavailable,
                "The Phone webcam native component is not installed with this build.");
        }

        ProcessResult result = await RunAsync("install", cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            PhoneWebcamFeatureStatus current = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return current.ShouldRemove
                ? new PhoneWebcamFeatureStatus(
                    current.State,
                    FailedMessage("Installation did not complete; remove the recoverable installation before retrying.", result),
                    HasError: true)
                : Failed("Voltura Air Webcam installation did not complete.", result);
        }

        PhoneWebcamFeatureStatus status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.IsInstalled && !status.HasError
            ? status
            : new PhoneWebcamFeatureStatus(
                PhoneWebcamFeatureState.Failed,
                "The camera installer completed, but Windows did not report Voltura Air Webcam as installed.");
    }

    public async Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_setupPath))
        {
            return new PhoneWebcamFeatureStatus(
                PhoneWebcamFeatureState.Unavailable,
                "The Phone webcam native component is not installed with this build.");
        }

        ProcessResult result = await RunAsync("remove", cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            PhoneWebcamFeatureStatus current = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return current.ShouldRemove
                ? new PhoneWebcamFeatureStatus(
                    current.State,
                    FailedMessage("Removal did not complete; the recoverable installation remains.", result),
                    HasError: true)
                : Failed("Voltura Air Webcam removal did not complete.", result);
        }

        PhoneWebcamFeatureStatus status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.State == PhoneWebcamFeatureState.NotInstalled
            ? status
            : new PhoneWebcamFeatureStatus(
                PhoneWebcamFeatureState.Failed,
                "The camera remover completed, but Windows still reports Voltura Air Webcam as installed.");
    }

    private async Task<ProcessResult> RunAsync(string argument, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _setupPath,
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

            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
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
        out bool updateRequired)
    {
        installed = false;
        cleanupRequired = false;
        updateRequired = false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            if (!TryReadBoolean(document.RootElement, "installed", out installed) ||
                !TryReadBoolean(document.RootElement, "cleanupRequired", out cleanupRequired) ||
                !TryReadBoolean(document.RootElement, "updateRequired", out updateRequired))
            {
                return false;
            }

            return (!installed || cleanupRequired) && (installed || !updateRequired);
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
        string detail = string.IsNullOrWhiteSpace(result.Error)
            ? $"setup exit code {result.ExitCode}"
            : result.Error.Trim();
        return $"{prefix} {detail}";
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
