using Microsoft.Win32;
using System.Security;
using System.Runtime.InteropServices;

namespace VolturaAir.Host;

public enum WorkstationLockPolicyState
{
    NotExplicitlyDisabled,
    Disabled,
    Unavailable
}

public sealed record WorkstationLockPolicyStatus(WorkstationLockPolicyState State, string? Diagnostic = null);

public sealed record WorkstationLockEnableResult(bool Succeeded, string Message);

public interface IWorkstationLockPolicy
{
    event EventHandler? Changed;

    WorkstationLockPolicyStatus GetStatus();

    WorkstationLockEnableResult TryEnable();
}

public sealed partial class WorkstationLockPolicy : IWorkstationLockPolicy
{
    internal const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    internal const string ValueName = "DisableLockWorkstation";
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly nint HwndBroadcast = new(0xffff);
    private readonly Func<LockPolicyRegistryValue> _readValue;
    private readonly Action _writeEnabledValue;
    private readonly Action _broadcastPolicyChange;
    private readonly IAppLog _appLog;

    public WorkstationLockPolicy(IAppLog? appLog = null)
        : this(ReadRegistryValue, WriteEnabledRegistryValue, BroadcastPolicyChange, appLog)
    {
    }

    internal WorkstationLockPolicy(
        Func<LockPolicyRegistryValue> readValue,
        Action writeEnabledValue,
        Action broadcastPolicyChange,
        IAppLog? appLog = null)
    {
        _readValue = readValue;
        _writeEnabledValue = writeEnabledValue;
        _broadcastPolicyChange = broadcastPolicyChange;
        _appLog = appLog ?? NullAppLog.Instance;
    }

    public event EventHandler? Changed;

    public WorkstationLockPolicyStatus GetStatus()
    {
        try
        {
            var value = _readValue();
            if (!value.Exists)
            {
                return new WorkstationLockPolicyStatus(WorkstationLockPolicyState.NotExplicitlyDisabled);
            }

            if (!value.IsDword)
            {
                return new WorkstationLockPolicyStatus(
                    WorkstationLockPolicyState.Unavailable,
                    $"HKCU\\{DefaultKeyPath}\\{ValueName} is not a REG_DWORD value.");
            }

            return new WorkstationLockPolicyStatus(
                value.Value == 0 ? WorkstationLockPolicyState.NotExplicitlyDisabled : WorkstationLockPolicyState.Disabled);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return new WorkstationLockPolicyStatus(WorkstationLockPolicyState.Unavailable, ex.Message);
        }
    }

    public WorkstationLockEnableResult TryEnable()
    {
        WriteEnableLog("started");
        try
        {
            _writeEnabledValue();
            _broadcastPolicyChange();
            var value = _readValue();
            if (!value.Exists || !value.IsDword || value.Value != 0)
            {
                WriteEnableLog("failed", "VAIR-LOCK-POLICY-READBACK");
                return new WorkstationLockEnableResult(
                    false,
                    "Windows did not keep the locking setting. An administrator or device policy may control it.");
            }

            Changed?.Invoke(this, EventArgs.Empty);
            WriteEnableLog("succeeded");
            return new WorkstationLockEnableResult(true, "Windows locking is enabled for this user.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            Console.Error.WriteLine("Voltura Air could not enable Windows locking: {0}", ex.Message);
            WriteEnableLog("failed", "VAIR-LOCK-POLICY-ACCESS-DENIED", ex.Message);
            return new WorkstationLockEnableResult(
                false,
                "Windows or an administrator protects this setting. Voltura Air cannot change it for this user.");
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine("Voltura Air could not enable Windows locking: {0}", ex.Message);
            WriteEnableLog("failed", "VAIR-LOCK-POLICY-WRITE-FAILED", ex.Message);
            return new WorkstationLockEnableResult(
                false,
                "Windows could not save the locking setting. Try again, or ask an administrator to check this PC's policy.");
        }
    }

    private void WriteEnableLog(string outcome, string? code = null, string? detail = null)
    {
        _appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: "enable_windows_locking",
            Outcome: outcome,
            Code: code,
            Detail: detail));
    }

    private static LockPolicyRegistryValue ReadRegistryValue()
    {
        using var currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = currentUser.OpenSubKey(DefaultKeyPath, writable: false);
        if (key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not object value)
        {
            return new LockPolicyRegistryValue(false, false, 0);
        }

        return key.GetValueKind(ValueName) == RegistryValueKind.DWord && value is int dword
            ? new LockPolicyRegistryValue(true, true, dword)
            : new LockPolicyRegistryValue(true, false, 0);
    }

    private static void WriteEnabledRegistryValue()
    {
        using var currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = currentUser.OpenSubKey(DefaultKeyPath, writable: true) ??
            currentUser.CreateSubKey(DefaultKeyPath, writable: true);
        key.SetValue(ValueName, 0, RegistryValueKind.DWord);
    }

    private static void BroadcastPolicyChange()
    {
        _ = SendMessageTimeout(
            HwndBroadcast,
            WmSettingChange,
            nuint.Zero,
            "Policy",
            SmtoAbortIfHung,
            1000,
            out _);
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint SendMessageTimeout(
        nint hWnd,
        uint message,
        nuint wParam,
        string lParam,
        uint flags,
        uint timeout,
        out nuint result);
}

internal readonly record struct LockPolicyRegistryValue(bool Exists, bool IsDword, int Value);
