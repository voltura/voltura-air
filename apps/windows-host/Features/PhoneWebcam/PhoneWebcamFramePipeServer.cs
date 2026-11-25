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

internal sealed class PhoneWebcamFrameSequence
{
    private long _value;

    internal ulong Next()
    {
        long value = Interlocked.Increment(ref _value);
        return value > 0
            ? (ulong)value
            : throw new OverflowException("The Phone-webcam frame sequence was exhausted.");
    }
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
    private readonly CancellationTokenSource _disposedSignal = new();
    private PhoneWebcamFrame? _latestFrame;
    private bool _hasLatestRecord;
    private ulong _latestSequence;
    private bool _disposed;
    private int _activeWaiters;
    private bool _waitResourcesDisposed;
    private bool _disposeCancellationCompleted;

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

            bool hadPendingRecord = _hasLatestRecord;
            displaced = _latestFrame;
            _latestFrame = frame;
            _hasLatestRecord = true;
            _latestSequence = frame.Sequence;
            if (!hadPendingRecord)
            {
                _frameReady.Release();
            }
        }

        displaced?.Dispose();
    }

    internal void Clear()
    {
        PhoneWebcamFrame? displaced;
        lock (_frameLock)
        {
            if (_disposed)
            {
                return;
            }

            bool hadPendingRecord = _hasLatestRecord;
            displaced = _latestFrame;
            _latestFrame = null;
            _hasLatestRecord = true;
            if (!hadPendingRecord)
            {
                _frameReady.Release();
            }
        }

        displaced?.Dispose();
    }

    internal async Task<PhoneWebcamFrame?> TakeAsync(CancellationToken cancellationToken)
    {
        lock (_frameLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeWaiters += 1;
        }

        CancellationTokenSource? wait = null;
        try
        {
            wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposedSignal.Token);
            await _frameReady.WaitAsync(wait.Token).ConfigureAwait(false);
            lock (_frameLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_hasLatestRecord)
                {
                    throw new InvalidOperationException("The latest-record signal had no record.");
                }

                PhoneWebcamFrame? frame = _latestFrame;
                _latestFrame = null;
                _hasLatestRecord = false;
                return frame;
            }
        }
        finally
        {
            wait?.Dispose();
            ReleaseWaiter();
        }
    }

    public void Dispose()
    {
        PhoneWebcamFrame? pending;
        lock (_frameLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pending = _latestFrame;
            _latestFrame = null;
            _hasLatestRecord = false;
        }

        pending?.Dispose();
        _disposedSignal.Cancel();
        lock (_frameLock)
        {
            _disposeCancellationCompleted = true;
        }
        DisposeWaitResourcesIfIdle();
    }

    private void ReleaseWaiter()
    {
        lock (_frameLock)
        {
            _activeWaiters -= 1;
        }
        DisposeWaitResourcesIfIdle();
    }

    private void DisposeWaitResourcesIfIdle()
    {
        lock (_frameLock)
        {
            if (!_disposed || !_disposeCancellationCompleted || _activeWaiters != 0 || _waitResourcesDisposed)
            {
                return;
            }
            _waitResourcesDisposed = true;
        }

        _frameReady.Dispose();
        _disposedSignal.Dispose();
    }
}

internal sealed class PhoneWebcamFramePipeServer : IAsyncDisposable
{
    private static readonly byte[] Handshake = "VAWH"u8.ToArray();
    private static readonly byte[] RecordMagic = "VAWF"u8.ToArray();
    private static readonly byte[] ClearMagic = "VAWC"u8.ToArray();
    private readonly SecurityIdentifier _userSid = WindowsIdentity.GetCurrent().User
        ?? throw new InvalidOperationException("The current Windows user has no SID.");
    private readonly SecurityIdentifier _frameServerSid = (SecurityIdentifier)new NTAccount("NT SERVICE", "FrameServer")
        .Translate(typeof(SecurityIdentifier));
    private readonly PhoneWebcamLatestFrameQueue _frames = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private readonly Func<NamedPipeServerStream> _createPipe;
    private readonly Action<NamedPipeServerStream> _authenticateClient;
    private readonly TimeSpan _writeTimeout;
    private readonly IDisposable _tokenAccessLease;
    private readonly Lock _disposeGate = new();
    private Task? _disposal;

    internal PhoneWebcamFramePipeServer(Func<NamedPipeServerStream>? createPipe = null)
        : this(createPipe, null, TimeSpan.FromSeconds(2))
    {
    }

    internal PhoneWebcamFramePipeServer(
        Func<NamedPipeServerStream>? createPipe,
        Action<NamedPipeServerStream>? authenticateClient,
        TimeSpan writeTimeout)
    {
        _tokenAccessLease = PhoneWebcamProcessTokenAccess.Grant(_frameServerSid);
        try
        {
            _createPipe = createPipe ?? CreatePipe;
            _authenticateClient = authenticateClient ?? AuthenticateClient;
            _writeTimeout = writeTimeout;
            NamedPipeServerStream initialPipe = _createPipe();
            _worker = RunAsync(initialPipe, _shutdown.Token);
        }
        catch
        {
            _frames.Dispose();
            _shutdown.Dispose();
            _tokenAccessLease.Dispose();
            throw;
        }
    }

    internal event Action<PhoneWebcamFramePipeServer, Exception>? Failed;

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

    internal void Clear() => _frames.Clear();

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            return new ValueTask(_disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await _shutdown.CancelAsync().ConfigureAwait(false);
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            _frames.Dispose();
            _shutdown.Dispose();
            _tokenAccessLease.Dispose();
        }
    }

    private async Task RunAsync(NamedPipeServerStream initialPipe, CancellationToken cancellationToken)
    {
        NamedPipeServerStream? nextPipe = initialPipe;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using NamedPipeServerStream pipe = nextPipe ?? _createPipe();
                nextPipe = null;
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

                    _authenticateClient(pipe);
                    if (!handshake.AsSpan(0, 4).SequenceEqual(Handshake) ||
                        BinaryPrimitives.ReadInt32LittleEndian(handshake.AsSpan(4)) != PhoneWebcamFrameContract.ProtocolVersion)
                    {
                        throw new InvalidDataException("The virtual camera sent an invalid pipe handshake.");
                    }

                    await ServeClientAsync(pipe, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    // A closed or rejected consumer only retires this pipe instance.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed?.Invoke(this, exception);
        }
        finally
        {
            if (nextPipe is not null)
            {
                await nextPipe.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task ServeClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task clientClosed = WatchClientAsync(pipe, connection);
        byte[] header = new byte[40];
        try
        {
            while (!connection.IsCancellationRequested)
            {
                using PhoneWebcamFrame? frame = await _frames.TakeAsync(connection.Token).ConfigureAwait(false);
                Array.Clear(header);
                (frame is null ? ClearMagic : RecordMagic).CopyTo(header, 0);
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), PhoneWebcamFrameContract.ProtocolVersion);
                if (frame is not null)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), frame.Sequence);
                    BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), frame.SourceTimestamp90Khz);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), PhoneWebcamFrameContract.Width);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), PhoneWebcamFrameContract.Height);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(32), PhoneWebcamFrameContract.Nv12Format);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(36), PhoneWebcamFrameContract.FrameBytes);
                }

                connection.CancelAfter(_writeTimeout);
                await pipe.WriteAsync(header, connection.Token).ConfigureAwait(false);
                if (frame is not null) await pipe.WriteAsync(frame.Payload, connection.Token).ConfigureAwait(false);
                connection.CancelAfter(Timeout.InfiniteTimeSpan);
            }
        }
        catch (OperationCanceledException) when (connection.IsCancellationRequested)
        {
        }
        finally
        {
            await connection.CancelAsync().ConfigureAwait(false);
            await clientClosed.ConfigureAwait(false);
        }
    }

    private static async Task WatchClientAsync(NamedPipeServerStream pipe, CancellationTokenSource connection)
    {
        try
        {
            // The handshake is the client's only version 1 record. EOF or more data ends this connection.
            await pipe.ReadAsync(new byte[1], connection.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (connection.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            await connection.CancelAsync().ConfigureAwait(false);
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
