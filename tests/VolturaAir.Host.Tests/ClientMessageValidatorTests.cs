using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class ClientMessageValidatorTests
{
    [Theory]
    [InlineData("file.session.open", """{ "type": "file.session.open", "operationId": "files-1" }""", true)]
    [InlineData("file.page.get", """{ "type": "file.page.get", "operationId": "files-2", "sessionId": "session", "panel": "left", "revision": "revision", "continuation": "opaque" }""", true)]
    [InlineData("file.page.get", """{ "type": "file.page.get", "operationId": "files-2", "sessionId": "session", "panel": "left", "revision": "revision", "continuation": "opaque", "path": "C:\\" }""", false)]
    [InlineData("file.sort", """{ "type": "file.sort", "operationId": "files-3", "sessionId": "session", "panel": "right", "sortBy": "modified", "descending": true }""", true)]
    [InlineData("file.sort", """{ "type": "file.sort", "operationId": "files-3", "sessionId": "session", "panel": "right", "sortBy": "path", "descending": true }""", false)]
    [InlineData("file.job.create", """{ "type": "file.job.create", "operationId": "files-4", "sessionId": "session", "panel": "left", "revision": "revision", "operation": "copy", "destinationPanel": "right", "destinationRevision": "destination-revision", "selectionAll": true, "entryIds": [], "excludedEntryIds": ["entry-a"] }""", true)]
    [InlineData("file.job.create", """{ "type": "file.job.create", "operationId": "files-4", "sessionId": "session", "panel": "left", "revision": "revision", "operation": "copy", "destinationPanel": "right", "selectionAll": true, "entryIds": [], "excludedEntryIds": [] }""", false)]
    [InlineData("file.job.create", """{ "type": "file.job.create", "operationId": "files-4", "sessionId": "session", "panel": "left", "revision": "revision", "operation": "copy", "destinationPanel": "right", "destinationRevision": "destination-revision", "selectionAll": true, "entryIds": [], "excludedEntryIds": [], "sourcePath": "C:\\Users" }""", false)]
    [InlineData("file.job.control", """{ "type": "file.job.control", "operationId": "files-5", "jobId": "job-a", "action": "dismiss" }""", true)]
    [InlineData("file.job.control", """{ "type": "file.job.control", "operationId": "files-5", "jobId": "job-a", "action": "remove" }""", false)]
    public void ValidatesOpaqueFileManagerMessages(string type, string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, type));
    }

    [Theory]
    [InlineData("""{ "type": "appearance.control-depth.set", "controlDepth": true }""", true)]
    [InlineData("""{ "type": "appearance.control-depth.set", "controlDepth": false }""", true)]
    [InlineData("""{ "type": "appearance.control-depth.set", "controlDepth": 1 }""", false)]
    [InlineData("""{ "type": "appearance.control-depth.set", "controlDepth": true, "extra": true }""", false)]
    public void ValidatesControlDepthAppearanceMessages(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, "appearance.control-depth.set"));
    }

    [Theory]
    [InlineData("""{ "type": "screen.view.start", "operationId": "screen-1", "displayId": "display-1", "clientSignature": "proof" }""", true)]
    [InlineData("""{ "type": "screen.view.start", "operationId": "screen-1", "displayId": "display-1", "clientSignature": "proof", "streamFormats": "image-v1,fmp4-h264" }""", false)]
    [InlineData("""{ "type": "screen.view.start", "operationId": "screen-1", "displayId": "display-1", "clientEphemeralPublicKey": "key", "clientSignature": "proof" }""", false)]
    public void BoundsScreenViewOfferRequests(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, "screen.view.start"));
    }

    [Fact]
    public void BoundsScreenViewAnswers()
    {
        using var valid = JsonDocument.Parse("""{ "type": "screen.view.answer", "operationId": "screen-1", "answerSdp": "v=0\\r\\n", "clientSignature": "proof" }""");
        Assert.True(ClientMessageValidator.IsValidAuthenticatedMessage(valid.RootElement, "screen.view.answer"));

        string oversized = new('a', ScreenViewProtocol.MaxSdpLength + 1);
        using var invalid = JsonDocument.Parse(JsonSerializer.Serialize(new { type = "screen.view.answer", operationId = "screen-1", answerSdp = oversized, clientSignature = "proof" }));
        Assert.False(ClientMessageValidator.IsValidAuthenticatedMessage(invalid.RootElement, "screen.view.answer"));
    }

    [Theory]
    [InlineData("""{ "type": "phone.webcam.start", "operationId": "webcam-1", "captureWidth": 1920, "captureHeight": 1080, "captureFps": 30, "clientSignature": "proof" }""", true)]
    [InlineData("""{ "type": "phone.webcam.start", "operationId": "webcam-1", "captureWidth": 0, "captureHeight": 1080, "captureFps": 30, "clientSignature": "proof" }""", false)]
    [InlineData("""{ "type": "phone.webcam.start", "operationId": "webcam-1", "captureWidth": 1920, "captureHeight": 1080, "captureFps": 61, "clientSignature": "proof" }""", false)]
    [InlineData("""{ "type": "phone.webcam.start", "operationId": "webcam-1", "captureWidth": 1920, "captureHeight": 1080, "captureFps": 30, "clientSignature": "proof", "audio": true }""", false)]
    public void BoundsPhoneWebcamStartRequests(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, "phone.webcam.start"));
    }

    [Fact]
    public void BoundsPhoneWebcamAnswers()
    {
        using var valid = JsonDocument.Parse("""{ "type": "phone.webcam.answer", "operationId": "webcam-1", "answerSdp": "v=0\\r\\n", "clientSignature": "proof" }""");
        Assert.True(ClientMessageValidator.IsValidAuthenticatedMessage(valid.RootElement, "phone.webcam.answer"));

        string oversized = new('a', ScreenViewProtocol.MaxSdpLength + 1);
        using var invalid = JsonDocument.Parse(JsonSerializer.Serialize(new { type = "phone.webcam.answer", operationId = "webcam-1", answerSdp = oversized, clientSignature = "proof" }));
        Assert.False(ClientMessageValidator.IsValidAuthenticatedMessage(invalid.RootElement, "phone.webcam.answer"));
    }

    [Theory]
    [InlineData("""{ "type": "presentation.command", "operationId": "laser-1", "target": "powerpoint", "action": "pointer", "enabled": true }""", true)]
    [InlineData("""{ "type": "presentation.command", "operationId": "laser-1", "target": "pdf", "action": "pointer" }""", false)]
    [InlineData("""{ "type": "presentation.command", "operationId": "next-1", "target": "powerpoint", "action": "next", "enabled": false }""", false)]
    public void RequiresDesiredStateOnlyForLaserPointerCommands(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, "presentation.command"));
    }

    [Theory]
    [InlineData("""{ "type": "custom.screen.invoke", "operationId": "laser-1", "screenId": "screen-1", "screenRevision": "revision-1", "buttonId": "button-1" }""", true)]
    [InlineData("""{ "type": "custom.screen.invoke", "operationId": "laser-1", "screenId": "screen-1", "screenRevision": "revision-1", "buttonId": "button-1", "enabled": false }""", true)]
    [InlineData("""{ "type": "custom.screen.invoke", "operationId": "laser-1", "screenId": "screen-1", "screenRevision": "revision-1", "buttonId": "button-1", "enabled": "false" }""", false)]
    public void ValidatesOptionalCustomScreenLaserState(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(
            expected,
            ClientMessageValidator.IsValidAuthenticatedMessage(
                document.RootElement,
                "custom.screen.invoke"));
    }

    [Theory]
    [InlineData("""{ "type": "presentation.command", "operationId": "go-1", "target": "powerpoint", "action": "goto", "runtimePresentationId": "runtime-1", "slideNumber": 8 }""", "presentation.command", true)]
    [InlineData("""{ "type": "presentation.command", "operationId": "go-1", "target": "powerpoint", "action": "goto", "slideNumber": 0 }""", "presentation.command", false)]
    [InlineData("""{ "type": "presentation.command", "operationId": "pause-1", "target": "powerpoint", "action": "pause" }""", "presentation.command", false)]
    [InlineData("""{ "type": "presentation.command", "operationId": "focus-1", "target": "powerpoint", "action": "activate", "runtimePresentationId": "runtime-1" }""", "presentation.command", true)]
    [InlineData("""{ "type": "presentation.command", "operationId": "focus-1", "target": "pdf", "action": "activate" }""", "presentation.command", false)]
    [InlineData("""{ "type": "presentation.command", "operationId": "first-1", "target": "pdf", "action": "first" }""", "presentation.command", false)]
    [InlineData("""{ "type": "presentation.powerpoint.refresh", "operationId": "refresh-1" }""", "presentation.powerpoint.refresh", true)]
    [InlineData("""{ "type": "presentation.powerpoint.launch", "operationId": "launch-1", "presentationId": "report-1" }""", "presentation.powerpoint.launch", true)]
    [InlineData("""{ "type": "presentation.powerpoint.launch", "operationId": "launch-1", "presentationId": "../bad" }""", "presentation.powerpoint.launch", false)]
    [InlineData("""{ "type": "presentation.session", "operationId": "session-1", "action": "start", "runtimePresentationId": "runtime-1" }""", "presentation.session", true)]
    [InlineData("""{ "type": "presentation.session", "operationId": "break-1", "action": "break", "enabled": true }""", "presentation.session", true)]
    [InlineData("""{ "type": "presentation.session", "operationId": "save-1", "action": "save", "enabled": false }""", "presentation.session", false)]
    public void ValidatesExpandedPresentationMessages(
        string json,
        string type,
        bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(
            expected,
            ClientMessageValidator.IsValidAuthenticatedMessage(
                document.RootElement,
                type));
    }

    [Fact]
    public void DecodesAndNormalizesPointerInputOnce()
    {
        using var document = JsonDocument.Parse("""{ "type": "pointer.move", "seq": 42, "dx": 4.7, "dy": -3.2 }""");

        var decoded = ClientMessageValidator.TryDecodeInputMessage(document.RootElement, "pointer.move", out var command);

        Assert.True(decoded);
        Assert.Equal(InputCommandKind.PointerMove, command.Kind);
        Assert.Equal(42, command.Sequence);
        Assert.Equal(5, command.Dx);
        Assert.Equal(-3, command.Dy);
        Assert.Equal("pointer.move", command.Type);
    }

    [Theory]
    [InlineData("""{ "type": "screen.pointer.move", "seq": 7, "displayId": "display-1", "x": 0.25, "y": 1 }""", true)]
    [InlineData("""{ "type": "screen.pointer.button", "displayId": "display-1", "x": 0, "y": 0, "button": "right", "action": "down" }""", true)]
    [InlineData("""{ "type": "screen.pointer.wheel", "displayId": "display-1", "x": 1, "y": 0.5, "dx": 0, "dy": -12 }""", true)]
    [InlineData("""{ "type": "screen.pointer.move", "displayId": "display-1", "x": -0.01, "y": 0.5 }""", false)]
    [InlineData("""{ "type": "screen.pointer.button", "displayId": "display-1", "x": 0.5, "y": 0.5, "button": "left", "action": "click" }""", false)]
    [InlineData("""{ "type": "screen.pointer.wheel", "displayId": "display-1", "x": 0.5, "y": 0.5, "dx": 0, "dy": 1, "extra": true }""", false)]
    public void StrictlyValidatesDirectScreenPointerMessages(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);
        string type = document.RootElement.GetProperty("type").GetString()!;

        Assert.Equal(expected, ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, type));
        if (expected)
        {
            Assert.True(ClientMessageValidator.TryDecodeInputMessage(document.RootElement, type, out _));
        }
    }

    [Fact]
    public void DecodesValidatedKeyboardModifiersWithoutASecondJsonRead()
    {
        using var document = JsonDocument.Parse("""{ "type": "keyboard.special", "seq": 9, "key": "Enter", "modifiers": ["Control", "Shift"] }""");

        var decoded = ClientMessageValidator.TryDecodeInputMessage(document.RootElement, "keyboard.special", out var command);

        Assert.True(decoded);
        Assert.Equal(InputCommandKind.KeyboardSpecial, command.Kind);
        Assert.Equal("Enter", command.Key);
        Assert.Equal(["Control", "Shift"], command.Modifiers);
    }

    [Theory]
    [InlineData("""{ "type": "pointer.move", "seq": 0, "dx": 1, "dy": 1 }""", "pointer.move")]
    [InlineData("""{ "type": "pointer.move", "dx": 5001, "dy": 1 }""", "pointer.move")]
    [InlineData("""{ "type": "pointer.button", "button": "middle", "action": "click" }""", "pointer.button")]
    [InlineData("""{ "type": "keyboard.special", "key": "Enter", "modifiers": [1] }""", "keyboard.special")]
    public void RejectsInvalidInputWithoutProducingACommand(string json, string type)
    {
        using var document = JsonDocument.Parse(json);

        Assert.False(ClientMessageValidator.TryDecodeInputMessage(document.RootElement, type, out _));
    }
}
