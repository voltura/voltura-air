namespace VolturaAir.Host;

public enum PowerPointDiscoveryState
{
    Ready,
    Unavailable,
    Inaccessible,
    Busy
}

public sealed record PowerPointPresentationSnapshot(
    string RuntimePresentationId,
    string Name,
    bool IsPresenting,
    int SlideCount,
    int? CurrentSlideIndex,
    int? CurrentShowPosition,
    string SlideShowState,
    string? SourcePath = null);

public sealed record PowerPointAutomationSnapshot(
    PowerPointDiscoveryState State,
    IReadOnlyList<PowerPointPresentationSnapshot> Presentations)
{
    public static PowerPointAutomationSnapshot Unavailable { get; } =
        new(PowerPointDiscoveryState.Unavailable, []);
}

public sealed record PowerPointCommand(
    string Action,
    string? RuntimePresentationId = null,
    int? SlideNumber = null,
    bool? Enabled = null,
    string? SourcePath = null);

public sealed record PowerPointAutomationResult(
    bool Succeeded,
    string? Code,
    string Message,
    PowerPointAutomationSnapshot Snapshot,
    PowerPointPresentationSnapshot? Presentation = null);

public interface IPowerPointAutomationService : IAsyncDisposable
{
    PowerPointAutomationSnapshot Snapshot { get; }

    event EventHandler? SnapshotChanged;

    Task<PowerPointAutomationResult> RefreshAsync(CancellationToken cancellationToken);

    Task<PowerPointAutomationResult> ExecuteAsync(
        PowerPointCommand command,
        CancellationToken cancellationToken);
}

internal sealed class InertPowerPointAutomationService : IPowerPointAutomationService
{
    internal static InertPowerPointAutomationService Instance { get; } = new();

    private InertPowerPointAutomationService()
    {
    }

    public PowerPointAutomationSnapshot Snapshot => PowerPointAutomationSnapshot.Unavailable;

    public event EventHandler? SnapshotChanged
    {
        add { }
        remove { }
    }

    public Task<PowerPointAutomationResult> RefreshAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Unavailable());

    public Task<PowerPointAutomationResult> ExecuteAsync(
        PowerPointCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(Unavailable());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static PowerPointAutomationResult Unavailable() =>
        new(
            false,
            "powerpoint-unavailable",
            "Open PowerPoint and a presentation on the PC, then refresh.",
            PowerPointAutomationSnapshot.Unavailable);
}
