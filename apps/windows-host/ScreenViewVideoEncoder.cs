using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace VolturaAir.Host;

internal sealed class ScreenViewVideoEncoder : IDisposable
{
    private static readonly Guid H264VideoFormat = new(0x34363248, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);
    private readonly MemoryStream _output = new();
    private readonly MFByteStream _byteStream;
    private readonly IMFMediaSink _mediaSink;
    private readonly IMFSinkWriter _writer;
    private readonly IMFDXGIDeviceManager? _deviceManager;
    private readonly int _streamIndex;
    private readonly int _width;
    private readonly int _height;
    private readonly int _framesPerSecond;
    private readonly long _frameDuration;
    private readonly int _framesPerFragment;
    private readonly Dictionary<uint, ulong> _decodeTimes = [];
    private long _nextTimestamp;
    private int _rawReadOffset;
    private int _frameCount;
    private bool _finalized;
    private string _mimeType = "video/mp4; codecs=\"avc1.42E01E\"";

    public ScreenViewVideoEncoder(int width, int height, int framesPerSecond, int bitrate, ID3D11Device? d3dDevice = null)
    {
        if (width <= 0 || height <= 0 || framesPerSecond <= 0 || bitrate <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        _width = width & ~1;
        _height = height & ~1;
        _framesPerSecond = framesPerSecond;
        _frameDuration = 10_000_000L / framesPerSecond;
        _framesPerFragment = Math.Max(1, (int)Math.Round(framesPerSecond / 10d));
        _deviceManager = null;
        MFStartup(true).CheckError();
        try
        {
            using IMFMediaType outputType = CreateVideoType(H264VideoFormat, _width, _height, framesPerSecond, bitrate);
            _byteStream = new MFByteStream(_output);
            MFCreateFMPEG4MediaSink(_byteStream, outputType, null, out _mediaSink).CheckError();
            using IMFAttributes attributes = MFCreateAttributes(3);
            attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u).CheckError();
            attributes.Set(SinkWriterAttributeKeys.DisableThrottling, 1u).CheckError();
            attributes.Set(SinkWriterAttributeKeys.LowLatency, true).CheckError();
            if (d3dDevice is not null)
            {
                _deviceManager = MFCreateDXGIDeviceManager();
                _deviceManager.ResetDevice(d3dDevice).CheckError();
                attributes.Set(SinkWriterAttributeKeys.D3DManager, _deviceManager).CheckError();
            }
            _writer = MFCreateSinkWriterFromMediaSink(_mediaSink, attributes);
            _streamIndex = 0;
            using IMFMediaType inputType = CreateVideoType(
                d3dDevice is null ? VideoFormatGuids.Rgb32 : VideoFormatGuids.NV12,
                _width,
                _height,
                framesPerSecond,
                0);
            _writer.SetInputMediaType(_streamIndex, inputType, null);
            _writer.BeginWriting();
        }
        catch
        {
            MFShutdown();
            throw;
        }
    }

    public int Width => _width;
    public int Height => _height;
    public int FramesPerSecond => _framesPerSecond;
    public bool SupportsGpuSurfaces => _deviceManager is not null;
    public bool ShouldRestart => _frameCount >= _framesPerSecond * 30 || _output.Length >= 16 * 1024 * 1024;
    public string MimeType => _mimeType;

    public byte[] Encode(Bitmap source)
    {
        ObjectDisposedException.ThrowIf(_finalized, this);
        using var scaled = new Bitmap(_width, _height, PixelFormat.Format32bppRgb);
        using (Graphics graphics = Graphics.FromImage(scaled))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            graphics.DrawImage(source, new Rectangle(0, 0, _width, _height));
        }

        int byteCount = checked(_width * _height * 4);
        using IMFMediaBuffer buffer = MFCreateMemoryBuffer(byteCount);
        buffer.Lock(out IntPtr destination, out _, out _);
        try
        {
            BitmapData data = scaled.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                for (int row = 0; row < _height; row++)
                {
                    IntPtr sourceRow = data.Scan0 + row * data.Stride;
                    IntPtr destinationRow = destination + (_height - row - 1) * _width * 4;
                    unsafe
                    {
                        Buffer.MemoryCopy(sourceRow.ToPointer(), destinationRow.ToPointer(), _width * 4, _width * 4);
                    }
                }
            }
            finally
            {
                scaled.UnlockBits(data);
            }
        }
        finally
        {
            buffer.Unlock();
        }
        buffer.CurrentLength = byteCount;

        return WriteSample(buffer);
    }

    public byte[] Encode(ID3D11Texture2D source)
    {
        ObjectDisposedException.ThrowIf(_finalized, this);
        if (_deviceManager is null)
            throw new InvalidOperationException("The screen video encoder was not configured for GPU surfaces.");
        Texture2DDescription description = source.Description;
        if (description.Width != _width || description.Height != _height)
            throw new ArgumentException("The GPU surface dimensions do not match the video stream.", nameof(source));

        using IMFMediaBuffer buffer = MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, source, 0, false);
        buffer.CurrentLength = checked(_width * _height * 3 / 2);
        return WriteSample(buffer);
    }

    private byte[] WriteSample(IMFMediaBuffer buffer)
    {
        using IMFSample sample = MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = _nextTimestamp;
        sample.SampleDuration = _frameDuration;
        _nextTimestamp += _frameDuration;
        _writer.WriteSample(_streamIndex, sample);
        _frameCount++;
        if (_frameCount % _framesPerFragment == 0) _writer.NotifyEndOfSegment(_streamIndex);
        byte[] output = DrainOutput();
        _mimeType = ReadMimeType(output) ?? _mimeType;
        return output;
    }

    public byte[] Finish()
    {
        if (_finalized) return [];
        _writer.Finalize();
        _finalized = true;
        return DrainOutput();
    }

    private byte[] DrainOutput()
    {
        if (!_output.TryGetBuffer(out ArraySegment<byte> rawBuffer))
            throw new InvalidOperationException("Screen video output buffer is unavailable.");
        ReadOnlySpan<byte> source = rawBuffer.AsSpan(0, checked((int)_output.Length));
        using var output = new MemoryStream();
        while (TryReadBox(source, _rawReadOffset, out int size, out int headerSize) && _rawReadOffset + size <= source.Length)
        {
            ReadOnlySpan<byte> box = source.Slice(_rawReadOffset, size);
            if (headerSize == 8 && box.Slice(4, 4).SequenceEqual("moof"u8))
                output.Write(RewriteMoofForMediaSource(box, _rawReadOffset, _decodeTimes));
            else
                output.Write(box);
            _rawReadOffset += size;
        }
        return output.ToArray();
    }

    private static byte[] RewriteMoofForMediaSource(
        ReadOnlySpan<byte> moof,
        int absoluteOffset,
        Dictionary<uint, ulong> decodeTimes)
    {
        var removals = new List<int>();
        var insertions = new List<(int Offset, byte[] Bytes)>();
        var sizePatches = new List<(int Offset, int Size)>();
        var flagPatches = new List<(int Offset, uint Value)>();
        var dataOffsetPatches = new List<(int Offset, long BaseOffset, int OriginalDataOffset)>();

        int childOffset = 8;
        while (TryReadBox(moof, childOffset, out int childSize, out int childHeader) && childOffset + childSize <= moof.Length)
        {
            if (childHeader == 8 && moof.Slice(childOffset + 4, 4).SequenceEqual("traf"u8))
            {
                long? baseOffset = null;
                uint? trackId = null;
                int tfhdEnd = 0;
                bool hasDecodeTime = false;
                ulong fragmentDuration = 0;
                var trunOffsets = new List<(int Offset, int OriginalDataOffset)>();
                int trafEnd = childOffset + childSize;
                int trafChildOffset = childOffset + 8;
                int trafRemovalCount = 0;
                while (TryReadBox(moof, trafChildOffset, out int trafChildSize, out int trafChildHeader) && trafChildOffset + trafChildSize <= trafEnd)
                {
                    ReadOnlySpan<byte> type = moof.Slice(trafChildOffset + 4, 4);
                    if (trafChildHeader == 8 && type.SequenceEqual("tfhd"u8))
                    {
                        uint fullBox = BinaryPrimitives.ReadUInt32BigEndian(moof.Slice(trafChildOffset + 8, 4));
                        uint flags = fullBox & 0x00FF_FFFFu;
                        trackId = BinaryPrimitives.ReadUInt32BigEndian(moof.Slice(trafChildOffset + 12, 4));
                        tfhdEnd = trafChildOffset + trafChildSize;
                        if ((flags & 0x000001u) != 0)
                        {
                            if (trafChildSize < 24) throw new InvalidOperationException("Invalid fragmented MP4 track header.");
                            baseOffset = checked((long)BinaryPrimitives.ReadUInt64BigEndian(moof.Slice(trafChildOffset + 16, 8)));
                            removals.Add(trafChildOffset + 16);
                            trafRemovalCount++;
                            sizePatches.Add((trafChildOffset, trafChildSize - 8));
                            uint rewrittenFlags = (fullBox & 0xFF00_0000u) | ((flags & ~0x000001u) | 0x020000u);
                            flagPatches.Add((trafChildOffset + 8, rewrittenFlags));
                        }
                    }
                    else if (trafChildHeader == 8 && type.SequenceEqual("tfdt"u8))
                    {
                        hasDecodeTime = true;
                    }
                    else if (trafChildHeader == 8 && type.SequenceEqual("trun"u8))
                    {
                        uint fullBox = BinaryPrimitives.ReadUInt32BigEndian(moof.Slice(trafChildOffset + 8, 4));
                        uint flags = fullBox & 0x00FF_FFFFu;
                        if ((flags & 0x000001u) == 0 || trafChildSize < 20)
                            throw new InvalidOperationException("Fragmented MP4 run has no data offset.");
                        trunOffsets.Add((trafChildOffset + 16, BinaryPrimitives.ReadInt32BigEndian(moof.Slice(trafChildOffset + 16, 4))));
                        fragmentDuration = checked(fragmentDuration + ReadTrackRunDuration(moof.Slice(trafChildOffset, trafChildSize), flags));
                    }
                    trafChildOffset += trafChildSize;
                }

                if (baseOffset.HasValue)
                {
                    if (!trackId.HasValue || tfhdEnd == 0) throw new InvalidOperationException("Fragmented MP4 track is incomplete.");
                    int trafInsertedBytes = 0;
                    if (!hasDecodeTime)
                    {
                        ulong decodeTime = decodeTimes.GetValueOrDefault(trackId.Value);
                        insertions.Add((tfhdEnd, CreateDecodeTimeBox(decodeTime)));
                        trafInsertedBytes = 20;
                    }
                    decodeTimes[trackId.Value] = checked(decodeTimes.GetValueOrDefault(trackId.Value) + fragmentDuration);
                    sizePatches.Add((childOffset, childSize - trafRemovalCount * 8 + trafInsertedBytes));
                    foreach ((int offset, int originalDataOffset) in trunOffsets)
                        dataOffsetPatches.Add((offset, baseOffset.Value, originalDataOffset));
                }
            }
            childOffset += childSize;
        }

        if (removals.Count == 0) return moof.ToArray();
        removals.Sort();
        insertions.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        int removedBytes = checked(removals.Count * 8);
        int totalInsertedBytes = insertions.Sum(insertion => insertion.Bytes.Length);
        sizePatches.Add((0, moof.Length - removedBytes + totalInsertedBytes));
        byte[] rewritten = new byte[moof.Length - removedBytes + totalInsertedBytes];
        int sourceOffset = 0;
        int destinationOffset = 0;
        int removalIndex = 0;
        int insertionIndex = 0;
        while (sourceOffset < moof.Length)
        {
            while (insertionIndex < insertions.Count && insertions[insertionIndex].Offset == sourceOffset)
            {
                byte[] insertion = insertions[insertionIndex++].Bytes;
                insertion.CopyTo(rewritten, destinationOffset);
                destinationOffset += insertion.Length;
            }
            if (removalIndex < removals.Count && removals[removalIndex] == sourceOffset)
            {
                sourceOffset += 8;
                removalIndex++;
                continue;
            }
            rewritten[destinationOffset++] = moof[sourceOffset++];
        }

        int MapOffset(int original) => original
            - removals.Count(removal => removal < original) * 8
            + insertions.Where(insertion => insertion.Offset <= original).Sum(insertion => insertion.Bytes.Length);
        foreach ((int patchOffset, int size) in sizePatches)
            BinaryPrimitives.WriteUInt32BigEndian(rewritten.AsSpan(MapOffset(patchOffset), 4), checked((uint)size));
        foreach ((int patchOffset, uint value) in flagPatches)
            BinaryPrimitives.WriteUInt32BigEndian(rewritten.AsSpan(MapOffset(patchOffset), 4), value);
        foreach ((int patchOffset, long baseOffset, int originalDataOffset) in dataOffsetPatches)
        {
            long relativeOffset = checked(baseOffset + originalDataOffset - absoluteOffset - removedBytes + totalInsertedBytes);
            BinaryPrimitives.WriteInt32BigEndian(rewritten.AsSpan(MapOffset(patchOffset), 4), checked((int)relativeOffset));
        }
        return rewritten;
    }

    private static byte[] CreateDecodeTimeBox(ulong decodeTime)
    {
        byte[] box = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), 20u);
        "tfdt"u8.CopyTo(box.AsSpan(4, 4));
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(8, 4), 0x0100_0000u);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(12, 8), decodeTime);
        return box;
    }

    private static ulong ReadTrackRunDuration(ReadOnlySpan<byte> trun, uint flags)
    {
        if ((flags & 0x000100u) == 0)
            throw new InvalidOperationException("Fragmented MP4 run has no sample durations.");
        uint sampleCount = BinaryPrimitives.ReadUInt32BigEndian(trun.Slice(12, 4));
        int offset = 16;
        if ((flags & 0x000001u) != 0) offset += 4;
        if ((flags & 0x000004u) != 0) offset += 4;
        ulong duration = 0;
        for (uint sample = 0; sample < sampleCount; sample++)
        {
            if (offset + 4 > trun.Length) throw new InvalidOperationException("Fragmented MP4 run is truncated.");
            duration = checked(duration + BinaryPrimitives.ReadUInt32BigEndian(trun.Slice(offset, 4)));
            offset += 4;
            if ((flags & 0x000200u) != 0) offset += 4;
            if ((flags & 0x000400u) != 0) offset += 4;
            if ((flags & 0x000800u) != 0) offset += 4;
            if (offset > trun.Length) throw new InvalidOperationException("Fragmented MP4 run is truncated.");
        }
        return duration;
    }

    private static bool TryReadBox(ReadOnlySpan<byte> bytes, int offset, out int size, out int headerSize)
    {
        size = 0;
        headerSize = 0;
        if (offset < 0 || offset + 8 > bytes.Length) return false;
        uint size32 = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
        if (size32 == 0) return false;
        if (size32 == 1)
        {
            if (offset + 16 > bytes.Length) return false;
            ulong size64 = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(offset + 8, 8));
            if (size64 > int.MaxValue) throw new InvalidOperationException("Fragmented MP4 box is too large.");
            size = checked((int)size64);
            headerSize = 16;
        }
        else
        {
            size = checked((int)size32);
            headerSize = 8;
        }
        return size >= headerSize;
    }

    private static IMFMediaType CreateVideoType(Guid subtype, int width, int height, int framesPerSecond, int bitrate)
    {
        IMFMediaType type = MFCreateMediaType();
        try
        {
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            type.Set(MediaTypeAttributeKeys.Subtype, subtype).CheckError();
            MFSetAttributeSize(type, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height).CheckError();
            MFSetAttributeRatio(type, MediaTypeAttributeKeys.FrameRate, (uint)framesPerSecond, 1).CheckError();
            MFSetAttributeRatio(type, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
            type.SetEnumValue(MediaTypeAttributeKeys.InterlaceMode, VideoInterlaceMode.Progressive).CheckError();
            if (bitrate > 0)
            {
                type.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate).CheckError();
                type.Set(MediaTypeAttributeKeys.Mpeg2Profile, 66u).CheckError();
            }
            return type;
        }
        catch
        {
            type.Dispose();
            throw;
        }
    }

    internal static string? ReadMimeType(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> marker = "avcC"u8;
        int markerIndex = bytes.IndexOf(marker);
        if (markerIndex < 0 || markerIndex + 8 > bytes.Length || bytes[markerIndex + 4] != 1) return null;
        return $"video/mp4; codecs=\"avc1.{bytes[markerIndex + 5]:X2}{bytes[markerIndex + 6]:X2}{bytes[markerIndex + 7]:X2}\"";
    }

    public void Dispose()
    {
        if (!_finalized)
        {
            try { Finish(); }
            catch (SharpGenException) { }
        }
        _writer.Dispose();
        _mediaSink.Shutdown();
        _mediaSink.Dispose();
        _byteStream.Dispose();
        _deviceManager?.Dispose();
        _output.Dispose();
        MFShutdown();
    }

}
