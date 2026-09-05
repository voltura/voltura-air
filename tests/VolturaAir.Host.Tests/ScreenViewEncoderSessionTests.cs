using Vortice.Direct3D11;

namespace VolturaAir.Host.Tests;

public sealed class ScreenViewEncoderSessionTests
{
    [Fact]
    public void StaticOrPacedOutFramesDoNotConsumeCoalescedRecoveryRequests()
    {
        var encoder = new FakeEncoder();
        int created = 0;
        using var session = new ScreenViewEncoderSession((_, _, _, _) => { created++; return encoder; });
        session.Configure(1280, 720, 30, 4_000_000, false);
        session.Encode(null!); // Fake encoder does not touch a GPU surface.
        Assert.False(session.KeyFramePending);
        session.Configure(1280, 720, 30, 4_000_000, true);
        session.Configure(1280, 720, 30, 4_000_000, true);
        session.Configure(1280, 720, 30, 4_000_000, false);
        Assert.True(session.KeyFramePending);
        Assert.Equal(0, encoder.Requests);
        Assert.True(session.Encode(null!).IsKeyFrame);
        Assert.False(session.KeyFramePending);
        Assert.Equal(1, encoder.Requests);
        Assert.Equal(1, created);
    }

    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(true, true, 2)]
    public void RecoveryUsesReplacementOnlyForRejectedOrIgnoredControls(bool supported, bool ignored, int expectedCreations)
    {
        var encoders = new List<FakeEncoder>();
        using var session = new ScreenViewEncoderSession((_, _, _, _) =>
        {
            var encoder = new FakeEncoder { SupportsKeyframes = supported, IgnoresKeyframes = ignored };
            encoders.Add(encoder);
            return encoder;
        });
        session.Configure(1280, 720, 30, 4_000_000, false);
        session.Encode(null!);
        session.Configure(1280, 720, 30, 4_000_000, true);
        session.Encode(null!);
        if (session.KeyFramePending) session.Encode(null!);
        Assert.False(session.KeyFramePending);
        Assert.Equal(expectedCreations, encoders.Count);
        Assert.Equal(expectedCreations == 2, encoders[0].Disposed);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public void BitrateOnlyChangeReusesEncoderWhenSupported(bool supported, int expected)
    {
        var encoders = new List<FakeEncoder>();
        using var session = new ScreenViewEncoderSession((_, _, _, _) =>
        {
            var encoder = new FakeEncoder { SupportsBitrate = supported };
            encoders.Add(encoder);
            return encoder;
        });
        session.Configure(1280, 720, 30, 4_000_000, false);
        session.Encode(null!);
        session.Configure(1280, 720, 30, 2_000_000, false);
        session.Encode(null!);
        Assert.Equal(expected, encoders.Count);
        Assert.Equal(!supported, encoders[0].Disposed);
    }

    [Theory]
    [InlineData(640, 360, 30)]
    [InlineData(1280, 720, 15)]
    public void DimensionOrFrameRateChangesDisposeAndReplace(int width, int height, int fps)
    {
        var old = new FakeEncoder();
        var next = new FakeEncoder();
        int created = 0;
        using var session = new ScreenViewEncoderSession((_, _, _, _) => ++created == 1 ? old : next);
        session.Configure(1280, 720, 30, 4_000_000, false);
        session.Encode(null!);
        session.Configure(width, height, fps, 4_000_000, false);
        Assert.True(old.Disposed);
        Assert.True(session.KeyFramePending);
        session.Encode(null!);
        Assert.Equal(2, created);
    }

    [Fact]
    public void FailedCreationKeepsRecoveryPendingAndDisposedSessionCannotRestart()
    {
        using var session = new ScreenViewEncoderSession((_, _, _, _) => throw new InvalidOperationException("injected"));
        session.Configure(1280, 720, 30, 4_000_000, true);
        Assert.Throws<InvalidOperationException>(() => session.Encode(null!));
        Assert.True(session.KeyFramePending);
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.Encode(null!));
    }

    private sealed class FakeEncoder : IScreenViewFrameEncoder
    {
        public bool SupportsKeyframes { get; init; } = true;
        public bool SupportsBitrate { get; init; } = true;
        public bool IgnoresKeyframes { get; init; }
        public bool Disposed { get; private set; }
        public int Requests { get; private set; }
        private bool _keyframe = true;
        public bool TryRequestKeyFrame()
        {
            Requests++;
            if (SupportsKeyframes && !IgnoresKeyframes) _keyframe = true;
            return SupportsKeyframes;
        }
        public bool TrySetBitrate(int bitrate) => SupportsBitrate;
        public ScreenViewEncodedFrame Encode(ID3D11Texture2D surface)
        {
            bool keyframe = _keyframe;
            _keyframe = false;
            return new([1], 1280, 720, 30, keyframe);
        }
        public void Dispose() => Disposed = true;
    }
}
