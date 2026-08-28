using System.Collections.Concurrent;
using VolturaAir.Host.Features.AiAssistant;

namespace VolturaAir.Host.Tests;

public sealed class AiAssistantPendingOpenPublicationTests
{
    [Fact]
    public async Task RevocationCannotPublishClosedBeforeAnAdmittedOpenSuccess()
    {
        using var publication = new AiAssistantPendingOpenPublication();
        var observed = new ConcurrentQueue<string>();
        var successAdmissionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSuccessAdmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pendingOpenCancellation = new CancellationTokenSource();

        Task success = publication.PublishOpenResultAsync(
            async publicationToken =>
            {
                Assert.False(publicationToken.CanBeCanceled);
                successAdmissionStarted.TrySetResult();
                await releaseSuccessAdmission.Task.ConfigureAwait(false);
                observed.Enqueue("success");
            },
            pendingOpenCancellation.Token);
        await successAdmissionStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        pendingOpenCancellation.Cancel();

        Task revocation = publication.PublishAccessRevocationAsync(
            () =>
            {
                observed.Enqueue("cleanup");
                return Task.CompletedTask;
            },
            () =>
            {
                observed.Enqueue("failure");
                return Task.CompletedTask;
            },
            () =>
            {
                observed.Enqueue("closed");
                return Task.CompletedTask;
            });

        Assert.False(revocation.IsCompleted);
        releaseSuccessAdmission.TrySetResult();
        await Task.WhenAll(success, revocation);

        Assert.Equal(["success", "cleanup", "closed"], observed);
    }
}
