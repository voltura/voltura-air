using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VolturaAir.Host.Features.AiAssistant;

internal sealed class CodexAppServerClient : IAiAssistantClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private const int MaximumHeldNotifications = 64;
    private readonly JsonRpcConnection _rpc;
    private readonly CodexAppServerProcess? _process;
    private readonly IReadOnlyList<string> _disabledMcpServers;
    private readonly ConcurrentDictionary<string, byte> _activeThreads = new();
    private readonly Lock _notificationGate = new();
    private readonly HashSet<string> _heldNotificationThreads = [];
    private readonly List<(string Method, JsonElement Parameters)> _heldNotifications = [];
    private int _disposed;

    private CodexAppServerClient(CodexAppServerProcess process, IReadOnlyList<string> disabledMcpServers)
        : this(process.Connection, disabledMcpServers)
    {
        _process = process;
    }

    internal CodexAppServerClient(JsonRpcConnection rpc, IReadOnlyList<string> disabledMcpServers)
    {
        _disabledMcpServers = disabledMcpServers;
        _rpc = rpc;
        _rpc.ServerRequestReceived = HandleServerRequestAsync;
        _rpc.NotificationReceived += ObserveNotification;
        _rpc.ConnectionClosed += () => ConnectionClosed?.Invoke();
    }

    public event Action<string, string, string, string>? AgentMessageCompleted;
    public event Action<string, string, string>? TurnCompleted;
    public event Action? ConnectionClosed;

    internal static async Task<CodexAppServerClient> ConnectAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> configuredServers = await DiscoverMcpServersAsync(cancellationToken).ConfigureAwait(false);
        var client = new CodexAppServerClient(CodexAppServerProcess.Start(configuredServers), configuredServers);
        try
        {
            await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await client.VerifyIsolationAsync(cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<IReadOnlyList<string>> DiscoverMcpServersAsync(CancellationToken cancellationToken)
    {
        var discovery = new CodexAppServerClient(CodexAppServerProcess.Start([]), []);
        try
        {
            await discovery.InitializeAsync(cancellationToken).ConfigureAwait(false);
            JsonElement result = await discovery.InvokeAsync(
                "config/read",
                new { cwd = Path.GetFullPath(AiAssistantProfile.KnowledgeRoot), includeLayers = false },
                cancellationToken).ConfigureAwait(false);
            JsonElement config = RequireObject(result, "config");
            if (!config.TryGetProperty("mcp_servers", out JsonElement servers) || servers.ValueKind != JsonValueKind.Object)
                throw ShapeError("config/read");
            var names = new List<string>();
            foreach (JsonProperty entry in servers.EnumerateObject())
            {
                string name = entry.Name;
                if (!Regex.IsMatch(name, "^[A-Za-z0-9_-]{1,80}$", RegexOptions.CultureInvariant))
                    throw new CodexCompatibilityException("A configured Codex integration has a name that cannot be safely isolated.");
                names.Add(name);
            }
            return names;
        }
        finally
        {
            await discovery.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        JsonElement result = await InvokeAsync("initialize", new
        {
            clientInfo = new { name = "voltura_air", title = "Voltura Air", version = typeof(CodexAppServerClient).Assembly.GetName().Version?.ToString() ?? "unknown" },
            capabilities = new { experimentalApi = true }
        }, cancellationToken).ConfigureAwait(false);
        string? platform = ReadString(result, "platformOs");
        if (!string.IsNullOrWhiteSpace(platform) && !string.Equals(platform, "windows", StringComparison.OrdinalIgnoreCase))
            throw new CodexCompatibilityException($"Codex reported unsupported platform '{platform}'.");
        await _rpc.NotifyAsync("initialized", cancellationToken).ConfigureAwait(false);
    }

    private async Task VerifyIsolationAsync(CancellationToken cancellationToken)
    {
        JsonElement result = await InvokeAsync("config/read", new
        {
            cwd = Path.GetFullPath(AiAssistantProfile.KnowledgeRoot),
            includeLayers = false
        }, cancellationToken).ConfigureAwait(false);
        JsonElement config = RequireObject(result, "config");
        VerifyIsolation(config, _disabledMcpServers);
    }

    internal static void VerifyIsolation(JsonElement config, IReadOnlyList<string> disabledMcpServers)
    {
        var violations = new List<string>();
        if (ReadString(config, "web_search") != "disabled") violations.Add("web search");
        if (!config.TryGetProperty("features", out JsonElement features) || features.ValueKind != JsonValueKind.Object)
            violations.Add("feature inventory");
        else
            violations.AddRange(AiAssistantProfile.DisabledFeatures.Where(feature => !IsFalse(features, feature)));
        if (!config.TryGetProperty("mcp_servers", out JsonElement servers) || servers.ValueKind != JsonValueKind.Object)
            violations.Add("integration inventory");
        if (violations.Count > 0)
            throw new CodexCompatibilityException($"Codex did not apply these AI Assistant isolation settings: {string.Join(", ", violations)}.");
        foreach (JsonProperty configuredServer in servers.EnumerateObject())
        {
            JsonElement server = configuredServer.Value;
            if (server.ValueKind != JsonValueKind.Object ||
                !server.TryGetProperty("enabled", out JsonElement enabled) || enabled.ValueKind != JsonValueKind.False)
                throw new CodexCompatibilityException("Codex did not disable every configured integration for the AI Assistant.");
        }
        if (disabledMcpServers.Any(name => !servers.TryGetProperty(name, out _)))
            throw new CodexCompatibilityException("Codex did not retain the complete disabled integration inventory.");
    }

    public async Task<CodexThreadSummary?> FindAssistantAsync(CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(AiAssistantProfile.KnowledgeRoot);
        JsonElement result = await InvokeAsync("thread/list", new
        {
            limit = 50,
            sortKey = "updated_at",
            sortDirection = "desc",
            cwd = root
        }, cancellationToken).ConfigureAwait(false);
        if (!result.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            throw ShapeError("thread/list");
        List<CodexThreadSummary> candidates = [.. SelectAssistantThreads(data.EnumerateArray().Select(ParseSummary), root)];
        if (candidates.Count == 0) return null;

        CodexThreadSummary selected = candidates[0];
        foreach (CodexThreadSummary stale in candidates.Skip(1))
            await SetThreadNameAsync(stale.Id, PreviousThreadName(), cancellationToken).ConfigureAwait(false);
        if (!string.Equals(selected.Title, AiAssistantProfile.ThreadName, StringComparison.Ordinal))
            await SetThreadNameAsync(selected.Id, AiAssistantProfile.ThreadName, cancellationToken).ConfigureAwait(false);
        return selected with { Title = AiAssistantProfile.ThreadName };
    }

    internal static CodexThreadSummary? SelectAssistantThread(IEnumerable<CodexThreadSummary> threads, string root) =>
        SelectAssistantThreads(threads, root).FirstOrDefault();

    private static IEnumerable<CodexThreadSummary> SelectAssistantThreads(IEnumerable<CodexThreadSummary> threads, string root)
    {
        string normalizedRoot = Path.GetFullPath(root);
        foreach (CodexThreadSummary thread in threads)
        {
            bool recoverableTitle = string.Equals(thread.Title, AiAssistantProfile.ThreadName, StringComparison.Ordinal) ||
                string.Equals(thread.Title, "Untitled assistant", StringComparison.Ordinal);
            if (!recoverableTitle) continue;
            bool matchesRoot = false;
            try
            {
                matchesRoot = string.Equals(Path.GetFullPath(thread.WorkingDirectory), normalizedRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
            if (matchesRoot) yield return thread;
        }
    }

    public async Task<CodexThreadSummary> StartAssistantAsync(CancellationToken cancellationToken)
    {
        JsonElement result = await InvokeAsync("thread/start", ThreadConfiguration(), cancellationToken).ConfigureAwait(false);
        CodexThreadSummary thread = ParseSummary(RequireObject(result, "thread"));
        _activeThreads.Clear();
        _activeThreads[thread.Id] = 0;
        await InvokeAsync("thread/name/set", new { threadId = thread.Id, name = AiAssistantProfile.ThreadName }, cancellationToken).ConfigureAwait(false);
        return thread with { Title = AiAssistantProfile.ThreadName };
    }

    public async Task<CodexThreadSummary> ReplaceAssistantAsync(string previousThreadId, CancellationToken cancellationToken)
    {
        CodexThreadSummary replacement = await StartAssistantAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(replacement.Id, previousThreadId, StringComparison.Ordinal))
            throw new CodexCompatibilityException("Codex reused the previous Assistant thread during reset.");
        await SetThreadNameAsync(previousThreadId, PreviousThreadName(), cancellationToken).ConfigureAwait(false);
        return replacement;
    }

    public async Task ResumeAssistantAsync(string threadId, CancellationToken cancellationToken)
    {
        await InvokeAsync("thread/resume", new
        {
            threadId,
            approvalPolicy = "never",
            sandbox = "read-only",
            developerInstructions = AiAssistantProfile.DeveloperInstructions,
            personality = "friendly",
            dynamicTools = AiAssistantReadTools.Specifications,
            excludeTurns = true
        }, cancellationToken).ConfigureAwait(false);
        _activeThreads.Clear();
        _activeThreads[threadId] = 0;
    }

    private async Task SetThreadNameAsync(string threadId, string name, CancellationToken cancellationToken) =>
        _ = await InvokeAsync("thread/name/set", new { threadId, name }, cancellationToken).ConfigureAwait(false);

    public async Task<CodexThreadDetail> ReadThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        JsonElement result = await InvokeAsync("thread/read", new { threadId, includeTurns = false }, cancellationToken).ConfigureAwait(false);
        JsonElement thread = RequireObject(result, "thread");
        JsonElement turnResult;
        try
        {
            turnResult = await InvokeAsync("thread/turns/list", new
            {
                threadId,
                limit = AiAssistantProtocol.MaximumTranscriptTurns,
                sortDirection = "desc"
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (CodexCompatibilityException exception) when (IsEmptyThreadHistory(exception))
        {
            return new(ParseSummary(thread), []);
        }
        if (!turnResult.TryGetProperty("data", out JsonElement turns) || turns.ValueKind != JsonValueKind.Array)
            throw ShapeError("thread/turns/list");
        var entries = new List<CodexTranscriptEntry>();
        int fallbackId = 0;
        foreach (JsonElement turn in turns.EnumerateArray().Reverse())
        {
            if (!turn.TryGetProperty("items", out JsonElement items) || items.ValueKind != JsonValueKind.Array)
                throw ShapeError("thread/turns/list");
            foreach (JsonElement item in items.EnumerateArray())
            {
                string? type = ReadString(item, "type");
                string itemId = ReadString(item, "id") ?? $"snapshot-{++fallbackId}";
                if (type == "agentMessage" && ReadString(item, "text") is { Length: > 0 } assistant)
                    entries.Add(new(itemId, "assistant", BoundText(assistant)));
                else if (type == "userMessage" && ReadUserText(item) is { Length: > 0 } user)
                    entries.Add(new(itemId, "user", BoundText(user)));
            }
        }
        return new(ParseSummary(thread), [.. entries.TakeLast(AiAssistantProtocol.MaximumTranscriptMessages)]);
    }

    private static bool IsEmptyThreadHistory(CodexCompatibilityException exception) =>
        exception.Message.StartsWith("Codex method 'thread/turns/list' failed:", StringComparison.Ordinal) &&
        exception.Message.Contains("invalid paginated history lineage", StringComparison.OrdinalIgnoreCase) &&
        exception.Message.Contains("missing source rollout", StringComparison.OrdinalIgnoreCase);

    public async Task<CodexTurnHandle> StartTurnAsync(string threadId, string question, CancellationToken cancellationToken)
    {
        lock (_notificationGate) _heldNotificationThreads.Add(threadId);
        try
        {
            JsonElement result = await InvokeAsync("turn/start", new
            {
                threadId,
                input = new object[]
                {
                    new { type = "text", text = question, text_elements = Array.Empty<object>() },
                    new { type = "skill", name = "voltura-air-assistant", path = Path.GetFullPath(AiAssistantProfile.SkillPath) }
                },
                approvalPolicy = "never",
                sandboxPolicy = new { type = "readOnly", networkAccess = false }
            }, cancellationToken).ConfigureAwait(false);
            JsonElement turn = RequireObject(result, "turn");
            return new(threadId, ReadString(turn, "id") ?? throw ShapeError("turn/start"));
        }
        catch
        {
            ReleaseTurnNotifications(threadId);
            throw;
        }
    }

    public void ReleaseTurnNotifications(string threadId)
    {
        while (true)
        {
            List<(string Method, JsonElement Parameters)> pending;
            lock (_notificationGate)
            {
                pending = [.. _heldNotifications.Where(item => ReadString(item.Parameters, "threadId") == threadId)];
                _heldNotifications.RemoveAll(item => ReadString(item.Parameters, "threadId") == threadId);
                if (pending.Count == 0)
                {
                    _heldNotificationThreads.Remove(threadId);
                    return;
                }
            }
            foreach ((string method, JsonElement parameters) in pending)
                DispatchNotification(method, parameters);
        }
    }

    private static object ThreadConfiguration() => new
    {
        cwd = Path.GetFullPath(AiAssistantProfile.KnowledgeRoot),
        ephemeral = false,
        approvalPolicy = "never",
        sandbox = "read-only",
        developerInstructions = AiAssistantProfile.DeveloperInstructions,
        personality = "friendly",
        serviceName = "voltura_air_assistant",
        dynamicTools = AiAssistantReadTools.Specifications,
        config = IsolatedConfiguration()
    };

    private static object IsolatedConfiguration() => new
    {
        web_search = "disabled",
        apps = new { },
        plugins = new { },
        features = new
        {
            apps = false,
            auth_elicitation = false,
            browser_use = false,
            browser_use_external = false,
            browser_use_full_cdp_access = false,
            computer_use = false,
            hooks = false,
            image_generation = false,
            in_app_browser = false,
            multi_agent = false,
            plugin_sharing = false,
            plugins = false,
            recommended_plugins = false,
            remote_plugin = false,
            shell_snapshot = false,
            shell_tool = false,
            skill_mcp_dependency_install = false,
            tool_call_mcp_elicitation = false,
            unified_exec = false
        }
    };

    private Task<object?> HandleServerRequestAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (method != "item/tool/call") throw new NotSupportedException();
        string? threadId = ReadString(parameters, "threadId");
        string? tool = ReadString(parameters, "tool");
        if (threadId is null || tool is null || !_activeThreads.ContainsKey(threadId) ||
            !parameters.TryGetProperty("arguments", out JsonElement arguments))
            return Task.FromResult<object?>(AiAssistantReadTools.Failure("The requested read-only operation was rejected."));
        return AiAssistantReadTools.InvokeAsync(tool, arguments, cancellationToken);
    }

    private Task<JsonElement> InvokeAsync(string method, object parameters, CancellationToken cancellationToken) =>
        _rpc.RequestAsync(method, parameters, RequestTimeout, cancellationToken);

    private void ObserveNotification(string method, JsonElement parameters)
    {
        string? notificationThread = ReadString(parameters, "threadId");
        lock (_notificationGate)
        {
            if (notificationThread is not null &&
                _heldNotificationThreads.Contains(notificationThread) &&
                method is "item/completed" or "turn/completed")
            {
                if (_heldNotifications.Count >= MaximumHeldNotifications)
                    throw new CodexCompatibilityException("Codex produced too many notifications before confirming the turn.");
                _heldNotifications.Add((method, parameters.Clone()));
                return;
            }
        }
        DispatchNotification(method, parameters);
    }

    private void DispatchNotification(string method, JsonElement parameters)
    {
        try
        {
            if (method == "item/completed" &&
                ReadString(parameters, "threadId") is { } itemThread &&
                ReadString(parameters, "turnId") is { } itemTurn &&
                parameters.TryGetProperty("item", out JsonElement completedItem) &&
                completedItem.ValueKind == JsonValueKind.Object &&
                ReadString(completedItem, "type") == "agentMessage" &&
                ReadString(completedItem, "id") is { } completedItemId &&
                ReadString(completedItem, "text") is { Length: > 0 } completedText)
                AgentMessageCompleted?.Invoke(itemThread, itemTurn, completedItemId, BoundText(completedText));
            else if (method == "turn/completed" &&
                ReadString(parameters, "threadId") is { } completedThread &&
                parameters.TryGetProperty("turn", out JsonElement turn) && turn.ValueKind == JsonValueKind.Object &&
                ReadString(turn, "id") is { } completedTurn)
                TurnCompleted?.Invoke(completedThread, completedTurn, ReadStatus(turn));
        }
        catch (InvalidOperationException) { }
    }

    private static CodexThreadSummary ParseSummary(JsonElement thread)
    {
        string id = ReadString(thread, "id") ?? throw ShapeError("thread");
        string cwd = ReadString(thread, "cwd") ?? AiAssistantProfile.KnowledgeRoot;
        string title = ReadString(thread, "name") ?? "Untitled assistant";
        return new(id, title, cwd);
    }

    private static string? ReadUserText(JsonElement item)
    {
        if (!item.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array) return null;
        return string.Join(Environment.NewLine, content.EnumerateArray()
            .Where(part => ReadString(part, "type") == "text")
            .Select(part => ReadString(part, "text"))
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static JsonElement RequireObject(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw ShapeError(property);
    private static string? ReadString(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement candidate) && candidate.ValueKind == JsonValueKind.String
            ? candidate.GetString()
            : null;
    private static bool IsFalse(JsonElement value, string property) =>
        value.TryGetProperty(property, out JsonElement candidate) && candidate.ValueKind == JsonValueKind.False;
    private static string ReadStatus(JsonElement turn) =>
        turn.TryGetProperty("status", out JsonElement status)
            ? status.ValueKind == JsonValueKind.String ? status.GetString() ?? "unknown" : ReadString(status, "type") ?? "unknown"
            : "unknown";
    internal static string BoundText(string value) =>
        AiAssistantProtocol.BoundWithEllipsis(value, AiAssistantProtocol.MaximumMessageCharacters);
    private static string PreviousThreadName() =>
        $"{AiAssistantProfile.ThreadName} — previous {DateTimeOffset.Now:g}";
    private static CodexCompatibilityException ShapeError(string method) => new($"Codex method '{method}' returned an unfamiliar response.");

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _rpc.NotificationReceived -= ObserveNotification;
        _rpc.ServerRequestReceived = null;
        if (_process is not null) await _process.DisposeAsync().ConfigureAwait(false);
        else await _rpc.DisposeAsync().ConfigureAwait(false);
    }
}
