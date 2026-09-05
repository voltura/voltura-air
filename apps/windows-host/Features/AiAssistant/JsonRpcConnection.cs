using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VolturaAir.Host.Features.AiAssistant;

internal sealed class JsonRpcConnection : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly IJsonLineTransport _transport;
    private readonly ConcurrentDictionary<long, PendingRequest> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _reader;
    private long _nextId;
    private int _disposed;

    internal JsonRpcConnection(IJsonLineTransport transport)
    {
        _transport = transport;
        _reader = Task.Run(ReadLoopAsync);
    }

    internal event Action<string, JsonElement>? NotificationReceived;
    internal event Action? ConnectionClosed;
    internal Func<string, JsonElement, CancellationToken, Task<object?>>? ServerRequestReceived { get; set; }

    internal async Task<JsonElement> RequestAsync(string method, object? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        long id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, new(method, completion))) throw new InvalidOperationException("Could not allocate a request identifier.");
        try
        {
            await WriteAsync(JsonSerializer.Serialize(new RpcRequest(id, method, parameters), JsonOptions), deadline.Token).ConfigureAwait(false);
            return await completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new CodexCompatibilityException($"Codex method '{method}' timed out.", exception);
        }
        finally { _pending.TryRemove(id, out _); }
    }

    internal Task NotifyAsync(string method, CancellationToken cancellationToken) =>
        WriteAsync(JsonSerializer.Serialize(new RpcNotification(method, null), JsonOptions), cancellationToken);

    private async Task WriteAsync(string payload, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _transport.WriteLineAsync(payload, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            throw new CodexCompatibilityException("The Codex app-server connection closed.", exception);
        }
        finally { _writeLock.Release(); }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                string? line = await _transport.ReadLineAsync(_lifetime.Token).ConfigureAwait(false);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    await HandleAsync(document.RootElement).ConfigureAwait(false);
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { }
        finally
        {
            var closed = new CodexCompatibilityException("The Codex app-server connection closed.");
            foreach (PendingRequest request in _pending.Values) request.Completion.TrySetException(closed);
            ConnectionClosed?.Invoke();
        }
    }

    private async Task HandleAsync(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return;
        bool hasMethod = root.TryGetProperty("method", out JsonElement methodElement) && methodElement.ValueKind == JsonValueKind.String;
        bool hasId = root.TryGetProperty("id", out JsonElement idElement);
        if (hasMethod && hasId)
        {
            await RespondToServerRequestAsync(
                idElement.Clone(),
                methodElement.GetString() ?? "unknown",
                root.TryGetProperty("params", out JsonElement requestParameters) ? requestParameters.Clone() : default).ConfigureAwait(false);
            return;
        }
        if (hasMethod)
        {
            JsonElement parameters = root.TryGetProperty("params", out JsonElement value) ? value.Clone() : default;
            NotificationReceived?.Invoke(methodElement.GetString() ?? "unknown", parameters);
            return;
        }
        if (!hasId || idElement.ValueKind != JsonValueKind.Number || !idElement.TryGetInt64(out long id) || !_pending.TryGetValue(id, out PendingRequest? pending)) return;
        if (root.TryGetProperty("error", out JsonElement error))
        {
            string message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement text) && text.ValueKind == JsonValueKind.String
                ? text.GetString() ?? "Unknown app-server error"
                : "Unknown app-server error";
            pending.Completion.TrySetException(new CodexCompatibilityException($"Codex method '{pending.Method}' failed: {Bound(message)}"));
        }
        else if (root.TryGetProperty("result", out JsonElement result)) pending.Completion.TrySetResult(result.Clone());
        else pending.Completion.TrySetException(new CodexCompatibilityException($"Codex method '{pending.Method}' returned an unfamiliar response."));
    }

    private async Task RespondToServerRequestAsync(JsonElement id, string method, JsonElement parameters)
    {
        Func<string, JsonElement, CancellationToken, Task<object?>>? handler = ServerRequestReceived;
        if (handler is null)
        {
            await RespondUnsupportedAsync(id).ConfigureAwait(false);
            return;
        }
        try
        {
            object? result = await handler(method, parameters, _lifetime.Token).ConfigureAwait(false);
            await WriteAsync(JsonSerializer.Serialize(new { id, result }, JsonOptions), _lifetime.Token).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            await RespondUnsupportedAsync(id).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            await WriteAsync(
                JsonSerializer.Serialize(new { id, error = new { code = -32603, message = "Voltura Air could not complete this app-server request." } }, JsonOptions),
                _lifetime.Token).ConfigureAwait(false);
        }
    }

    private Task RespondUnsupportedAsync(JsonElement id) => WriteAsync(
        JsonSerializer.Serialize(new { id, error = new { code = -32601, message = "Voltura Air does not support this app-server request." } }, JsonOptions),
        _lifetime.Token);

    private static string Bound(string value) => value.Length <= 300 ? value : value[..300] + "…";

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);
        try { await _reader.ConfigureAwait(false); } catch (Exception) { }
        _writeLock.Dispose();
        _lifetime.Dispose();
    }

    private sealed record PendingRequest(string Method, TaskCompletionSource<JsonElement> Completion);
    private sealed record RpcRequest(long Id, string Method, object? Params);
    private sealed record RpcNotification(string Method, object? Params);
}
