using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed class PresentationCommandHandler(
    InputDispatcher inputDispatcher,
    HostStatusPayloadFactory statusFactory,
    PresentationLaserPointerController laserPointer,
    IPowerPointAutomationService powerPoint,
    PowerPointPresentationCatalog presentationCatalog,
    PowerPointPresentationSessionService presentationSession,
    IPresentationBlankOverlay blankOverlay,
    PairingManager pairingManager,
    WebSocketTransport transport,
    IAppLogWriter appLog)
{
    public async Task HandleAsync(
        WebSocket socket,
        string clientId,
        JsonElement message,
        CancellationToken cancellationToken)
    {
        var operationId = ProtocolMessageFields.GetString(message, "operationId");
        var target = ProtocolMessageFields.GetString(message, "target");
        var action = ProtocolMessageFields.GetString(message, "action");
        var requestedState = message.TryGetProperty("enabled", out var enabled)
            ? enabled.GetBoolean()
            : (bool?)null;
        var runtimePresentationId =
            ProtocolMessageFields.GetString(message, "runtimePresentationId") is { Length: > 0 } runtimeId
                ? runtimeId
                : null;
        var slideNumber = message.TryGetProperty("slideNumber", out var slide)
            ? slide.GetInt32()
            : (int?)null;
        var result = await ExecuteAsync(
            clientId,
            target,
            action,
            runtimePresentationId,
            slideNumber,
            requestedState,
            cancellationToken).ConfigureAwait(false);
        WriteOutcome(clientId, target, action, result);

        await transport.SendAsync(socket, new
        {
            type = "presentation.command.result",
            operationId,
            target,
            action,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message,
            laserPointerActive = laserPointer.IsEnabled,
            runtimePresentationId = result.RuntimePresentationId,
            presentation = result.Presentation
        }, cancellationToken);
    }

    public async Task HandlePowerPointRefreshAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        CancellationToken cancellationToken)
    {
        presentationCatalog.Refresh();
        if (!AppDeveloperSettings.EnableAlphaFeatures())
        {
            await SendRefreshResultAsync(
                socket,
                operationId,
                new(
                    false,
                    "feature-disabled",
                    "Presentation is an alpha feature and is disabled on the PC.",
                    powerPoint.Snapshot),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!statusFactory.CanControlPresentations(clientId))
        {
            await SendRefreshResultAsync(
                socket,
                operationId,
                new(
                    false,
                    "permission-denied",
                    "Presentation control is disabled for this device on the PC.",
                    powerPoint.Snapshot),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = await powerPoint.RefreshAsync(cancellationToken).ConfigureAwait(false);
        await SendRefreshResultAsync(socket, operationId, result, cancellationToken).ConfigureAwait(false);
    }

    public void DisableLaserForClient(string clientId) => laserPointer.DisableForClient(clientId);

    private async Task<PresentationCommandResult> ExecuteAsync(
        string clientId,
        string target,
        string action,
        string? runtimePresentationId,
        int? slideNumber,
        bool? requestedState,
        CancellationToken cancellationToken)
    {
        if (action == "pointer" &&
            PresentationCommands.IsTarget(target) &&
            requestedState is false)
        {
            return await DisablePointerAsync(
                clientId,
                target).ConfigureAwait(false);
        }

        if (!AppDeveloperSettings.EnableAlphaFeatures())
        {
            return new(
                false,
                "feature-disabled",
                "Presentation is an alpha feature and is disabled on the PC.");
        }

        if (!statusFactory.CanControlPresentations(clientId))
        {
            return new(
                false,
                "permission-denied",
                "Presentation control is disabled for this device on the PC.");
        }

        if (string.Equals(target, "powerpoint", StringComparison.Ordinal))
        {
            return await ExecutePowerPointAsync(
                clientId,
                action,
                runtimePresentationId,
                slideNumber,
                requestedState,
                cancellationToken).ConfigureAwait(false);
        }

        if (action == "pointer" && requestedState is true)
        {
            return EnableNonPowerPointLaser(clientId);
        }

        if (!PresentationCommands.TryResolve(target, action, out var shortcut))
        {
            return new(
                false,
                "unsupported-action",
                "That control is not available for the selected presentation target.");
        }

        try
        {
            PresentationCommandResult result =
                inputDispatcher.DispatchShortcut(shortcut.Key, shortcut.Modifiers) switch
                {
                    InputDispatchOutcome.Executed => new(true, null, shortcut.ResultMessage),
                    InputDispatchOutcome.Blocked => new(
                        false,
                        "host-ui-blocked",
                        "Switch focus from Voltura Air to the presentation, then try again."),
                    _ => new(
                        false,
                        "input-failed",
                        "Windows did not complete the presentation command. Try again.")
                };
            if (action == "end")
            {
                laserPointer.DisableForClient(clientId);
            }

            return result;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            WriteFailure(clientId, "presentation_input", exception);
            return new(
                false,
                "input-failed",
                "Windows did not accept the presentation command. Try again.");
        }
    }

    private async Task<PresentationCommandResult> ExecutePowerPointAsync(
        string clientId,
        string action,
        string? runtimePresentationId,
        int? slideNumber,
        bool? requestedState,
        CancellationToken cancellationToken)
    {
        var selected = ResolveSelectedPresentation(runtimePresentationId);
        if (laserPointer.IsEnabled &&
            runtimePresentationId is not null &&
            !string.Equals(
                laserPointer.RuntimePresentationId,
                runtimePresentationId,
                StringComparison.Ordinal))
        {
            return new(
                false,
                "pointer-owner-active",
                "Turn off the active laser pointer before choosing another presentation.");
        }

        if (action == "pointer" && requestedState is true)
        {
            if (laserPointer.IsEnabled &&
                (!laserPointer.IsOwnedBy(clientId) ||
                 runtimePresentationId is not null &&
                 !string.Equals(
                     laserPointer.RuntimePresentationId,
                     runtimePresentationId,
                     StringComparison.Ordinal)))
            {
                return new(
                    false,
                    "pointer-owner-active",
                    "Turn off the active laser pointer before choosing another presentation.");
            }

            if (selected is not { IsPresenting: true })
            {
                return new(
                    false,
                    selected is null
                        ? "powerpoint-target-stale"
                        : "powerpoint-not-presenting",
                    selected is null
                        ? "Choose an available PowerPoint presentation."
                        : "Start the selected PowerPoint slideshow before using the laser pointer.",
                    selected?.RuntimePresentationId,
                    selected is null ? null : ToProtocolPresentation(selected));
            }

            try
            {
                laserPointer.SetEnabled(
                    clientId,
                    enabled: true,
                    selected.RuntimePresentationId);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                WriteFailure(clientId, "presentation_laser_pointer", exception);
                return new(
                    false,
                    "pointer-failed",
                    "Windows could not change the Voltura Air laser pointer.");
            }

            var visibility = await powerPoint.ExecuteAsync(
                new(action, selected.RuntimePresentationId, Enabled: true),
                cancellationToken).ConfigureAwait(false);
            if (!visibility.Succeeded)
            {
                WriteDegraded(
                    clientId,
                    "presentation_laser_pointer_visibility",
                    visibility.Code);
            }

            return new(
                true,
                null,
                "Voltura Air laser pointer enabled.",
                selected.RuntimePresentationId,
                ToProtocolPresentation(visibility.Presentation ?? selected));
        }

        if (action is "black" or "white" &&
            selected is { IsPresenting: false })
        {
            var blank = blankOverlay.TryShowPresentationBlank(
                selected.RuntimePresentationId,
                action == "white");
            return blank.Succeeded
                ? new(
                    true,
                    null,
                    action == "white"
                        ? "Voltura Air white screen toggled."
                        : "Voltura Air black screen toggled.",
                    selected.RuntimePresentationId,
                    ToProtocolPresentation(selected, blankOverlay.Snapshot))
                : new(
                    false,
                    "presentation-blank-failed",
                    action == "white"
                        ? "Voltura Air could not show the white screen."
                        : "Voltura Air could not show the black screen.",
                    selected.RuntimePresentationId,
                    ToProtocolPresentation(selected, blankOverlay.Snapshot));
        }

        if (action is "next" or "previous" &&
            selected is { IsPresenting: false, CurrentSlideIndex: null })
        {
            return new(
                false,
                "powerpoint-current-slide-unavailable",
                "PowerPoint could not determine the current editor slide.",
                selected.RuntimePresentationId,
                ToProtocolPresentation(selected));
        }

        var navigatesFromReady = action is "next" or "previous" &&
            selected is { IsPresenting: false, CurrentSlideIndex: not null };
        var startsSlideshow = action is "start" or "start-current" ||
            action == "goto" && selected is { IsPresenting: false } ||
            navigatesFromReady;
        var automationAction = action switch
        {
            "start" when selected is { IsPresenting: true } => "first",
            "start-current" when selected is { IsPresenting: true } => "activate",
            _ => action
        };
        if (startsSlideshow && selected is not null)
        {
            var canStart = presentationSession.CanStartOrResume(selected);
            if (!canStart.Succeeded)
            {
                return new(
                    false,
                    canStart.Code,
                    canStart.Message,
                    selected.RuntimePresentationId,
                    ToProtocolPresentation(selected));
            }

            if (action == "goto" &&
                (slideNumber is null ||
                 slideNumber < 1 ||
                 slideNumber > selected.SlideCount))
            {
                return new(
                    false,
                    "powerpoint-invalid-slide",
                    $"Choose a slide from 1 to {selected.SlideCount}.",
                    selected.RuntimePresentationId,
                    ToProtocolPresentation(selected));
            }

            _ = blankOverlay.DismissPresentationBlankIfActive();
        }

        if (action == "end" &&
            string.Equals(
                laserPointer.RuntimePresentationId,
                runtimePresentationId,
                StringComparison.Ordinal))
        {
            _ = await powerPoint.ExecuteAsync(
                new("pointer", runtimePresentationId, Enabled: false),
                cancellationToken).ConfigureAwait(false);
            laserPointer.DisableForClient(clientId, restorePowerPoint: false);
        }

        PowerPointAutomationResult result;
        if (action == "goto" && selected is { IsPresenting: false })
        {
            var started = await powerPoint.ExecuteAsync(
                new("start", selected.RuntimePresentationId),
                cancellationToken).ConfigureAwait(false);
            if (!started.Succeeded)
            {
                return FromPowerPoint(started);
            }

            presentationSession.PrepareCommand(action, selected.RuntimePresentationId);
            result = await powerPoint.ExecuteAsync(
                new(action, selected.RuntimePresentationId, slideNumber, requestedState),
                cancellationToken).ConfigureAwait(false);
        }
        else if (navigatesFromReady)
        {
            var started = await powerPoint.ExecuteAsync(
                new("start-current", selected!.RuntimePresentationId),
                cancellationToken).ConfigureAwait(false);
            if (!started.Succeeded)
            {
                return FromPowerPoint(started);
            }

            presentationSession.PrepareCommand(action, selected.RuntimePresentationId);
            result = await powerPoint.ExecuteAsync(
                new(action, selected.RuntimePresentationId),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            presentationSession.PrepareCommand(
                automationAction,
                runtimePresentationId);
            result = await powerPoint.ExecuteAsync(
                new(
                    automationAction,
                    runtimePresentationId,
                    slideNumber,
                    requestedState),
                cancellationToken).ConfigureAwait(false);
        }

        presentationSession.CompleteCommand(result.Presentation);
        if (result.Succeeded &&
            startsSlideshow &&
            result.Presentation is { } startedPresentation &&
            pairingManager.GetDeviceName(clientId) is { } deviceName)
        {
            var sessionResult = presentationSession.StartOrResume(
                clientId,
                deviceName,
                startedPresentation);
            if (!sessionResult.Succeeded)
            {
                return new(
                    false,
                    sessionResult.Code,
                    sessionResult.Message,
                    startedPresentation.RuntimePresentationId,
                    ToProtocolPresentation(startedPresentation));
            }
        }

        return FromPowerPoint(result);
    }

    private PowerPointPresentationSnapshot? ResolveSelectedPresentation(
        string? runtimePresentationId)
    {
        var snapshot = powerPoint.Snapshot;
        if (snapshot.State != PowerPointDiscoveryState.Ready)
        {
            return null;
        }

        return runtimePresentationId is { Length: > 0 }
            ? snapshot.Presentations.FirstOrDefault(presentation =>
                string.Equals(
                    presentation.RuntimePresentationId,
                    runtimePresentationId,
                    StringComparison.Ordinal))
            : snapshot.Presentations.Count == 1
                ? snapshot.Presentations[0]
                : null;
    }

    private Task<PresentationCommandResult> DisablePointerAsync(
        string clientId,
        string target)
    {
        if (laserPointer.IsEnabled && !laserPointer.IsOwnedBy(clientId))
        {
            return Task.FromResult<PresentationCommandResult>(new(
                false,
                "pointer-owner-active",
                "Only the device that enabled the laser pointer can turn it off."));
        }

        try
        {
            if (string.Equals(target, "powerpoint", StringComparison.Ordinal) &&
                laserPointer.RuntimePresentationId is { Length: > 0 } runtimeId)
            {
                laserPointer.DisableForClient(clientId);
                return Task.FromResult<PresentationCommandResult>(new(
                    true,
                    null,
                    "Voltura Air laser pointer disabled.",
                    runtimeId,
                    ResolveSelectedPresentation(runtimeId) is { } selected
                        ? ToProtocolPresentation(selected)
                        : null));
            }

            laserPointer.DisableForClient(clientId);
            return Task.FromResult<PresentationCommandResult>(
                new(true, null, "Voltura Air laser pointer disabled."));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            WriteFailure(clientId, "presentation_laser_pointer", exception);
            return Task.FromResult<PresentationCommandResult>(new(
                false,
                "pointer-failed",
                "Windows could not change the Voltura Air laser pointer."));
        }
    }

    private PresentationCommandResult EnableNonPowerPointLaser(string clientId)
    {
        try
        {
            laserPointer.SetEnabled(clientId, enabled: true);
            return new(true, null, "Voltura Air laser pointer enabled.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            WriteFailure(clientId, "presentation_laser_pointer", exception);
            return new(
                false,
                "pointer-failed",
                "Windows could not change the Voltura Air laser pointer.");
        }
    }

    private async Task SendRefreshResultAsync(
        WebSocket socket,
        string operationId,
        PowerPointAutomationResult result,
        CancellationToken cancellationToken)
    {
        await transport.SendAsync(socket, new
        {
            type = "presentation.powerpoint.refresh.result",
            operationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message,
            state = ToProtocolState(result.Snapshot.State),
            presentations = result.Snapshot.Presentations.Select(
                presentation => ToProtocolPresentation(
                    presentation,
                    blankOverlay.Snapshot))
        }, cancellationToken);
    }

    internal static object ToProtocolPresentation(
        PowerPointPresentationSnapshot presentation,
        PresentationBlankOverlaySnapshot? blank = null) => new
        {
            runtimePresentationId = presentation.RuntimePresentationId,
            name = presentation.Name,
            state = presentation.IsPresenting ? "presenting" : "ready",
            slideCount = presentation.SlideCount,
            currentSlideIndex = presentation.CurrentSlideIndex,
            currentShowPosition = presentation.CurrentShowPosition,
            slideShowState = blank is not null &&
            string.Equals(
                blank.RuntimePresentationId,
                presentation.RuntimePresentationId,
                StringComparison.Ordinal)
                ? blank.SlideShowState
                : presentation.SlideShowState
        };

    internal static string ToProtocolState(PowerPointDiscoveryState state) => state switch
    {
        PowerPointDiscoveryState.Ready => "ready",
        PowerPointDiscoveryState.Busy => "busy",
        _ => "unavailable"
    };

    private static PresentationCommandResult FromPowerPoint(
        PowerPointAutomationResult result) =>
        new(
            result.Succeeded,
            result.Code,
            result.Message,
            result.Presentation?.RuntimePresentationId,
            result.Presentation is null
                ? null
                : ToProtocolPresentation(result.Presentation));

    private void WriteOutcome(
        string clientId,
        string target,
        string action,
        PresentationCommandResult result) =>
        appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: "presentation.command",
            Action: $"{target}:{action}",
            Outcome: result.Succeeded ? "executed" : result.Code));

    private void WriteFailure(
        string clientId,
        string action,
        Exception exception) =>
        appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            ClientId: clientId,
            Action: action,
            Outcome: "failed",
            Detail: exception.Message));

    private void WriteDegraded(
        string clientId,
        string action,
        string? detail) =>
        appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            ClientId: clientId,
            Action: action,
            Outcome: "degraded",
            Detail: detail));
}

internal readonly record struct PresentationCommandResult(
    bool Succeeded,
    string? Code,
    string Message,
    string? RuntimePresentationId = null,
    object? Presentation = null);
