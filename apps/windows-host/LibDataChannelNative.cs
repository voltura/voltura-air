using System.Runtime.InteropServices;

namespace VolturaAir.Host;

internal static partial class LibDataChannelNative
{
    private const string LibraryName = "datachannel.dll";

    static LibDataChannelNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(LibDataChannelNative).Assembly, static (libraryName, assembly, searchPath) =>
        {
            if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal)) return 0;
            string bundledPath = Path.Combine(AppContext.BaseDirectory, LibraryName);
            return File.Exists(bundledPath) && NativeLibrary.TryLoad(bundledPath, assembly, searchPath, out nint handle)
                ? handle
                : 0;
        });
    }

    internal const int Success = 0;
    internal const int ErrorInvalid = -1;
    internal const int ErrorFailure = -2;
    internal const int ErrorNotAvailable = -3;
    internal const int ErrorBufferTooSmall = -4;

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

    internal enum Direction
    {
        Unknown,
        SendOnly,
        ReceiveOnly,
        SendReceive,
        Inactive
    }

    internal enum Codec
    {
        H264 = 0
    }

    internal enum NalUnitSeparator
    {
        Length,
        LongStartSequence,
        ShortStartSequence,
        StartSequence
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct Reliability
    {
        internal byte Unordered;
        internal byte Unreliable;
        internal uint MaxPacketLifeTime;
        internal uint MaxRetransmits;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DataChannelInit
    {
        internal Reliability Reliability;
        internal nint Protocol;
        internal byte Negotiated;
        internal byte ManualStream;
        internal ushort Stream;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TrackInit
    {
        internal Direction Direction;
        internal Codec Codec;
        internal int PayloadType;
        internal uint Ssrc;
        internal nint Mid;
        internal nint Name;
        internal nint Msid;
        internal nint TrackId;
        internal nint Profile;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PacketizerInit
    {
        internal uint Ssrc;
        internal nint Cname;
        internal byte PayloadType;
        internal uint ClockRate;
        internal ushort SequenceNumber;
        internal uint Timestamp;
        internal ushort MaxFragmentSize;
        internal NalUnitSeparator NalSeparator;
        internal int ObuPacketization;
        internal byte PlayoutDelayId;
        internal ushort PlayoutDelayMin;
        internal ushort PlayoutDelayMax;
        internal byte ColorSpaceId;
        internal byte ColorChromaSitingHorz;
        internal byte ColorChromaSitingVert;
        internal byte ColorRange;
        internal byte ColorPrimaries;
        internal byte ColorTransfer;
        internal byte ColorMatrix;
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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void PliCallback(int track, nint userPointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void RembCallback(int track, uint bitrate, nint userPointer);

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

    [LibraryImport(LibraryName)]
    internal static partial int rtcAddTrackEx(int peer, in TrackInit initialization);

    [LibraryImport(LibraryName)]
    internal static partial int rtcDeleteTrack(int track);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSetH264Packetizer(int track, in PacketizerInit initialization);

    [LibraryImport(LibraryName)]
    internal static partial int rtcChainRtcpSrReporter(int track);

    [LibraryImport(LibraryName)]
    internal static partial int rtcChainRtcpNackResponder(int track, uint maxStoredPacketsCount);

    [LibraryImport(LibraryName)]
    internal static partial int rtcChainPliHandler(int track, PliCallback callback);

    [LibraryImport(LibraryName)]
    internal static partial int rtcChainRembHandler(int track, RembCallback callback);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSetTrackRtpTimestamp(int track, uint timestamp);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSetOpenCallback(int id, OpenCallback callback);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSetClosedCallback(int id, ClosedCallback callback);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSetErrorCallback(int id, ErrorCallback callback);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSetMessageCallback(int id, MessageCallback callback);

    [LibraryImport(LibraryName)]
    internal static partial int rtcSendMessage(int id, byte[] data, int size);

    [LibraryImport(LibraryName, EntryPoint = "rtcSendMessage")]
    internal static partial int rtcSendTextMessage(int id, nint data, int size);

    [LibraryImport(LibraryName)]
    internal static partial int rtcGetBufferedAmount(int id);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int rtcCreateDataChannel(int peer, string label);

    [LibraryImport(LibraryName)]
    internal static partial int rtcDeleteDataChannel(int dataChannel);

    [LibraryImport(LibraryName)]
    internal static partial int rtcGetLocalAddress(int peer, nint buffer, int size);

    [LibraryImport(LibraryName)]
    internal static partial int rtcGetRemoteAddress(int peer, nint buffer, int size);

    [LibraryImport(LibraryName)]
    internal static partial int rtcChainRtcpReceivingSession(int track);

    [LibraryImport(LibraryName)]
    internal static partial int rtcRequestKeyframe(int track);

    [LibraryImport(LibraryName)]
    internal static partial int rtcGetSelectedCandidatePair(int peer, nint local, int localSize, nint remote, int remoteSize);

}
