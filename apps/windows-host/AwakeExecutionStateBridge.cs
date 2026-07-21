using System.Runtime.InteropServices;

namespace VolturaAir.Host;

[Flags]
internal enum AwakeExecutionState : uint
{
    Continuous = 0x80000000,
    SystemRequired = 0x00000001,
    DisplayRequired = 0x00000002
}

internal interface IAwakeExecutionStateBridge
{
    bool TrySet(AwakeExecutionState state);
}

internal sealed partial class WindowsAwakeExecutionStateBridge : IAwakeExecutionStateBridge
{
    public bool TrySet(AwakeExecutionState state) => SetThreadExecutionState(state) != 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial AwakeExecutionState SetThreadExecutionState(AwakeExecutionState executionState);
}

internal sealed class NoOpAwakeExecutionStateBridge : IAwakeExecutionStateBridge
{
    public bool TrySet(AwakeExecutionState state) => true;
}
