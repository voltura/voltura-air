using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal static class PhoneWebcamFrameContract
{
    internal const string PipeName = "VolturaAirWebcam-v1";
    internal const int ProtocolVersion = 1;
    internal const int Nv12Format = 1;
    internal const int Width = 1920;
    internal const int Height = 1080;
    internal const int FrameBytes = Width * Height * 3 / 2;
}

internal sealed class PhoneWebcamFrame(
    ulong sequence,
    ulong sourceTimestamp90Khz,
    IMemoryOwner<byte> owner) : IDisposable
{
    internal ulong Sequence { get; } = sequence;

    internal ulong SourceTimestamp90Khz { get; } = sourceTimestamp90Khz;

    internal Memory<byte> Payload { get; } = owner.Memory;

    public void Dispose() => owner.Dispose();
}

internal sealed class PhoneWebcamLatestFrameQueue : IDisposable
{
    private readonly Lock _frameLock = new();
    private readonly SemaphoreSlim _frameReady = new(0, 1);
    private PhoneWebcamFrame? _latestFrame;
    private ulong _latestSequence;
    private bool _disposed;

    internal void Publish(PhoneWebcamFrame frame)
    {
        PhoneWebcamFrame? displaced;
        lock (_frameLock)
        {
            if (_disposed)
            {
                frame.Dispose();
                return;
            }

            if (frame.Sequence == 0 || frame.Sequence <= _latestSequence)
            {
                frame.Dispose();
                throw new InvalidDataException("Phone-webcam frame sequences must be nonzero and strictly monotonic.");
            }

            displaced = _latestFrame;
            _latestFrame = frame;
            _latestSequence = frame.Sequence;
            if (displaced is null)
            {
                _frameReady.Release();
            }
        }

        displaced?.Dispose();
    }

    internal async Task<PhoneWebcamFrame> TakeAsync(CancellationToken cancellationToken)
    {
        await _frameReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_frameLock)
        {
            PhoneWebcamFrame frame = _latestFrame
                ?? throw new InvalidOperationException("The latest-frame signal had no frame.");
            _latestFrame = null;
            return frame;
        }
    }

    public void Dispose()
    {
        lock (_frameLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _latestFrame?.Dispose();
            _latestFrame = null;
        }

        _frameReady.Dispose();
    }
}

internal sealed class PhoneWebcamFramePipeServer : IAsyncDisposable
{
    private static readonly byte[] Handshake = "VAWH"u8.ToArray();
    private static readonly byte[] RecordMagic = "VAWF"u8.ToArray();
    private readonly SecurityIdentifier _userSid = WindowsIdentity.GetCurrent().User
        ?? throw new InvalidOperationException("The current Windows user has no SID.");
    private readonly SecurityIdentifier _frameServerSid = (SecurityIdentifier)new NTAccount("NT SERVICE", "FrameServer")
        .Translate(typeof(SecurityIdentifier));
    private readonly PhoneWebcamLatestFrameQueue _frames = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;

    internal PhoneWebcamFramePipeServer()
    {
        PhoneWebcamProcessTokenAccess.Grant(_frameServerSid);
        _worker = RunAsync(_shutdown.Token);
    }

    internal void Publish(PhoneWebcamFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Payload.Length != PhoneWebcamFrameContract.FrameBytes)
        {
            frame.Dispose();
            throw new ArgumentException("The phone-webcam frame does not satisfy the version 1 contract.", nameof(frame));
        }

        _frames.Publish(frame);
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _frames.Dispose();
        _shutdown.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using NamedPipeServerStream pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                byte[] handshake = new byte[8];
                using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    await pipe.ReadExactlyAsync(handshake, handshakeTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new InvalidDataException("The virtual camera pipe handshake timed out.");
                }

                AuthenticateClient(pipe);
                if (!handshake.AsSpan(0, 4).SequenceEqual(Handshake) ||
                    BinaryPrimitives.ReadInt32LittleEndian(handshake.AsSpan(4)) != PhoneWebcamFrameContract.ProtocolVersion)
                {
                    throw new InvalidDataException("The virtual camera sent an invalid pipe handshake.");
                }

                byte[] header = new byte[40];
                while (!cancellationToken.IsCancellationRequested)
                {
                    using PhoneWebcamFrame frame = await _frames.TakeAsync(cancellationToken).ConfigureAwait(false);
                    RecordMagic.CopyTo(header, 0);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), PhoneWebcamFrameContract.ProtocolVersion);
                    BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), frame.Sequence);
                    BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), frame.SourceTimestamp90Khz);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), PhoneWebcamFrameContract.Width);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), PhoneWebcamFrameContract.Height);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(32), PhoneWebcamFrameContract.Nv12Format);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(36), PhoneWebcamFrameContract.FrameBytes);
                    await pipe.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                    await pipe.WriteAsync(
                        frame.Payload[..PhoneWebcamFrameContract.FrameBytes],
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // A camera consumer may close at any time. The next connection starts
                // from an empty latest-frame slot and never accumulates a backlog.
            }
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.SetOwner(_userSid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(_userSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(_frameServerSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            PhoneWebcamFrameContract.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security,
            HandleInheritability.None,
            PipeAccessRights.ChangePermissions);
    }

    private void AuthenticateClient(NamedPipeServerStream pipe)
    {
        bool isFrameServer = false;
        pipe.RunAsClient(() =>
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            isFrameServer = identity.Groups?.Contains(_frameServerSid) == true;
        });
        if (!isFrameServer)
        {
            throw new UnauthorizedAccessException("The pipe client is not Windows FrameServer.");
        }
    }
}
