using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace VolturaAir.Host;

internal sealed class TrayAwakeMenuController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IAwakeService _awakeService;
    private readonly IActivitySimulationService _activitySimulationService;
    private readonly Action<AwakeOperationResult> _reportFailure;
    private readonly Action<string> _reportActivityFailure;
    // MenuItem owns and disposes these child items after the context menu is disposed.
#pragma warning disable CA2213
    private readonly Forms.ToolStripMenuItem _offItem;
    private readonly Forms.ToolStripMenuItem _timedItem;
    private readonly Forms.ToolStripMenuItem _expirationItem;
    private readonly Forms.ToolStripMenuItem _indefiniteItem;
    private readonly Forms.ToolStripMenuItem _keepScreenOnItem;
    private readonly Forms.ToolStripMenuItem _simulateActivityItem;
#pragma warning restore CA2213
    private bool _disposed;
    private int _operationRunning;

    public TrayAwakeMenuController(
        Dispatcher dispatcher,
        IAwakeService awakeService,
        IActivitySimulationService activitySimulationService,
        Action showPreferences,
        Action<AwakeOperationResult> reportFailure,
        Action<string> reportActivityFailure)
    {
        _dispatcher = dispatcher;
        _awakeService = awakeService;
        _activitySimulationService = activitySimulationService;
        _reportFailure = reportFailure;
        _reportActivityFailure = reportActivityFailure;

        MenuItem = new Forms.ToolStripMenuItem("Keep awake");
        _offItem = new Forms.ToolStripMenuItem(
            "Use selected power plan",
            null,
            async (_, _) => await RunProtectedAsync(() => _awakeService.SetOffAsync()));
        _timedItem = new Forms.ToolStripMenuItem("For an interval");
        AddInterval("30 minutes", 30);
        AddInterval("1 hour", 60);
        AddInterval("2 hours", 120);
        _expirationItem = new Forms.ToolStripMenuItem("Until...", null, (_, _) => RunProtected(showPreferences));
        _indefiniteItem = new Forms.ToolStripMenuItem(
            "Indefinitely",
            null,
            async (_, _) => await RunProtectedAsync(() => _awakeService.SetIndefiniteAsync()));
        _keepScreenOnItem = new Forms.ToolStripMenuItem(
            "Keep screen on",
            null,
            async (_, _) => await RunProtectedAsync(() => _awakeService.SetKeepScreenOnAsync(!_awakeService.State.KeepScreenOn)));
        _simulateActivityItem = new Forms.ToolStripMenuItem(
            "Simulate activity every 59 seconds",
            null,
            async (_, _) => await RunActivityProtectedAsync(!_activitySimulationService.Enabled));

        MenuItem.DropDownItems.Add(_offItem);
        MenuItem.DropDownItems.Add(_timedItem);
        MenuItem.DropDownItems.Add(_expirationItem);
        MenuItem.DropDownItems.Add(_indefiniteItem);
        MenuItem.DropDownItems.Add(new Forms.ToolStripSeparator());
        MenuItem.DropDownItems.Add(_keepScreenOnItem);
        MenuItem.DropDownItems.Add(_simulateActivityItem);

        ApplyState();
        _awakeService.StateChanged += OnStateChanged;
        _activitySimulationService.StateChanged += OnStateChanged;
        _activitySimulationService.FailureStreakStarted += OnActivityFailureStreakStarted;
    }

    public Forms.ToolStripMenuItem MenuItem { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _awakeService.StateChanged -= OnStateChanged;
        _activitySimulationService.StateChanged -= OnStateChanged;
        _activitySimulationService.FailureStreakStarted -= OnActivityFailureStreakStarted;
    }

    private void AddInterval(string label, int minutes)
    {
        _timedItem.DropDownItems.Add(
            label,
            null,
            async (_, _) => await RunProtectedAsync(() => _awakeService.SetTimedAsync(TimeSpan.FromMinutes(minutes))));
    }

    private async Task RunProtectedAsync(Func<Task<AwakeOperationResult>> operation)
    {
        if (HostUiInputGuard.IsRecentProtectedClientInput() || Interlocked.Exchange(ref _operationRunning, 1) != 0)
        {
            return;
        }

        MenuItem.Enabled = false;
        try
        {
            var result = await operation();
            if (!result.Succeeded)
            {
                _reportFailure(result);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _reportFailure(new AwakeOperationResult(false, exception.Message, AwakeOperationFailure.Unavailable));
        }
        finally
        {
            Volatile.Write(ref _operationRunning, 0);
            if (!_disposed)
            {
                MenuItem.Enabled = true;
            }
        }
    }

    private async Task RunActivityProtectedAsync(bool enabled)
    {
        if (HostUiInputGuard.IsRecentProtectedClientInput() || Interlocked.Exchange(ref _operationRunning, 1) != 0)
        {
            return;
        }

        MenuItem.Enabled = false;
        try
        {
            var result = await _activitySimulationService.SetEnabledAsync(enabled);
            if (!result.Succeeded)
            {
                _reportActivityFailure(result.Error ?? "Simulated activity could not be updated.");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _reportActivityFailure($"Simulated activity could not be updated: {exception.Message}");
        }
        finally
        {
            Volatile.Write(ref _operationRunning, 0);
            if (!_disposed)
            {
                MenuItem.Enabled = true;
            }
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                ApplyState();
            }
        });
    }

    private void OnActivityFailureStreakStarted(object? sender, ActivitySimulationFailureEventArgs e)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                _reportActivityFailure(e.Error);
            }
        });
    }

    private void ApplyState()
    {
        var state = _awakeService.State;
        _offItem.Checked = state.Mode == AwakeMode.Off;
        _timedItem.Checked = state.Mode == AwakeMode.Timed;
        _expirationItem.Checked = state.Mode == AwakeMode.Expiration;
        _indefiniteItem.Checked = state.Mode == AwakeMode.Indefinite;
        _keepScreenOnItem.Checked = state.KeepScreenOn;
        _keepScreenOnItem.Enabled = state.IsActive;
        _simulateActivityItem.Checked = _activitySimulationService.Enabled;
    }

    private static void RunProtected(Action action)
    {
        if (!HostUiInputGuard.IsRecentProtectedClientInput())
        {
            action();
        }
    }
}
