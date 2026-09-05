using System.Diagnostics;

namespace VolturaAir.Host;

// Temporary, opt-in development evidence for a connected-but-frozen Screen View.
// No timer, frame contents, input data, session identifiers, or release-build calls.
internal static class ScreenViewDevelopmentTrace
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("VOLTURA_SCREEN_TRACE") == "1";
    private static string _stage = "idle";
    private static long _stageAt;
    private static long _presents;
    private static long _encoded;
    private static long _keyframes;
    private static long _sent;
    private static long _rejected;
    private static long _pictureLoss;
    private static long _nextReport;

    [Conditional("DEBUG")]
    public static void Stage(string stage)
    {
        if (!Enabled) return;
        Volatile.Write(ref _stage, stage);
        Interlocked.Exchange(ref _stageAt, Stopwatch.GetTimestamp());
    }

    [Conditional("DEBUG")]
    public static void Present(bool changed)
    {
        if (Enabled && changed) Interlocked.Increment(ref _presents);
    }

    [Conditional("DEBUG")]
    public static void Encoded(bool keyframe)
    {
        if (!Enabled) return;
        Interlocked.Increment(ref _encoded);
        if (keyframe) Interlocked.Increment(ref _keyframes);
    }

    [Conditional("DEBUG")]
    public static void Sent(bool accepted)
    {
        if (!Enabled) return;
        if (accepted) Interlocked.Increment(ref _sent);
        else Interlocked.Increment(ref _rejected);
    }

    [Conditional("DEBUG")]
    public static void PictureLoss()
    {
        if (Enabled) Interlocked.Increment(ref _pictureLoss);
    }

    // Called by the existing receiver feedback, serialized by the coordinator.
    [Conditional("DEBUG")]
    public static void Report(IAppLogWriter log, ScreenViewReceiverQuality receiver)
    {
        if (!Enabled) return;
        long now = Stopwatch.GetTimestamp();
        if (now < _nextReport) return;
        _nextReport = now + Stopwatch.Frequency * 10;
        log.Write(new AppLogEntry("screen_view", "windows_host", Action: "development_progress",
            Detail: FormattableString.Invariant($"stage={Volatile.Read(ref _stage)} stageMs={Stopwatch.GetElapsedTime(Interlocked.Read(ref _stageAt), now).TotalMilliseconds:F0} presents={Interlocked.Read(ref _presents)} encoded={Interlocked.Read(ref _encoded)} keyframes={Interlocked.Read(ref _keyframes)} sent={Interlocked.Read(ref _sent)} rejected={Interlocked.Read(ref _rejected)} pli={Interlocked.Read(ref _pictureLoss)} decodedDelta={receiver.FramesDecoded} droppedDelta={receiver.FramesDropped} lostDelta={receiver.PacketsLost} freezesDelta={receiver.FreezeCount} fps={receiver.FramesPerSecond:F1}")));
    }
}
