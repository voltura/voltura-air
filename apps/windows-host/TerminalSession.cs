using System.Net.WebSockets;
using System.Threading.Channels;

namespace VolturaAir.Host;

internal sealed class TerminalSession : IAsyncDisposable
{
    internal readonly Lock Gate = new();
    internal readonly LinkedList<TerminalOutputChunk> Output = [];
    internal readonly SemaphoreSlim OutputChanged = new(0);
    internal readonly SemaphoreSlim OutputSpace = new(0);
    internal readonly SemaphoreSlim Negotiation = new(1, 1);
    internal readonly Channel<byte[]> Input = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });
    internal readonly CancellationTokenSource Lifetime = new();
    internal CancellationTokenSource? PeerLifetime;
    internal CancellationTokenSource? OfferLifetime;
    internal CancellationTokenSource? ReconnectLifetime;
    internal ITerminalWebRtcPeer? Peer;
    internal WebSocket? ControlSocket;
    internal string? OfferOperationId;
    internal string? OfferHash;
    internal long NextOutputOffset;
    internal long AcknowledgedOutputOffset;
    internal long SentOutputOffset;
    internal int QueuedInputBytes;
    internal bool Attached;
    internal bool AnswerStarted;
    internal int DisposeStarted;
    internal Task? OutputTask;
    internal Task? InputTask;

    internal TerminalSession(string id, string clientId, ITerminalProcess process)
    {
        Id = id;
        ClientId = clientId;
        Process = process;
        LifetimeToken = Lifetime.Token;
    }

    internal string Id { get; }
    internal string ClientId { get; }
    internal ITerminalProcess Process { get; }
    internal CancellationToken LifetimeToken { get; }

    internal TerminalPeerRetirement TakePeerLocked()
    {
        var retired = new TerminalPeerRetirement(Peer, PeerLifetime, OfferLifetime);
        Peer = null;
        PeerLifetime = null;
        OfferLifetime = null;
        Attached = false;
        return retired;
    }

    internal void ReleaseNegotiation()
    {
        lock (Gate)
        {
            if (Volatile.Read(ref DisposeStarted) == 0) Negotiation.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref DisposeStarted, 1) != 0) return;
        TerminalPeerRetirement retired;
        CancellationTokenSource? reconnect;
        lock (Gate)
        {
            retired = TakePeerLocked();
            reconnect = ReconnectLifetime;
            ReconnectLifetime = null;
        }
        await Lifetime.CancelAsync().ConfigureAwait(false);
        reconnect?.Dispose();
        Input.Writer.TryComplete();
        Process.Terminate();
        await retired.DisposeAsync().ConfigureAwait(false);
        await Process.DisposeAsync().ConfigureAwait(false);
        Task[] workers = [OutputTask ?? Task.CompletedTask, InputTask ?? Task.CompletedTask];
        try { await Task.WhenAll(workers).ConfigureAwait(false); }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException) { }
        Lifetime.Dispose();
        OutputChanged.Dispose();
        OutputSpace.Dispose();
        Negotiation.Dispose();
    }
}

internal readonly record struct TerminalPeerRetirement(
    ITerminalWebRtcPeer? Peer,
    CancellationTokenSource? Lifetime,
    CancellationTokenSource? OfferLifetime)
{
    internal async ValueTask DisposeAsync()
    {
        try
        {
            if (Lifetime is not null) await Lifetime.CancelAsync().ConfigureAwait(false);
            if (Peer is not null) await Peer.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            OfferLifetime?.Dispose();
            Lifetime?.Dispose();
        }
    }
}

internal sealed record TerminalOutputChunk(long Offset, byte[] Bytes)
{
    internal long EndOffset => Offset + Bytes.Length;
}

internal sealed record TerminalCapabilityState(bool Active, bool OwnedByClient, string? TerminalId, string? DeviceName);
