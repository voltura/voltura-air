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
    private readonly IPhoneWebcamAudioRecorder _capture;
    private readonly IPhoneWebcamAudioPlayer _output;
    private readonly BufferedWaveProvider _buffer;
    private int _started;
    private int _stopping;
    private int _failed;
    private int _disposed;

    private PhoneWebcamAudioMonitor(
        Action<string> reportFailure,
        IPhoneWebcamAudioRecorder capture,
        IPhoneWebcamAudioPlayer output,
        BufferedWaveProvider buffer)
    {
        _reportFailure = reportFailure;
        _capture = capture;
        _output = output;
        _buffer = buffer;
        _capture.DataAvailable += OnDataAvailable;
        _capture.Stopped += OnRecordingStopped;
        _output.Stopped += OnPlaybackStopped;
    }

    internal static async Task<IPhoneWebcamAudioMonitor> CreateAsync(
        Action<string> reportFailure,
        IPhoneWebcamAudioDeviceFactory? deviceFactory = null)
    {
        IPhoneWebcamAudioDeviceFactory factory = deviceFactory ?? PhoneWebcamAudioDeviceFactory.Instance;
        IPhoneWebcamAudioRecorder capture = await factory.CreateCableRecorderAsync().ConfigureAwait(false);
        var buffer = new BufferedWaveProvider(capture.WaveFormat, MaximumBufferedAudio)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
        try
        {
            IPhoneWebcamAudioPlayer output = await factory.CreateDefaultPlayerAsync(buffer).ConfigureAwait(false);
            return new PhoneWebcamAudioMonitor(reportFailure, capture, output, buffer);
        }
        catch
        {
            await TryDisposeAsync(capture).ConfigureAwait(false);
            throw;
        }
    }

    internal static IPhoneWebcamAudioMonitor CreateForTest(
        Action<string> reportFailure,
        IPhoneWebcamAudioRecorder capture,
        IPhoneWebcamAudioPlayer output)
    {
        var buffer = new BufferedWaveProvider(capture.WaveFormat, MaximumBufferedAudio)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
        return new PhoneWebcamAudioMonitor(reportFailure, capture, output, buffer);
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
            _capture.Start();
        }
        catch
        {
            Interlocked.Exchange(ref _stopping, 1);
            TryStop(_capture.Stop);
            TryStop(_output.Stop);
            throw;
        }
    }

    private void OnDataAvailable(ReadOnlySpan<byte> data)
    {
        if (Volatile.Read(ref _stopping) != 0 || data.IsEmpty)
        {
            return;
        }

        try
        {
            if (_buffer.BufferedDuration >= MaximumBufferedAudio)
            {
                _buffer.ClearBuffer();
            }
            _buffer.AddSamples(data);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportFailure();
        }
    }

    private void OnRecordingStopped(object? sender, PhoneWebcamAudioStoppedEventArgs args)
    {
        if (Volatile.Read(ref _stopping) == 0)
        {
            ReportFailure();
        }
    }

    private void OnPlaybackStopped(object? sender, PhoneWebcamAudioStoppedEventArgs args)
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _stopping, 1);
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Stopped -= OnRecordingStopped;
        _output.Stopped -= OnPlaybackStopped;
        TryStop(_capture.Stop);
        TryStop(_output.Stop);
        await TryDisposeAsync(_capture).ConfigureAwait(false);
        await TryDisposeAsync(_output).ConfigureAwait(false);
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

    private static async ValueTask TryDisposeAsync(IAsyncDisposable disposable)
    {
        try
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }
}
