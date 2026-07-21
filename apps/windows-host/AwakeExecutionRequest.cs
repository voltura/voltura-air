namespace VolturaAir.Host;

internal enum AwakeMutationKind
{
    Off,
    Indefinite,
    Timed,
    Expiration,
    KeepScreenOn,
    Reapply
}

internal readonly record struct AwakeMutation(
    AwakeMutationKind Kind,
    int IntervalMinutes = 0,
    DateTimeOffset? ExpiresAt = null,
    bool KeepScreenOnValue = false)
{
    public static AwakeMutation Off { get; } = new(AwakeMutationKind.Off);
    public static AwakeMutation Indefinite { get; } = new(AwakeMutationKind.Indefinite);
    public static AwakeMutation Reapply { get; } = new(AwakeMutationKind.Reapply);
    public static AwakeMutation Timed(int minutes) => new(AwakeMutationKind.Timed, IntervalMinutes: minutes);
    public static AwakeMutation Expiration(DateTimeOffset expiresAt) => new(AwakeMutationKind.Expiration, ExpiresAt: expiresAt);
    public static AwakeMutation KeepScreenOn(bool value) => new(AwakeMutationKind.KeepScreenOn, KeepScreenOnValue: value);

    public bool CommitState => Kind != AwakeMutationKind.Reapply;
    public bool ForceApply => Kind == AwakeMutationKind.Reapply;

    public AwakeState Apply(AwakeState state) => Kind switch
    {
        AwakeMutationKind.Off => state with { Mode = AwakeMode.Off, ExpiresAt = null },
        AwakeMutationKind.Indefinite => state with { Mode = AwakeMode.Indefinite, ExpiresAt = null },
        AwakeMutationKind.Timed => state with
        {
            Mode = AwakeMode.Timed,
            IntervalMinutes = IntervalMinutes,
            ExpiresAt = DateTimeOffset.Now.AddMinutes(IntervalMinutes)
        },
        AwakeMutationKind.Expiration => state with { Mode = AwakeMode.Expiration, ExpiresAt = ExpiresAt },
        AwakeMutationKind.KeepScreenOn => state with { KeepScreenOn = KeepScreenOnValue },
        _ => state
    };
}

internal sealed class AwakeExecutionRequest(AwakeMutation mutation)
{
    private int _state;

    public AwakeMutation Mutation { get; } = mutation;
    public TaskCompletionSource<AwakeOperationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool TryStart() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

    public bool TryClaimCompletion() => Interlocked.CompareExchange(ref _state, 3, 1) == 1;

    public void PublishCompletion(AwakeOperationResult result) => Completion.TrySetResult(result);

    public bool TryAbandon(AwakeOperationResult result)
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if (state is 2 or 3)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _state, 2, state) == state)
            {
                Completion.TrySetResult(result);
                return true;
            }
        }
    }
}
