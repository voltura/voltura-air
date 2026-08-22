using System.Net;
using System.Net.Http;
using VolturaAir.Host.Features.UsageTelemetry;

namespace VolturaAir.Host.Tests;

internal static class UsageTelemetryTestSupport
{
    public static readonly Guid InstallationId =
        Guid.Parse("0c99c983-09f8-42af-879c-42b51d625c69");

    public static UsageTelemetryService CreateService(
        FakeSettings settings,
        HttpMessageHandler handler,
        IReadOnlyList<TimeSpan>? retryDelays = null,
        IAppLog? appLog = null,
        TimeSpan? requestTimeout = null,
        bool networkAllowed = true,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null) =>
        new(
            settings,
            appLog ?? NullAppLog.Instance,
            new UsageTelemetryServiceOptions
            {
                HttpHandler = handler,
                NetworkAllowed = networkAllowed,
                InitialDelay = static () => TimeSpan.FromDays(1),
                DelayAsync = delayAsync ?? (static (delay, cancellationToken) =>
                    delay == TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken)),
                RetryDelays = retryDelays ?? [],
                RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5),
                BatchIdFactory = () => Guid.Parse("1f2e0a85-4115-40f2-b8cc-e46160186cb3")
            });

    public static FakeSettings EnabledSettings() => new(new(
        UsageStatisticsDistribution.Installed,
        UsageStatisticsConsent.Allowed,
        InstallationId));

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for telemetry state.");
            await Task.Delay(10);
        }
    }

    public static HttpResponseMessage Accepted() => new(HttpStatusCode.Accepted)
    {
        Content = new StringContent("{\"schemaVersion\":1,\"status\":\"accepted\"}")
    };
}

internal sealed class FakeSettings(UsageStatisticsPersistentState initial) : IUsageStatisticsSettings
{
    private int _nextIdentity;

    public UsageStatisticsPersistentState State { get; private set; } = initial;

    public bool DenyIdentityRemoved { get; init; } = true;

    public int DenyFailuresRemaining { get; set; }

    public int DenyCalls { get; private set; }

    public bool StaleIdentityRemoved { get; init; } = true;

    public int RepairCalls { get; private set; }

    public int DeleteStaleCalls { get; private set; }

    public UsageStatisticsPersistentState Read() => State;

    public UsageStatisticsSettingsResult RepairAllowedIdentity()
    {
        RepairCalls++;
        var identity = State.InstallationId ?? NewIdentity();
        State = State with { InstallationId = identity };
        return new(true, identity);
    }

    public UsageStatisticsSettingsResult AllowWithNewIdentity()
    {
        var identity = NewIdentity();
        State = State with { Consent = UsageStatisticsConsent.Allowed, InstallationId = identity };
        return new(true, identity);
    }

    public UsageStatisticsSettingsResult DenyAndDeleteIdentity()
    {
        DenyCalls++;
        if (DenyFailuresRemaining > 0)
        {
            DenyFailuresRemaining--;
            return new UsageStatisticsSettingsResult(false);
        }

        State = State with
        {
            Consent = UsageStatisticsConsent.Denied,
            InstallationId = DenyIdentityRemoved ? null : State.InstallationId
        };
        return new(true, IdentityRemoved: DenyIdentityRemoved);
    }

    public bool DeleteStaleIdentity()
    {
        DeleteStaleCalls++;
        if (StaleIdentityRemoved)
        {
            State = State with { InstallationId = null };
        }
        return StaleIdentityRemoved;
    }

    private Guid NewIdentity()
    {
        _nextIdentity++;
        return Guid.Parse($"{_nextIdentity + 1:x8}-89ab-4cde-8fab-0123456789ab");
    }
}

internal sealed class RecordingAppLog : IAppLog
{
    private readonly object _gate = new();
    private readonly List<AppLogEntry> _entries = [];

    public string LogDirectory => string.Empty;

    public IReadOnlyList<AppLogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public void Write(AppLogEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    public AppLogReadResult Read(AppLogQuery query) => new(true, []);

    public AppLogDeleteResult DeleteAll() => new(true, 0);
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<int, CancellationToken, Task<HttpResponseMessage>> _respond;
    private readonly SemaphoreSlim _requests = new(0);
    private int _attempts;

    public RecordingHandler(Func<int, HttpResponseMessage> respond)
        : this((attempt, _) => Task.FromResult(respond(attempt)))
    {
    }

    public RecordingHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public List<string> Bodies { get; } = [];

    public async Task WaitForCountAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            lock (Bodies)
            {
                if (Bodies.Count >= count)
                {
                    return;
                }
            }

            var remaining = deadline - DateTime.UtcNow;
            Assert.True(remaining > TimeSpan.Zero, "Timed out waiting for a telemetry request.");
            Assert.True(await _requests.WaitAsync(remaining));
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Bodies)
        {
            Bodies.Add(body);
        }
        _requests.Release();
        return await _respond(Interlocked.Increment(ref _attempts), cancellationToken);
    }
}
