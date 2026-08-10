using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace WebRtcSpike.Host;

internal sealed class WebRtcPeer : IAsyncDisposable
{
    private const int MaximumSdpLength = 32 * 1024;
    private const int MaximumMessageLength = 32 * 1024;
    private const int CandidateBufferLength = 4096;
    private readonly TaskCompletionSource<string> _offer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _dataChannelOpen = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly LibDataChannelNative.DescriptionCallback _descriptionCallback;
    private readonly LibDataChannelNative.StateCallback _stateCallback;
    private readonly LibDataChannelNative.GatheringCallback _gatheringCallback;
    private readonly LibDataChannelNative.OpenCallback _openCallback;
    private readonly LibDataChannelNative.ClosedCallback _closedCallback;
    private readonly LibDataChannelNative.ErrorCallback _errorCallback;
    private readonly LibDataChannelNative.MessageCallback _messageCallback;
    private readonly GCHandle _selfHandle;
    private int _peer;
    private int _dataChannel;
    private int _messageCount;
    private bool _disposed;

    internal WebRtcPeer()
    {
        _descriptionCallback = OnDescription;
        _stateCallback = OnState;
        _gatheringCallback = OnGathering;
        _openCallback = OnOpen;
        _closedCallback = OnClosed;
        _errorCallback = OnError;
        _messageCallback = OnMessage;
        _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        nint pointer = GCHandle.ToIntPtr(_selfHandle);

        try
        {
            var configuration = new LibDataChannelNative.Configuration
            {
                IceServers = 0,
                IceServersCount = 0,
                CertificateType = LibDataChannelNative.CertificateType.Ecdsa,
                IceTransportPolicy = LibDataChannelNative.TransportPolicy.All,
                EnableIceTcp = 0,
                DisableAutoNegotiation = 1,
                ForceMediaTransport = 0,
                Mtu = 1280,
                MaxMessageSize = MaximumMessageLength
            };

            _peer = EnsureCreated(LibDataChannelNative.rtcCreatePeerConnection(in configuration), "create peer connection");
            LibDataChannelNative.rtcSetUserPointer(_peer, pointer);
            EnsureSuccess(LibDataChannelNative.rtcSetLocalDescriptionCallback(_peer, _descriptionCallback), "set description callback");
            EnsureSuccess(LibDataChannelNative.rtcSetStateChangeCallback(_peer, _stateCallback), "set state callback");
            EnsureSuccess(LibDataChannelNative.rtcSetGatheringStateChangeCallback(_peer, _gatheringCallback), "set gathering callback");

            _dataChannel = EnsureCreated(LibDataChannelNative.rtcCreateDataChannel(_peer, "spike-data"), "create DataChannel");
            LibDataChannelNative.rtcSetUserPointer(_dataChannel, pointer);
            EnsureSuccess(LibDataChannelNative.rtcSetOpenCallback(_dataChannel, _openCallback), "set open callback");
            EnsureSuccess(LibDataChannelNative.rtcSetClosedCallback(_dataChannel, _closedCallback), "set closed callback");
            EnsureSuccess(LibDataChannelNative.rtcSetErrorCallback(_dataChannel, _errorCallback), "set error callback");
            EnsureSuccess(LibDataChannelNative.rtcSetMessageCallback(_dataChannel, _messageCallback), "set message callback");
        }
        catch
        {
            DisposeNative();
            throw;
        }
    }

    internal Task DataChannelOpen => _dataChannelOpen.Task;

    internal async Task<string> CreateOfferAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSuccess(LibDataChannelNative.rtcSetLocalDescription(_peer, "offer"), "create complete offer");
        return await _offer.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
    }

    internal void ApplyAnswer(string answerSdp)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(answerSdp) || answerSdp.Length > MaximumSdpLength)
        {
            throw new InvalidOperationException("The browser answer was empty or too large.");
        }

        EnsureSuccess(LibDataChannelNative.rtcSetRemoteDescription(_peer, answerSdp, "answer"), "apply browser answer");
    }

    internal void SendJson<T>(T value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string json = JsonSerializer.Serialize(value);
        if (Encoding.UTF8.GetByteCount(json) > MaximumMessageLength)
        {
            throw new InvalidOperationException("The outgoing DataChannel message was too large.");
        }

        nint message = Marshal.StringToCoTaskMemUTF8(json);
        try
        {
            EnsureSuccess(LibDataChannelNative.rtcSendMessage(_dataChannel, message, -1), "send DataChannel message");
        }
        finally
        {
            Marshal.FreeCoTaskMem(message);
        }
    }

    internal void PrintSelectedRoute()
    {
        Console.WriteLine($"Selected local address: {GetAddress(LibDataChannelNative.rtcGetLocalAddress)}");
        Console.WriteLine($"Selected remote address: {GetAddress(LibDataChannelNative.rtcGetRemoteAddress)}");

        nint local = Marshal.AllocHGlobal(CandidateBufferLength);
        nint remote = Marshal.AllocHGlobal(CandidateBufferLength);
        try
        {
            int result = LibDataChannelNative.rtcGetSelectedCandidatePair(
                _peer,
                local,
                CandidateBufferLength,
                remote,
                CandidateBufferLength);
            if (result < 0)
            {
                Console.WriteLine($"Selected candidate pair: unavailable (native error {result})");
                return;
            }

            Console.WriteLine($"Selected local candidate: {Marshal.PtrToStringUTF8(local) ?? "unavailable"}");
            Console.WriteLine($"Selected remote candidate: {Marshal.PtrToStringUTF8(remote) ?? "unavailable"}");
        }
        finally
        {
            Marshal.FreeHGlobal(local);
            Marshal.FreeHGlobal(remote);
        }
    }

    public ValueTask DisposeAsync()
    {
        DisposeNative();
        return ValueTask.CompletedTask;
    }

    private void DisposeNative()
    {
        if (_disposed) return;
        _disposed = true;
        _offer.TrySetCanceled();
        _dataChannelOpen.TrySetCanceled();

        int channel = Interlocked.Exchange(ref _dataChannel, 0);
        int peer = Interlocked.Exchange(ref _peer, 0);
        if (channel > 0)
        {
            LibDataChannelNative.rtcSetUserPointer(channel, 0);
            _ = LibDataChannelNative.rtcDeleteDataChannel(channel);
        }

        if (peer > 0)
        {
            LibDataChannelNative.rtcSetUserPointer(peer, 0);
            _ = LibDataChannelNative.rtcClosePeerConnection(peer);
            _ = LibDataChannelNative.rtcDeletePeerConnection(peer);
        }

        if (_selfHandle.IsAllocated) _selfHandle.Free();
    }

    private void CompleteOffer()
    {
        int size = LibDataChannelNative.rtcGetLocalDescription(_peer, 0, 0);
        if (size <= 1 || size > MaximumSdpLength + 1)
        {
            _offer.TrySetException(new InvalidOperationException("The generated offer was unavailable or too large."));
            return;
        }

        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            int result = LibDataChannelNative.rtcGetLocalDescription(_peer, buffer, size);
            string? sdp = result > 1 ? Marshal.PtrToStringUTF8(buffer) : null;
            if (string.IsNullOrWhiteSpace(sdp) || sdp.Length > MaximumSdpLength)
            {
                _offer.TrySetException(new InvalidOperationException("The generated offer was invalid."));
                return;
            }

            _offer.TrySetResult(sdp);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void ReceiveMessage(nint message, int size)
    {
        try
        {
            string? text;
            if (size < 0)
            {
                text = Marshal.PtrToStringUTF8(message);
            }
            else
            {
                if (size > MaximumMessageLength)
                {
                    Console.Error.WriteLine("Rejected an oversized DataChannel message.");
                    return;
                }

                byte[] bytes = new byte[size];
                Marshal.Copy(message, bytes, 0, size);
                text = Encoding.UTF8.GetString(bytes);
            }

            if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaximumMessageLength)
            {
                Console.Error.WriteLine("Rejected an empty or oversized DataChannel message.");
                return;
            }

            int count = Interlocked.Increment(ref _messageCount);
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            string type = root.TryGetProperty("type", out JsonElement typeElement)
                ? typeElement.GetString() ?? "unknown"
                : "unknown";

            if (type == "orientation")
            {
                Console.WriteLine($"Motion: alpha={Number(root, "alpha")} beta={Number(root, "beta")} gamma={Number(root, "gamma")}");
            }
            else if (type == "motion")
            {
                Console.WriteLine($"Motion: rotationRate alpha={NestedNumber(root, "rotationRate", "alpha")} beta={NestedNumber(root, "rotationRate", "beta")} gamma={NestedNumber(root, "rotationRate", "gamma")}");
            }
            else
            {
                Console.WriteLine($"Messages received: {count}; type={type}; payload={text}");
            }

            SendJson(new { type = "ack", receivedType = type, messagesReceived = count });
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Rejected a malformed DataChannel message: {exception.Message}");
        }
    }

    private static string Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double number)
            ? number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            : "null";

    private static string NestedNumber(JsonElement root, string parent, string name) =>
        root.TryGetProperty(parent, out JsonElement nested) && nested.ValueKind == JsonValueKind.Object
            ? Number(nested, name)
            : "null";

    private string GetAddress(Func<int, nint, int, int> getter)
    {
        int size = getter(_peer, 0, 0);
        if (size <= 1 || size > CandidateBufferLength) return $"unavailable (native error {size})";

        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            int result = getter(_peer, buffer, size);
            return result > 1 ? Marshal.PtrToStringUTF8(buffer) ?? "unavailable" : $"unavailable (native error {result})";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static WebRtcPeer? From(nint pointer) =>
        pointer == 0 ? null : GCHandle.FromIntPtr(pointer).Target as WebRtcPeer;

    private static void OnDescription(int peer, nint sdp, nint type, nint pointer)
    {
        _ = peer;
        _ = sdp;
        _ = type;
        _ = pointer;
    }

    private static void OnState(int peer, LibDataChannelNative.PeerState state, nint pointer)
    {
        _ = peer;
        WebRtcPeer? owner = From(pointer);
        if (owner is null) return;
        Console.WriteLine($"ICE: {state.ToString().ToLowerInvariant()}");
        if (state is LibDataChannelNative.PeerState.Failed or LibDataChannelNative.PeerState.Disconnected)
        {
            var exception = new InvalidOperationException($"The WebRTC connection entered the {state} state.");
            if (owner._dataChannelOpen.TrySetException(exception)) return;
            Console.WriteLine("The browser session is no longer active. The spike host remains open; press Ctrl+C to stop.");
        }
    }

    private static void OnGathering(int peer, LibDataChannelNative.GatheringState state, nint pointer)
    {
        _ = peer;
        WebRtcPeer? owner = From(pointer);
        if (owner is null) return;
        Console.WriteLine($"ICE gathering: {state.ToString().ToLowerInvariant()}");
        if (state == LibDataChannelNative.GatheringState.Complete) owner.CompleteOffer();
    }

    private static void OnOpen(int id, nint pointer)
    {
        WebRtcPeer? owner = From(pointer);
        if (owner is null || id != owner._dataChannel) return;
        Console.WriteLine("DataChannel: open");
        owner._dataChannelOpen.TrySetResult();
    }

    private static void OnClosed(int id, nint pointer)
    {
        WebRtcPeer? owner = From(pointer);
        if (owner is null || id != owner._dataChannel) return;
        Console.WriteLine("DataChannel: closed");
        if (owner._dataChannelOpen.Task.IsCompletedSuccessfully)
        {
            Console.WriteLine("The browser session is no longer active. The spike host remains open; press Ctrl+C to stop.");
        }
    }

    private static void OnError(int id, nint message, nint pointer)
    {
        _ = id;
        WebRtcPeer? owner = From(pointer);
        if (owner is null) return;
        string detail = Marshal.PtrToStringUTF8(message) ?? "unknown native error";
        var exception = new InvalidOperationException($"DataChannel error: {detail}");
        if (owner._dataChannelOpen.TrySetException(exception)) return;
        Console.WriteLine("The browser session encountered an error. The spike host remains open; press Ctrl+C to stop.");
    }

    private static void OnMessage(int id, nint message, int size, nint pointer)
    {
        WebRtcPeer? owner = From(pointer);
        if (owner is not null && id == owner._dataChannel) owner.ReceiveMessage(message, size);
    }

    private static int EnsureCreated(int result, string operation)
    {
        if (result < 0) throw new InvalidOperationException($"Could not {operation} (native error {result}).");
        return result;
    }

    private static void EnsureSuccess(int result, string operation)
    {
        if (result < 0) throw new InvalidOperationException($"Could not {operation} (native error {result}).");
    }
}
