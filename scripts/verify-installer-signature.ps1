#requires -Version 7.6 -PSEdition Core

param(
    [Parameter(Mandatory)] [string]$Path,
    [Parameter(Mandatory)] [string]$CertificateThumbprint,
    [switch]$AllowSelfSigned
)

$ErrorActionPreference = 'Stop'
$file = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).ProviderPath
if (!('VolturaSignatureVerifier' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class VolturaSignatureVerifier {
    [StructLayout(LayoutKind.Sequential)]
    struct FileInfo {
        public uint Size;
        public IntPtr Path, Handle, KnownSubject;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct TrustData {
        public uint Size;
        public IntPtr Policy, Sip;
        public uint Ui, Revocation, Union;
        public IntPtr File;
        public uint StateAction;
        public IntPtr State, Url;
        public uint Flags, Context;
        public IntPtr SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)]
    static extern uint WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    public static uint Verify(string path) {
        var name = Marshal.StringToCoTaskMemUni(path);
        var info = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = name };
        var pointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<FileInfo>());
        try {
            Marshal.StructureToPtr(info, pointer, false);
            var data = new TrustData {
                Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Union = 1, File = pointer,
                Flags = 0x10
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            return WinVerifyTrust(new IntPtr(-1), ref action, ref data);
        } finally {
            Marshal.FreeCoTaskMem(pointer);
            Marshal.FreeCoTaskMem(name);
        }
    }
}
'@
}
$signature = Get-AuthenticodeSignature -LiteralPath $file
if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ine $CertificateThumbprint) {
    throw 'The executable is not signed by the selected certificate.'
}
if (!$signature.TimeStamperCertificate) { throw 'The executable needs an Authenticode timestamp.' }
$result = [VolturaSignatureVerifier]::Verify($file)
$selfSigned = $signature.SignerCertificate.Subject -ceq $signature.SignerCertificate.Issuer
# Only the expected untrusted-root result is acceptable for an explicitly selected self-signed key.
# Digest, signature, timestamp, and other certificate errors still fail verification.
if ($result -ne 0 -and !($AllowSelfSigned -and $selfSigned -and $result -eq 0x800B0109L)) {
    throw ('Authenticode verification failed: 0x{0:X8}' -f $result)
}
[pscustomobject]@{
    status = [string]$signature.Status
    trust = if ($selfSigned) { 'SelfSigned' } else { 'Trusted' }
    signer = $signature.SignerCertificate.Subject
    thumbprint = $signature.SignerCertificate.Thumbprint
    timestamped = $true
    digestVerified = $true
}

