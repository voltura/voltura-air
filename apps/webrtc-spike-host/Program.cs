using System.Security.Cryptography;
using System.Text.Json;

namespace WebRtcSpike.Host;

internal static class Program
{
    private static readonly TimeSpan AnswerWait = TimeSpan.FromMinutes(5);

    public static async Task<int> Main(string[] args)
    {
        Uri signalEndpoint;
        try
        {
            signalEndpoint = ResolveSignalEndpoint(args);
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

        string room = CreateRoomToken();
        await using var peer = new WebRtcPeer();
        using var signaling = new SignalingClient(signalEndpoint);
        bool roomCreated = false;

        try
        {
            Console.WriteLine("ICE: gathering");
            string offer = await peer.CreateOfferAsync(cancellation.Token).ConfigureAwait(false);
            await signaling.CreateOfferAsync(room, offer, cancellation.Token).ConfigureAwait(false);
            roomCreated = true;

            Uri pageUri = new(signalEndpoint, "./");
            string pageUrl = new UriBuilder(pageUri) { Fragment = room }.Uri.AbsoluteUri;
            Console.WriteLine($"Room: {room}");
            Console.WriteLine($"URL: {pageUrl}");
            Console.WriteLine("Open the URL in Safari on an iPhone connected to the same private LAN.");
            Console.WriteLine("If Windows Firewall prompts, allow this spike on Private networks only.");
            Console.WriteLine("Waiting for the browser answer (up to 5 minutes)...");

            string answer = await signaling.WaitForAnswerAsync(room, AnswerWait, cancellation.Token).ConfigureAwait(false);
            roomCreated = false; // get_answer consumes the temporary room state.
            peer.ApplyAnswer(answer);

            await peer.DataChannelOpen.WaitAsync(TimeSpan.FromSeconds(30), cancellation.Token).ConfigureAwait(false);
            peer.PrintSelectedRoute();
            peer.SendJson(new { type = "host-ready", message = "Direct DataChannel is open." });
            Console.WriteLine("The spike is connected. Press Ctrl+C to stop.");

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

    private static Uri ResolveSignalEndpoint(string[] args)
    {
        string value = args.Length switch
        {
            0 => "https://voltura.se/spike/signal.php",
            2 when string.Equals(args[0], "--signal", StringComparison.Ordinal) => args[1],
            _ => throw new ArgumentException("Usage: WebRtcSpike.Host [--signal https://voltura.se/spike/signal.php]")
        };

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("The signaling endpoint must be an absolute HTTP or HTTPS URL.");
        }

        if (endpoint.Scheme != Uri.UriSchemeHttps && !endpoint.IsLoopback)
        {
            throw new ArgumentException("Non-loopback signaling endpoints must use HTTPS.");
        }

        return endpoint;
    }

    private static string CreateRoomToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
