namespace VolturaAir.Host;

internal sealed class UnavailableScreenViewCaptureSource : IScreenViewCaptureSource
{
    public IReadOnlyList<ScreenViewSource> GetSources() => [];

    public void EndCapture() { }
}
