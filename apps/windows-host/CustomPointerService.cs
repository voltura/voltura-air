using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace VolturaAir.Host;

/// <summary>Applies Voltura's generated cursor scheme without altering the user's Windows cursor registry settings.</summary>
public sealed partial class CustomPointerService : IDisposable
{
    private const uint SpiSetCursors = 0x0057;
    private const int TemplateSize = 256;
    private static readonly CursorRole[] Roles =
    [
        new("Arrow", 32512), new("IBeam", 32513), new("Wait", 32514), new("Crosshair", 32515),
        new("UpArrow", 32516), new("SizeNWSE", 32642), new("SizeNESW", 32643), new("SizeWE", 32644),
        new("SizeNS", 32645), new("SizeAll", 32646), new("No", 32648), new("Hand", 32649),
        new("AppStarting", 32650), new("Help", 32651), new("NWPen", 32631), new("Pin", 32671), new("Person", 32672)
    ];

    private readonly Lock _gate = new();
    private readonly string _templateDirectory;
    private readonly Func<bool> _useRecoveryMonitoring;
    private readonly Action _ensureRecoveryMonitoring;
    private readonly Action _stopRecoveryMonitoring;
    private bool _mayHaveApplied;
    private bool _presentationLaserPointerEnabled;
    private CustomPointerSettings _settings = new(
        false,
        AppPointerSettings.DefaultCustomPointerSize,
        AppPointerSettings.DefaultCustomPointerColor);
    private PresentationLaserPointerSettings _presentationLaserPointerSettings = new(
        AppPointerSettings.DefaultPresentationLaserSize,
        PresentationLaserColor.Red);
    private bool _disposed;

    public CustomPointerService()
        : this(static () => false, static () => { }, static () => { })
    {
    }

    internal CustomPointerService(
        Func<bool> useRecoveryMonitoring,
        Action ensureRecoveryMonitoring,
        Action stopRecoveryMonitoring)
    {
        _templateDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "CustomPointerTemplates");
        _useRecoveryMonitoring = useRecoveryMonitoring;
        _ensureRecoveryMonitoring = ensureRecoveryMonitoring;
        _stopRecoveryMonitoring = stopRecoveryMonitoring;
    }

    public void Apply(CustomPointerSettings settings)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _settings = settings;
            if (_presentationLaserPointerEnabled)
            {
                return;
            }

            ApplyCore(settings);
        }
    }

    public void SetPresentationLaserPointer(bool enabled)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_presentationLaserPointerEnabled == enabled)
            {
                return;
            }

            if (enabled)
            {
                try
                {
                    ApplyLaserPointerCore();
                    _presentationLaserPointerEnabled = true;
                }
                catch
                {
                    _presentationLaserPointerEnabled = false;
                    ApplyCore(_settings);
                    throw;
                }

                return;
            }

            _presentationLaserPointerEnabled = false;
            try
            {
                ApplyCore(_settings);
            }
            catch
            {
                // Turning the presentation laser off is the safety-critical outcome.
                // If the configured custom scheme cannot be restored, leave Windows'
                // default scheme active rather than reporting the laser as still on.
                Restore();
            }
        }
    }

    public void ApplyPresentationLaserPointerSettings(PresentationLaserPointerSettings settings)
    {
        var normalized = settings with
        {
            Size = AppPointerSettings.NormalizeCustomPointerSize(settings.Size),
            Color = Enum.IsDefined(settings.Color) ? settings.Color : PresentationLaserColor.Red
        };
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_presentationLaserPointerEnabled)
            {
                _presentationLaserPointerSettings = normalized;
                return;
            }

            var previous = _presentationLaserPointerSettings;
            _presentationLaserPointerSettings = normalized;
            try
            {
                ApplyLaserPointerCore();
            }
            catch
            {
                _presentationLaserPointerSettings = previous;
                try
                {
                    ApplyLaserPointerCore();
                }
                catch
                {
                    _presentationLaserPointerEnabled = false;
                    ApplyCore(_settings);
                }

                throw;
            }
        }
    }

    private void ApplyCore(CustomPointerSettings settings)
    {
        if (!settings.Enabled)
        {
            Restore();
            _stopRecoveryMonitoring();
            return;
        }

        if (!_useRecoveryMonitoring())
        {
            throw new CursorWatchdogUnavailableException("Custom pointer requires the cursor recovery watchdog.");
        }

        try
        {
            // Readiness means the watchdog holds a synchronized host handle before any cursor is replaced.
            RefreshRecoveryMonitoringCore(customPointerActive: true);
            _mayHaveApplied = true;
            foreach (var role in Roles)
            {
                SafeCursorHandle? cursor = null;
                try
                {
                    cursor = CreateCursor(role, settings);
                    var cursorHandle = cursor.Detach();
                    cursor.Dispose();
                    cursor = null;
                    if (!SetSystemCursor(cursorHandle, role.SystemCursorId))
                    {
                        _ = DestroyCursor(cursorHandle);
                        throw new InvalidOperationException($"Windows did not apply the {role.TemplateName} custom pointer.");
                    }
                }
                finally
                {
                    cursor?.Dispose();
                }
            }
        }
        catch
        {
            Restore();
            _stopRecoveryMonitoring();
            throw;
        }
    }

    private void ApplyLaserPointerCore()
    {
        try
        {
            RefreshRecoveryMonitoringCore(customPointerActive: true);
            _mayHaveApplied = true;
            using var bitmap = CreateLaserPointerBitmap(_presentationLaserPointerSettings);
            foreach (var role in Roles)
            {
                SafeCursorHandle? cursor = null;
                try
                {
                    cursor = CreateCursor(bitmap, (uint)(bitmap.Width / 2), (uint)(bitmap.Height / 2));
                    var cursorHandle = cursor.Detach();
                    cursor.Dispose();
                    cursor = null;
                    if (!SetSystemCursor(cursorHandle, role.SystemCursorId))
                    {
                        _ = DestroyCursor(cursorHandle);
                        throw new InvalidOperationException($"Windows did not apply the presentation laser pointer to the {role.TemplateName} role.");
                    }
                }
                finally
                {
                    cursor?.Dispose();
                }
            }
        }
        catch
        {
            Restore();
            _stopRecoveryMonitoring();
            throw;
        }
    }

    public void Restore()
    {
        if (!_mayHaveApplied)
        {
            return;
        }

        RestoreWindowsCursorScheme();
        _mayHaveApplied = false;
    }

    internal static void RestoreWindowsCursorScheme() =>
        _ = SystemParametersInfo(SpiSetCursors, 0, nint.Zero, 0);

    internal void RefreshRecoveryMonitoring()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_mayHaveApplied && !_useRecoveryMonitoring())
            {
                Restore();
                _stopRecoveryMonitoring();
                return;
            }

            RefreshRecoveryMonitoringCore(_mayHaveApplied);
        }
    }

    private void RefreshRecoveryMonitoringCore(bool customPointerActive)
    {
        if (customPointerActive && _useRecoveryMonitoring())
        {
            _ensureRecoveryMonitoring();
        }
        else
        {
            _stopRecoveryMonitoring();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Restore();
            // Final shutdown leaves a ready watchdog alive until this host exits,
            // so it can perform the harmless fallback restoration after host cleanup.
            _disposed = true;
        }
    }

    private SafeCursorHandle CreateCursor(CursorRole role, CustomPointerSettings settings)
    {
        var path = Path.Combine(_templateDirectory, $"{role.TemplateName}.cur");
        using var source = LoadTemplateBitmap(path, role.TemplateName);
        var canvasSize = GetCanvasSize(settings.Size);
        using var bitmap = new Bitmap(canvasSize, canvasSize, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.DrawImage(source, new Rectangle(0, 0, canvasSize, canvasSize));
        }
        Recolor(bitmap, settings.Color);
        var (hotspotX, hotspotY) = ReadHotspot(path, (uint)source.Width, canvasSize);
        return CreateCursor(bitmap, hotspotX, hotspotY);
    }

    private static SafeCursorHandle CreateCursor(Bitmap bitmap, uint hotspotX, uint hotspotY)
    {
        var colorBitmap = CreateAlphaBitmap(bitmap);
        var maskBitmap = CreateBitmap(bitmap.Width, bitmap.Height, 1, 1, nint.Zero);
        if (maskBitmap == nint.Zero)
        {
            _ = DeleteObject(colorBitmap);
            throw new InvalidOperationException("Windows could not create a custom pointer mask.");
        }

        try
        {
            var iconInfo = new IconInfo
            {
                IsIcon = 0,
                XHotspot = hotspotX,
                YHotspot = hotspotY,
                ColorBitmap = colorBitmap,
                MaskBitmap = maskBitmap
            };
            var handle = CreateIconIndirect(in iconInfo);
            if (handle == nint.Zero)
            {
                throw new InvalidOperationException("Windows could not create a custom pointer.");
            }

            return new SafeCursorHandle(handle);
        }
        finally
        {
            _ = DeleteObject(colorBitmap);
            _ = DeleteObject(maskBitmap);
        }
    }

    internal static Bitmap CreateLaserPointerBitmap() =>
        CreateLaserPointerBitmap(new(
            AppPointerSettings.DefaultPresentationLaserSize,
            PresentationLaserColor.Red));

    internal static Bitmap CreateLaserPointerBitmap(PresentationLaserPointerSettings settings)
    {
        // Share the regular custom pointer's size scale while keeping the
        // visible core compact and the surrounding light diffuse.
        var size = GetCanvasSize(settings.Size);
        var center = size / 2f;
        var glowRadius = size * 0.4464f;
        var (glowRed, glowGreen, glowBlue) = GetLaserGlowColor(settings.Color);
        var (ringRed, ringGreen, ringBlue) = GetLaserRingColor(settings.Color);
        var (centerRed, centerGreen, centerBlue) = GetLaserCenterColor(settings.Color);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = MathF.Sqrt(MathF.Pow(x + 0.5f - center, 2) + MathF.Pow(y + 0.5f - center, 2));
                if (distance >= glowRadius)
                {
                    continue;
                }

                var strength = 1 - distance / glowRadius;
                var alpha = (int)MathF.Round(210 * MathF.Pow(strength, 1.2f));
                bitmap.SetPixel(x, y, Color.FromArgb(alpha, glowRed, glowGreen, glowBlue));
            }
        }

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var ringBrush = new SolidBrush(Color.FromArgb(255, ringRed, ringGreen, ringBlue));
        using var centerBrush = new SolidBrush(Color.FromArgb(255, centerRed, centerGreen, centerBlue));
        var ringSize = size * 0.2679f;
        var centerSize = size * 0.0893f;
        graphics.FillEllipse(ringBrush, center - ringSize / 2, center - ringSize / 2, ringSize, ringSize);
        graphics.FillEllipse(centerBrush, center - centerSize / 2, center - centerSize / 2, centerSize, centerSize);
        return bitmap;
    }

    private static (int Red, int Green, int Blue) GetLaserGlowColor(PresentationLaserColor color) => color switch
    {
        PresentationLaserColor.Green => (30, 210, 120),
        PresentationLaserColor.Blue => (35, 150, 255),
        _ => (255, 30, 38)
    };

    private static (int Red, int Green, int Blue) GetLaserRingColor(PresentationLaserColor color) => color switch
    {
        PresentationLaserColor.Green => (18, 196, 106),
        PresentationLaserColor.Blue => (25, 140, 255),
        _ => (255, 18, 28)
    };

    private static (int Red, int Green, int Blue) GetLaserCenterColor(PresentationLaserColor color) => color switch
    {
        PresentationLaserColor.Green => (8, 158, 82),
        PresentationLaserColor.Blue => (8, 118, 220),
        _ => (168, 0, 8)
    };

    internal static Bitmap LoadTemplateBitmap(string path, string name)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 22)
        {
            throw new InvalidOperationException($"The {name} cursor template is invalid.");
        }

        var width = bytes[6] == 0 ? TemplateSize : bytes[6];
        var imageOffset = BitConverter.ToInt32(bytes, 18);
        if (imageOffset < 0 || imageOffset + 40 > bytes.Length || BitConverter.ToUInt16(bytes, imageOffset + 14) != 32)
        {
            throw new InvalidOperationException($"The {name} cursor template is not a 32-bit cursor frame.");
        }

        var bitmapHeight = Math.Abs(BitConverter.ToInt32(bytes, imageOffset + 8)) / 2;
        var pixelsOffset = imageOffset + BitConverter.ToInt32(bytes, imageOffset);
        var stride = width * 4;
        if (bitmapHeight != width || pixelsOffset < 0 || pixelsOffset + stride * bitmapHeight > bytes.Length)
        {
            throw new InvalidOperationException($"The {name} cursor template has unexpected frame dimensions.");
        }

        var bitmap = new Bitmap(width, bitmapHeight, PixelFormat.Format32bppArgb);
        for (var y = 0; y < bitmapHeight; y++)
        {
            var rowOffset = pixelsOffset + (bitmapHeight - 1 - y) * stride;
            for (var x = 0; x < width; x++)
            {
                var pixelOffset = rowOffset + x * 4;
                bitmap.SetPixel(x, y, Color.FromArgb(bytes[pixelOffset + 3], bytes[pixelOffset + 2], bytes[pixelOffset + 1], bytes[pixelOffset]));
            }
        }

        return bitmap;
    }

    internal static int GetCanvasSize(int size) => 16 * (AppPointerSettings.NormalizeCustomPointerSize(size) + 1);

    internal static void Recolor(Bitmap bitmap, uint color)
    {
        var target = Color.FromArgb((int)(color >> 16) & 0xFF, (int)(color >> 8) & 0xFF, (int)color & 0xFF);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var source = bitmap.GetPixel(x, y);
                if (source.A == 0 || (source.R < 48 && source.G < 48 && source.B < 48))
                {
                    continue;
                }

                bitmap.SetPixel(x, y, Color.FromArgb(source.A, target));
            }
        }
    }

    internal static (uint X, uint Y) ScaleHotspot(uint sourceX, uint sourceY, uint sourceWidth, int canvasSize)
    {
        return ((uint)Math.Round(sourceX * canvasSize / (double)sourceWidth), (uint)Math.Round(sourceY * canvasSize / (double)sourceWidth));
    }

    private static (uint X, uint Y) ReadHotspot(string path, uint loadedWidth, int canvasSize)
    {
        var bytes = File.ReadAllBytes(path);
        return ScaleHotspot(BitConverter.ToUInt16(bytes, 10), BitConverter.ToUInt16(bytes, 12), loadedWidth, canvasSize);
    }

    private static nint CreateAlphaBitmap(Bitmap bitmap)
    {
        var bitmapInfo = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = bitmap.Width,
                Height = -bitmap.Height,
                Planes = 1,
                BitCount = 32
            }
        };
        var handle = CreateDibSection(nint.Zero, in bitmapInfo, 0, out var pixels, nint.Zero, 0);
        if (handle == nint.Zero || pixels == nint.Zero)
        {
            throw new InvalidOperationException("Windows could not create an alpha-capable custom pointer bitmap.");
        }

        var locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var byteCount = Math.Abs(locked.Stride) * bitmap.Height;
            var bytes = new byte[byteCount];
            Marshal.Copy(locked.Scan0, bytes, 0, byteCount);
            Marshal.Copy(bytes, 0, pixels, byteCount);
            return handle;
        }
        catch
        {
            _ = DeleteObject(handle);
            throw;
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint XHotspot;
        public uint YHotspot;
        public nint MaskBitmap;
        public nint ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint ImageSize;
        public int XpelsPerMeter;
        public int YpelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
    }

    private sealed class SafeCursorHandle : SafeHandle
    {
        public SafeCursorHandle(nint handle) : base(nint.Zero, ownsHandle: true) => SetHandle(handle);
        public override bool IsInvalid => handle == nint.Zero;
        public nint Detach()
        {
            var result = handle;
            SetHandleAsInvalid();
            return result;
        }

        protected override bool ReleaseHandle() => DestroyCursor(handle);
    }

    private readonly record struct CursorRole(string TemplateName, uint SystemCursorId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetSystemCursor(nint cursor, uint cursorId);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint parameter, nint value, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateIconIndirect(in IconInfo iconInfo);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyCursor(nint cursor);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, nint bits);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection", SetLastError = true)]
    private static partial nint CreateDibSection(nint deviceContext, in BitmapInfo bitmapInfo, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint objectHandle);
}
