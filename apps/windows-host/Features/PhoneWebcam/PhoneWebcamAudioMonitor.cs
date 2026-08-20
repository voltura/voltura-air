using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal interface IPhoneWebcamAudioMonitor : IAsyncDisposable
{
    void Start();
}

internal sealed class PhoneWebcamAudioMonitor : IPhoneWebcamAudioMonitor
{
    private static readonly TimeSpan MaximumBufferedAudio = TimeSpan.FromMilliseconds(200);
    private readonly Action<string> _reportFailure;
    private readonly MMDevice _captureEndpoint;
    private readonly MMDevice _playbackEndpoint;
    private readonly WasapiCapture _capture;
    private readonly WasapiOut _output;
    private readonly BufferedWaveProvider _buffer;
    private int _started;
    private int _stopping;
    private int _failed;
    private int _disposed;

    internal PhoneWebcamAudioMonitor(Action<string> reportFailure)
    {
        _reportFailure = reportFailure;
        _captureEndpoint = OpenCableCaptureEndpoint();
        try
        {
            _playbackEndpoint = OpenDefaultPlaybackEndpoint();
        }
        catch
        {
            _captureEndpoint.Dispose();
            throw;
        }

        try
        {
            _capture = new WasapiCapture(_captureEndpoint);
            _buffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                BufferDuration = MaximumBufferedAudio,
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
            _output = new WasapiOut(
                _playbackEndpoint,
                AudioClientShareMode.Shared,
                useEventSync: true,
                latency: 40);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Init(_buffer);
        }
        catch
        {
            _capture?.Dispose();
            _output?.Dispose();
            _playbackEndpoint.Dispose();
            _captureEndpoint.Dispose();
            throw;
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        try
        {
            _output.Play();
            _capture.StartRecording();
        }
        catch
        {
            Interlocked.Exchange(ref _stopping, 1);
            TryStop(_capture.StopRecording);
            TryStop(_output.Stop);
            throw;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (Volatile.Read(ref _stopping) != 0 || args.BytesRecorded <= 0)
        {
            return;
        }

        try
        {
            if (_buffer.BufferedDuration >= MaximumBufferedAudio)
            {
                _buffer.ClearBuffer();
            }
            _buffer.AddSamples(args.Buffer, 0, args.BytesRecorded);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportFailure();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (Volatile.Read(ref _stopping) == 0)
        {
            ReportFailure();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
    {
        if (Volatile.Read(ref _stopping) == 0)
        {
            ReportFailure();
        }
    }

    private void ReportFailure()
    {
        if (Interlocked.Exchange(ref _failed, 1) == 0)
        {
            _reportFailure("Audio test stopped because Windows could not continue monitoring CABLE Output.");
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        Interlocked.Exchange(ref _stopping, 1);
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _output.PlaybackStopped -= OnPlaybackStopped;
        TryStop(_capture.StopRecording);
        TryStop(_output.Stop);
        try { _capture.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        try { _output.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        try { _playbackEndpoint.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        try { _captureEndpoint.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        return ValueTask.CompletedTask;
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

    private static void TryStop(Action stop)
    {
        try
        {
            stop();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }
}
