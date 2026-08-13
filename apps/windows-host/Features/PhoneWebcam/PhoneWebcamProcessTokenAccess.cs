using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal static partial class PhoneWebcamProcessTokenAccess
{
    private const int DaclSecurityInformation = 0x00000004;
    private const int ProcessQueryLimitedInformation = 0x00001000;
    private const int TokenQuery = 0x00000008;
    private const int ReadControl = 0x00020000;
    private const int WriteDac = 0x00040000;

    internal static void Grant(SecurityIdentifier serviceSid)
    {
        ArgumentNullException.ThrowIfNull(serviceSid);
        nint process = GetCurrentProcess();
        Grant(process, serviceSid, ProcessQueryLimitedInformation, "host process");

        if (!OpenProcessToken(process, TokenQuery | ReadControl | WriteDac, out nint token))
        {
            throw new InvalidOperationException($"Could not open the host token security descriptor ({Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            Grant(token, serviceSid, TokenQuery, "host token");
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static void Grant(nint handle, SecurityIdentifier serviceSid, int accessMask, string objectName)
    {
        GetKernelObjectSecurity(handle, DaclSecurityInformation, null, 0, out uint required);
        if (required == 0)
        {
            throw new InvalidOperationException("The host process security descriptor is unavailable.");
        }

        byte[] descriptorBytes = new byte[required];
        if (!GetKernelObjectSecurity(handle, DaclSecurityInformation, descriptorBytes, required, out _))
        {
            throw new InvalidOperationException($"Could not read the {objectName} security descriptor ({Marshal.GetLastPInvokeError()}).");
        }

        var descriptor = new RawSecurityDescriptor(descriptorBytes, 0);
        RawAcl dacl = descriptor.DiscretionaryAcl ?? new RawAcl(GenericAcl.AclRevision, 1);
        for (int index = 0; index < dacl.Count; index += 1)
        {
            if (dacl[index] is CommonAce existing &&
                existing.AceQualifier == AceQualifier.AccessAllowed &&
                existing.SecurityIdentifier == serviceSid &&
                (existing.AccessMask & accessMask) == accessMask)
            {
                return;
            }
        }

        dacl.InsertAce(0, new CommonAce(
            AceFlags.None,
            AceQualifier.AccessAllowed,
            accessMask,
            serviceSid,
            isCallback: false,
            opaque: null));
        descriptor.DiscretionaryAcl = dacl;
        byte[] updated = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(updated, 0);
        if (!SetKernelObjectSecurity(handle, DaclSecurityInformation, updated))
        {
            throw new InvalidOperationException($"Could not authorize Frame Server to query the {objectName} ({Marshal.GetLastPInvokeError()}).");
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint processHandle, int desiredAccess, out nint tokenHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetKernelObjectSecurity(
        nint handle,
        int requestedInformation,
        byte[]? securityDescriptor,
        uint length,
        out uint required);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetKernelObjectSecurity(
        nint handle,
        int securityInformation,
        byte[] securityDescriptor);
}
