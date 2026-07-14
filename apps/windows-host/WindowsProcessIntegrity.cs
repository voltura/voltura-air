using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VolturaAir.Host;

internal static partial class WindowsProcessIntegrity
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    internal static bool TryGetCurrentProcessIntegrityLevel(out uint integrityLevel)
    {
        return TryGetProcessIntegrityLevel((uint)Environment.ProcessId, out integrityLevel);
    }

    internal static bool TryGetWindowIntegrityLevel(nint windowHandle, out uint integrityLevel)
    {
        integrityLevel = 0;
        if (windowHandle == nint.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(windowHandle, out var processId);
        return processId != 0 && TryGetProcessIntegrityLevel(processId, out integrityLevel);
    }

    internal static bool IsHigherIntegrity(uint hostIntegrityLevel, uint foregroundIntegrityLevel)
    {
        return foregroundIntegrityLevel > hostIntegrityLevel;
    }

    private static bool TryGetProcessIntegrityLevel(uint processId, out uint integrityLevel)
    {
        integrityLevel = 0;
        using var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid || !OpenProcessToken(process, TokenQuery, out var token))
        {
            return false;
        }

        using (token)
        {
            _ = GetTokenInformation(token, TokenIntegrityLevel, nint.Zero, 0, out var requiredLength);
            if (requiredLength <= 0)
            {
                return false;
            }

            var buffer = Marshal.AllocHGlobal(requiredLength);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, requiredLength, out _))
                {
                    return false;
                }

                var sid = Marshal.ReadIntPtr(buffer);
                var subAuthorityCountAddress = GetSidSubAuthorityCount(sid);
                if (sid == nint.Zero || subAuthorityCountAddress == nint.Zero)
                {
                    return false;
                }

                var subAuthorityCount = Marshal.ReadByte(subAuthorityCountAddress);
                var integrityLevelAddress = subAuthorityCount > 0
                    ? GetSidSubAuthority(sid, (uint)(subAuthorityCount - 1))
                    : nint.Zero;
                if (integrityLevelAddress == nint.Zero)
                {
                    return false;
                }

                integrityLevel = unchecked((uint)Marshal.ReadInt32(integrityLevelAddress));
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "OpenProcess")]
    private static partial SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenProcessToken")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(
        SafeProcessHandle processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);

    [LibraryImport("advapi32.dll", EntryPoint = "GetTokenInformation")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        int tokenInformationClass,
        nint tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [LibraryImport("advapi32.dll", EntryPoint = "GetSidSubAuthorityCount")]
    private static partial nint GetSidSubAuthorityCount(nint sid);

    [LibraryImport("advapi32.dll", EntryPoint = "GetSidSubAuthority")]
    private static partial nint GetSidSubAuthority(nint sid, uint subAuthority);
}
