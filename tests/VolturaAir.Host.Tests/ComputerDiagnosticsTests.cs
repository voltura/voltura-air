using VolturaAir.Host.Features.Diagnostics;

namespace VolturaAir.Host.Tests;

public sealed class ComputerDiagnosticsTests
{
    [Fact]
    public void FormatsControlledComputerSnapshot()
    {
        var provider = new ComputerDiagnosticsProvider(new FakeComputerDiagnosticsProbe());

        var snapshot = provider.Capture();

        Assert.Equal("Windows 11 Pro, version 24H2, build 26100", snapshot.Windows);
        Assert.Equal("Voltura ExampleBook", snapshot.System);
        Assert.Equal("Example Processor", snapshot.Processor);
        Assert.Equal("12", snapshot.LogicalProcessors);
        Assert.Equal("3840 × 2160 at 60 Hz", snapshot.PrimaryDisplay);
        Assert.Equal("16.0 GiB", snapshot.InstalledMemory);
        Assert.Equal("6.5 GiB", snapshot.AvailableMemory);
        Assert.Equal("500.0 GiB total, 125.0 GiB free", snapshot.SystemDisk);
        Assert.Equal("2d 3h 4m", snapshot.SystemUptime);
    }

    [Fact]
    public void OneFailedProbeLeavesOtherFieldsAvailable()
    {
        var probe = new FakeComputerDiagnosticsProbe { ProcessorFailure = new InvalidOperationException("injected") };

        var snapshot = new ComputerDiagnosticsProvider(probe).Capture();

        Assert.Equal(ComputerDiagnosticsProvider.Unavailable, snapshot.Processor);
        Assert.Equal("Windows 11 Pro, version 24H2, build 26100", snapshot.Windows);
        Assert.Equal("16.0 GiB", snapshot.InstalledMemory);
        Assert.Equal("500.0 GiB total, 125.0 GiB free", snapshot.SystemDisk);
    }

    [Theory]
    [InlineData("Windows 10 Pro", "26200", "Windows 11 Pro")]
    [InlineData("Windows 10 Pro", "19045", "Windows 10 Pro")]
    [InlineData("Windows 11 Pro", "26200", "Windows 11 Pro")]
    public void CorrectsOnlyTheLegacyWindowsElevenRegistryLabel(string productName, string build, string expected)
    {
        Assert.Equal(expected, WindowsComputerDiagnosticsProbe.NormalizeWindowsProductName(productName, build));
    }

    internal sealed class FakeComputerDiagnosticsProbe : IComputerDiagnosticsProbe
    {
        public Exception? ProcessorFailure { get; init; }
        public int CaptureCount { get; private set; }

        public string GetWindows() { CaptureCount++; return "Windows 11 Pro, version 24H2, build 26100"; }
        public string GetSystem() { CaptureCount++; return "Voltura ExampleBook"; }
        public string GetProcessor() { CaptureCount++; return ProcessorFailure is null ? "Example Processor" : throw ProcessorFailure; }
        public int GetLogicalProcessorCount() { CaptureCount++; return 12; }
        public (uint Width, uint Height, uint RefreshRate) GetPrimaryDisplayMode() { CaptureCount++; return (3840, 2160, 60); }
        public ulong GetInstalledMemoryBytes() { CaptureCount++; return 16UL * 1024 * 1024 * 1024; }
        public ulong GetAvailableMemoryBytes() { CaptureCount++; return 13UL * 512 * 1024 * 1024; }
        public (long TotalBytes, long FreeBytes) GetSystemDisk() { CaptureCount++; return (500L * 1024 * 1024 * 1024, 125L * 1024 * 1024 * 1024); }
        public TimeSpan GetSystemUptime() { CaptureCount++; return new TimeSpan(2, 3, 4, 5); }
    }
}
