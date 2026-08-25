using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VolturaAir.Host.Features.Diagnostics;

internal sealed record ComputerDiagnosticsSnapshot(
    string Windows,
    string System,
    string Processor,
    string LogicalProcessors,
    string PrimaryDisplay,
    string InstalledMemory,
    string AvailableMemory,
    string SystemDisk,
    string SystemUptime)
{
    public IEnumerable<DiagnosticItem> ToItems()
    {
        yield return new("Windows", Windows);
        yield return new("System", System);
        yield return new("Processor", Processor);
        yield return new("Logical processors", LogicalProcessors);
        yield return new("Primary display", PrimaryDisplay);
        yield return new("Installed memory", InstalledMemory);
        yield return new("Available memory", AvailableMemory);
        yield return new("System disk", SystemDisk);
        yield return new("System uptime", SystemUptime);
    }
}

internal interface IComputerDiagnosticsProbe
{
    string GetWindows();
    string GetSystem();
    string GetProcessor();
    int GetLogicalProcessorCount();
    (uint Width, uint Height, uint RefreshRate) GetPrimaryDisplayMode();
    ulong GetInstalledMemoryBytes();
    ulong GetAvailableMemoryBytes();
    (long TotalBytes, long FreeBytes) GetSystemDisk();
    TimeSpan GetSystemUptime();
}

internal sealed class ComputerDiagnosticsProvider(IComputerDiagnosticsProbe probe)
{
    public const string Unavailable = "Unavailable";

    public ComputerDiagnosticsProvider()
        : this(new WindowsComputerDiagnosticsProbe())
    {
    }

    public ComputerDiagnosticsSnapshot Capture() => new(
        CaptureString(probe.GetWindows),
        CaptureString(probe.GetSystem),
        CaptureString(probe.GetProcessor),
        CaptureValue(probe.GetLogicalProcessorCount, static value => value.ToString(CultureInfo.InvariantCulture)),
        CaptureValue(probe.GetPrimaryDisplayMode, static value =>
            $"{value.Width.ToString(CultureInfo.InvariantCulture)} × {value.Height.ToString(CultureInfo.InvariantCulture)} at {value.RefreshRate.ToString(CultureInfo.InvariantCulture)} Hz"),
        CaptureValue(probe.GetInstalledMemoryBytes, FormatBytes),
        CaptureValue(probe.GetAvailableMemoryBytes, FormatBytes),
        CaptureValue(probe.GetSystemDisk, static value =>
            $"{FormatBytes(checked((ulong)value.TotalBytes))} total, {FormatBytes(checked((ulong)value.FreeBytes))} free"),
        CaptureValue(probe.GetSystemUptime, FormatDuration));

    internal static string FormatBytes(ulong bytes)
    {
        const double gibibyte = 1024d * 1024d * 1024d;
        const double mebibyte = 1024d * 1024d;
        return bytes >= gibibyte
            ? $"{(bytes / gibibyte).ToString("N1", CultureInfo.InvariantCulture)} GiB"
            : $"{(bytes / mebibyte).ToString("N1", CultureInfo.InvariantCulture)} MiB";
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        return duration.Days > 0
            ? $"{duration.Days.ToString(CultureInfo.InvariantCulture)}d {duration.Hours.ToString(CultureInfo.InvariantCulture)}h {duration.Minutes.ToString(CultureInfo.InvariantCulture)}m"
            : duration.Hours > 0
                ? $"{duration.Hours.ToString(CultureInfo.InvariantCulture)}h {duration.Minutes.ToString(CultureInfo.InvariantCulture)}m"
                : $"{duration.Minutes.ToString(CultureInfo.InvariantCulture)}m";
    }

    private static string CaptureString(Func<string> read) => CaptureValue(
        read,
        static value => string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException() : Limit(value.Trim()));

    private static string CaptureValue<T>(Func<T> read, Func<T, string> format)
    {
        try
        {
            return format(read());
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            return Unavailable;
        }
    }

    private static string Limit(string value) => value.Length <= 256 ? value : value[..256];

    private static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or AccessViolationException;
}

internal sealed partial class WindowsComputerDiagnosticsProbe : IComputerDiagnosticsProbe
{
    private const string WindowsVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const string ProcessorKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";

    public string GetWindows()
    {
        using var key = OpenLocalMachineKey(WindowsVersionKey);
        var name = ReadRegistryString(key, "ProductName");
        var version = ReadRegistryString(key, "DisplayVersion");
        var build = ReadRegistryString(key, "CurrentBuildNumber");
        return $"{NormalizeWindowsProductName(name, build)}, version {version}, build {build}";
    }

    internal static string NormalizeWindowsProductName(string productName, string build) =>
        int.TryParse(build, NumberStyles.None, CultureInfo.InvariantCulture, out var buildNumber) &&
        buildNumber >= 22_000 && productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase)
            ? $"Windows 11{productName["Windows 10".Length..]}"
            : productName;

    public string GetSystem()
    {
        using var key = OpenLocalMachineKey(BiosKey);
        return $"{ReadRegistryString(key, "SystemManufacturer")} {ReadRegistryString(key, "SystemProductName")}".Trim();
    }

    public string GetProcessor()
    {
        using var key = OpenLocalMachineKey(ProcessorKey);
        return ReadRegistryString(key, "ProcessorNameString");
    }

    public int GetLogicalProcessorCount() => Environment.ProcessorCount;

    public unsafe (uint Width, uint Height, uint RefreshRate) GetPrimaryDisplayMode()
    {
        var mode = new DevMode { Size = checked((ushort)sizeof(DevMode)) };
        if (!EnumDisplaySettings(null, -1, ref mode) || mode.PelsWidth == 0 || mode.PelsHeight == 0 || mode.DisplayFrequency <= 1)
        {
            throw new InvalidOperationException();
        }

        return (mode.PelsWidth, mode.PelsHeight, mode.DisplayFrequency);
    }

    public ulong GetInstalledMemoryBytes() => ReadMemoryStatus().TotalPhysical;

    public ulong GetAvailableMemoryBytes() => ReadMemoryStatus().AvailablePhysical;

    public (long TotalBytes, long FreeBytes) GetSystemDisk()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var root = Path.GetPathRoot(systemDirectory);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new DriveNotFoundException();
        }

        var drive = new DriveInfo(root);
        if (!drive.IsReady)
        {
            throw new DriveNotFoundException();
        }

        return (drive.TotalSize, drive.AvailableFreeSpace);
    }

    public TimeSpan GetSystemUptime() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    private static RegistryKey OpenLocalMachineKey(string path)
    {
        using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return localMachine.OpenSubKey(path, writable: false) ?? throw new InvalidDataException();
    }

    private static string ReadRegistryString(RegistryKey key, string name) =>
        key.GetValue(name) as string is { } value && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidDataException();

    private static MemoryStatus ReadMemoryStatus()
    {
        var status = new MemoryStatus { Length = checked((uint)Marshal.SizeOf<MemoryStatus>()) };
        return GlobalMemoryStatusEx(ref status) ? status : throw new InvalidOperationException();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct DevMode
    {
        public fixed char DeviceName[32];
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        public fixed char FormName[32];
        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplaySettings(string? deviceName, int modeNumber, ref DevMode mode);
}
