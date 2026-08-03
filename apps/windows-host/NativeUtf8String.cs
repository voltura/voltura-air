using System.Runtime.InteropServices;

namespace VolturaAir.Host;

internal sealed class NativeIceServerList : IDisposable
{
    private readonly Utf8String[] _values;

    public NativeIceServerList(IReadOnlyList<string> values)
    {
        _values = [.. values.Select(value => new Utf8String(value))];
        Count = _values.Length;
        if (Count == 0) return;
        Pointer = Marshal.AllocHGlobal(IntPtr.Size * Count);
        for (var index = 0; index < Count; index++)
            Marshal.WriteIntPtr(Pointer, index * IntPtr.Size, _values[index].Pointer);
    }

    public nint Pointer { get; }
    public int Count { get; }

    public void Dispose()
    {
        if (Pointer != 0) Marshal.FreeHGlobal(Pointer);
        foreach (Utf8String value in _values) value.Dispose();
    }
}

internal sealed class Utf8String(string value) : IDisposable
{
    public nint Pointer { get; private set; } = Marshal.StringToCoTaskMemUTF8(value);

    public void Dispose()
    {
        if (Pointer == 0) return;
        Marshal.FreeCoTaskMem(Pointer);
        Pointer = 0;
    }
}
