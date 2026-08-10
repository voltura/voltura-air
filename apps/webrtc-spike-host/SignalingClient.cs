using System.Net.Http.Json;
using System.Text.Json;

namespace WebRtcSpike.Host;

internal sealed class SignalingClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;

    internal SignalingClient(Uri endpoint)
    {
        _endpoint = endpoint;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    internal async Task CreateOfferAsync(string room, string offer, CancellationToken cancellationToken)
    {
        SignalResponse response = await PostAsync(new { op = "create", room, offer }, cancellationToken).ConfigureAwait(false);
        EnsureOk(response, "publish the offer");
    }

    internal async Task<string> WaitForAnswerAsync(
        string room,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        while (true)
        {
            SignalResponse response = await PostAsync(new { op = "get_answer", room }, deadline.Token).ConfigureAwait(false);
            EnsureOk(response, "retrieve the answer");
            if (response.Ready && !string.IsNullOrWhiteSpace(response.Answer)) return response.Answer;
            await Task.Delay(TimeSpan.FromSeconds(2), deadline.Token).ConfigureAwait(false);
        }
    }

    internal async Task DeleteAsync(string room, CancellationToken cancellationToken)
    {
        SignalResponse response = await PostAsync(new { op = "delete", room }, cancellationToken).ConfigureAwait(false);
        EnsureOk(response, "delete the room");
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<SignalResponse> PostAsync<T>(T payload, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(_endpoint, payload, JsonOptions, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        SignalResponse? result;
        try
        {
            result = JsonSerializer.Deserialize<SignalResponse>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Signaling returned HTTP {(int)response.StatusCode} with an invalid response.", exception);
        }

        if (result is null)
        {
            throw new InvalidOperationException($"Signaling returned HTTP {(int)response.StatusCode} with an empty response.");
        }

        if (!response.IsSuccessStatusCode && result.Ok)
        {
            throw new InvalidOperationException($"Signaling returned HTTP {(int)response.StatusCode}.");
        }

        return result;
    }

    private static void EnsureOk(SignalResponse response, string operation)
    {
        if (!response.Ok) throw new InvalidOperationException($"Could not {operation}: {response.Error ?? "unknown signaling error"}");
    }

    private sealed record SignalResponse(bool Ok, bool Ready = false, string? Answer = null, string? Error = null);
}
