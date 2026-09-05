using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using VolturaAir.Host.Features.PhoneWebcam;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class ScreenViewGpuTests : IsolatedHostSettingsTest
{
    // Opt-in: exercises the real local GPU, never a TCP server or a desktop capture.
    [Fact]
    public void SyntheticColorAndEncoderProbe()
    {
        string? destination = Environment.GetEnvironmentVariable("VOLTURA_SCREEN_PROBE_DIRECTORY");
        Assert.SkipWhen(string.IsNullOrEmpty(destination), "Set VOLTURA_SCREEN_PROBE_DIRECTORY for the isolated hardware probe.");
        Directory.CreateDirectory(destination!);
        using IDXGIFactory1 factory = CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out IDXGIAdapter1 adapter).CheckError();
        using (adapter)
        {
            D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
            using (device)
            using (context)
            using (var converter = new D3D11DesktopFrameConverter(device, context, ModeRotation.Identity))
            {
                const int width = 1280, height = 720, fps = 30, bitrate = 3_555_556;
                uint[] colors = [0xff000000, 0xff404040, 0xff808080, 0xffc0c0c0, 0xffffffff, 0xffff0000, 0xff00ff00, 0xff0000ff];
                uint[] pixels = new uint[width * height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++) pixels[y * width + x] = colors[x * colors.Length / width];
                using ID3D11Texture2D source = device.CreateTexture2D<uint>(pixels, Format.B8G8R8A8_UNorm,
                    width, height, 1, 1, BindFlags.ShaderResource);
                ID3D11Texture2D nv12 = converter.RenderNv12(source, width, height);
                byte[] converted = ReadNv12(device, context, nv12);
                File.WriteAllBytes(Path.Combine(destination!, "color-bars.nv12"), converted);
                int[] ySamples = Enumerable.Range(0, colors.Length).Select(i => (int)converted[height / 2 * width + (2 * i + 1) * width / (2 * colors.Length)]).ToArray();
                int[] uvSamples = Enumerable.Range(0, colors.Length).SelectMany(i =>
                {
                    int offset = width * height + height / 4 * width + ((2 * i + 1) * width / (2 * colors.Length) & ~1);
                    return new[] { (int)converted[offset], (int)converted[offset + 1] };
                }).ToArray();
                using var encoder = new ScreenViewHardwareH264Encoder(device, width, height, fps, bitrate);
                using var output = File.Create(Path.Combine(destination!, "stream.h264"));
                var durations = new List<double>();
                var sizes = new List<int>();
                var keyframes = new List<int>();
                var encodedFrames = new List<byte[]>();
                for (int frameIndex = 0; frameIndex < 90; frameIndex++)
                {
                    // Deterministic moving pattern also exercises changing complexity at a fixed budget.
                    if (frameIndex >= 30)
                    {
                        for (int y = 0; y < height; y++)
                            for (int x = 0; x < width; x++)
                                pixels[y * width + x] = colors[((x + frameIndex * 13) / 32 + y / 32) % colors.Length];
                        context.UpdateSubresource<uint>(pixels, source, 0, width * sizeof(uint));
                        nv12 = converter.RenderNv12(source, width, height);
                    }
                    long started = Stopwatch.GetTimestamp();
                    ScreenViewEncodedFrame frame = encoder.Encode(nv12);
                    durations.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    sizes.Add(frame.Bytes.Length);
                    if (frame.IsKeyFrame) keyframes.Add(frameIndex);
                    output.Write(frame.Bytes);
                    encodedFrames.Add(frame.Bytes);
                }
                bool keyframeControl = encoder.TryRequestKeyFrame();
                ScreenViewEncodedFrame requested = encoder.Encode(nv12);
                if (keyframeControl) Assert.True(requested.IsKeyFrame);
                bool bitrateControl = encoder.TrySetBitrate(bitrate / 2);
                ScreenViewEncodedFrame updated = encoder.Encode(nv12);
                Assert.NotEmpty(updated.Bytes);
                var tuning = new List<object>();
                foreach (bool enabled in new[] { false, true, false, true })
                {
                    using var candidate = new ScreenViewHardwareH264Encoder(device, width, height, fps, bitrate, enabled);
                    var times = new List<double>();
                    int bytes = 0;
                    for (int i = 0; i < 45; i++)
                    {
                        long started = Stopwatch.GetTimestamp();
                        ScreenViewEncodedFrame frame = candidate.Encode(nv12);
                        if (i >= 5) times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        bytes += frame.Bytes.Length;
                    }
                    tuning.Add(new { enabled, MeanMs = times.Average(), MaxMs = times.Max(), bytes });
                }
                int decodedFrames = VerifyDecodedFrames(encodedFrames, ySamples);
                int[] hdrSamples = ProbeHdr(device, context, converter);
                var displays = new List<object>();
                for (uint index = 0; adapter.EnumOutputs(index, out IDXGIOutput display).Success; index++)
                {
                    using (display)
                    using (IDXGIOutputDuplication duplication = ScreenViewDisplayColor.DuplicateOutput(display, device))
                    using (var color = new ScreenViewDisplayColor(display))
                    {
                        displays.Add(new { Format = duplication.Description.ModeDescription.Format.ToString(), color.WhiteScale, color.IsHdr });
                    }
                }
                File.WriteAllText(Path.Combine(destination!, "probe.json"), JsonSerializer.Serialize(new
                {
                    Adapter = adapter.Description.Description,
                    width,
                    height,
                    fps,
                    bitrate,
                    Y = ySamples,
                    UV = uvSamples,
                    MeanEncodeMs = durations.Average(),
                    MaxEncodeMs = durations.Max(),
                    EncodedBitrate = sizes.Sum() * 8d / 3,
                    PeakFrameBytes = sizes.Max(),
                    Keyframes = keyframes,
                    keyframeControl,
                    bitrateControl,
                    tuning,
                    decodedFrames,
                    hdrSamples,
                    displays
                }));
                Assert.NotEmpty(keyframes);
            }
        }
    }

    private static int VerifyDecodedFrames(List<byte[]> frames, int[] expectedY)
    {
        using var decoder = new MediaFoundationH264Decoder();
        int decoded = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            byte[]? frame = decoder.Decode(frames[i]);
            if (frame is null) continue;
            try
            {
                decoded++;
                if (i >= 30) continue;
                for (int patch = 0; patch < expectedY.Length; patch++)
                {
                    // The existing decoder centers the 1280x720 image in a 1920x1080
                    // canvas without upscaling it (left=320, top=180).
                    int actual = frame[540 * 1920 + 320 + (2 * patch + 1) * 1280 / (2 * expectedY.Length)];
                    Assert.InRange(Math.Abs(actual - expectedY[patch]), 0, 5);
                }
            }
            finally { MediaFoundationH264Decoder.ReturnFrame(frame); }
        }
        Assert.InRange(decoded, frames.Count - 1, frames.Count);
        return decoded;
    }

    private static int[] ProbeHdr(ID3D11Device device, ID3D11DeviceContext context, D3D11DesktopFrameConverter converter)
    {
        const int width = 320, height = 180;
        float[] levels = [0, 0.18f, 1, 2.5f, 10];
        Half[] pixels = new Half[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = (Half)levels[x * levels.Length / width];
                pixels[offset + 3] = (Half)1;
            }
        using ID3D11Texture2D source = device.CreateTexture2D<Half>(pixels, Format.R16G16B16A16_Float,
            width, height, 1, 1, BindFlags.ShaderResource);
        byte[] nominal = ReadNv12(device, context, converter.RenderNv12(source, width, height));
        byte[] normalized = ReadNv12(device, context, converter.RenderNv12(source, width, height, 0.4f));
        byte[] linearSdr = ReadNv12(device, context, converter.RenderNv12(source, width, height, 0.4f, toneMapHdr: false));
        Assert.InRange((int)linearSdr[height / 2 * width + width / 2], 234, 236);
        int[] samples = Enumerable.Range(0, levels.Length).Select(i => (int)normalized[height / 2 * width + (2 * i + 1) * width / (2 * levels.Length)]).ToArray();
        Assert.InRange(samples[0], 15, 17);
        Assert.True(samples.Zip(samples.Skip(1)).All(pair => pair.First < pair.Second));
        Assert.True(normalized[height / 2 * width + width / 2] < nominal[height / 2 * width + width / 2]);
        return samples;
    }

    internal static byte[] ReadNv12(ID3D11Device device, ID3D11DeviceContext context, ID3D11Texture2D source)
    {
        Texture2DDescription description = source.Description;
        int width = checked((int)description.Width), height = checked((int)description.Height);
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        using ID3D11Texture2D staging = device.CreateTexture2D(description);
        context.CopyResource(staging, source);
        MappedSubresource mapped = context.Map(staging, 0, MapMode.Read);
        try
        {
            byte[] bytes = new byte[width * height * 3 / 2];
            for (int row = 0; row < height * 3 / 2; row++)
                Marshal.Copy(mapped.DataPointer + row * checked((int)mapped.RowPitch), bytes, row * width, width);
            return bytes;
        }
        finally { context.Unmap(staging, 0); }
    }
}
