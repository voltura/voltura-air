using System.Runtime.InteropServices;

namespace WebRtcSpike.Host;

internal static partial class LibDataChannelNative
{
    private const string LibraryName = "datachannel.dll";

    static LibDataChannelNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(LibDataChannelNative).Assembly, static (name, assembly, searchPath) =>
        {
            if (!string.Equals(name, LibraryName, StringComparison.Ordinal)) return 0;
            string bundledPath = Path.Combine(AppContext.BaseDirectory, LibraryName);
            return File.Exists(bundledPath) && NativeLibrary.TryLoad(bundledPath, assembly, searchPath, out nint handle)
                ? handle
                : 0;
        });
    }

    internal enum PeerState
    {
        New,
        Connecting,
        Connected,
        Disconnected,
        Failed,
        Closed
    }

    internal enum GatheringState
    {
        New,
        InProgress,
        Complete
    }

    internal enum CertificateType
    {
        Default,
        Ecdsa,
        Rsa
    }

    internal enum TransportPolicy
    {
        All,
        Relay
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Configuration
    {
        internal nint IceServers;
        internal int IceServersCount;
        internal nint ProxyServer;
        internal nint BindAddress;
        internal CertificateType CertificateType;
        internal TransportPolicy IceTransportPolicy;
        internal byte EnableIceTcp;
        internal byte EnableIceUdpMux;
        internal byte DisableAutoNegotiation;
        internal byte ForceMediaTransport;
        internal ushort PortRangeBegin;
        internal ushort PortRangeEnd;
        internal int Mtu;
        internal int MaxMessageSize;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void DescriptionCallback(int peer, nint sdp, nint type, nint userPointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void StateCallback(int peer, PeerState state, nint userPointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void GatheringCallback(int peer, GatheringState state, nint userPointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void OpenCallback(int id, nint userPointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ClosedCallback(int id, nint userPointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ErrorCallback(int id, nint message, nint userPointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void MessageCallback(int id, nint message, int size, nint userPointer);

    [LibraryImport(LibraryName)]
    internal static partial int rtcCreatePeerConnection(in Configuration configuration);

    [LibraryImport(LibraryName)]
    internal static partial int rtcClosePeerConnection(int peer);

    [LibraryImport(LibraryName)]
    internal static partial int rtcDeletePeerConnection(int peer);

    [LibraryImport(LibraryName)]
    internal static partial void rtcSetUserPointer(int id, nint pointer);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSetLocalDescriptionCallback(int peer, DescriptionCallback callback);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSetStateChangeCallback(int peer, StateCallback callback);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSetGatheringStateChangeCallback(int peer, GatheringCallback callback);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int rtcSetLocalDescription(int peer, string type);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int rtcSetRemoteDescription(int peer, string sdp, string type);

    [LibraryImport(LibraryName)]
    internal static partial int rtcGetLocalDescription(int peer, nint buffer, int size);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int rtcCreateDataChannel(int peer, string label);

    [LibraryImport(LibraryName)]
    internal static partial int rtcDeleteDataChannel(int dataChannel);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSetOpenCallback(int id, OpenCallback callback);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSetClosedCallback(int id, ClosedCallback callback);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSetErrorCallback(int id, ErrorCallback callback);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSetMessageCallback(int id, MessageCallback callback);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSendMessage(int id, nint data, int size);

    [LibraryImport(LibraryName)]
    internal static partial int rtcGetLocalAddress(int peer, nint buffer, int size);

    [LibraryImport(LibraryName)]
    internal static partial int rtcGetRemoteAddress(int peer, nint buffer, int size);

    [LibraryImport(LibraryName)]
    internal static partial int rtcGetSelectedCandidatePair(
        int peer,
        nint localBuffer,
        int localSize,
        nint remoteBuffer,
        int remoteSize);
}
