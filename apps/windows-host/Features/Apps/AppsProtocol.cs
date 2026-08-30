using System.Buffers.Binary;
using System.Text;

namespace VolturaAir.Host.Features.Apps;

internal static class AppsProtocol
{
    internal const int MaximumWindows = 48;
    internal const int MaximumRequestedPreviews = 3;
    internal const int MaximumWindowTitleLength = 256;
    internal const int MaximumApplicationNameLength = 128;
    internal const int MaximumPreviewWidth = 1024;
    internal const int MaximumPreviewHeight = 640;
    internal const int MaximumPreviewPixels = 16_777_216;
    internal const int MaximumPreviewBytes = 1536 * 1024;
    internal const int PreviewChunkBytes = 48 * 1024;
    internal const int MaximumRecordBytes = 1 + OpaqueIdLength + 4 + PreviewChunkBytes;
    internal const int MinimumRecordBytes = 1 + OpaqueIdLength + 4;
    internal const string DataChannelLabel = "voltura-apps-preview";
    internal const int OpaqueIdLength = 32;
    internal const byte Version = 1;

    private const byte RequestKind = 1;
    private const byte HeaderKind = 2;
    private const byte DataKind = 3;
    private const int RequestPrefixBytes = 1 + OpaqueIdLength + 1;
    private const int HeaderBytes = 1 + OpaqueIdLength + 1 + 2 + 2 + 4 + 1;
    private const int DataHeaderBytes = 1 + OpaqueIdLength + 4;

    internal static bool TryParseRequest(
        ReadOnlyMemory<byte> bytes,
        out string revision,
        out string[] windowIds)
    {
        revision = string.Empty;
        windowIds = [];
        if (bytes.Length < RequestPrefixBytes || DecodeKind(bytes.Span[0]) != RequestKind)
        {
            return false;
        }

        int count = bytes.Span[1 + OpaqueIdLength];
        if (count is < 1 or > MaximumRequestedPreviews ||
            bytes.Length != RequestPrefixBytes + count * OpaqueIdLength ||
            !TryDecodeId(bytes.Span.Slice(1, OpaqueIdLength), out revision))
        {
            return false;
        }

        var ids = new string[count];
        for (int index = 0; index < count; index++)
        {
            if (!TryDecodeId(
                    bytes.Span.Slice(RequestPrefixBytes + index * OpaqueIdLength, OpaqueIdLength),
                    out ids[index]))
            {
                return false;
            }
        }

        windowIds = ids;
        return true;
    }

    internal static byte[] CreateUnavailableHeader(string windowId)
        => CreateHeader(windowId, available: false, width: 0, height: 0, encodedBytes: 0);

    internal static byte[] CreatePreviewHeader(
        string windowId,
        int width,
        int height,
        int encodedBytes)
        => CreateHeader(windowId, available: true, width, height, encodedBytes);

    internal static byte[] CreatePreviewData(string windowId, int offset, ReadOnlySpan<byte> payload)
    {
        if (!IsOpaqueId(windowId) || offset < 0 || payload.Length is < 1 or > PreviewChunkBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        var result = new byte[DataHeaderBytes + payload.Length];
        result[0] = EncodeKind(DataKind);
        Encoding.ASCII.GetBytes(windowId, result.AsSpan(1, OpaqueIdLength));
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(1 + OpaqueIdLength, 4), offset);
        payload.CopyTo(result.AsSpan(DataHeaderBytes));
        return result;
    }

    internal static bool IsOpaqueId(string? value) =>
        value is { Length: OpaqueIdLength } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static byte[] CreateHeader(
        string windowId,
        bool available,
        int width,
        int height,
        int encodedBytes)
    {
        if (!IsOpaqueId(windowId) ||
            width is < 0 or > MaximumPreviewWidth ||
            height is < 0 or > MaximumPreviewHeight ||
            encodedBytes is < 0 or > MaximumPreviewBytes ||
            available != (width > 0 && height > 0 && encodedBytes > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(encodedBytes));
        }

        var result = new byte[HeaderBytes];
        result[0] = EncodeKind(HeaderKind);
        Encoding.ASCII.GetBytes(windowId, result.AsSpan(1, OpaqueIdLength));
        result[1 + OpaqueIdLength] = available ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2 + OpaqueIdLength, 2), checked((ushort)width));
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4 + OpaqueIdLength, 2), checked((ushort)height));
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(6 + OpaqueIdLength, 4), encodedBytes);
        result[10 + OpaqueIdLength] = 1; // image/jpeg
        return result;
    }

    private static bool TryDecodeId(ReadOnlySpan<byte> bytes, out string value)
    {
        value = Encoding.ASCII.GetString(bytes);
        return IsOpaqueId(value);
    }

    private static byte EncodeKind(byte kind) => (byte)((Version << 4) | kind);
    private static byte DecodeKind(byte value) => (value >> 4) == Version ? (byte)(value & 0x0f) : (byte)0;
}

internal sealed record AppsWindowSnapshot(
    nint Handle,
    string Title,
    string ApplicationName,
    bool Active,
    bool Minimized,
    bool MaximizeSupported,
    bool PreviewSupported);

internal sealed record AppsWindowDiscoveryResult(
    bool Succeeded,
    string Code,
    string Message,
    IReadOnlyList<AppsWindowSnapshot> Windows);

internal sealed record AppsWindowActionResult(bool Succeeded, string Code, string Message);

internal sealed record AppsPreviewCaptureResult(
    bool Succeeded,
    byte[]? Content,
    int Width,
    int Height);

internal interface IAppsWindowAdapter : IDisposable
{
    AppsWindowDiscoveryResult Discover(bool includeVolturaAir);
    bool IsUsable(nint windowHandle, bool includeVolturaAir);
    AppsWindowActionResult Activate(nint windowHandle, bool includeVolturaAir);
    AppsWindowActionResult Close(nint windowHandle, bool includeVolturaAir);
    AppsPreviewCaptureResult CapturePreview(
        nint windowHandle,
        bool includeVolturaAir,
        CancellationToken cancellationToken);

    void IDisposable.Dispose()
    {
    }
}

internal sealed class UnavailableAppsWindowAdapter : IAppsWindowAdapter
{
    public AppsWindowDiscoveryResult Discover(bool includeVolturaAir) =>
        new(false, "unavailable", "Open applications are unavailable in isolated mode.", []);

    public bool IsUsable(nint windowHandle, bool includeVolturaAir) => false;

    public AppsWindowActionResult Activate(nint windowHandle, bool includeVolturaAir) =>
        new(false, "unavailable", "The application window is unavailable.");

    public AppsWindowActionResult Close(nint windowHandle, bool includeVolturaAir) =>
        new(false, "unavailable", "The application window is unavailable.");

    public AppsPreviewCaptureResult CapturePreview(
        nint windowHandle,
        bool includeVolturaAir,
        CancellationToken cancellationToken) =>
        new(false, null, 0, 0);
}
