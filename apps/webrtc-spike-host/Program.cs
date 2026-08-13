using System.Buffers;
using System.Security.Cryptography;
using VolturaAir.Host;

namespace WebRtcSpike.Host;

internal static class Program
{
    private static readonly TimeSpan AnswerWait = TimeSpan.FromMinutes(5);

    public static async Task<int> Main(string[] args)
    {
        SpikeOptions options;
        try
        {
            options = ParseOptions(args);
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        using ProductionHostExclusion? hostExclusion = ProductionHostExclusion.TryAcquireCurrentUser();
        if (hostExclusion is null)
        {
            Console.Error.WriteLine("Spike failed: Voltura Air or another webcam spike host is already running.");
            return 1;
        }

        if (options.PipeTest)
        {
            return await RunPipeTestAsync(cancellation.Token).ConfigureAwait(false);
        }

        string room = CreateRoomToken();
        byte[] signalingKey = RandomNumberGenerator.GetBytes(32);
        using RelayRoutingIdentity? relayIdentity = options.Relay ? RelayRoutingIdentity.OpenCurrentUser() : null;
        await using RelayHostConnection? relayConnection = relayIdentity is null
            ? null
            : new RelayHostConnection(
                RelayEndpointDescriptor.Official(),
                relayIdentity,
                static (_, _, _) => Task.CompletedTask,
                SpikeRelayLog.Instance);
        if (relayConnection is not null)
        {
            Console.WriteLine("Authenticating the existing Relay host route...");
            relayConnection.Start();
            await WaitForRelayConnectionAsync(relayConnection, cancellation.Token).ConfigureAwait(false);
        }
        RelayTurnConfiguration? relay = relayConnection is null
            ? null
            : await relayConnection.GetTurnConfigurationAsync(RelayScreenQuality.Standard, cancellation.Token).ConfigureAwait(false);
        if (options.Relay && relay is null)
        {
            Console.Error.WriteLine("Spike failed: the existing Relay route did not return usable TURN credentials.");
            CryptographicOperations.ZeroMemory(signalingKey);
            return 1;
        }
        if (relay is not null)
        {
            Console.WriteLine($"Relay TURN expires: {relay.ExpiresAt:O}; effective maximum bitrate: {relay.MaximumBitrate}; usage bytes: {relay.UsageBytes}");
        }
        await using var pipeline = new VideoPipeline();
        await using var peer = new WebRtcPeer(relay);
        peer.AccessUnitReceived += pipeline.Submit;
        pipeline.KeyFrameRequested += peer.RequestKeyFrame;
        using var signaling = new SignalingClient(options.SignalEndpoint);
        bool roomCreated = false;

        try
        {
            Console.WriteLine("ICE: gathering");
            string offer = await peer.CreateOfferAsync(cancellation.Token).ConfigureAwait(false);
            EncryptedEnvelope encryptedOffer = SignalingCrypto.Encrypt(
                signalingKey,
                room,
                new OfferPayload(
                    "offer",
                    offer,
                    relay?.IceServers.Select(server => new BrowserIceServer(server.Urls, server.Username, server.Credential)).ToArray() ?? [],
                    RelayAvailable: relay is not null,
                    RelayRequired: options.Relay,
                    MaximumBitrate: relay?.MaximumBitrate));
            await signaling.CreateOfferAsync(room, encryptedOffer, cancellation.Token).ConfigureAwait(false);
            roomCreated = true;

            Uri pageUri = new(options.SignalEndpoint, "./");
            string pageUrl = new UriBuilder(pageUri) { Fragment = $"{room}.{SignalingCrypto.Base64Url(signalingKey)}" }.Uri.AbsoluteUri;
            Console.WriteLine($"Room: {room}");
            Console.WriteLine($"URL: {pageUrl}");
            Console.WriteLine(options.Relay
                ? "Open the URL in the iPhone browser being tested. Relay is selected and enforced."
                : "Open the URL in the iPhone browser being tested. Direct LAN is selected.");
            Console.WriteLine("If Windows Firewall prompts, allow this spike on Private networks only.");
            Console.WriteLine("Waiting for the browser answer (up to 5 minutes)...");

            EncryptedEnvelope encryptedAnswer = await signaling.WaitForAnswerAsync(room, AnswerWait, cancellation.Token).ConfigureAwait(false);
            roomCreated = false; // get_answer consumes the temporary room state.
            AnswerPayload answer = SignalingCrypto.Decrypt<AnswerPayload>(signalingKey, room, encryptedAnswer);
            if (!string.Equals(answer.Type, "answer", StringComparison.Ordinal) ||
                answer.Transport is not ("direct" or "relay") ||
                answer.Transport == "relay" && relay is null ||
                options.Relay && answer.Transport != "relay")
            {
                throw new InvalidOperationException("The browser returned an invalid transport answer.");
            }
            peer.ApplyAnswer(answer.Sdp);

            int accessUnits = 0;
            peer.AccessUnitReceived += (bytes, _) =>
            {
                int count = Interlocked.Increment(ref accessUnits);
                if (count == 1) Console.WriteLine($"H.264 access units received; first access unit bytes: {bytes.Length}");
            };
            await peer.TrackOpen.WaitAsync(TimeSpan.FromSeconds(30), cancellation.Token).ConfigureAwait(false);
            peer.PrintSelectedRoute();
            Console.WriteLine($"The {answer.Transport} H.264 receive track is connected. Press Ctrl+C to stop.");

            if (options.Benchmark)
            {
                string outputPath = options.BenchmarkOutput ?? Path.Combine(
                    "apps",
                    "webrtc-spike-host",
                    "artifacts",
                    $"webcam-benchmark-{answer.Transport}.json");
                BenchmarkResult result = await BenchmarkSession.RunAsync(
                    answer.Transport,
                    outputPath,
                    () => pipeline.Failure,
                    () => pipeline.VisibleSourceSize,
                    cancellation.Token).ConfigureAwait(false);
                Console.WriteLine(FormattableString.Invariant(
                    $"Benchmark: source {result.SourceWidth}x{result.SourceHeight}; {result.EffectiveFps:F1} fps; p50 {result.P50LatencyMilliseconds:F0} ms; p95 {result.P95LatencyMilliseconds:F0} ms; drops {result.Drops}."));
                return BenchmarkSession.MeetsPassCriteria(result) ? 0 : 1;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token).ConfigureAwait(false);
            return 0; // Unreachable; cancellation is handled below.
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.WriteLine("Stopped.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Spike failed: {exception.Message}");
            return 1;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signalingKey);
            if (roomCreated)
            {
                try
                {
                    await signaling.DeleteAsync(room, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Could not remove the temporary signaling room: {exception.Message}");
                }
            }
        }
    }

    private static SpikeOptions ParseOptions(string[] args)
    {
        string value = "https://voltura.se/spike/signal.php";
        bool relay = false;
        bool pipeTest = false;
        bool benchmark = false;
        string? benchmarkOutput = null;
        for (int index = 0; index < args.Length; ++index)
        {
            if (args[index] == "--relay") relay = true;
            else if (args[index] == "--signal" && index + 1 < args.Length) value = args[++index];
            else if (args[index] == "--pipe-test") pipeTest = true;
            else if (args[index] == "--benchmark") benchmark = true;
            else if (args[index] == "--benchmark-output" && index + 1 < args.Length) benchmarkOutput = args[++index];
            else throw new ArgumentException("Usage: WebRtcSpike.Host [--relay] [--signal https://voltura.se/spike/signal.php] [--pipe-test] [--benchmark] [--benchmark-output result.json]");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("The signaling endpoint must be an absolute HTTP or HTTPS URL.");
        }

        if (endpoint.Scheme != Uri.UriSchemeHttps && !endpoint.IsLoopback)
        {
            throw new ArgumentException("Non-loopback signaling endpoints must use HTTPS.");
        }

        if (pipeTest && (relay || benchmark || benchmarkOutput is not null))
            throw new ArgumentException("--pipe-test cannot be combined with Relay or benchmark options.");
        if (benchmarkOutput is not null && !benchmark)
            throw new ArgumentException("--benchmark-output requires --benchmark.");

        return new SpikeOptions(endpoint, relay, pipeTest, benchmark, benchmarkOutput);
    }

    private static async Task WaitForRelayConnectionAsync(
        RelayHostConnection connection,
        CancellationToken cancellationToken)
    {
        if (connection.State == RelayConnectionState.Connected) return;
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void StateChanged(object? sender, EventArgs eventArgs)
        {
            _ = sender;
            _ = eventArgs;
            if (connection.State == RelayConnectionState.Connected) connected.TrySetResult();
        }

        connection.StateChanged += StateChanged;
        try
        {
            if (connection.State == RelayConnectionState.Connected) return;
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            Console.WriteLine("Relay host route authenticated.");
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                $"The existing Relay host route did not authenticate within 20 seconds (state {connection.State}; code {connection.FailureCode ?? "none"}).");
        }
        finally
        {
            connection.StateChanged -= StateChanged;
        }
    }

    private static async Task<int> RunPipeTestAsync(CancellationToken cancellationToken)
    {
        await using var pipe = new FramePipeServer();
        byte[] frame = ArrayPool<byte>.Shared.Rent(MediaFoundationH264Decoder.FrameBytes);
        frame.AsSpan(0, MediaFoundationH264Decoder.Width * MediaFoundationH264Decoder.Height).Fill(90);
        frame.AsSpan(
            MediaFoundationH264Decoder.Width * MediaFoundationH264Decoder.Height,
            MediaFoundationH264Decoder.FrameBytes - MediaFoundationH264Decoder.Width * MediaFoundationH264Decoder.Height).Fill(128);
        pipe.Publish(new DecodedFrame(1, 0, frame));
        Console.WriteLine("Pipe test waiting for a virtual-camera consumer (20 seconds)...");
        try
        {
            await pipe.WaitForFirstFrameAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            Console.WriteLine("Pipe test passed.");
            return 0;
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("Pipe test failed: no authenticated virtual-camera consumer received the frame.");
            return 1;
        }
    }

    private static string CreateRoomToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record SpikeOptions(Uri SignalEndpoint, bool Relay, bool PipeTest, bool Benchmark, string? BenchmarkOutput);
    private sealed record BrowserIceServer(IReadOnlyList<string> Urls, string Username, string Credential);
    private sealed record OfferPayload(
        string Type,
        string Sdp,
        BrowserIceServer[] IceServers,
        bool RelayAvailable,
        bool RelayRequired,
        int? MaximumBitrate);
    private sealed record AnswerPayload(string Type, string Sdp, string Transport);
}
