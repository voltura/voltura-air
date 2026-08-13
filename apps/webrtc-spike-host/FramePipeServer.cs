using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace WebRtcSpike.Host;

internal sealed partial class FramePipeServer : IAsyncDisposable
{
    internal const string PipeName = "VolturaAirWebcam-v1";
    internal const int ProtocolVersion = 1;
    internal const int Nv12Format = 1;
    private static readonly byte[] Handshake = "VAWH"u8.ToArray();
    private static readonly byte[] RecordMagic = "VAWF"u8.ToArray();
    private readonly SecurityIdentifier _userSid = WindowsIdentity.GetCurrent().User
        ?? throw new InvalidOperationException("The current Windows user has no SID.");
    private readonly SecurityIdentifier _frameServerSid = (SecurityIdentifier)new NTAccount("NT SERVICE", "FrameServer")
        .Translate(typeof(SecurityIdentifier));
    private readonly object _frameLock = new();
    private readonly SemaphoreSlim _frameReady = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _firstFrameSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _worker;
    private DecodedFrame? _latestFrame;

    internal FramePipeServer()
    {
        ProcessTokenQueryAccess.Grant(_frameServerSid);
        _worker = RunAsync(_shutdown.Token);
    }

    internal void Publish(DecodedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        DecodedFrame? displaced;
        lock (_frameLock)
        {
            displaced = _latestFrame;
            _latestFrame = frame;
            if (displaced is null) _frameReady.Release();
        }
        displaced?.Dispose();
    }

    internal Task WaitForFirstFrameAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _firstFrameSent.Task.WaitAsync(timeout, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        try { await _worker.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        lock (_frameLock)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }
        _frameReady.Dispose();
        _shutdown.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        bool disconnectReported = false;
        bool connectionReported = false;
        bool frameReported = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            await using NamedPipeServerStream pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                byte[] handshake = new byte[8];
                using (var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                    try
                    {
                        await pipe.ReadExactlyAsync(handshake, handshakeTimeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new InvalidDataException("The virtual camera pipe handshake timed out.");
                    }
                }
                AuthenticateClient(pipe);
                if (!handshake.AsSpan(0, 4).SequenceEqual(Handshake) || BinaryPrimitives.ReadInt32LittleEndian(handshake.AsSpan(4)) != ProtocolVersion)
                    throw new InvalidDataException("The virtual camera sent an invalid pipe handshake.");
                if (!connectionReported)
                {
                    Console.WriteLine("Virtual camera consumer connected.");
                    connectionReported = true;
                }

                byte[] header = new byte[40];
                while (!cancellationToken.IsCancellationRequested)
                {
                    DecodedFrame frame = await TakeLatestAsync(cancellationToken).ConfigureAwait(false);
                    using (frame)
                    {
                        RecordMagic.CopyTo(header, 0);
                        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), ProtocolVersion);
                        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), frame.Sequence);
                        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), frame.RtpTimestamp90Khz);
                        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), MediaFoundationH264Decoder.Width);
                        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), MediaFoundationH264Decoder.Height);
                        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(32), Nv12Format);
                        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(36), MediaFoundationH264Decoder.FrameBytes);
                        await pipe.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                        await pipe.WriteAsync(frame.Bytes.AsMemory(0, MediaFoundationH264Decoder.FrameBytes), cancellationToken).ConfigureAwait(false);
                        if (!frameReported)
                        {
                            Console.WriteLine("First decoded frame sent to virtual camera.");
                            frameReported = true;
                            _firstFrameSent.TrySetResult();
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                if (!disconnectReported)
                {
                    Console.Error.WriteLine($"Virtual camera pipe disconnected: {exception.Message}");
                    disconnectReported = true;
                }
            }
        }
    }

    private async Task<DecodedFrame> TakeLatestAsync(CancellationToken cancellationToken)
    {
        await _frameReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_frameLock)
        {
            DecodedFrame frame = _latestFrame
                ?? throw new InvalidOperationException("The latest-frame signal had no frame.");
            _latestFrame = null;
            return frame;
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
            PipeName,
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
            throw new UnauthorizedAccessException("The pipe client is not Windows FrameServer.");
    }
}

internal sealed record DecodedFrame(ulong Sequence, uint RtpTimestamp90Khz, byte[] Bytes) : IDisposable
{
    public void Dispose() => MediaFoundationH264Decoder.ReturnFrame(Bytes);
}
