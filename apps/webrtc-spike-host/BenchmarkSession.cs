using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using QRCoder;
using SharpGen.Runtime;
using Vortice.MediaFoundation;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using static Vortice.MediaFoundation.MediaFactory;

namespace WebRtcSpike.Host;

internal static class BenchmarkSession
{
    private const string CameraName = "Voltura Air Webcam";
    internal const int PatternIntervalMilliseconds = 33;
    internal const double MinimumEffectiveFps = 28;
    private static readonly TimeSpan MeasurementDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AlignmentTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<BenchmarkResult> RunAsync(
        string transport,
        string outputPath,
        Func<Exception?> getPipelineFailure,
        Func<(int Width, int Height)> getSourceDimensions,
        CancellationToken cancellationToken)
    {
        using var pattern = new BenchmarkPatternWindow();
        pattern.Start();
        Console.WriteLine("Benchmark pattern opened. Point the iPhone camera at the QR pattern; measurement starts when it is decoded.");

        BenchmarkResult result = await Task.Run(
            () => Capture(transport, getPipelineFailure, getSourceDimensions, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        EnsurePipelineHealthy(getPipelineFailure);
        string json = JsonSerializer.Serialize(result, JsonOptions);
        string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(outputDirectory)) Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Benchmark written: {Path.GetFullPath(outputPath)}");
        return result;
    }

    private static BenchmarkResult Capture(
        string transport,
        Func<Exception?> getPipelineFailure,
        Func<(int Width, int Height)> getSourceDimensions,
        CancellationToken cancellationToken)
    {
        MFStartup(true).CheckError();
        try
        {
            using IMFActivateCollection devices = MFEnumVideoDeviceSources();
            IMFActivate activate = devices.FirstOrDefault(device =>
                    device.FriendlyName.StartsWith(CameraName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"{CameraName} is not installed.");
            using IMFMediaSource source = activate.ActivateObject<IMFMediaSource>();
            try
            {
                using IMFAttributes attributes = MFCreateAttributes(1);
                attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, false).CheckError();
                using IMFSourceReader reader = MFCreateSourceReaderFromMediaSource(source, attributes);
                using IMFMediaType requestedType = CreateCaptureType();
                reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, requestedType);
                return CaptureFrames(reader, transport, getPipelineFailure, getSourceDimensions, cancellationToken);
            }
            finally
            {
                try
                {
                    source.Shutdown();
                }
                catch (SharpGenException exception) when (exception.ResultCode == ResultCode.Shutdown)
                {
                    // A timed-out or stopped source may already be shut down.
                }
            }
        }
        finally
        {
            MFShutdown();
        }
    }

    private static BenchmarkResult CaptureFrames(
        IMFSourceReader reader,
        string transport,
        Func<Exception?> getPipelineFailure,
        Func<(int Width, int Height)> getSourceDimensions,
        CancellationToken cancellationToken)
    {
        BarcodeReaderGeneric qrReader = CreateQrReader();
        byte[] reducedLuma = GC.AllocateUninitializedArray<byte>(
            MediaFoundationH264Decoder.Width / 2 * MediaFoundationH264Decoder.Height / 2);
        var latencies = new List<double>();
        Stopwatch alignment = Stopwatch.StartNew();
        Stopwatch? measurement = null;
        TimeSpan startingCpu = default;
        int frames = 0;
        int decodedPatterns = 0;
        int drops = 0;
        bool fullHdThroughout = true;
        int sourceWidth = 0;
        int sourceHeight = 0;
        long? lastPatternSequence = null;
        long nextRead = Stopwatch.GetTimestamp();
        long readInterval = Stopwatch.Frequency / 30;

        while (measurement is null || measurement.Elapsed < MeasurementDuration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsurePipelineHealthy(getPipelineFailure);
            if (measurement is null && alignment.Elapsed >= AlignmentTimeout)
                throw new TimeoutException("The benchmark pattern was not detected within 30 seconds.");

            PaceCapture(ref nextRead, readInterval);

            using IMFSample? sample = reader.ReadSample(
                SourceReaderIndex.FirstVideoStream,
                SourceReaderControlFlag.None,
                out _,
                out SourceReaderFlag flags,
                out _);
            if ((flags & SourceReaderFlag.Error) != 0)
                throw new InvalidOperationException("The benchmark camera consumer reported a capture error.");
            if (sample is null) continue;

            using IMFMediaBuffer buffer = sample.ConvertToContiguousBuffer();
            if (buffer.CurrentLength < MediaFoundationH264Decoder.FrameBytes)
                throw new InvalidOperationException("The benchmark consumer received a truncated NV12 frame.");
            buffer.Lock(out nint pointer, out _, out _);
            try
            {
                DownsampleLuma(pointer, reducedLuma);
            }
            finally
            {
                buffer.Unlock();
            }
            ZXing.Result? decoded = qrReader.Decode(new RGBLuminanceSource(
                reducedLuma,
                MediaFoundationH264Decoder.Width / 2,
                MediaFoundationH264Decoder.Height / 2,
                RGBLuminanceSource.BitmapFormat.Gray8));
            if (!TryReadPattern(decoded?.Text, out long displayedTicks, out long patternSequence)) continue;

            if (measurement is null)
            {
                measurement = Stopwatch.StartNew();
                startingCpu = Process.GetCurrentProcess().TotalProcessorTime;
            }
            if (!TryAccountPattern(patternSequence, ref lastPatternSequence, ref drops)) continue;

            ++frames;
            ++decodedPatterns;
            (sourceWidth, sourceHeight) = getSourceDimensions();
            fullHdThroughout &= IsFullHd(sourceWidth, sourceHeight);
            double latency = (Stopwatch.GetTimestamp() - displayedTicks) * 1000d / Stopwatch.Frequency;
            if (latency is >= 0 and < 10_000) latencies.Add(latency);
        }

        if (latencies.Count == 0)
            throw new InvalidOperationException("The benchmark captured frames but decoded no valid timestamps.");
        latencies.Sort();
        double seconds = measurement.Elapsed.TotalSeconds;
        TimeSpan cpu = Process.GetCurrentProcess().TotalProcessorTime - startingCpu;
        return new BenchmarkResult(
            transport,
            sourceWidth,
            sourceHeight,
            fullHdThroughout,
            frames,
            frames / seconds,
            Percentile(latencies, 0.50),
            Percentile(latencies, 0.95),
            drops,
            decodedPatterns,
            cpu.TotalMilliseconds / measurement.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
            Process.GetCurrentProcess().PeakWorkingSet64,
            DateTimeOffset.UtcNow);
    }

    internal static void EnsurePipelineHealthy(Func<Exception?> getPipelineFailure)
    {
        Exception? failure = getPipelineFailure();
        if (failure is not null)
            throw new InvalidOperationException("The video pipeline failed; benchmark evidence was not written.", failure);
    }

    private static void PaceCapture(ref long nextRead, long readInterval)
    {
        long now = Stopwatch.GetTimestamp();
        if (nextRead > now)
        {
            TimeSpan delay = TimeSpan.FromSeconds((double)(nextRead - now) / Stopwatch.Frequency);
            Thread.Sleep(delay);
            now = Stopwatch.GetTimestamp();
        }
        nextRead = now - nextRead > readInterval ? now + readInterval : nextRead + readInterval;
    }

    private static unsafe void DownsampleLuma(nint source, Span<byte> destination)
    {
        int destinationWidth = MediaFoundationH264Decoder.Width / 2;
        byte* input = (byte*)source;
        for (int y = 0; y < MediaFoundationH264Decoder.Height / 2; ++y)
        {
            int sourceOffset = y * 2 * MediaFoundationH264Decoder.Width;
            int destinationOffset = y * destinationWidth;
            for (int x = 0; x < destinationWidth; ++x)
                destination[destinationOffset + x] = input[sourceOffset + x * 2];
        }
    }

    internal static bool TryReadPattern(string? value, out long timestamp, out long sequence)
    {
        timestamp = 0;
        sequence = 0;
        string[] parts = value?.Split(':') ?? [];
        return parts.Length == 3 &&
               parts[0] == "VA1" &&
               long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out timestamp) &&
               long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out sequence);
    }

    internal static bool TryAccountPattern(long sequence, ref long? lastSequence, ref int drops)
    {
        if (lastSequence is not null && sequence <= lastSequence.Value) return false;
        if (lastSequence is not null)
            drops = checked(drops + checked((int)Math.Min(int.MaxValue, sequence - lastSequence.Value - 1)));
        lastSequence = sequence;
        return true;
    }

    internal static bool MeetsPassCriteria(BenchmarkResult result) =>
        result.P95LatencyMilliseconds <= 300 &&
        result.EffectiveFps >= MinimumEffectiveFps &&
        result.FullHdThroughout &&
        IsFullHd(result.SourceWidth, result.SourceHeight);

    internal static bool IsFullHd(int width, int height) =>
        width == 1920 && height == 1080 || width == 1080 && height == 1920;

    internal static BarcodeReaderGeneric CreateQrReader() => new()
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            PossibleFormats = [BarcodeFormat.QR_CODE],
            TryHarder = true
        }
    };

    internal static System.Drawing.Bitmap CreatePatternBitmap(long timestamp, long sequence)
    {
        string payload = FormattableString.Invariant($"VA1:{timestamp}:{sequence}");
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.L);
        using var code = new QRCode(data);
        return code.GetGraphic(18, System.Drawing.Color.Black, System.Drawing.Color.White, drawQuietZones: true);
    }

    private static double Percentile(List<double> sorted, double percentile)
    {
        int index = Math.Clamp((int)Math.Ceiling(sorted.Count * percentile) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }

    private static IMFMediaType CreateCaptureType()
    {
        IMFMediaType type = MFCreateMediaType();
        try
        {
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12).CheckError();
            MFSetAttributeSize(
                type,
                MediaTypeAttributeKeys.FrameSize,
                MediaFoundationH264Decoder.Width,
                MediaFoundationH264Decoder.Height).CheckError();
            MFSetAttributeRatio(type, MediaTypeAttributeKeys.FrameRate, 30, 1).CheckError();
            return type;
        }
        catch
        {
            type.Dispose();
            throw;
        }
    }
}

internal sealed record BenchmarkResult(
    string Transport,
    int SourceWidth,
    int SourceHeight,
    bool FullHdThroughout,
    int FrameCount,
    double EffectiveFps,
    double P50LatencyMilliseconds,
    double P95LatencyMilliseconds,
    int Drops,
    int DecodedPatterns,
    double CpuPercent,
    long PeakMemoryBytes,
    DateTimeOffset CompletedAtUtc);

internal sealed class BenchmarkPatternWindow : IDisposable
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private System.Windows.Forms.Form? _form;
    private long _sequence;

    internal BenchmarkPatternWindow()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Webcam benchmark pattern"
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    internal void Start()
    {
        _thread.Start();
        _ready.Task.Wait(TimeSpan.FromSeconds(5));
        if (!_ready.Task.IsCompletedSuccessfully)
            throw new InvalidOperationException("The benchmark pattern window did not start.");
    }

    private void Run()
    {
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        using var picture = new System.Windows.Forms.PictureBox
        {
            BackColor = System.Drawing.Color.White,
            Dock = System.Windows.Forms.DockStyle.Fill,
            SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom
        };
        using var label = new System.Windows.Forms.Label
        {
            BackColor = System.Drawing.Color.Black,
            Dock = System.Windows.Forms.DockStyle.Bottom,
            Font = new System.Drawing.Font("Segoe UI", 18, System.Drawing.FontStyle.Bold),
            ForeColor = System.Drawing.Color.White,
            Height = 56,
            Text = "Point the iPhone camera at this entire QR pattern",
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        };
        using var form = new System.Windows.Forms.Form
        {
            BackColor = System.Drawing.Color.White,
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
            Text = "Voltura Air webcam latency benchmark",
            TopMost = true,
            WindowState = System.Windows.Forms.FormWindowState.Maximized
        };
        _form = form;
        form.Controls.Add(picture);
        form.Controls.Add(label);
        using var timer = new System.Windows.Forms.Timer { Interval = BenchmarkSession.PatternIntervalMilliseconds };
        timer.Tick += (_, _) =>
        {
            System.Drawing.Bitmap next = BenchmarkSession.CreatePatternBitmap(
                Stopwatch.GetTimestamp(),
                Interlocked.Increment(ref _sequence));
            System.Drawing.Image? old = picture.Image;
            picture.Image = next;
            old?.Dispose();
        };
        form.Shown += (_, _) =>
        {
            timer.Start();
            _ready.TrySetResult();
        };
        System.Windows.Forms.Application.Run(form);
        picture.Image?.Dispose();
        _form = null;
    }

    public void Dispose()
    {
        System.Windows.Forms.Form? form = _form;
        if (form is not null && !form.IsDisposed)
        {
            try { form.BeginInvoke(form.Close); }
            catch (InvalidOperationException) { }
        }
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(2));
    }
}
