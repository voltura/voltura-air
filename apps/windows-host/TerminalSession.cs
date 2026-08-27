using System.Net.WebSockets;
using System.Threading.Channels;

namespace VolturaAir.Host;

internal sealed class TerminalSession(string id, string clientId, ITerminalProcess process) : IAsyncDisposable
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
    internal int OfferColumns;
    internal int OfferRows;
    internal long OfferAcknowledgedOffset;
    internal long NextOutputOffset;
    internal long AcknowledgedOutputOffset;
    internal long SentOutputOffset;
    internal int QueuedInputBytes;
    internal bool Attached;
    internal bool AnswerStarted;
    internal int DisposeStarted;
    internal Task? OutputTask;
    internal Task? InputTask;

    internal string Id { get; } = id;
    internal string ClientId { get; } = clientId;
    internal ITerminalProcess Process { get; } = process;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref DisposeStarted, 1) != 0) return;
        await Lifetime.CancelAsync().ConfigureAwait(false);
        if (PeerLifetime is not null) await PeerLifetime.CancelAsync().ConfigureAwait(false);
        if (OfferLifetime is not null) await OfferLifetime.CancelAsync().ConfigureAwait(false);
        if (ReconnectLifetime is not null) await ReconnectLifetime.CancelAsync().ConfigureAwait(false);
        Input.Writer.TryComplete();
        Process.Terminate();
        ITerminalWebRtcPeer? peer;
        lock (Gate) { peer = Peer; Peer = null; }
        if (peer is not null) await peer.DisposeAsync().ConfigureAwait(false);
        await Process.DisposeAsync().ConfigureAwait(false);
        Task[] workers = [OutputTask ?? Task.CompletedTask, InputTask ?? Task.CompletedTask];
        try { await Task.WhenAll(workers).ConfigureAwait(false); }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException) { }
        Lifetime.Dispose();
        PeerLifetime?.Dispose();
        OfferLifetime?.Dispose();
        ReconnectLifetime?.Dispose();
        OutputChanged.Dispose();
        OutputSpace.Dispose();
        Negotiation.Dispose();
    }
}

internal sealed record TerminalOutputChunk(long Offset, byte[] Bytes)
{
    internal long EndOffset => Offset + Bytes.Length;
}

internal sealed record TerminalCapabilityState(bool Active, bool OwnedByClient, string? TerminalId, string? DeviceName);
