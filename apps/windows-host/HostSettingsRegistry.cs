using Microsoft.Win32;

namespace VolturaAir.Host;

/// <summary>
/// Selects the registry key used for Voltura Air's host-owned settings.
/// Isolated hosts receive a process-scoped disposable key so they never read
/// or change the user's normal preferences.
/// </summary>
internal static class HostSettingsRegistry
{
    private const string ProductionSettingsKeyPath = @"Software\VolturaAir";
    private const string IsolatedSettingsRootKeyPath = @"Software\VolturaAir.Isolated";
    private static readonly Lock Gate = new();
    private static string s_settingsKeyPath = ProductionSettingsKeyPath;
    private static string? s_isolatedSettingsKeyPath;

    internal static string SettingsKeyPath => Volatile.Read(ref s_settingsKeyPath);
    internal static event Action? SettingsScopeChanged;

    internal static IDisposable BeginIsolatedScope()
    {
        string settingsKeyPath;
        lock (Gate)
        {
            if (s_isolatedSettingsKeyPath is not null)
            {
                throw new InvalidOperationException("An isolated Voltura Air settings scope is already active.");
            }

            settingsKeyPath = $@"{IsolatedSettingsRootKeyPath}\{Guid.NewGuid():N}";
            using var key = Registry.CurrentUser.CreateSubKey(settingsKeyPath, writable: true);
            s_isolatedSettingsKeyPath = settingsKeyPath;
            Volatile.Write(ref s_settingsKeyPath, settingsKeyPath);
        }

        SettingsScopeChanged?.Invoke();
        return new IsolatedScope(settingsKeyPath);
    }

    private sealed class IsolatedScope(string settingsKeyPath) : IDisposable
    {
        private string? _settingsKeyPath = settingsKeyPath;

        public void Dispose()
        {
            var keyPath = Interlocked.Exchange(ref _settingsKeyPath, null);
            if (keyPath is null)
            {
                return;
            }

            lock (Gate)
            {
                if (!string.Equals(s_isolatedSettingsKeyPath, keyPath, StringComparison.Ordinal))
                {
                    return;
                }

                Volatile.Write(ref s_settingsKeyPath, ProductionSettingsKeyPath);
                s_isolatedSettingsKeyPath = null;
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // A stranded isolated key contains only capture/test values and
                    // cannot affect the production settings key.
                }
            }

            // Isolated hosts and tests are process-scoped. Cached values stay
            // isolated during cleanup so automation never reads production
            // settings immediately before that process or test scope ends.
        }
    }
}
