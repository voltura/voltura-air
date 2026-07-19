using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace VolturaAir.Host;

internal sealed class TrayAwakeMenuController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IAwakeService _awakeService;
    private readonly Action<AwakeOperationResult> _reportFailure;
    // MenuItem owns and disposes these child items after the context menu is disposed.
#pragma warning disable CA2213
    private readonly Forms.ToolStripMenuItem _offItem;
    private readonly Forms.ToolStripMenuItem _timedItem;
    private readonly Forms.ToolStripMenuItem _expirationItem;
    private readonly Forms.ToolStripMenuItem _indefiniteItem;
    private readonly Forms.ToolStripMenuItem _keepScreenOnItem;
#pragma warning restore CA2213
    private bool _disposed;

    public TrayAwakeMenuController(
        Dispatcher dispatcher,
        IAwakeService awakeService,
        Action showPreferences,
        Action<AwakeOperationResult> reportFailure)
    {
        _dispatcher = dispatcher;
        _awakeService = awakeService;
        _reportFailure = reportFailure;

        MenuItem = new Forms.ToolStripMenuItem("Keep awake");
        _offItem = new Forms.ToolStripMenuItem(
            "Use selected power plan",
            null,
            (_, _) => RunProtected(() => Apply(_awakeService.SetOff())));
        _timedItem = new Forms.ToolStripMenuItem("For an interval");
        AddInterval("30 minutes", 30);
        AddInterval("1 hour", 60);
        AddInterval("2 hours", 120);
        _expirationItem = new Forms.ToolStripMenuItem("Until...", null, (_, _) => RunProtected(showPreferences));
        _indefiniteItem = new Forms.ToolStripMenuItem(
            "Indefinitely",
            null,
            (_, _) => RunProtected(() => Apply(_awakeService.SetIndefinite())));
        _keepScreenOnItem = new Forms.ToolStripMenuItem(
            "Keep screen on",
            null,
            (_, _) => RunProtected(() => Apply(_awakeService.SetKeepScreenOn(!_awakeService.State.KeepScreenOn))));

        MenuItem.DropDownItems.Add(_offItem);
        MenuItem.DropDownItems.Add(_timedItem);
        MenuItem.DropDownItems.Add(_expirationItem);
        MenuItem.DropDownItems.Add(_indefiniteItem);
        MenuItem.DropDownItems.Add(new Forms.ToolStripSeparator());
        MenuItem.DropDownItems.Add(_keepScreenOnItem);

        ApplyState();
        _awakeService.StateChanged += OnStateChanged;
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
    }

    private void AddInterval(string label, int minutes)
    {
        _timedItem.DropDownItems.Add(
            label,
            null,
            (_, _) => RunProtected(() => Apply(_awakeService.SetTimed(TimeSpan.FromMinutes(minutes)))));
    }

    private void Apply(AwakeOperationResult result)
    {
        if (!result.Succeeded)
        {
            _reportFailure(result);
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

    private void ApplyState()
    {
        var state = _awakeService.State;
        _offItem.Checked = state.Mode == AwakeMode.Off;
        _timedItem.Checked = state.Mode == AwakeMode.Timed;
        _expirationItem.Checked = state.Mode == AwakeMode.Expiration;
        _indefiniteItem.Checked = state.Mode == AwakeMode.Indefinite;
        _keepScreenOnItem.Checked = state.KeepScreenOn;
        _keepScreenOnItem.Enabled = state.IsActive;
    }

    private static void RunProtected(Action action)
    {
        if (!HostUiInputGuard.IsRecentProtectedClientInput())
        {
            action();
        }
    }
}
