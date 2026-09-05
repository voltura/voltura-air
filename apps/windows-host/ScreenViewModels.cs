using System.Text.Json.Serialization;

namespace VolturaAir.Host;

internal interface IScreenViewFrameEncoder : IDisposable
{
    bool TryRequestKeyFrame();
    bool TrySetBitrate(int bitrate);
    ScreenViewEncodedFrame Encode(Vortice.Direct3D11.ID3D11Texture2D surface);
}

internal static class ScreenViewScreenshotLimits
{
    internal const long MaximumPixels = 7680L * 4320L;
    internal const long MaximumEncodedBytes = 64L * 1024L * 1024L;
    internal const string Format = "image/png";
}

public enum ScreenViewRotation
{
    Identity,
    Rotate90,
    Rotate180,
    Rotate270
}

public sealed record ScreenViewSource(
    string Id,
    string Label,
    int Width,
    int Height,
    bool IsPrimary,
    [property: JsonIgnore] int DesktopLeft = 0,
    [property: JsonIgnore] int DesktopTop = 0,
    [property: JsonIgnore] ScreenViewRotation Rotation = ScreenViewRotation.Identity,
    [property: JsonIgnore] int EffectiveDpiX = 96,
    [property: JsonIgnore] int EffectiveDpiY = 96);

internal sealed record ScreenViewStartResult(
    bool Succeeded,
    string Code,
    string Message,
    string? OfferSdp = null,
    string? HostSignature = null,
    IReadOnlyList<RelayIceServer>? IceServers = null,
    DateTimeOffset? TurnExpiresAt = null,
    long? RelayUsageBytes = null,
    DateTimeOffset? RelayUsageCheckedAt = null,
    RelayScreenQuality? RelayScreenQuality = null);

internal sealed record ScreenViewOperationResult(bool Succeeded, string Code, string Message);

internal sealed record ScreenViewScreenshotResult(
    bool Succeeded,
    string Code,
    string Message,
    string? FileName = null,
    ScreenViewScreenshot? Screenshot = null);

internal sealed record ScreenPointerDispatchResult(bool Succeeded, string Code, string Message);

internal sealed record ScreenViewSourcesResult(
    bool Succeeded,
    string Code,
    string Message,
    IReadOnlyList<ScreenViewSource> Sources);

public sealed record ScreenViewPatch(int X, int Y, int Width, int Height, string MimeType, byte[] ImageBytes);

public sealed record ScreenViewVideoSegment(string MimeType, byte[] Bytes, bool Reset);

public sealed record ScreenViewCursorUpdate(
    bool Visible,
    int X,
    int Y,
    int HotSpotX,
    int HotSpotY,
    int Width,
    int Height,
    byte[]? PngBytes);

public sealed record ScreenViewFrame(
    long Sequence,
    int Width,
    int Height,
    bool IsResynchronization,
    IReadOnlyList<ScreenViewPatch> Patches,
    ScreenViewCursorUpdate? Cursor,
    bool HighMotion = false,
    ScreenViewVideoSegment? Video = null);

public sealed record ScreenViewCaptureProfile(
    int MaxWidth,
    int MaxHeight,
    bool HighMotion = false,
    int FramesPerSecond = 30);

public sealed record ScreenViewEncodedFrame(
    byte[] Bytes,
    int Width,
    int Height,
    int FramesPerSecond,
    bool IsKeyFrame,
    ScreenViewCursorUpdate? Cursor = null);

public sealed record ScreenViewScreenshot(Stream Content, long Length, int Width, int Height) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public interface IScreenViewCaptureSource
{
    IReadOnlyList<ScreenViewSource> GetSources();
    Task<ScreenViewEncodedFrame?> CaptureVideoAsync(
        string sourceId,
        ScreenViewCaptureProfile profile,
        int bitrate,
        bool forceKeyFrame,
        CancellationToken cancellationToken) =>
        Task.FromException<ScreenViewEncodedFrame?>(new ScreenViewCaptureException(
            "encoder-unavailable",
            "This screen capture source does not provide WebRTC video."));
    ScreenViewScreenshot CaptureScreenshot(string sourceId, CancellationToken cancellationToken) =>
        throw new ScreenViewCaptureException(
            "screenshot-unavailable",
            "This screen capture source does not provide screenshots.");
    void EndCapture();
}

internal sealed class ScreenViewCaptureException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
