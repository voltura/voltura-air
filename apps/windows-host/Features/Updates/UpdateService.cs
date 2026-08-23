using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace VolturaAir.Host.Features.Updates;

internal enum UpdateState { Idle, Checking, WaitingForDevices, Downloading, Ready }

/// <summary>
/// The only owner of GitHub release discovery and the staged installer. It deliberately
/// has no background work unless this is an installed, opted-in production host.
/// </summary>
internal sealed class UpdateService : IAsyncDisposable
{
    private const string ReleaseUrl = "https://api.github.com/repos/voltura/voltura-air/releases/latest";
    private const long MaxMetadataBytes = 256 * 1024;
    private readonly PairingManager _pairingManager;
    private readonly bool _eligible;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private readonly System.Net.Http.HttpClient _client;
    private readonly System.Net.Http.HttpClientHandler _handler;
    private readonly Action<string>? _requestApply;
    private readonly Func<CancellationToken, Task> _scheduleAutomaticWork;
    private readonly EventHandler _connectionChanged;
    private readonly System.Threading.Lock _settingsTransitionLock = new();
    private Task? _scheduled;
    private Task _settingsTransition = Task.CompletedTask;
    private CancellationTokenSource? _automaticCancellation;
    private int _disposed;

    internal UpdateService(
        PairingManager pairingManager,
        string[] args,
        Action<string>? requestApply = null,
        bool? eligibleOverride = null,
        Func<CancellationToken, Task>? scheduleAutomaticWork = null)
    {
        _pairingManager = pairingManager;
        _requestApply = requestApply;
        _connectionChanged = (_, _) =>
        {
            if (State == UpdateState.WaitingForDevices && !_pairingManager.HasActiveController)
            {
                _ = CheckForUpdatesAsync(manual: false, _shutdown.Token);
            }
        };
        _pairingManager.ConnectionChanged += _connectionChanged;
        AppUpdateSettings.Changed += OnSettingsChanged;
        _eligible = eligibleOverride ?? IsEligible(args, Environment.ProcessPath, out _);
        _handler = new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        _client = new System.Net.Http.HttpClient(_handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("VolturaAir", "1"));
        _scheduleAutomaticWork = scheduleAutomaticWork ?? ScheduleAsync;
        RestoreReadyState();
        if (_eligible && AppUpdateSettings.AutomaticUpdateDownloadsEnabled())
        {
            StartAutomaticWork();
        }
    }

    internal event EventHandler? StateChanged;
    internal UpdateState State { get; private set; } = UpdateState.Idle;
    internal string? TargetVersion { get; private set; }
    internal bool IsUpdateEligible => _eligible;

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        lock (_settingsTransitionLock)
        {
            if (Volatile.Read(ref _disposed) == 0) _settingsTransition = RestartAfterAsync(_settingsTransition);
        }
    }

    private async Task RestartAfterAsync(Task previous)
    {
        try { await previous.ConfigureAwait(false); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        await RestartAutomaticWorkAsync().ConfigureAwait(false);
    }

    private async Task RestartAutomaticWorkAsync()
    {
        try
        {
            await StopAutomaticWorkAsync().ConfigureAwait(false);
            if (_eligible && AppUpdateSettings.AutomaticUpdateDownloadsEnabled()) StartAutomaticWork();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private void StartAutomaticWork()
    {
        _automaticCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _scheduled = _scheduleAutomaticWork(_automaticCancellation.Token);
    }

    private async Task StopAutomaticWorkAsync()
    {
        var cancellation = _automaticCancellation;
        var scheduled = _scheduled;
        _automaticCancellation = null;
        _scheduled = null;
        if (cancellation is null) return;

        await cancellation.CancelAsync().ConfigureAwait(false);
        if (scheduled is not null)
        {
            try { await scheduled.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        cancellation.Dispose();
    }

    internal static bool IsEligible(string[] args, string? processPath, out string? modifyInstaller)
    {
        modifyInstaller = null;
        if (args.Any(arg => arg.Equals("--isolated-test-mode", StringComparison.OrdinalIgnoreCase) ||
                            arg.Equals("--site-screenshot-mode", StringComparison.OrdinalIgnoreCase) ||
                            arg.Equals("--installer-health-check", StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(processPath)) return false;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura Air", writable: false);
        var installLocation = key?.GetValue("InstallLocation") as string;
        if (string.IsNullOrWhiteSpace(installLocation) || !string.Equals(Path.GetFullPath(installLocation), Path.GetFullPath(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)), StringComparison.OrdinalIgnoreCase)) return false;
        var candidate = Path.Combine(Path.GetDirectoryName(installLocation) ?? string.Empty, "VolturaAir-Modify.exe");
        if (!File.Exists(candidate)) return false;
        modifyInstaller = candidate;
        return true;
    }

    internal async Task CheckForUpdatesAsync(bool manual, CancellationToken cancellationToken = default)
    {
        if (!_eligible || (!manual && !AppUpdateSettings.AutomaticUpdateDownloadsEnabled())) return;
        await _singleFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetState(UpdateState.Checking, null);
            AppUpdateSettings.SetLastUpdateCheckAttemptUtc(DateTimeOffset.UtcNow);
            using var response = await _client.GetAsync(ReleaseUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) { SetState(UpdateState.Idle, null); return; }
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var metadata = await UpdatePackageStager.ReadCappedAsync(stream, MaxMetadataBytes, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            if (root.GetProperty("prerelease").GetBoolean() || root.GetProperty("draft").GetBoolean()) { SetState(UpdateState.Idle, null); return; }
            var tag = root.GetProperty("tag_name").GetString();
            if (!TryParseVersion(tag, out var candidate) || candidate <= CurrentVersion()) { SetState(UpdateState.Idle, null); return; }
            TargetVersion = candidate.ToString();
            if (_pairingManager.HasActiveController) { SetState(UpdateState.WaitingForDevices, TargetVersion); return; }
            await new UpdatePackageStager(_client, _pairingManager, SelectInstallerName, VerifyManifest, SetState)
                .StageAsync(root.GetProperty("assets"), candidate, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!manual) { SetState(UpdateState.Idle, null); }
        catch (System.Net.Http.HttpRequestException) { SetState(UpdateState.Idle, null); }
        catch (JsonException) { SetState(UpdateState.Idle, null); }
        finally { _singleFlight.Release(); }
    }

    internal async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (State != UpdateState.Ready || string.IsNullOrWhiteSpace(TargetVersion) || _requestApply is null) return;
        await _singleFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pending = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltura Air", "Updates", "pending");
            var manifest = await File.ReadAllBytesAsync(Path.Combine(pending, "manifest.json"), cancellationToken).ConfigureAwait(false);
            var signature = await File.ReadAllBytesAsync(Path.Combine(pending, $"VolturaAir-Update-{TargetVersion}.sig"), cancellationToken).ConfigureAwait(false);
            if (!VerifyManifest(manifest, signature)) throw new IOException("The staged update could not be verified.");
            using var document = JsonDocument.Parse(manifest);
            var asset = document.RootElement.GetProperty("assets").EnumerateArray().Single(item => item.GetProperty("name").GetString() == SelectInstallerName());
            var file = Path.Combine(pending, asset.GetProperty("name").GetString()!);
            await using var installer = File.OpenRead(file);
            if (!File.Exists(file) || new FileInfo(file).Length != asset.GetProperty("size").GetInt64() ||
                !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(installer, cancellationToken)).ToLowerInvariant(), asset.GetProperty("sha256").GetString(), StringComparison.Ordinal))
                throw new IOException("The staged update could not be verified.");
            _requestApply(file);
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException)
        {
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltura Air", "Updates", "pending"), recursive: true); } catch (IOException) { }
            SetState(UpdateState.Idle, null);
        }
        finally { _singleFlight.Release(); }
    }

    private async Task ScheduleAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && AppUpdateSettings.AutomaticUpdateDownloadsEnabled())
        {
            var last = AppUpdateSettings.LastUpdateCheckAttemptUtc();
            var wait = last is null ? TimeSpan.FromMinutes(2) : TimeSpan.FromHours(24) - (DateTimeOffset.UtcNow - last.Value);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            await CheckForUpdatesAsync(false, cancellationToken).ConfigureAwait(false);
        }
    }

    private void RestoreReadyState()
    {
        var manifest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltura Air", "Updates", "pending", "manifest.json");
        if (!File.Exists(manifest)) return;
        // A full signature/hash verification is always performed immediately before apply.
        try
        {
            var bytes = File.ReadAllBytes(manifest);
            using var document = JsonDocument.Parse(bytes);
            TargetVersion = document.RootElement.GetProperty("version").GetString();
            var signature = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(manifest)!, $"VolturaAir-Update-{TargetVersion}.sig"));
            if (!VerifyManifest(bytes, signature)) return;
            if (!string.IsNullOrWhiteSpace(TargetVersion) && TryParseVersion(TargetVersion, out _)) SetState(UpdateState.Ready, TargetVersion);
        }
        catch (Exception ex) when (ex is JsonException or IOException or CryptographicException) { }
    }

    private string SelectInstallerName() => $"VolturaAir-Setup-{TargetVersion}-win-x64{(FileVersionInfo.GetVersionInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltura Air", "VolturaAir-Modify.exe")).OriginalFilename?.Contains("-full", StringComparison.OrdinalIgnoreCase) == true ? "-full" : string.Empty)}.exe";
    private static bool VerifyManifest(byte[] manifest, byte[] signature)
    {
        using var rsa = RSA.Create(); using var stream = typeof(UpdateService).Assembly.GetManifestResourceStream("VolturaAir.Host.Features.Updates.update-signing-public.pem");
        if (stream is null) return false; using var reader = new StreamReader(stream); rsa.ImportFromPem(reader.ReadToEnd()); return rsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }


    private void SetState(UpdateState state, string? version)
    {
        State = state;
        TargetVersion = version ?? TargetVersion;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Version CurrentVersion() => Version.TryParse(typeof(UpdateService).Assembly.GetName().Version?.ToString(), out var version) ? version : new Version(0, 0, 0);
    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (!Version.TryParse(value?.TrimStart('v'), out var parsed) || parsed is null || parsed.Build < 0) return false;
        version = parsed;
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _pairingManager.ConnectionChanged -= _connectionChanged;
        AppUpdateSettings.Changed -= OnSettingsChanged;
        Task settingsTransition;
        lock (_settingsTransitionLock) { settingsTransition = _settingsTransition; }
        try { await settingsTransition.ConfigureAwait(false); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        await StopAutomaticWorkAsync().ConfigureAwait(false);
        await _shutdown.CancelAsync();
        _client.Dispose(); _handler.Dispose(); _singleFlight.Dispose(); _shutdown.Dispose();
    }
}
