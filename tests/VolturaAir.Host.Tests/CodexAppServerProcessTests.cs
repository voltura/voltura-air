using VolturaAir.Host.Features.AiAssistant;

namespace VolturaAir.Host.Tests;

public sealed class CodexAppServerProcessTests
{
    [Fact]
    public void FindsInstalledCodexWhenPathDoesNotContainIt()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string installed = Path.Combine(root, "version-a", "codex.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
            File.WriteAllText(installed, string.Empty);

            Assert.Equal(installed, CodexAppServerProcess.FindExecutable([], root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PrefersPathBeforeInstalledFallback()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string pathDirectory = Path.Combine(root, "path");
            string installedRoot = Path.Combine(root, "installed");
            string pathExecutable = Path.Combine(pathDirectory, "codex.exe");
            string installedExecutable = Path.Combine(installedRoot, "version-a", "codex.exe");
            Directory.CreateDirectory(pathDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(installedExecutable)!);
            File.WriteAllText(pathExecutable, string.Empty);
            File.WriteAllText(installedExecutable, string.Empty);

            Assert.Equal(
                pathExecutable,
                CodexAppServerProcess.FindExecutable([pathDirectory], installedRoot));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"voltura-air-codex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
