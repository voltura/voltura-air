using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VolturaAir.Host;

public sealed record CustomScreenHostAction(
    string Id,
    string Label,
    string Confirmation,
    string? ConfirmationMessage = null);

public static class CustomScreenHostActions
{
    private static readonly CustomScreenHostAction[] Items =
    [
        new("power.lock", "Lock PC", "none"),
        new("power.sleep", "Sleep PC", "confirm", "Voltura Air will disconnect and cannot wake the PC remotely."),
        new("power.hibernate", "Hibernate PC", "confirm", "Voltura Air will disconnect and cannot wake the PC remotely."),
        new("power.restart", "Restart PC", "hold", "Unsaved work may be lost."),
        new("power.shutdown", "Shut down PC", "hold", "Unsaved work may be lost."),
        new("display.off", "Turn off display", "confirm", "Some PCs enter sleep or Modern Standby and then require physical input to wake."),
        new("display.duplicate", "Duplicate displays", "none"),
        new("display.extend", "Extend displays", "none"),
        new("display.pcOnly", "PC screen only", "none"),
        new("display.secondOnly", "Second screen only", "none")
    ];

    public static IReadOnlyList<CustomScreenHostAction> All => Items;

    public static CustomScreenHostAction? Find(string? id) =>
        Items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public static bool IsSupported(string? id) => Find(id) is not null;

    public static bool IsPermitted(string? id, HostPermissionSet permissions) => id switch
    {
        "power.lock" => permissions.AllowPcLock,
        "power.sleep" or "power.hibernate" => permissions.AllowPcSleep,
        "power.restart" => permissions.AllowRestart,
        "power.shutdown" => permissions.AllowShutdown,
        "display.off" or "display.duplicate" or "display.extend" or
            "display.pcOnly" or "display.secondOnly" => permissions.AllowDisplayControl,
        _ => false
    };

    public static bool IsAvailable(
        string? id,
        ISystemPowerController powerController,
        IWorkstationLockPolicy workstationLockPolicy) =>
        IsAvailable(
            id,
            powerController,
            workstationLockPolicy,
            WindowsSystemSuspendController.Instance);

    internal static bool IsAvailable(
        string? id,
        ISystemPowerController powerController,
        IWorkstationLockPolicy workstationLockPolicy,
        ISystemSuspendController suspendController) => id switch
        {
            "power.lock" =>
                workstationLockPolicy.GetStatus().State == WorkstationLockPolicyState.NotExplicitlyDisabled &&
                powerController.IsActionAvailable(SystemPowerActions.Lock),
            "power.sleep" => suspendController.IsAvailable(SystemSuspendActions.Sleep),
            "power.hibernate" => suspendController.IsAvailable(SystemSuspendActions.Hibernate),
            "power.restart" => powerController.IsActionAvailable(SystemPowerActions.Restart),
            "power.shutdown" => powerController.IsActionAvailable(SystemPowerActions.Shutdown),
            "display.off" => powerController.IsActionAvailable(SystemPowerActions.DisplayOff),
            _ => IsSupported(id)
        };

    public static SystemPowerExecutionResult Execute(
        string id,
        ISystemPowerController powerController) =>
        Execute(id, powerController, WindowsSystemSuspendController.Instance);

    internal static SystemPowerExecutionResult Execute(
        string id,
        ISystemPowerController powerController,
        ISystemSuspendController suspendController)
    {
        if (id is "power.lock" or "power.restart" or "power.shutdown" or "display.off")
        {
            var action = id switch
            {
                "power.lock" => SystemPowerActions.Lock,
                "power.restart" => SystemPowerActions.Restart,
                "power.shutdown" => SystemPowerActions.Shutdown,
                _ => SystemPowerActions.DisplayOff
            };
            return powerController.IsActionAvailable(action)
                ? powerController.TryExecute(action)
                : new SystemPowerExecutionResult(false);
        }

        if (id is "power.sleep" or "power.hibernate")
        {
            var action = id == "power.hibernate"
                ? SystemSuspendActions.Hibernate
                : SystemSuspendActions.Sleep;
            return suspendController.IsAvailable(action)
                ? suspendController.TryExecute(action)
                : new SystemPowerExecutionResult(false);
        }

        var argument = id switch
        {
            "display.duplicate" => "/clone",
            "display.extend" => "/extend",
            "display.pcOnly" => "/internal",
            "display.secondOnly" => "/external",
            _ => null
        };
        if (argument is null)
        {
            return new SystemPowerExecutionResult(false);
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "DisplaySwitch.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { argument }
            });
            return process is null
                ? new SystemPowerExecutionResult(false)
                : SystemPowerExecutionResult.Success;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new SystemPowerExecutionResult(false);
        }
    }
}

internal static class SystemSuspendActions
{
    public const string Sleep = "sleep";
    public const string Hibernate = "hibernate";
}

internal interface ISystemSuspendController
{
    bool IsAvailable(string action);

    SystemPowerExecutionResult TryExecute(string action);
}

internal sealed partial class WindowsSystemSuspendController : ISystemSuspendController
{
    public static WindowsSystemSuspendController Instance { get; } = new();

    public bool IsAvailable(string action)
    {
        if (!GetPwrCapabilities(out var capabilities))
        {
            return false;
        }

        return action switch
        {
            SystemSuspendActions.Sleep =>
                capabilities.SystemS1 != 0 ||
                capabilities.SystemS2 != 0 ||
                capabilities.SystemS3 != 0 ||
                capabilities.AoAc != 0,
            SystemSuspendActions.Hibernate =>
                capabilities.SystemS4 != 0 && capabilities.HiberFilePresent != 0,
            _ => false
        };
    }

    public SystemPowerExecutionResult TryExecute(string action)
    {
        if (!IsAvailable(action))
        {
            return new SystemPowerExecutionResult(false);
        }

        try
        {
            var state = action == SystemSuspendActions.Hibernate
                ? System.Windows.Forms.PowerState.Hibernate
                : System.Windows.Forms.PowerState.Suspend;
            return System.Windows.Forms.Application.SetSuspendState(
                state,
                force: false,
                disableWakeEvent: false)
                ? SystemPowerExecutionResult.Success
                : new SystemPowerExecutionResult(false);
        }
        catch (InvalidOperationException)
        {
            return new SystemPowerExecutionResult(false);
        }
    }

    [LibraryImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetPwrCapabilities(
        out NativeSystemPowerCapabilities capabilities);

    [StructLayout(LayoutKind.Explicit, Size = 76)]
    private struct NativeSystemPowerCapabilities
    {
        [FieldOffset(3)] public byte SystemS1;
        [FieldOffset(4)] public byte SystemS2;
        [FieldOffset(5)] public byte SystemS3;
        [FieldOffset(6)] public byte SystemS4;
        [FieldOffset(8)] public byte HiberFilePresent;
        [FieldOffset(20)] public byte AoAc;
    }
}
