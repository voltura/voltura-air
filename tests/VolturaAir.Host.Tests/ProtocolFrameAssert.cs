namespace VolturaAir.Host.Tests;

internal static class ProtocolFrameAssert
{
    private static readonly Dictionary<string, string[]> RequiredFields = new()
    {
        ["pair.accepted"] = ["clientId", "pcName", "paired"],
        ["pair.challenge"] = ["clientId", "challenge"],
        ["pair.rejected"] = ["reason"],
        ["status"] = ["connected"],
        ["health.pong"] = [],
        ["input.ack"] = [],
        ["input.error"] = ["message"],
        ["presentation.command.result"] = ["operationId", "target", "action", "succeeded", "message", "laserPointerActive"],
        ["presentation.report.save.result"] = ["operationId", "reportId", "succeeded", "message"],
        ["system.power.result"] = ["operationId", "action", "succeeded", "message"],
        ["awake.result"] = ["operationId", "enabled", "succeeded", "message"],
        ["app.launch.result"] = ["operationId", "actionId", "succeeded", "message"],
        ["url.open.result"] = ["operationId", "succeeded", "message"],
        ["text.send.result"] = ["operationId", "succeeded", "message"],
        ["clipboard.get.result"] = ["operationId", "succeeded", "message"],
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
                Assert.Fail($"Protocol frame '{type}' contains null at '{path}'. Omit absent fields instead.");
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
        type == "clipboard.get.result" && path == "text";
}
