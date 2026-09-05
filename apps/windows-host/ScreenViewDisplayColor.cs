using System.Runtime.InteropServices;
using Microsoft.Win32;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace VolturaAir.Host;

// Session-owned display metadata. The frame path reads one cached scalar only.
internal sealed partial class ScreenViewDisplayColor : IDisposable
{
    private readonly string _deviceName;
    private readonly IDXGIOutput6? _output;
    private readonly Lock _gate = new();
    private float _whiteScale;
    private bool _hdr;
    private bool _disposed;

    public ScreenViewDisplayColor(IDXGIOutput output)
    {
        _deviceName = output.Description.DeviceName;
        _output = output.QueryInterfaceOrNull<IDXGIOutput6>();
        try
        {
            Refresh();
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch { Dispose(); throw; }
    }

    public float WhiteScale => Volatile.Read(ref _whiteScale);
    public bool IsHdr => Volatile.Read(ref _hdr);
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Refresh();
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Refresh();
    private void Refresh()
    {
        lock (_gate)
        {
            if (_disposed) return;
            bool hdr = false;
            try { hdr = _output?.Description1.ColorSpace == ColorSpaceType.RgbFullG2084NoneP2020; }
            catch (SharpGenException) { }
            Volatile.Write(ref _hdr, hdr);
            Volatile.Write(ref _whiteScale, hdr ? ReadWhiteScale(_deviceName) : 1f);
        }
    }

    internal static float NormalizeWhiteLevel(uint whiteLevel) =>
        whiteLevel is >= 1000 and <= 125_000 ? 1000f / whiteLevel : 1f;

    internal static IDXGIOutputDuplication DuplicateOutput(IDXGIOutput output, ID3D11Device device)
    {
        using IDXGIOutput5? modern = output.QueryInterfaceOrNull<IDXGIOutput5>();
        if (modern is not null)
        {
            try { return modern.DuplicateOutput1(device, new[] { Format.R16G16B16A16_Float, Format.B8G8R8A8_UNorm }); }
            catch (SharpGenException exception) when (CanFallback(exception.ResultCode.Code)) { }
        }
        using IDXGIOutput1 legacy = output.QueryInterface<IDXGIOutput1>();
        return legacy.DuplicateOutput(device);
    }

    internal static bool CanFallback(int result) => result is unchecked((int)0x887A0004) or unchecked((int)0x80070057) or unchecked((int)0x80004002);

    private static unsafe float ReadWhiteScale(string deviceName)
    {
        const uint activePaths = 2;
        // A display change can race either query. The next notification/session retries;
        // missing metadata uses the Windows nominal scRGB white (80 nits).
        if (GetDisplayConfigBufferSizes(activePaths, out uint pathCount, out uint modeCount) != 0 ||
            pathCount is 0 or > 256 || modeCount > 512) return 1;
        var paths = new DisplayPath[pathCount];
        var modes = new DisplayMode[modeCount];
        fixed (DisplayPath* pathPointer = paths)
        fixed (DisplayMode* modePointer = modes)
        {
            if (QueryDisplayConfig(activePaths, ref pathCount, pathPointer, ref modeCount, modePointer, 0) != 0) return 1;
        }
        for (int i = 0; i < pathCount; i++)
        {
            DisplayPath path = paths[i];
            SourceName name = new() { Header = new(1, (uint)sizeof(SourceName), path.SourceAdapter, path.SourceId) };
            if (DisplayConfigGetDeviceInfo(&name) != 0 ||
                !string.Equals(new string(name.Name, 0, 32).TrimEnd('\0'), deviceName, StringComparison.OrdinalIgnoreCase)) continue;
            WhiteLevel level = new() { Header = new(11, (uint)sizeof(WhiteLevel), path.TargetAdapter, path.TargetId) };
            if (DisplayConfigGetDeviceInfo(&level) == 0) return NormalizeWhiteLevel(level.Value);
        }
        return 1;
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _output?.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct DeviceHeader(uint type, uint size, long adapter, uint id)
    {
        public readonly uint Type = type;
        public readonly uint Size = size;
        public readonly long Adapter = adapter;
        public readonly uint Id = id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct DisplayPath
    {
        [FieldOffset(0)] public long SourceAdapter;
        [FieldOffset(8)] public uint SourceId;
        [FieldOffset(20)] public long TargetAdapter;
        [FieldOffset(28)] public uint TargetId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct DisplayMode { }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private unsafe struct SourceName
    {
        public DeviceHeader Header;
        public fixed char Name[32];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct WhiteLevel
    {
        public DeviceHeader Header;
        public uint Value;
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int QueryDisplayConfig(uint flags, ref uint paths, DisplayPath* pathArray, ref uint modes, DisplayMode* modeArray, nint topology);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int DisplayConfigGetDeviceInfo(void* request);
}
