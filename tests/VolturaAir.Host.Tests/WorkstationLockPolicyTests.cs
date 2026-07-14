using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class WorkstationLockPolicyTests
{
    [Fact]
    public void MissingKeyReportsNoExplicitBlock()
    {
        var policy = CreatePolicy(new LockPolicyRegistryValue(false, false, 0));

        Assert.Equal(WorkstationLockPolicyState.NotExplicitlyDisabled, policy.GetStatus().State);
    }

    [Fact]
    public void MissingValueReportsNoExplicitBlock()
    {
        var policy = CreatePolicy(new LockPolicyRegistryValue(false, false, 0));

        Assert.Equal(WorkstationLockPolicyState.NotExplicitlyDisabled, policy.GetStatus().State);
    }

    [Theory]
    [InlineData(0, WorkstationLockPolicyState.NotExplicitlyDisabled)]
    [InlineData(1, WorkstationLockPolicyState.Disabled)]
    [InlineData(42, WorkstationLockPolicyState.Disabled)]
    public void ReadsDisableLockWorkstationDword(int value, WorkstationLockPolicyState expected)
    {
        var policy = CreatePolicy(new LockPolicyRegistryValue(true, true, value));

        Assert.Equal(expected, policy.GetStatus().State);
    }

    [Fact]
    public void NonDwordPolicyValueIsReportedAsUnavailable()
    {
        var policy = CreatePolicy(new LockPolicyRegistryValue(true, false, 0));

        var status = policy.GetStatus();

        Assert.Equal(WorkstationLockPolicyState.Unavailable, status.State);
        Assert.Contains("REG_DWORD", status.Diagnostic);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnableWritesDwordZeroBroadcastsPolicyChangeAndReadsBack(bool valueInitiallyExists)
    {
        var value = new LockPolicyRegistryValue(valueInitiallyExists, valueInitiallyExists, 1);
        var writeCount = 0;
        var broadcastCount = 0;
        var policy = new WorkstationLockPolicy(
            () => value,
            () =>
            {
                writeCount += 1;
                value = new LockPolicyRegistryValue(true, true, 0);
            },
            () => broadcastCount += 1);
        var changed = false;
        policy.Changed += (_, _) => changed = true;

        var result = policy.TryEnable();

        Assert.True(result.Succeeded);
        Assert.True(changed);
        Assert.Equal(1, writeCount);
        Assert.Equal(1, broadcastCount);
        Assert.Equal(new LockPolicyRegistryValue(true, true, 0), value);
    }

    [Fact]
    public void EnableHandlesRegistryWriteFailure()
    {
        var appLog = new RecordingAppLog();
        var policy = new WorkstationLockPolicy(
            () => new LockPolicyRegistryValue(false, false, 0),
            () => throw new UnauthorizedAccessException("blocked"),
            () => throw new InvalidOperationException("must not broadcast"),
            appLog);

        var result = policy.TryEnable();

        Assert.False(result.Succeeded);
        Assert.Equal(
            "Windows or an administrator protects this setting. Voltura Air cannot change it for this user.",
            result.Message);
        Assert.Contains(appLog.Entries, entry =>
            entry.Event == "host_action" &&
            entry.Action == "enable_windows_locking" &&
            entry.Outcome == "failed" &&
            entry.Code == "VAIR-LOCK-POLICY-ACCESS-DENIED" &&
            entry.Detail == "blocked");
    }

    [Fact]
    public void EnableReportsFailureWhenDwordZeroCannotBeReadBack()
    {
        var policy = new WorkstationLockPolicy(
            () => new LockPolicyRegistryValue(true, true, 1),
            () => { },
            () => { });

        var result = policy.TryEnable();

        Assert.False(result.Succeeded);
        Assert.Equal(
            "Windows did not keep the locking setting. An administrator or device policy may control it.",
            result.Message);
    }

    private static WorkstationLockPolicy CreatePolicy(LockPolicyRegistryValue value)
    {
        return new WorkstationLockPolicy(() => value, () => { }, () => { });
    }

    private sealed class RecordingAppLog : IAppLog
    {
        public event EventHandler? Changed;

        public string LogDirectory => string.Empty;

        public List<AppLogEntry> Entries { get; } = new();

        public void Write(AppLogEntry entry)
        {
            Entries.Add(entry);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public AppLogReadResult Read(AppLogQuery query) => new(true, []);

        public AppLogDeleteResult DeleteAll() => new(true, 0);
    }
}
