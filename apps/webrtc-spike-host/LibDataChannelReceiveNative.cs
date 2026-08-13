using System.Runtime.InteropServices;

namespace VolturaAir.Host;

internal static partial class LibDataChannelNative
{
    private const string ReceiveLibraryName = "datachannel.dll";

    [LibraryImport(ReceiveLibraryName)]
    internal static partial int rtcChainRtcpReceivingSession(int track);

    [LibraryImport(ReceiveLibraryName)]
    internal static partial int rtcRequestKeyframe(int track);

    [LibraryImport(ReceiveLibraryName)]
    internal static partial int rtcGetSelectedCandidatePair(int peer, nint local, int localSize, nint remote, int remoteSize);
}
