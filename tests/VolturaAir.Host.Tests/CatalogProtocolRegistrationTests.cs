using Microsoft.Win32;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class CatalogProtocolRegistrationTests
{
    [Fact]
    public void BuildOpenCommandPassesProtocolUriToExecutable()
    {
        Assert.Equal(
            "\"C:\\Voltura Air\\VolturaAir.Host.exe\" \"%1\"",
            CatalogProtocolRegistration.BuildOpenCommand(
                @"C:\Voltura Air\VolturaAir.Host.exe",
                null));
    }

    [Fact]
    public void BuildOpenCommandIncludesEntryAssemblyForDotnetHost()
    {
        Assert.Equal(
            "\"C:\\Program Files\\dotnet\\dotnet.exe\" \"C:\\Voltura Air\\VolturaAir.Host.dll\" \"%1\"",
            CatalogProtocolRegistration.BuildOpenCommand(
                @"C:\Program Files\dotnet\dotnet.exe",
                @"C:\Voltura Air\VolturaAir.Host.dll"));
    }

    [Fact]
    public void RegisterWritesCompleteUrlProtocolContract()
    {
        var testPath = $@"Software\Voltura Air Tests\{Guid.NewGuid():N}";
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(testPath, writable: true);
            Assert.NotNull(root);

            CatalogProtocolRegistration.Register(
                root,
                "\"C:\\VolturaAir.Host.exe\" \"%1\"",
                @"C:\VolturaAir.Host.exe");

            using var protocol = root.OpenSubKey("voltura-air");
            Assert.NotNull(protocol);
            Assert.Equal(
                "URL:Voltura Air custom screen",
                protocol.GetValue(null));
            Assert.Equal(string.Empty, protocol.GetValue("URL Protocol"));
            Assert.Equal(
                @"C:\VolturaAir.Host.exe,0",
                protocol.OpenSubKey("DefaultIcon")?.GetValue(null));
            Assert.Equal(
                "\"C:\\VolturaAir.Host.exe\" \"%1\"",
                protocol.OpenSubKey(@"shell\open\command")?.GetValue(null));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testPath, throwOnMissingSubKey: false);
        }
    }
}
