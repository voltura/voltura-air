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
    private static readonly Lock LeaseGate = new();
    private static SecurityIdentifier? _leasedServiceSid;
    private static byte[]? _processDescriptor;
    private static byte[]? _tokenDescriptor;
    private static nint _token;
    private static int _leaseCount;
    private static readonly AsyncLocal<Func<string, Exception?>?> RestoreFailureForTests = new();

    internal static IDisposable Grant(SecurityIdentifier serviceSid)
    {
        ArgumentNullException.ThrowIfNull(serviceSid);
        lock (LeaseGate)
        {
            if (_leaseCount > 0)
            {
                if (_leasedServiceSid != serviceSid)
                {
                    throw new InvalidOperationException("The host token-access lease is already owned by another service SID.");
                }

                _leaseCount += 1;
                return new AccessLease();
            }

            if (_leasedServiceSid is not null)
            {
                RestorePendingLease();
            }

            nint process = GetCurrentProcess();
            byte[] processDescriptor = ReadDescriptor(process, "host process");
            _leasedServiceSid = serviceSid;
            _processDescriptor = processDescriptor;
            try
            {
                Grant(process, processDescriptor, serviceSid, ProcessQueryLimitedInformation, "host process");
                if (!OpenProcessToken(process, TokenQuery | ReadControl | WriteDac, out nint token))
                {
                    throw new InvalidOperationException($"Could not open the host token security descriptor ({Marshal.GetLastPInvokeError()}).");
                }

                _token = token;
                byte[] tokenDescriptor = ReadDescriptor(token, "host token");
                _tokenDescriptor = tokenDescriptor;
                Grant(token, tokenDescriptor, serviceSid, TokenQuery, "host token");
                _leaseCount = 1;
                return new AccessLease();
            }
            catch (Exception grantFailure)
            {
                try
                {
                    RestorePendingLease();
                }
                catch (Exception restoreFailure)
                {
                    throw new AggregateException(
                        "Phone webcam token access failed and its ACL rollback also failed.",
                        grantFailure,
                        restoreFailure);
                }

                throw;
            }
        }
    }

    private static byte[] ReadDescriptor(nint handle, string objectName)
    {
        GetKernelObjectSecurity(handle, DaclSecurityInformation, null, 0, out uint required);
        if (required == 0)
        {
            throw new InvalidOperationException($"The {objectName} security descriptor is unavailable.");
        }

        byte[] descriptorBytes = new byte[required];
        if (!GetKernelObjectSecurity(handle, DaclSecurityInformation, descriptorBytes, required, out _))
        {
            throw new InvalidOperationException($"Could not read the {objectName} security descriptor ({Marshal.GetLastPInvokeError()}).");
        }

        return descriptorBytes;
    }

    private static void Grant(
        nint handle,
        byte[] descriptorBytes,
        SecurityIdentifier serviceSid,
        int accessMask,
        string objectName)
    {
        var descriptor = new RawSecurityDescriptor(descriptorBytes, 0);
        // A null DACL intentionally grants everyone full access. Replacing it with
        // an ACL containing only the Frame Server ACE would revoke every other
        // caller and is unnecessary because the requested access is already granted.
        if (descriptor.DiscretionaryAcl is null)
        {
            return;
        }
        RawAcl dacl = descriptor.DiscretionaryAcl;
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

    private static void Restore(nint handle, byte[] descriptor, string objectName)
    {
        if (RestoreFailureForTests.Value?.Invoke(objectName) is { } injectedFailure)
        {
            throw injectedFailure;
        }
        if (!SetKernelObjectSecurity(handle, DaclSecurityInformation, descriptor))
        {
            throw new InvalidOperationException($"Could not restore the {objectName} security descriptor ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static void Release()
    {
        lock (LeaseGate)
        {
            if (_leaseCount <= 0)
            {
                return;
            }
            if (_leaseCount > 1)
            {
                _leaseCount -= 1;
                return;
            }

            RestorePendingLease();
            _leaseCount = 0;
        }
    }

    private static void RestorePendingLease()
    {
        byte[] processDescriptor = _processDescriptor
            ?? throw new InvalidOperationException("The host token-access lease lost its process descriptor.");
        nint token = _token;
        byte[]? tokenDescriptor = _tokenDescriptor;

        List<Exception>? failures = null;
        if (token != 0 && tokenDescriptor is not null)
        {
            try
            {
                Restore(token, tokenDescriptor, "host token");
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        try
        {
            Restore(GetCurrentProcess(), processDescriptor, "host process");
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        if (failures is not null)
        {
            throw new AggregateException("Could not restore the host token-access ACL lease.", failures);
        }

        _leasedServiceSid = null;
        _processDescriptor = null;
        _tokenDescriptor = null;
        _token = 0;
        if (token != 0) CloseHandle(token);
    }

    internal static int ActiveLeaseCountForTests
    {
        get { lock (LeaseGate) return _leaseCount; }
    }

    internal static void SetRestoreFailureForTests(Func<string, Exception?>? failure)
    {
        RestoreFailureForTests.Value = failure;
    }

    private sealed class AccessLease : IDisposable
    {
        private readonly Lock _disposeGate = new();
        private bool _disposed;

        public void Dispose()
        {
            lock (_disposeGate)
            {
                if (_disposed)
                {
                    return;
                }

                Release();
                _disposed = true;
            }
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
