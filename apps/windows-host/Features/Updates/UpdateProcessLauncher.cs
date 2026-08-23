namespace VolturaAir.Host.Features.Updates;

internal static class UpdateProcessLauncher
{
    internal static bool TryLaunchInstaller(
        Func<System.Diagnostics.Process?> launch,
        Action relaunchCurrentHost)
    {
        try
        {
            if (launch() is not null) return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException or UnauthorizedAccessException)
        {
        }

        relaunchCurrentHost();
        return false;
    }

    internal static string[] BuildRestartArguments(
        IEnumerable<string> currentArguments,
        string? updateOutcomeArgument)
    {
        var arguments = currentArguments.Where(argument =>
            !argument.Equals("--updated", StringComparison.OrdinalIgnoreCase) &&
            !argument.Equals("--update-failed", StringComparison.OrdinalIgnoreCase)).ToList();
        if (updateOutcomeArgument is not null) arguments.Add(updateOutcomeArgument);
        return [.. arguments];
    }
}
