using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal sealed class PhoneWebcamAudioStoppedEventArgs(Exception? exception) : EventArgs
{
    internal Exception? Exception { get; } = exception;
}

internal delegate void PhoneWebcamAudioDataAvailableHandler(ReadOnlySpan<byte> data);

internal interface IPhoneWebcamAudioPlayer : IAsyncDisposable
{
    event EventHandler<PhoneWebcamAudioStoppedEventArgs>? Stopped;
    void Play();
    void Stop();
}

internal interface IPhoneWebcamAudioRecorder : IAsyncDisposable
{
    WaveFormat WaveFormat { get; }
    event PhoneWebcamAudioDataAvailableHandler? DataAvailable;
    event EventHandler<PhoneWebcamAudioStoppedEventArgs>? Stopped;
    void Start();
    void Stop();
}

internal interface IPhoneWebcamAudioDeviceFactory
{
    Task<IPhoneWebcamAudioPlayer> CreateCablePlayerAsync(
        IPhoneWebcamAudioTarget target,
        IWaveProvider source);
    Task<IPhoneWebcamAudioRecorder> CreateCableRecorderAsync();
    Task<IPhoneWebcamAudioPlayer> CreateDefaultPlayerAsync(IWaveProvider source);
}

internal sealed class PhoneWebcamAudioDeviceFactory : IPhoneWebcamAudioDeviceFactory
{
    internal static PhoneWebcamAudioDeviceFactory Instance { get; } = new();

    private PhoneWebcamAudioDeviceFactory() { }

    public Task<IPhoneWebcamAudioPlayer> CreateCablePlayerAsync(
        IPhoneWebcamAudioTarget target,
        IWaveProvider source) => CreatePlayerAsync(target.OpenReadyEndpoint(), source);

    public async Task<IPhoneWebcamAudioRecorder> CreateCableRecorderAsync()
    {
        MMDevice endpoint = OpenCableCaptureEndpoint();
        WasapiRecorder? recorder = null;
        try
        {
            recorder = await new WasapiRecorderBuilder()
                .WithDevice(endpoint)
                .WithSharedMode()
                .WithEventSync()
                .WithBufferLength(100)
                .BuildAsync()
                .ConfigureAwait(false);
            return new NAudioPhoneWebcamRecorder(recorder, endpoint);
        }
        catch
        {
            if (recorder is not null)
            {
                await TryDisposeAsync(recorder.DisposeAsync).ConfigureAwait(false);
            }
            endpoint.Dispose();
            throw;
        }
    }

    public Task<IPhoneWebcamAudioPlayer> CreateDefaultPlayerAsync(IWaveProvider source) =>
        CreatePlayerAsync(OpenDefaultPlaybackEndpoint(), source);

    private static async Task<IPhoneWebcamAudioPlayer> CreatePlayerAsync(
        MMDevice endpoint,
        IWaveProvider source)
    {
        WasapiPlayer? player = null;
        try
        {
            player = await new WasapiPlayerBuilder()
                .WithDevice(endpoint)
                .WithSharedMode()
                .WithEventSync()
                .WithLatency(40)
                .BuildAsync()
                .ConfigureAwait(false);
            player.Init(source);
            return new NAudioPhoneWebcamPlayer(player, endpoint);
        }
        catch
        {
            if (player is not null)
            {
                await TryDisposeAsync(player.DisposeAsync).ConfigureAwait(false);
            }
            endpoint.Dispose();
            throw;
        }
    }

    private static MMDevice OpenCableCaptureEndpoint()
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice? selected = null;
        foreach (MMDevice endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            if (selected is null && PhoneWebcamAudioTarget.IsBaseCableIdentity(
                    endpoint.FriendlyName,
                    endpoint.DeviceFriendlyName))
            {
                selected = endpoint;
            }
            else
            {
                endpoint.Dispose();
            }
        }

        return selected ?? throw new InvalidOperationException(
            "CABLE Output is unavailable. Enable VB-CABLE in Windows Sound settings and try again.");
    }

    private static MMDevice OpenDefaultPlaybackEndpoint()
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        if (PhoneWebcamAudioTarget.IsBaseCableIdentity(endpoint.FriendlyName, endpoint.DeviceFriendlyName))
        {
            endpoint.Dispose();
            throw new InvalidOperationException(
                "Choose speakers or headphones as the default Windows output before testing audio.");
        }
        return endpoint;
    }

    private static async ValueTask TryDisposeAsync(Func<ValueTask> dispose)
    {
        try
        {
            await dispose().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }

    private sealed class NAudioPhoneWebcamPlayer : IPhoneWebcamAudioPlayer
    {
        private readonly WasapiPlayer _player;
        private readonly MMDevice _endpoint;
        private int _disposed;

        internal NAudioPhoneWebcamPlayer(WasapiPlayer player, MMDevice endpoint)
        {
            _player = player;
            _endpoint = endpoint;
            _player.PlaybackStopped += OnPlaybackStopped;
        }

        public event EventHandler<PhoneWebcamAudioStoppedEventArgs>? Stopped;
        public void Play() => _player.Play();
        public void Stop() => _player.Stop();

        private void OnPlaybackStopped(object? sender, StoppedEventArgs args) =>
            Stopped?.Invoke(this, new PhoneWebcamAudioStoppedEventArgs(args.Exception));

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _player.PlaybackStopped -= OnPlaybackStopped;
            try
            {
                await _player.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _endpoint.Dispose();
            }
        }
    }

    private sealed class NAudioPhoneWebcamRecorder : IPhoneWebcamAudioRecorder
    {
        private readonly WasapiRecorder _recorder;
        private readonly MMDevice _endpoint;
        private int _disposed;

        internal NAudioPhoneWebcamRecorder(WasapiRecorder recorder, MMDevice endpoint)
        {
            _recorder = recorder;
            _endpoint = endpoint;
            _recorder.DataAvailable += OnDataAvailable;
            _recorder.RecordingStopped += OnRecordingStopped;
        }

        public WaveFormat WaveFormat => _recorder.WaveFormat;
        public event PhoneWebcamAudioDataAvailableHandler? DataAvailable;
        public event EventHandler<PhoneWebcamAudioStoppedEventArgs>? Stopped;
        public void Start() => _recorder.StartRecording();
        public void Stop() => _recorder.StopRecording();

        private void OnDataAvailable(
            ReadOnlySpan<byte> data,
            AudioClientBufferFlags flags,
            long devicePosition,
            long qpcPosition) => DataAvailable?.Invoke(data);

        private void OnRecordingStopped(object? sender, StoppedEventArgs args) =>
            Stopped?.Invoke(this, new PhoneWebcamAudioStoppedEventArgs(args.Exception));

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _recorder.DataAvailable -= OnDataAvailable;
            _recorder.RecordingStopped -= OnRecordingStopped;
            try
            {
                await _recorder.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _endpoint.Dispose();
            }
        }
    }
}
