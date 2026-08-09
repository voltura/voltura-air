using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class CustomScreenCommandHandler(
    CustomScreenService screens,
    HostStatusPayloadFactory statusFactory,
    InputDispatcher inputDispatcher,
    ISystemPowerController powerController,
    IWorkstationLockPolicy workstationLockPolicy,
    IAppLaunchService appLaunchService,
    IUrlOpenService urlOpenService,
    WebSocketTransport transport,
    IAppLogWriter appLog)
{
    public Task GetAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string screenId,
        CancellationToken cancellationToken)
    {
        var definition = screens.GetMobileDefinition(
            clientId,
            screenId,
            statusFactory.CanUseRemoteInput(clientId),
            statusFactory.CanLaunchRemoteApps(clientId),
            statusFactory.CanControlVolume(clientId),
            statusFactory.CanOpenUrls(clientId),
            statusFactory.GetEffectivePermissions(clientId),
            CustomScreenHostActions.All
                .Where(action => !CustomScreenHostActions.IsAvailable(
                    action.Id,
                    powerController,
                    workstationLockPolicy))
                .Select(action => action.Id)
                .ToHashSet(StringComparer.Ordinal));
        return definition is null
            ? SendGetFailureAsync(
                socket,
                operationId,
                "not-assigned",
                "This custom screen is not assigned to this device.",
                cancellationToken)
            : transport.SendAsync(socket, new
            {
                type = "custom.screen.get.result",
                operationId,
                succeeded = true,
                screen = definition
            }, cancellationToken);
    }

    public async Task InvokeAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string screenId,
        string screenRevision,
        string buttonId,
        CancellationToken cancellationToken)
    {
        var result = Execute(clientId, screenId, screenRevision, buttonId);
        appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: "custom.screen.invoke",
            Action: $"{screenId}/{buttonId}",
            Outcome: result.Succeeded ? "succeeded" : result.Code));
        await transport.SendAsync(socket, new
        {
            type = "custom.screen.invoke.result",
            operationId,
            screenId,
            buttonId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message
        }, cancellationToken);
    }

    private CustomScreenExecutionResult Execute(
        string clientId,
        string screenId,
        string screenRevision,
        string buttonId)
    {
        var screen = screens.Find(screenId);
        if (screen is null || !screen.AssignedClientIds.Contains(clientId, StringComparer.Ordinal))
        {
            return new(false, "not-assigned", "This custom screen is not assigned to this device.");
        }

        if (!string.Equals(screen.Revision, screenRevision, StringComparison.Ordinal))
        {
            return new(false, "stale-screen", "This custom screen changed on the PC. Refresh it and try again.");
        }

        var button = screen.Sections
            .SelectMany(section => section.Buttons)
            .FirstOrDefault(candidate => string.Equals(candidate.Id, buttonId, StringComparison.Ordinal));
        if (button is null)
        {
            return new(false, "button-not-found", "This button is no longer available.");
        }

        var requiredKnownApp = CustomScreenKnownAppDependency.Find(screen);
        if (requiredKnownApp is not null && !appLaunchService.GetKnownApplications()
                .Any(application =>
                    application.Available &&
                    string.Equals(application.Id, requiredKnownApp, StringComparison.Ordinal)))
        {
            return new(
                false,
                "action-unavailable",
                CustomScreenKnownAppDependency.UnavailableReason(requiredKnownApp));
        }

        var action = button.Action;
        if (action.Kind == "appLaunch")
        {
            if (!statusFactory.CanLaunchRemoteApps(clientId))
            {
                return new(false, "permission-denied", "Application launch is disabled for this device on the PC.");
            }

            var launch = appLaunchService.Execute(action.ActionId ?? string.Empty);
            return new(launch.Succeeded, launch.Code, launch.Message);
        }

        if (action.Kind == "knownApp")
        {
            if (!statusFactory.CanLaunchRemoteApps(clientId))
            {
                return new(false, "permission-denied", "Application launch is disabled for this device on the PC.");
            }

            var launch = appLaunchService.ExecuteKnown(action.ActionId ?? string.Empty);
            return new(launch.Succeeded, launch.Code, launch.Message);
        }

        if (action.Kind == "urlOpen")
        {
            if (!statusFactory.CanOpenUrls(clientId))
            {
                return new(false, "permission-denied", "Opening web addresses is disabled for this device on the PC.");
            }

            var opened = urlOpenService.Execute(action.Url ?? string.Empty);
            return new(opened.Succeeded, opened.Code, opened.Message);
        }

        if (action.Kind == "hostAction")
        {
            var actionId = action.ActionId ?? string.Empty;
            var permissions = statusFactory.GetEffectivePermissions(clientId);
            if (!CustomScreenHostActions.IsPermitted(actionId, permissions))
            {
                return new(false, "permission-denied", "This host or system action is disabled for this device on the PC.");
            }

            if (!CustomScreenHostActions.IsAvailable(actionId, powerController, workstationLockPolicy))
            {
                return new(false, "action-unavailable", "This host or system action is unavailable on this PC.");
            }

            var result = CustomScreenHostActions.Execute(actionId, powerController);
            return result.Succeeded
                ? new(true, "executed", "System action accepted by Windows.")
                : new(false, "dispatch-failed", "Windows did not complete this system action.");
        }

        if (!statusFactory.CanUseRemoteInput(clientId))
        {
            return new(false, "permission-denied", "Remote input is disabled for this device on the PC.");
        }

        var command = ResolveInput(action);
        if (command is null)
        {
            return new(false, "action-unavailable", "This button action is unavailable.");
        }

        try
        {
            _ = powerController.DismissBlackoutIfActive();
            if (!inputDispatcher.Dispatch(command.Value, out var outcome))
            {
                return new(false, "action-unavailable", "This button action is unavailable.");
            }

            return outcome switch
            {
                InputDispatchOutcome.Executed => new(true, "executed", "Action completed."),
                InputDispatchOutcome.Blocked => new(false, "input-blocked", "Voltura Air protected its own Windows controls from remote input."),
                _ => new(false, "dispatch-failed", "Windows did not complete this action.")
            };
        }
        catch (Exception ex) when (ex is InputDispatchException or InvalidOperationException)
        {
            InputDispatchDiagnostics.Write("custom.screen.invoke", null, string.Empty, ex);
            return new(false, "dispatch-failed", "Windows did not complete this action.");
        }
    }

    private static ValidatedInputCommand? ResolveInput(CustomScreenAction action)
    {
        if (action.Kind == "text")
        {
            return new(InputCommandKind.KeyboardText, Text: action.Text);
        }

        if (action.Kind == "shortcut")
        {
            return new(
                InputCommandKind.KeyboardSpecial,
                Key: action.Key,
                ModifierValues: [.. action.Modifiers ?? []]);
        }

        if (action.Kind == "builtIn" && CustomScreenBuiltIns.Find(action.BuiltIn) is { } builtIn)
        {
            return new(
                InputCommandKind.KeyboardSpecial,
                Key: builtIn.Key,
                ModifierValues: [.. builtIn.Modifiers]);
        }

        return null;
    }

    private Task SendGetFailureAsync(
        WebSocket socket,
        string operationId,
        string code,
        string message,
        CancellationToken cancellationToken) =>
        transport.SendAsync(socket, new
        {
            type = "custom.screen.get.result",
            operationId,
            succeeded = false,
            code,
            message
        }, cancellationToken);

    private sealed record CustomScreenExecutionResult(bool Succeeded, string Code, string Message);
}
