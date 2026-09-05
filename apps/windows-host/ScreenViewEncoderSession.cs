using Vortice.Direct3D11;

namespace VolturaAir.Host;

// Capture-session subowner: separates encoder recovery from DXGI frame acquisition so
// timeout/pacing cannot consume a recovery request and native failures can be tested.
internal sealed class ScreenViewEncoderSession : IDisposable
{
    private readonly Func<int, int, int, int, IScreenViewFrameEncoder> _create;
    private IScreenViewFrameEncoder? _encoder;
    private int _width, _height, _fps, _bitrate;
    private bool _keyFrameControlIgnored;
    private bool _disposed;

    public ScreenViewEncoderSession(ID3D11Device device) : this(
        (width, height, fps, bitrate) => new ScreenViewHardwareH264Encoder(device, width, height, fps, bitrate))
    {
    }

    internal ScreenViewEncoderSession(Func<int, int, int, int, IScreenViewFrameEncoder> create) => _create = create;

    public bool HasEncoder => _encoder is not null;
    public bool KeyFramePending { get; private set; } = true;

    public void Configure(int width, int height, int fps, int bitrate, bool requestKeyFrame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        KeyFramePending |= requestKeyFrame;
        if (width != _width || height != _height || fps != _fps ||
            (bitrate != _bitrate && _encoder is not null && !_encoder.TrySetBitrate(bitrate)))
        {
            _encoder?.Dispose();
            _encoder = null;
            _keyFrameControlIgnored = false;
            KeyFramePending = true;
        }
        (_width, _height, _fps, _bitrate) = (width, height, fps, bitrate);
    }

    public ScreenViewEncodedFrame Encode(ID3D11Texture2D surface)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (KeyFramePending && _encoder is not null &&
            (_keyFrameControlIgnored || !_encoder.TryRequestKeyFrame()))
        {
            _encoder.Dispose();
            _encoder = null;
        }
        bool created = _encoder is null;
        _encoder ??= _create(_width, _height, _fps, _bitrate);
        ScreenViewEncodedFrame frame = _encoder.Encode(surface);
        if (KeyFramePending)
        {
            if (created && !frame.IsKeyFrame)
                throw new ScreenViewCaptureException("encoder-failed", "The screen encoder did not provide a recovery frame.");
            _keyFrameControlIgnored = !frame.IsKeyFrame;
            KeyFramePending = !frame.IsKeyFrame;
        }
        return frame;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _encoder?.Dispose();
        _encoder = null;
    }
}
