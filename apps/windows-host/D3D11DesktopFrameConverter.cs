using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace VolturaAir.Host;

/// <summary>
/// Scales Desktop Duplication textures on the GPU and reads back only the
/// bounded stream-sized BGRA result. The duplicated texture is never retained
/// after the corresponding DXGI frame is released.
/// </summary>
internal sealed class D3D11DesktopFrameConverter : IDisposable
{
    private const string ShaderSource = """
        Texture2D<float4> Desktop : register(t0);
        SamplerState LinearSampler : register(s0);

        struct VertexOutput
        {
            float4 Position : SV_Position;
            float2 Uv : TEXCOORD0;
        };

        VertexOutput VertexMain(uint vertexId : SV_VertexID)
        {
            VertexOutput output;
            float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
            output.Position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
            output.Uv = uv;
            return output;
        }

        float3 LinearToSrgb(float3 value)
        {
            float3 low = value * 12.92;
            float3 high = 1.055 * pow(value, 1.0 / 2.4) - 0.055;
            return lerp(low, high, step(0.0031308, value));
        }

        float3 ToneMapScRgb(float3 value)
        {
            value = max(value, 0.0);
            float3 mapped = (value * (2.51 * value + 0.03)) / (value * (2.43 * value + 0.59) + 0.14);
            return LinearToSrgb(saturate(mapped));
        }

        float2 RotateUv(float2 uv)
        {
        #if ROTATION == 1
            return float2(uv.y, 1.0 - uv.x);
        #elif ROTATION == 2
            return float2(1.0 - uv.x, 1.0 - uv.y);
        #elif ROTATION == 3
            return float2(1.0 - uv.y, uv.x);
        #else
            return uv;
        #endif
        }

        float4 PixelMain(VertexOutput input) : SV_Target
        {
            float4 color = Desktop.SampleLevel(LinearSampler, RotateUv(input.Uv), 0.0);
        #if HDR_INPUT
            color.rgb = ToneMapScRgb(color.rgb);
        #endif
            return float4(saturate(color.rgb), 1.0);
        }
        """;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _sdrPixelShader;
    private readonly ID3D11PixelShader _hdrPixelShader;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext _videoContext;
    private ID3D11Texture2D? _shaderInput;
    private ID3D11ShaderResourceView? _shaderInputView;
    private ID3D11Texture2D? _renderTarget;
    private ID3D11RenderTargetView? _renderTargetView;
    private ID3D11VideoProcessorEnumerator? _videoEnumerator;
    private ID3D11VideoProcessor? _videoProcessor;
    private ID3D11VideoProcessorInputView? _videoInputView;
    private ID3D11Texture2D? _nv12Target;
    private ID3D11VideoProcessorOutputView? _videoOutputView;
    private Texture2DDescription _inputDescription;
    private int _outputWidth;
    private int _outputHeight;

    public D3D11DesktopFrameConverter(ID3D11Device device, ID3D11DeviceContext context, ModeRotation rotation)
    {
        _device = device;
        _context = context;
        _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = context.QueryInterface<ID3D11VideoContext>();
        int rotationValue = rotation switch
        {
            ModeRotation.Rotate90 => 1,
            ModeRotation.Rotate180 => 2,
            ModeRotation.Rotate270 => 3,
            _ => 0
        };
        ReadOnlyMemory<byte> vertexBytecode = Compiler.Compile(
            ShaderSource,
            "VertexMain",
            "VolturaAirScreenView.hlsl",
            "vs_5_0",
            ShaderFlags.OptimizationLevel3);
        ReadOnlyMemory<byte> sdrBytecode = CompilePixelShader(rotationValue, false);
        ReadOnlyMemory<byte> hdrBytecode = CompilePixelShader(rotationValue, true);
        _vertexShader = _device.CreateVertexShader(vertexBytecode.Span);
        _sdrPixelShader = _device.CreatePixelShader(sdrBytecode.Span);
        _hdrPixelShader = _device.CreatePixelShader(hdrBytecode.Span);
        _sampler = _device.CreateSamplerState(new SamplerDescription(
            Filter.MinMagMipLinear,
            TextureAddressMode.Clamp,
            0,
            1,
            ComparisonFunction.Always,
            0,
            float.MaxValue));
    }

    public ID3D11Texture2D RenderNv12(ID3D11Texture2D source, int outputWidth, int outputHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(outputWidth, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(outputHeight, 2);
        Texture2DDescription sourceDescription = source.Description;
        EnsureInputResources(sourceDescription);
        EnsureOutputResources(outputWidth, outputHeight);

        _context.CopyResource(_shaderInput!, source);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.RSSetViewport(0, 0, outputWidth, outputHeight);
        _context.OMSetRenderTargets(_renderTargetView!);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(IsScRgb(sourceDescription.Format) ? _hdrPixelShader : _sdrPixelShader);
        _context.PSSetShaderResource(0, _shaderInputView!);
        _context.PSSetSampler(0, _sampler);
        _context.Draw(3, 0);
        _context.PSUnsetShaderResource(0);
        _context.UnsetRenderTargets();
        var stream = new VideoProcessorStream
        {
            Enable = true,
            InputSurface = _videoInputView
        };
        _videoContext.VideoProcessorBlt(_videoProcessor!, _videoOutputView!, 0, [stream]).CheckError();
        return _nv12Target!;
    }

    private static ReadOnlyMemory<byte> CompilePixelShader(int rotation, bool hdrInput)
    {
        ShaderMacro[] macros =
        [
            new ShaderMacro("ROTATION", rotation.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new ShaderMacro("HDR_INPUT", hdrInput ? "1" : "0")
        ];
        return Compiler.Compile(
            ShaderSource,
            macros,
            "PixelMain",
            "VolturaAirScreenView.hlsl",
            "ps_5_0",
            ShaderFlags.OptimizationLevel3);
    }

    private void EnsureInputResources(Texture2DDescription sourceDescription)
    {
        if (_shaderInput is not null
            && _inputDescription.Width == sourceDescription.Width
            && _inputDescription.Height == sourceDescription.Height
            && _inputDescription.Format == sourceDescription.Format)
        {
            return;
        }

        _shaderInputView?.Dispose();
        _shaderInput?.Dispose();
        var description = new Texture2DDescription(
            sourceDescription.Format,
            sourceDescription.Width,
            sourceDescription.Height,
            1,
            1,
            BindFlags.ShaderResource,
            ResourceUsage.Default,
            CpuAccessFlags.None);
        _shaderInput = _device.CreateTexture2D(description);
        _shaderInputView = _device.CreateShaderResourceView(_shaderInput);
        _inputDescription = sourceDescription;
    }

    private void EnsureOutputResources(int width, int height)
    {
        if (_renderTarget is not null && _outputWidth == width && _outputHeight == height) return;

        _videoOutputView?.Dispose();
        _nv12Target?.Dispose();
        _videoInputView?.Dispose();
        _videoProcessor?.Dispose();
        _videoEnumerator?.Dispose();
        _renderTargetView?.Dispose();
        _renderTarget?.Dispose();
        var renderDescription = new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            (uint)width,
            (uint)height,
            1,
            1,
            BindFlags.RenderTarget | BindFlags.ShaderResource,
            ResourceUsage.Default,
            CpuAccessFlags.None);
        _renderTarget = _device.CreateTexture2D(renderDescription);
        _renderTargetView = _device.CreateRenderTargetView(_renderTarget);
        var videoDescription = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputFrameRate = new Rational(30, 1),
            InputWidth = (uint)width,
            InputHeight = (uint)height,
            OutputFrameRate = new Rational(30, 1),
            OutputWidth = (uint)width,
            OutputHeight = (uint)height,
            Usage = VideoUsage.OptimalSpeed
        };
        _videoEnumerator = _videoDevice.CreateVideoProcessorEnumerator(videoDescription);
        _videoEnumerator.CheckVideoProcessorFormat(Format.B8G8R8A8_UNorm, out VideoProcessorFormatSupport inputSupport).CheckError();
        _videoEnumerator.CheckVideoProcessorFormat(Format.NV12, out VideoProcessorFormatSupport outputSupport).CheckError();
        if (!inputSupport.HasFlag(VideoProcessorFormatSupport.Input) || !outputSupport.HasFlag(VideoProcessorFormatSupport.Output))
            throw new NotSupportedException("The graphics adapter cannot convert the desktop surface to NV12 video.");
        _videoProcessor = _videoDevice.CreateVideoProcessor(_videoEnumerator, 0);
        var inputViewDescription = new VideoProcessorInputViewDescription
        {
            FourCC = 0,
            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 }
        };
        _videoInputView = _videoDevice.CreateVideoProcessorInputView(_renderTarget, _videoEnumerator, inputViewDescription);
        var nv12Description = new Texture2DDescription(
            Format.NV12,
            (uint)width,
            (uint)height,
            1,
            1,
            BindFlags.RenderTarget | BindFlags.VideoEncoder,
            ResourceUsage.Default,
            CpuAccessFlags.None);
        _nv12Target = _device.CreateTexture2D(nv12Description);
        var outputViewDescription = new VideoProcessorOutputViewDescription
        {
            ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 }
        };
        _videoOutputView = _videoDevice.CreateVideoProcessorOutputView(_nv12Target, _videoEnumerator, outputViewDescription);
        _outputWidth = width;
        _outputHeight = height;
    }

    private static bool IsScRgb(Format format) => format == Format.R16G16B16A16_Float;

    public void Dispose()
    {
        _videoOutputView?.Dispose();
        _nv12Target?.Dispose();
        _videoInputView?.Dispose();
        _videoProcessor?.Dispose();
        _videoEnumerator?.Dispose();
        _renderTargetView?.Dispose();
        _renderTarget?.Dispose();
        _shaderInputView?.Dispose();
        _shaderInput?.Dispose();
        _sampler.Dispose();
        _hdrPixelShader.Dispose();
        _sdrPixelShader.Dispose();
        _vertexShader.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
    }
}
