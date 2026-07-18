using Forms = System.Windows.Forms;

namespace VolturaAir.Host;

internal sealed partial class WpfTrayApplicationContext
{
    private void BuildAwakeMenu()
    {
        var awakeMenu = new Forms.ToolStripMenuItem("Keep awake");
        _awakeOffItem = new Forms.ToolStripMenuItem("Use selected power plan", null, (_, _) => RunTrayCommand(() => ApplyAwake(_awakeService.SetOff())));
        _awakeTimedItem = new Forms.ToolStripMenuItem("For an interval");
        AddAwakeInterval(_awakeTimedItem, "30 minutes", 30);
        AddAwakeInterval(_awakeTimedItem, "1 hour", 60);
        AddAwakeInterval(_awakeTimedItem, "2 hours", 120);
        _awakeExpirationItem = new Forms.ToolStripMenuItem("Until...", null, (_, _) => RunTrayCommand(_mainWindow.ShowAwakePreferences));
        _awakeIndefiniteItem = new Forms.ToolStripMenuItem("Indefinitely", null, (_, _) => RunTrayCommand(() => ApplyAwake(_awakeService.SetIndefinite())));
        _awakeKeepScreenOnItem = new Forms.ToolStripMenuItem("Keep screen on", null, (_, _) => RunTrayCommand(() =>
            ApplyAwake(_awakeService.SetKeepScreenOn(!_awakeService.State.KeepScreenOn))));

        awakeMenu.DropDownItems.Add(_awakeOffItem);
        awakeMenu.DropDownItems.Add(_awakeTimedItem);
        awakeMenu.DropDownItems.Add(_awakeExpirationItem);
        awakeMenu.DropDownItems.Add(_awakeIndefiniteItem);
        awakeMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
        awakeMenu.DropDownItems.Add(_awakeKeepScreenOnItem);
        _trayMenu.Items.Add(awakeMenu);
        ApplyAwakeMenuState();
    }

    private void AddAwakeInterval(Forms.ToolStripMenuItem parent, string label, int minutes)
    {
        parent.DropDownItems.Add(label, null, (_, _) => RunTrayCommand(() => ApplyAwake(_awakeService.SetTimed(TimeSpan.FromMinutes(minutes)))));
    }

    private void ApplyAwake(AwakeOperationResult result)
    {
        if (!result.Succeeded)
        {
            _trayIcon.ShowBalloonTip(3000, "Keep awake", result.Error ?? "Windows rejected the request.", Forms.ToolTipIcon.Warning);
        }
    }

    private void ApplyAwakeMenuState()
    {
        var state = _awakeService.State;
        _awakeOffItem.Checked = state.Mode == AwakeMode.Off;
        _awakeTimedItem.Checked = state.Mode == AwakeMode.Timed;
        _awakeExpirationItem.Checked = state.Mode == AwakeMode.Expiration;
        _awakeIndefiniteItem.Checked = state.Mode == AwakeMode.Indefinite;
        _awakeKeepScreenOnItem.Checked = state.KeepScreenOn;
        _awakeKeepScreenOnItem.Enabled = state.IsActive;
    }

    private static void RunTrayCommand(Action action)
    {
        if (HostUiInputGuard.IsRecentProtectedClientInput())
        {
            return;
        }

        action();
    }

}
