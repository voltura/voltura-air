using System.Runtime.InteropServices;

namespace VolturaAir.Host;

// The encoder owns this ICodecAPI reference. No RCW or control calls escape its capture thread.
internal sealed unsafe class ScreenViewEncoderControls : IDisposable
{
    private static readonly Guid InterfaceId = new("901db4c7-31ce-41a2-85dc-8fa0bf41b8da");
    internal static readonly Guid ForceKeyFrame = new("398c1b98-8353-475a-9ef2-8f265d260345");
    internal static readonly Guid MeanBitrate = new("f7222374-2144-4815-b550-a37f8e12ee52");
    internal static readonly Guid MaximumBitrate = new("9651eae4-39b9-4ebf-85ef-d7f444ec7465");
    private static readonly Guid RateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423");
    private static readonly Guid LowLatency = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    private readonly HashSet<Guid> _unsupported = [];
    private readonly Func<Guid, uint, bool, bool> _set;
    private nint _pointer;
    private bool _peakConstrained;

    public ScreenViewEncoderControls(nint transform)
    {
        Guid iid = InterfaceId;
        if (Marshal.QueryInterface(transform, in iid, out _pointer) < 0) _pointer = 0;
        _set = SetNative;
    }

    internal ScreenViewEncoderControls(Func<Guid, uint, bool, bool> set) => _set = set;

    public void Configure(int bitrate)
    {
        TrySet(LowLatency, 1, boolean: true);
        // Set the limits before enabling the mode, so a rejected optional setting cannot
        // activate an unconstrained replacement for the existing encoder configuration.
        _peakConstrained = TrySet(MaximumBitrate, checked((uint)bitrate)) &&
            TrySet(MeanBitrate, checked((uint)bitrate)) && TrySet(RateControlMode, 1);
    }

    public bool TryRequestKeyFrame() => TrySet(ForceKeyFrame, 1);

    public bool TrySetBitrate(int bitrate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitrate);
        return (!_peakConstrained || TrySet(MaximumBitrate, checked((uint)bitrate))) &&
            TrySet(MeanBitrate, checked((uint)bitrate));
    }

    private bool TrySet(Guid property, uint value, bool boolean = false)
    {
        if (_unsupported.Contains(property)) return false;
        if (_set(property, value, boolean)) return true;
        _unsupported.Add(property);
        return false;
    }

    private bool SetNative(Guid property, uint value, bool boolean)
    {
        if (_pointer == 0) return false;
        void** methods = *(void***)_pointer;
        // ICodecAPI::IsSupported and IsModifiable return S_FALSE when unavailable.
        if (((delegate* unmanaged[Stdcall]<nint, Guid*, int>)methods[3])(_pointer, &property) != 0 ||
            ((delegate* unmanaged[Stdcall]<nint, Guid*, int>)methods[4])(_pointer, &property) != 0) return false;
        Variant variant = new() { Type = boolean ? (ushort)11 : (ushort)19, Value = boolean ? 0xffffu : value };
        return ((delegate* unmanaged[Stdcall]<nint, Guid*, Variant*, int>)methods[9])(_pointer, &property, &variant) >= 0;
    }

    public void Dispose()
    {
        nint pointer = Interlocked.Exchange(ref _pointer, 0);
        if (pointer != 0) Marshal.Release(pointer);
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public uint Value;
    }
}
