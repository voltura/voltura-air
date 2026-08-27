using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VolturaAir.Host;

internal sealed partial class ConPtyTerminalProcessFactory : ITerminalProcessFactory
{
    public ITerminalProcess Start(ushort columns, ushort rows) => ConPtyTerminalProcess.Start(columns, rows);

    private sealed partial class ConPtyTerminalProcess : ITerminalProcess
    {
        private const uint CreateSuspended = 0x00000004;
        private const uint ExtendedStartupInfoPresent = 0x00080000;
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private const uint StartfUseStdHandles = 0x00000100;
        private static readonly nuint ProcThreadAttributePseudoConsole = 0x00020016;
        private readonly Lock _gate = new();
        private readonly SafeFileHandle _processHandle;
        private readonly SafeFileHandle _jobHandle;
        private readonly nint _pseudoConsole;
        private bool _disposed;

        private ConPtyTerminalProcess(
            Stream input,
            Stream output,
            SafeFileHandle processHandle,
            SafeFileHandle jobHandle,
            nint pseudoConsole,
            int processId)
        {
            Input = input;
            Output = output;
            _processHandle = processHandle;
            _jobHandle = jobHandle;
            _pseudoConsole = pseudoConsole;
            ExitCode = ObserveExitAsync(processId, processHandle);
        }

        public Stream Input { get; }
        public Stream Output { get; }
        public Task<int> ExitCode { get; }

        internal static ConPtyTerminalProcess Start(ushort columns, ushort rows)
        {
            TerminalProtocol.ValidateDimensions(columns, rows);
            SafeFileHandle? conptyInputRead = null;
            SafeFileHandle? hostInputWrite = null;
            SafeFileHandle? hostOutputRead = null;
            SafeFileHandle? conptyOutputWrite = null;
            SafeFileHandle? processHandle = null;
            SafeFileHandle? threadHandle = null;
            SafeFileHandle? jobHandle = null;
            Stream? input = null;
            Stream? output = null;
            nint pseudoConsole = 0;
            nint attributes = 0;
            bool processOwnershipTransferred = false;
            try
            {
                Ensure(CreatePipe(out conptyInputRead, out hostInputWrite, 0, 0), "create the terminal input pipe");
                Ensure(CreatePipe(out hostOutputRead, out conptyOutputWrite, 0, 0), "create the terminal output pipe");
                EnsureHResult(CreatePseudoConsole(new Coord((short)columns, (short)rows), conptyInputRead, conptyOutputWrite, 0, out pseudoConsole), "create the pseudoconsole");

                nuint attributesSize = 0;
                _ = InitializeProcThreadAttributeList(0, 1, 0, ref attributesSize);
                attributes = Marshal.AllocHGlobal(checked((int)attributesSize));
                Ensure(InitializeProcThreadAttributeList(attributes, 1, 0, ref attributesSize), "initialize the process attributes");
                Ensure(UpdateProcThreadAttribute(attributes, 0, ProcThreadAttributePseudoConsole, pseudoConsole, (nuint)nint.Size, 0, 0), "attach the pseudoconsole");

                var startup = new StartupInfoEx
                {
                    StartupInfo = new StartupInfo
                    {
                        Size = Marshal.SizeOf<StartupInfoEx>(),
                        Flags = StartfUseStdHandles
                    },
                    AttributeList = attributes
                };
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string executable = Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                Ensure(CreateProcess(executable, $"\"{executable}\" -NoLogo", 0, 0, false,
                    CreateSuspended | ExtendedStartupInfoPresent, 0, workingDirectory, ref startup, out var process),
                    "start Windows PowerShell");
                processHandle = new SafeFileHandle(process.Process, ownsHandle: true);
                threadHandle = new SafeFileHandle(process.Thread, ownsHandle: true);

                jobHandle = CreateJobObject(0, null);
                if (jobHandle.IsInvalid) throw LastError("create the terminal job");
                var limits = new JobObjectExtendedLimitInformation
                {
                    BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose }
                };
                Ensure(SetInformationJobObject(jobHandle, 9, in limits, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()), "configure terminal process cleanup");
                Ensure(AssignProcessToJobObject(jobHandle, processHandle), "assign Windows PowerShell to the terminal job");
                if (ResumeThread(threadHandle) == uint.MaxValue) throw LastError("resume Windows PowerShell");

                conptyInputRead.Dispose();
                conptyOutputWrite.Dispose();
                threadHandle.Dispose();
                input = new FileStream(hostInputWrite, FileAccess.Write, 16 * 1024, isAsync: false);
                output = new FileStream(hostOutputRead, FileAccess.Read, 16 * 1024, isAsync: false);
                hostInputWrite = null;
                hostOutputRead = null;
                var result = new ConPtyTerminalProcess(input, output, processHandle, jobHandle, pseudoConsole, checked((int)process.ProcessId));
                processHandle = null;
                jobHandle = null;
                pseudoConsole = 0;
                processOwnershipTransferred = true;
                input = null;
                output = null;
                return result;
            }
            finally
            {
                input?.Dispose();
                output?.Dispose();
                if (!processOwnershipTransferred && processHandle is not null && !processHandle.IsInvalid)
                {
                    _ = TerminateProcess(processHandle, 1);
                }
                jobHandle?.Dispose();
                processHandle?.Dispose();
                threadHandle?.Dispose();
                conptyInputRead?.Dispose();
                hostInputWrite?.Dispose();
                hostOutputRead?.Dispose();
                conptyOutputWrite?.Dispose();
                if (attributes != 0)
                {
                    DeleteProcThreadAttributeList(attributes);
                    Marshal.FreeHGlobal(attributes);
                }
                if (pseudoConsole != 0) ClosePseudoConsole(pseudoConsole);
            }
        }

        public void Resize(ushort columns, ushort rows)
        {
            TerminalProtocol.ValidateDimensions(columns, rows);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                EnsureHResult(ResizePseudoConsole(_pseudoConsole, new Coord((short)columns, (short)rows)), "resize the pseudoconsole");
            }
        }

        public void Terminate()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _jobHandle.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            Input.Dispose();
            _jobHandle.Dispose();
            ClosePseudoConsole(_pseudoConsole);
            Output.Dispose();
            try { await ExitCode.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch (TimeoutException) { }
            _processHandle.Dispose();
        }

        private static async Task<int> ObserveExitAsync(int processId, SafeFileHandle processHandle)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                await process.WaitForExitAsync().ConfigureAwait(false);
                return GetExitCodeProcess(processHandle, out uint code) ? unchecked((int)code) : -1;
            }
            catch (ArgumentException) { return GetExitCodeProcess(processHandle, out uint code) ? unchecked((int)code) : -1; }
        }

        private static void Ensure(bool succeeded, string operation)
        {
            if (!succeeded) throw LastError(operation);
        }

        private static void EnsureHResult(int result, string operation)
        {
            if (result < 0) throw new InvalidOperationException($"Could not {operation} (HRESULT 0x{result:X8}).");
        }

        private static Win32Exception LastError(string operation) => new(Marshal.GetLastWin32Error(), $"Could not {operation}.");

        [StructLayout(LayoutKind.Sequential)] private readonly record struct Coord(short X, short Y);
        [StructLayout(LayoutKind.Sequential)] private struct StartupInfo { public int Size; public nint Reserved; public nint Desktop; public nint Title; public uint X; public uint Y; public uint XSize; public uint YSize; public uint XCountChars; public uint YCountChars; public uint FillAttribute; public uint Flags; public ushort ShowWindow; public ushort Reserved2; public nint Reserved2Pointer; public nint StdInput; public nint StdOutput; public nint StdError; }
        [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo StartupInfo; public nint AttributeList; }
        [StructLayout(LayoutKind.Sequential)] private readonly struct ProcessInformation { public readonly nint Process; public readonly nint Thread; public readonly uint ProcessId; public readonly uint ThreadId; }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperationCount; public ulong WriteOperationCount; public ulong OtherOperationCount; public ulong ReadTransferCount; public ulong WriteTransferCount; public ulong OtherTransferCount; }
        [StructLayout(LayoutKind.Sequential)] private struct JobObjectBasicLimitInformation { public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags; public nuint MinimumWorkingSetSize; public nuint MaximumWorkingSetSize; public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass; public uint SchedulingClass; }
        [StructLayout(LayoutKind.Sequential)] private struct JobObjectExtendedLimitInformation { public JobObjectBasicLimitInformation BasicLimitInformation; public IoCounters IoInfo; public nuint ProcessMemoryLimit; public nuint JobMemoryLimit; public nuint PeakProcessMemoryUsed; public nuint PeakJobMemoryUsed; }

        [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static partial bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, nint pipeAttributes, uint size);
        [LibraryImport("kernel32.dll")] private static partial int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out nint pseudoConsole);
        [LibraryImport("kernel32.dll")] private static partial int ResizePseudoConsole(nint pseudoConsole, Coord size);
        [LibraryImport("kernel32.dll")] private static partial void ClosePseudoConsole(nint pseudoConsole);
        [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static partial bool InitializeProcThreadAttributeList(nint attributeList, int attributeCount, uint flags, ref nuint size);
        [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static partial bool UpdateProcThreadAttribute(nint attributeList, uint flags, nuint attribute, nint value, nuint size, nint previousValue, nint returnSize);
        [LibraryImport("kernel32.dll")] private static partial void DeleteProcThreadAttributeList(nint attributeList);
        [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static partial bool CreateProcess(string applicationName, [MarshalAs(UnmanagedType.LPWStr)] string commandLine, nint processAttributes, nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, nint environment, string currentDirectory, ref StartupInfoEx startupInfo, out ProcessInformation processInformation);
        [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)] private static partial SafeFileHandle CreateJobObject(nint jobAttributes, string? name);
        [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetInformationJobObject(SafeFileHandle job, int informationClass, in JobObjectExtendedLimitInformation information, uint length);
        [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static partial bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);
        [LibraryImport("kernel32.dll", SetLastError = true)] private static partial uint ResumeThread(SafeFileHandle thread);
        [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetExitCodeProcess(SafeFileHandle process, out uint exitCode);
        [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static partial bool TerminateProcess(SafeFileHandle process, uint exitCode);
    }
}
