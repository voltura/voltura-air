using System.Text.Json;
using System.Globalization;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class AudioMessageRouterTests
{
    [Fact]
    public void ToggleMuteReturnsUpdatedAudioState()
    {
        var audio = new FakeAudioController(new AudioState(42, false));

        Assert.True(AudioMessageRouter.TryHandle(Parse("""{ "type": "audio.mute.toggle" }"""), audio, out var state));

        Assert.Equal(new AudioState(42, true), state);
    }

    [Theory]
    [InlineData(-4, 0)]
    [InlineData(73.6, 74)]
    [InlineData(125, 100)]
    public void SetVolumeClampsAndUnmutes(double requestedVolume, int expectedVolume)
    {
        var audio = new FakeAudioController(new AudioState(42, true));

        var json = $$"""{ "type": "audio.volume.set", "volume": {{requestedVolume.ToString(CultureInfo.InvariantCulture)}} }""";

        Assert.True(AudioMessageRouter.TryHandle(Parse(json), audio, out var state));

        Assert.Equal(new AudioState(expectedVolume, false), state);
    }

    [Fact]
    public void TryGetStateReturnsHeartbeatAudioState()
    {
        var audio = new FakeAudioController(new AudioState(67, false));

        Assert.Equal(new AudioState(67, false), AudioMessageRouter.TryGetState(audio));
    }

    [Fact]
    public void AudioFailuresAreHandledWithoutThrowing()
    {
        var audio = new FakeAudioController(new AudioState(67, false)) { ThrowOnAccess = true };

        Assert.True(AudioMessageRouter.TryHandle(Parse("""{ "type": "audio.mute.toggle" }"""), audio, out var state));

        Assert.Null(state);
        Assert.Null(AudioMessageRouter.TryGetState(audio));
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FakeAudioController : ISystemAudioController
    {
        private AudioState _state;

        public FakeAudioController(AudioState state)
        {
            _state = state;
        }

        public bool ThrowOnAccess { get; set; }

        public AudioState GetState()
        {
            ThrowIfNeeded();
            return _state;
        }

        public AudioState ToggleMute()
        {
            ThrowIfNeeded();
            _state = _state with { Muted = !_state.Muted };
            return _state;
        }

        public AudioState SetVolume(int volume)
        {
            ThrowIfNeeded();
            _state = new AudioState(volume, false);
            return _state;
        }

        private void ThrowIfNeeded()
        {
            if (ThrowOnAccess)
            {
                throw new InvalidOperationException("Audio unavailable.");
            }
        }
    }
}
