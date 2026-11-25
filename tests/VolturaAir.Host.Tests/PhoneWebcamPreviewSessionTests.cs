using VolturaAir.Host.Features.PhoneWebcam;
using Vortice.MediaFoundation;

namespace VolturaAir.Host.Tests;

public sealed class PhoneWebcamPreviewSessionTests
{
    [Fact]
    public void EndOfStreamIsReportedInsteadOfPollingTheClosedReader()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            PhoneWebcamPreviewSession.ValidateCaptureFlags(SourceReaderFlag.EndOfStream));
        Assert.Equal("The Windows camera stopped sending preview frames.", failure.Message);
    }

    [Fact]
    public void AStreamTickWithoutASampleCanContinue()
    {
        PhoneWebcamPreviewSession.ValidateCaptureFlags(SourceReaderFlag.StreamTick);
    }
}
