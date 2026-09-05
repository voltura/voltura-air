using System.Text.Json;
using VolturaAir.Host.Features.AiAssistant;

namespace VolturaAir.Host.Tests;

public sealed class JsonRpcConnectionLifetimeTests
{
    [Fact]
    public async Task RequestDeadlineIncludesABlockedTransportWrite()
    {
        await using var transport = new BlockedTransport();
        await using var connection = new JsonRpcConnection(transport);
        using var cleanup = new CancellationTokenSource();
        Task<JsonElement> request = connection.RequestAsync("blocked", null, TimeSpan.FromMilliseconds(50), cleanup.Token);
        try
        {
            await transport.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            CodexCompatibilityException error = await Assert.ThrowsAsync<CodexCompatibilityException>(
                () => request.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Contains("timed out", error.Message, StringComparison.Ordinal);
            Assert.Equal(0, transport.ActiveWrites);
        }
        finally
        {
            await cleanup.CancelAsync();
            try { await request; } catch (Exception) { }
        }
    }

    [Fact]
    public async Task RequestDeadlineIncludesWaitingForAnotherWriter()
    {
        await using var transport = new BlockedTransport();
        await using var connection = new JsonRpcConnection(transport);
        using var cleanup = new CancellationTokenSource();
        Task<JsonElement> first = connection.RequestAsync("first", null, Timeout.InfiniteTimeSpan, cleanup.Token);
        try
        {
            await transport.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task<JsonElement> queued = connection.RequestAsync("queued", null, TimeSpan.FromMilliseconds(50), cleanup.Token);
            CodexCompatibilityException error = await Assert.ThrowsAsync<CodexCompatibilityException>(
                () => queued.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Contains("timed out", error.Message, StringComparison.Ordinal);
            Assert.Equal(1, transport.ActiveWrites);
        }
        finally
        {
            await cleanup.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        }
        Assert.Equal(0, transport.ActiveWrites);
    }

    [Fact]
    public async Task CallerCancellationRemainsCancellationInsteadOfATimeout()
    {
        await using var transport = new BlockedTransport();
        await using var connection = new JsonRpcConnection(transport);
        using var cancellation = new CancellationTokenSource();
        Task<JsonElement> request = connection.RequestAsync("cancelled", null, TimeSpan.FromSeconds(10), cancellation.Token);
        await transport.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Equal(0, transport.ActiveWrites);
    }

    private sealed class BlockedTransport : IJsonLineTransport
    {
        internal TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int ActiveWrites;

        public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }

        public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref ActiveWrites);
            WriteStarted.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            finally { Interlocked.Decrement(ref ActiveWrites); }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
