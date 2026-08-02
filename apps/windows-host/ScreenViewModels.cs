namespace VolturaAir.Host;

public sealed record ScreenViewSource(string Id, string Label, int Width, int Height, bool IsPrimary);

internal sealed record ScreenViewStartResult(
    bool Succeeded,
    string Code,
    string Message,
    string? OfferSdp = null,
    string? HostSignature = null);

internal sealed record ScreenViewOperationResult(bool Succeeded, string Code, string Message);

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
    void EndCapture();
}

internal sealed class ScreenViewCaptureException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
