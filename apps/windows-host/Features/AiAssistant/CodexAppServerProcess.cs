using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;

namespace VolturaAir.Host.Features.AiAssistant;

internal sealed class CodexAppServerProcess : IAsyncDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);
    private static readonly Lock ExecutableGate = new();
    private static string? _executablePath;
    private readonly Process _process;
    private readonly Task _stderrDrain;
    private int _disposed;

    private CodexAppServerProcess(Process process)
    {
        _process = process;
        _stderrDrain = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is not null)
                {
                    // Drain without retaining potentially sensitive app-server diagnostics.
                }
            }
            catch (Exception) { }
        });
#pragma warning disable CA2000 // JsonRpcConnection owns and disposes the transport.
        Connection = new JsonRpcConnection(new StdioJsonLineTransport(process.StandardOutput, process.StandardInput));
#pragma warning restore CA2000
    }

    internal JsonRpcConnection Connection { get; }
    internal static bool IsAvailable => ResolveExecutable() is not null;

    internal static CodexAppServerProcess Start(IReadOnlyList<string> disabledMcpServers)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutable() ?? "codex",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--stdio");
        foreach (string value in new[] { "web_search=\"disabled\"", "apps={}", "plugins={}" })
        {
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(value);
        }
        foreach (string serverName in disabledMcpServers)
        {
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add($"mcp_servers.{serverName}.enabled=false");
        }
        foreach (string feature in AiAssistantProfile.DisabledFeatures)
        {
            startInfo.ArgumentList.Add("--disable");
            startInfo.ArgumentList.Add(feature);
        }
        try
        {
            Process process = Process.Start(startInfo) ?? throw new CodexCompatibilityException("Windows did not start Codex app-server.");
            return new(process);
        }
        catch (Win32Exception exception)
        {
            throw new CodexCompatibilityException("Codex is not installed or its command-line component is unavailable.", exception);
        }
    }

    private static string? FindExecutable()
    {
        string[] paths =
        [
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty,
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? string.Empty
        ];
        string installedBinRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI",
            "Codex",
            "bin");
        return FindExecutable(paths, installedBinRoot);
    }

    internal static string? FindExecutable(IEnumerable<string> paths, string installedBinRoot)
    {
        foreach (string directory in paths
            .SelectMany(path => path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim('"'), "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { }
        }

        string? newestInstalled = null;
        DateTime newestWriteTime = DateTime.MinValue;
        try
        {
            foreach (string versionDirectory in Directory.EnumerateDirectories(installedBinRoot))
            {
                try
                {
                    string candidate = Path.Combine(versionDirectory, "codex.exe");
                    if (!File.Exists(candidate)) continue;
                    DateTime writeTime = File.GetLastWriteTimeUtc(candidate);
                    if (newestInstalled is null || writeTime > newestWriteTime)
                    {
                        newestInstalled = candidate;
                        newestWriteTime = writeTime;
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException) { }
        if (newestInstalled is not null) return newestInstalled;

        return null;
    }

    private static string? ResolveExecutable()
    {
        lock (ExecutableGate)
        {
            if (_executablePath is not null && File.Exists(_executablePath)) return _executablePath;
            _executablePath = FindExecutable();
            return _executablePath;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Exception? connectionFailure = null;
        try { await Connection.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            connectionFailure = exception;
        }
        try
        {
            if (!_process.HasExited)
            {
                try { await _process.WaitForExitAsync().WaitAsync(ShutdownTimeout).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    try { _process.Kill(entireProcessTree: true); }
                    catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException) { }
                    try { await _process.WaitForExitAsync().WaitAsync(ShutdownTimeout).ConfigureAwait(false); }
                    catch (TimeoutException) { }
                }
            }
        }
        finally
        {
            try
            {
                if (!_process.HasExited)
                {
                    try { _process.Kill(entireProcessTree: true); }
                    catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException) { }
                }
                try { await _stderrDrain.WaitAsync(ShutdownTimeout).ConfigureAwait(false); }
                catch (TimeoutException) { }
            }
            finally
            {
                _process.Dispose();
            }
        }
        if (connectionFailure is not null && connectionFailure is not (IOException or ObjectDisposedException or OperationCanceledException))
            ExceptionDispatchInfo.Capture(connectionFailure!).Throw();
    }
}
