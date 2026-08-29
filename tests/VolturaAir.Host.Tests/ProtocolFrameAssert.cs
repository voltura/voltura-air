namespace VolturaAir.Host.Tests;

internal static class ProtocolFrameAssert
{
    private static readonly Dictionary<string, string[]> RequiredFields = new()
    {
        ["pair.accepted"] = ["clientId", "pcName", "paired"],
        ["pair.disconnect.accepted"] = [],
        ["pair.challenge"] = ["clientId", "challenge"],
        ["pair.bootstrap.challenge"] = ["clientId", "clientNonce", "serverNonce", "hostIdentity", "proof"],
        ["pair.rejected"] = ["reason"],
        ["status"] = ["connected"],
        ["health.pong"] = [],
        ["input.ack"] = [],
        ["input.error"] = ["message"],
        ["presentation.command.result"] = ["operationId", "target", "action", "succeeded", "message", "laserPointerActive"],
        ["presentation.powerpoint.refresh.result"] = ["operationId", "succeeded", "message", "state", "presentations"],
        ["presentation.powerpoint.launch.result"] = ["operationId", "presentationId", "succeeded", "message"],
        ["presentation.session.result"] = ["operationId", "action", "succeeded", "message"],
        ["presentation.report.save.result"] = ["operationId", "reportId", "succeeded", "message"],
        ["system.power.result"] = ["operationId", "action", "succeeded", "message"],
        ["awake.result"] = ["operationId", "enabled", "succeeded", "message"],
        ["app.launch.result"] = ["operationId", "actionId", "succeeded", "message"],
        ["url.open.result"] = ["operationId", "succeeded", "message"],
        ["text.send.result"] = ["operationId", "succeeded", "message"],
        ["clipboard.get.result"] = ["operationId", "succeeded", "message"],
        ["diagnostics.get.result"] = ["operationId", "succeeded", "message"],
        ["custom.screen.get.result"] = ["operationId", "succeeded"],
        ["custom.screen.invoke.result"] = ["operationId", "screenId", "buttonId", "succeeded", "code", "message"],
        ["screen.view.sources.result"] = ["operationId", "succeeded", "message", "sources"],
        ["screen.view.start.result"] = ["operationId", "displayId", "succeeded", "message"],
        ["screen.view.answer.result"] = ["operationId", "succeeded", "message"],
        ["screen.view.source.result"] = ["operationId", "displayId", "succeeded", "message"],
        ["screen.view.stop.result"] = ["operationId", "succeeded", "message"],
        ["phone.webcam.start.result"] = ["operationId", "succeeded", "message"],
        ["phone.webcam.answer.result"] = ["operationId", "succeeded", "message"],
        ["phone.webcam.stop.result"] = ["operationId", "succeeded", "message"],
        ["phone.webcam.ended"] = ["operationId", "reason", "message"],
        ["file.transfer.start.result"] = ["operationId", "succeeded", "message"],
        ["file.transfer.offer"] = ["transferId", "direction", "fileName", "declaredSize", "offerSdp", "hostSignature"],
        ["file.transfer.answer.result"] = ["operationId", "succeeded", "message"],
        ["file.transfer.cancel.result"] = ["operationId", "succeeded", "message"],
        ["file.transfer.status"] = ["transferId", "direction", "state", "bytesCompleted", "bytesTotal"],
        ["file.transfer.result"] = ["transferId", "direction", "succeeded", "message", "fileName", "declaredSize"],
        ["terminal.start.result"] = ["operationId", "succeeded", "message"],
        ["terminal.attach.result"] = ["operationId", "succeeded", "message"],
        ["terminal.answer.result"] = ["operationId", "succeeded", "message"],
        ["terminal.stop.result"] = ["operationId", "succeeded", "message"],
        ["terminal.offer"] = ["operationId", "terminalId", "columns", "rows", "acknowledgedOffset", "offerSdp", "hostSignature"],
        ["terminal.status"] = ["terminalId", "state", "acknowledgedOffset"],
        ["terminal.ended"] = ["terminalId", "reason"],
        ["ai.assistant.open.result"] = ["operationId", "succeeded", "message"],
        ["ai.assistant.ask.result"] = ["operationId", "succeeded", "message"],
        ["ai.assistant.reset.result"] = ["operationId", "succeeded", "message"],
        ["ai.assistant.close.result"] = ["operationId", "succeeded", "message"],
        ["ai.assistant.snapshot.start"] = [],
        ["ai.assistant.snapshot.complete"] = ["messageCount"],
        ["ai.assistant.message"] = ["sequence", "messageId", "chunkIndex", "finalChunk", "sender", "text"],
        ["ai.assistant.state"] = ["state"],
        ["ai.assistant.closed"] = ["reason"],
        ["audio.state"] = ["volume", "muted"]
    };

    public static void Conforms(JsonElement frame)
    {
        Assert.Equal(JsonValueKind.Object, frame.ValueKind);
        Assert.True(frame.TryGetProperty("type", out var typeProperty));
        var type = typeProperty.GetString();
        Assert.NotNull(type);
        Assert.True(RequiredFields.TryGetValue(type, out var required), $"Unknown protocol frame type '{type}'. Add it to the protocol test catalog.");

        foreach (var field in required)
        {
            Assert.True(frame.TryGetProperty(field, out _), $"Protocol frame '{type}' is missing required field '{field}'.");
        }

        AssertNoNullOrPlaceholderEmptyValue(frame, type!, path: string.Empty);
    }

    private static void AssertNoNullOrPlaceholderEmptyValue(JsonElement value, string type, string path)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                if (!IsMeaningfulNull(type, path))
                {
                    Assert.Fail($"Protocol frame '{type}' contains null at '{path}'. Omit absent fields instead.");
                }
                return;
            case JsonValueKind.String:
                if (value.GetString()?.Length == 0)
                {
                    Assert.True(IsMeaningfulEmptyString(type, path), $"Protocol frame '{type}' contains an empty placeholder at '{path}'. Omit it instead.");
                }
                return;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    var childPath = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                    AssertNoNullOrPlaceholderEmptyValue(property.Value, type, childPath);
                }
                return;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    AssertNoNullOrPlaceholderEmptyValue(item, type, path);
                }
                return;
        }
    }

    private static bool IsMeaningfulEmptyString(string type, string path) =>
        type == "clipboard.get.result" && path == "text" ||
        type == "ai.assistant.message" && path == "text";

    private static bool IsMeaningfulNull(string type, string path) =>
        (type == "pair.accepted" || type == "status") && path == "host.accentColor";
}
