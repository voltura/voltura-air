using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class PowerPointPresentationLaunchHandler(
    PairingManager pairingManager,
    HostStatusPayloadFactory statusFactory,
    PowerPointPresentationCatalog catalog,
    IAppLaunchService appLaunchService,
    IPowerPointAutomationService powerPoint,
    PowerPointPresentationSessionService session,
    PresentationLaserPointerController laserPointer,
    WebSocketTransport transport,
    IAppLogWriter appLog)
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromMilliseconds(250);
    private int _launchActive;

    internal async Task HandleAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string presentationId,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(clientId, presentationId, cancellationToken).ConfigureAwait(false);
        appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: "presentation.powerpoint.launch",
            Action: "open-and-present",
            Outcome: result.Succeeded ? "executed" : result.Code));
        await transport.SendAsync(socket, new
        {
            type = "presentation.powerpoint.launch.result",
            operationId,
            presentationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message,
            runtimePresentationId = result.Presentation?.RuntimePresentationId,
            presentation = result.Presentation is null
                ? null
                : PresentationCommandHandler.ToProtocolPresentation(result.Presentation)
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LaunchResult> ExecuteAsync(
        string clientId,
        string presentationId,
        CancellationToken cancellationToken)
    {
        if (!AppDeveloperSettings.EnableAlphaFeatures())
        {
            return Failure("feature-disabled", "Presentation is disabled on the PC.");
        }

        if (!statusFactory.CanControlPresentations(clientId))
        {
            return Failure("permission-denied", "Presentation control is disabled for this device.");
        }

        if (Interlocked.CompareExchange(ref _launchActive, 1, 0) != 0)
        {
            return Failure("powerpoint-busy", "Another presentation is already opening.");
        }

        try
        {
            if (session.Snapshot.State != "inactive")
            {
                return Failure("session-active", "Save or discard the current presentation before opening another.");
            }

            if (laserPointer.IsEnabled)
            {
                return Failure("pointer-owner-active", "Turn off the laser pointer before changing presentations.");
            }

            var candidate = catalog.Resolve(presentationId);
            if (candidate is null || !File.Exists(candidate.CanonicalPath))
            {
                return Failure("powerpoint-source-missing", "That saved PowerPoint file is no longer available. Refresh the list.");
            }

            var presentation = FindOpen(candidate.CanonicalPath, powerPoint.Snapshot);
            if (presentation is null && powerPoint.Snapshot.State == PowerPointDiscoveryState.Ready)
            {
                var opened = await powerPoint.ExecuteAsync(
                    new("open", SourcePath: candidate.CanonicalPath),
                    cancellationToken).ConfigureAwait(false);
                if (!opened.Succeeded)
                {
                    return Failure(opened.Code ?? "powerpoint-open-failed", opened.Message);
                }

                presentation = opened.Presentation;
            }

            if (presentation is null)
            {
                var started = appLaunchService.ExecutePowerPointFile(candidate.CanonicalPath);
                if (!started.Succeeded)
                {
                    return Failure(started.Code, started.Message);
                }

                presentation = await WaitForPresentationAsync(candidate.CanonicalPath, cancellationToken).ConfigureAwait(false);
                if (presentation is null)
                {
                    return Failure("powerpoint-open-timeout", "PowerPoint started, but the selected presentation was not ready in time.");
                }
            }

            var slideshow = await powerPoint.ExecuteAsync(
                new("start", presentation.RuntimePresentationId),
                cancellationToken).ConfigureAwait(false);
            if (!slideshow.Succeeded || slideshow.Presentation is null)
            {
                return Failure(slideshow.Code ?? "powerpoint-automation-failed", slideshow.Message);
            }

            var deviceName = pairingManager.GetDeviceName(clientId);
            if (deviceName is null)
            {
                return Failure("device-revoked", "This device is no longer paired with the PC.");
            }

            var sessionResult = session.Start(clientId, deviceName, slideshow.Presentation);
            return sessionResult.Succeeded
                ? new(true, null, "Presentation opened and started.", slideshow.Presentation)
                : Failure(sessionResult.Code ?? "session-active", sessionResult.Message, slideshow.Presentation);
        }
        finally
        {
            Volatile.Write(ref _launchActive, 0);
        }
    }

    private async Task<PowerPointPresentationSnapshot?> WaitForPresentationAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + DiscoveryTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(DiscoveryInterval, cancellationToken).ConfigureAwait(false);
            var refreshed = await powerPoint.RefreshAsync(cancellationToken).ConfigureAwait(false);
            var presentation = FindOpen(path, refreshed.Snapshot);
            if (presentation is not null)
            {
                return presentation;
            }
        }

        return null;
    }

    private static PowerPointPresentationSnapshot? FindOpen(
        string path,
        PowerPointAutomationSnapshot snapshot) =>
        snapshot.Presentations.FirstOrDefault(item =>
            string.Equals(
                PowerPointPresentationCatalog.NormalizePath(item.SourcePath),
                path,
                StringComparison.OrdinalIgnoreCase));

    private static LaunchResult Failure(
        string code,
        string message,
        PowerPointPresentationSnapshot? presentation = null) =>
        new(false, code, message, presentation);

    private sealed record LaunchResult(
        bool Succeeded,
        string? Code,
        string Message,
        PowerPointPresentationSnapshot? Presentation);
}
