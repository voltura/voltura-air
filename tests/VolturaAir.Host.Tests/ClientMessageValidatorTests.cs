using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class ClientMessageValidatorTests
{
    [Theory]
    [InlineData("apps.list", """{ "type": "apps.list", "operationId": "apps-1" }""", true)]
    [InlineData("apps.activate", """{ "type": "apps.activate", "operationId": "apps-2", "revision": "0123456789abcdef0123456789abcdef", "windowId": "fedcba9876543210fedcba9876543210" }""", true)]
    [InlineData("apps.close", """{ "type": "apps.close", "operationId": "apps-3", "revision": "0123456789abcdef0123456789abcdef", "windowId": "42" }""", false)]
    [InlineData("apps.preview.answer", """{ "type": "apps.preview.answer", "operationId": "apps-4", "offerOperationId": "apps-1", "previewId": "0123456789abcdef0123456789abcdef", "answerSdp": "v=0\r\n", "clientSignature": "proof" }""", true)]
    [InlineData("apps.preview.stop", """{ "type": "apps.preview.stop", "operationId": "apps-5", "previewId": "0123456789abcdef0123456789abcdef", "processId": 42 }""", false)]
    public void ValidatesExactAppsMessages(string type, string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, type));
    }

    [Theory]
    [InlineData("terminal.start", """{ "type": "terminal.start", "operationId": "terminal-1", "columns": 80, "rows": 24, "clientSignature": "proof" }""", true)]
    [InlineData("terminal.start", """{ "type": "terminal.start", "operationId": "terminal-1", "columns": 9, "rows": 24, "clientSignature": "proof" }""", false)]
    [InlineData("terminal.attach", """{ "type": "terminal.attach", "operationId": "terminal-2", "terminalId": "0123456789abcdef0123456789abcdef", "acknowledgedOffset": 42, "columns": 120, "rows": 40, "clientSignature": "proof" }""", true)]
    [InlineData("terminal.attach", """{ "type": "terminal.attach", "operationId": "terminal-2", "terminalId": "0123456789abcdef0123456789abcdef", "acknowledgedOffset": -1, "columns": 120, "rows": 40, "clientSignature": "proof" }""", false)]
    [InlineData("terminal.answer", """{ "type": "terminal.answer", "operationId": "terminal-3", "offerOperationId": "terminal-2", "terminalId": "0123456789abcdef0123456789abcdef", "answerSdp": "v=0\r\n", "clientSignature": "proof" }""", true)]
    [InlineData("terminal.stop", """{ "type": "terminal.stop", "operationId": "terminal-4", "terminalId": "0123456789abcdef0123456789abcdef", "command": "whoami" }""", false)]
    public void ValidatesExactTerminalMessages(string type, string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, type));
    }

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
    [InlineData("file.job.create", """{ "type": "file.job.create", "operationId": "files-upload", "sessionId": "session", "panel": "left", "revision": "revision", "operation": "upload", "selectionAll": false, "entryIds": [], "excludedEntryIds": [] }""", false)]
    [InlineData("file.transfer.start", """{ "type": "file.transfer.start", "operationId": "transfer-1", "direction": "download", "sessionId": "session", "panel": "left", "revision": "revision", "entryId": "entry", "clientSignature": "proof" }""", true)]
    [InlineData("file.transfer.start", """{ "type": "file.transfer.start", "operationId": "transfer-2", "direction": "upload", "sessionId": "session", "panel": "right", "revision": "revision", "fileName": "empty.txt", "declaredSize": 0, "clientSignature": "proof" }""", true)]
    [InlineData("file.transfer.start", """{ "type": "file.transfer.start", "operationId": "transfer-3", "direction": "upload", "sessionId": "session", "panel": "right", "revision": "revision", "fileName": "bad.txt", "declaredSize": -1, "clientSignature": "proof" }""", false)]
    [InlineData("file.transfer.start", """{ "type": "file.transfer.start", "operationId": "transfer-4", "direction": "upload", "sessionId": "session", "panel": "right", "revision": "revision", "fileName": "bad.txt", "declaredSize": 9007199254740992, "clientSignature": "proof" }""", false)]
    [InlineData("file.transfer.start", """{ "type": "file.transfer.start", "operationId": "capture-1", "direction": "download", "source": "screen-capture", "screenOperationId": "screen-1", "displayId": "display-1-1", "clientSignature": "proof" }""", true)]
    [InlineData("file.transfer.start", """{ "type": "file.transfer.start", "operationId": "capture-2", "direction": "upload", "source": "screen-capture", "screenOperationId": "screen-1", "displayId": "display-1-1", "clientSignature": "proof" }""", false)]
    [InlineData("file.transfer.start", """{ "type": "file.transfer.start", "operationId": "capture-3", "direction": "download", "source": "screen-capture", "screenOperationId": "screen-1", "displayId": "display-1-1", "sessionId": "unexpected", "clientSignature": "proof" }""", false)]
    [InlineData("file.transfer.start", """{ "type": "file.transfer.start", "operationId": "capture-4", "direction": "download", "source": "screen-capture", "screenOperationId": "bad_operation", "displayId": "display-1-1", "clientSignature": "proof" }""", false)]
    [InlineData("file.transfer.answer", """{ "type": "file.transfer.answer", "operationId": "answer-1", "transferId": "transfer-1", "answerSdp": "v=0\\r\\n", "clientSignature": "proof" }""", true)]
    [InlineData("file.transfer.cancel", """{ "type": "file.transfer.cancel", "operationId": "cancel-1", "transferId": "transfer-1" }""", true)]
    [InlineData("file.transfer.cancel", """{ "type": "file.transfer.cancel", "operationId": "cancel-2", "requestId": "transfer-start-1" }""", true)]
    [InlineData("file.transfer.cancel", """{ "type": "file.transfer.cancel", "operationId": "cancel-3" }""", false)]
    [InlineData("file.transfer.cancel", """{ "type": "file.transfer.cancel", "operationId": "cancel-4", "transferId": "transfer-1", "requestId": "transfer-start-1" }""", false)]
    [InlineData("file.job.conflict.resolve", """{ "type": "file.job.conflict.resolve", "operationId": "resolve-1", "jobId": "job-a", "resolution": "keep-both", "applyToAll": false }""", true)]
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
    [InlineData("""{ "type": "appearance.accent-color.set", "accentColor": "#5FC8B4" }""", true)]
    [InlineData("""{ "type": "appearance.accent-color.set", "accentColor": null }""", true)]
    [InlineData("""{ "type": "appearance.accent-color.set", "accentColor": "#5fc8b4" }""", false)]
    [InlineData("""{ "type": "appearance.accent-color.set", "accentColor": "5FC8B4" }""", false)]
    [InlineData("""{ "type": "appearance.accent-color.set", "accentColor": 123 }""", false)]
    [InlineData("""{ "type": "appearance.accent-color.set", "accentColor": "#5FC8B4", "extra": true }""", false)]
    public void ValidatesAccentColorAppearanceMessages(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, "appearance.accent-color.set"));
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

    [Theory]
    [InlineData("""{ "type": "screen.view.quality", "operationId": "screen-1", "width": 3840, "height": 2160, "framesPerSecond": 29.97, "framesDecoded": 60, "framesDropped": 1, "freezeCount": 0, "packetsLost": 0 }""", true)]
    [InlineData("""{ "type": "screen.view.quality", "operationId": "screen-1", "width": 3840, "height": 2160, "framesPerSecond": 29.97, "framesDecoded": 60, "framesDropped": 1, "freezeCount": 0, "packetsLost": 0, "detail": "forbidden" }""", false)]
    [InlineData("""{ "type": "screen.view.quality", "operationId": "screen-1", "width": 3840, "height": 2160, "framesPerSecond": 29.97, "framesDecoded": -1, "framesDropped": 1, "freezeCount": 0, "packetsLost": 0 }""", false)]
    public void BoundsScreenViewQualityReports(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, "screen.view.quality"));
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
    [InlineData("""{ "type": "phone.webcam.start", "operationId": "webcam-1", "captureWidth": 1920, "captureHeight": 1080, "captureFps": 30, "useMicrophone": false, "clientSignature": "proof" }""", true)]
    [InlineData("""{ "type": "phone.webcam.start", "operationId": "webcam-1", "captureWidth": 0, "captureHeight": 1080, "captureFps": 30, "useMicrophone": false, "clientSignature": "proof" }""", false)]
    [InlineData("""{ "type": "phone.webcam.start", "operationId": "webcam-1", "captureWidth": 1920, "captureHeight": 1080, "captureFps": 61, "useMicrophone": false, "clientSignature": "proof" }""", false)]
    [InlineData("""{ "type": "phone.webcam.start", "operationId": "webcam-1", "captureWidth": 1920, "captureHeight": 1080, "captureFps": 30, "useMicrophone": false, "clientSignature": "proof", "audio": true }""", false)]
    [InlineData("""{ "type": "phone.webcam.start", "operationId": "webcam-1", "captureWidth": 1920, "captureHeight": 1080, "captureFps": 30, "clientSignature": "proof" }""", false)]
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
    [InlineData("""{ "type": "pointer.move", "dx": 1, "dy": 2, "inputContext": "trackpad" }""", "Trackpad")]
    [InlineData("""{ "type": "keyboard.special", "key": "Enter", "inputContext": "keyboard" }""", "Keyboard")]
    [InlineData("""{ "type": "keyboard.text", "text": "private", "inputContext": "dictation" }""", "Dictation")]
    [InlineData("""{ "type": "keyboard.special", "key": "MediaPlayPause", "inputContext": "media-controls" }""", "MediaControls")]
    [InlineData("""{ "type": "pointer.button", "button": "left", "action": "click", "inputContext": "presentation" }""", "Presentation")]
    [InlineData("""{ "type": "pointer.button", "button": "left", "action": "click", "inputContext": "custom-screens" }""", "CustomScreens")]
    [InlineData("""{ "type": "pointer.move", "dx": 1, "dy": 2, "inputContext": "screen-view" }""", "ScreenView")]
    [InlineData("""{ "type": "pointer.move", "dx": 1, "dy": 2, "inputContext": "gyro-mouse" }""", "GyroMouse")]
    public void DecodesClosedInputContexts(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);
        var type = document.RootElement.GetProperty("type").GetString()!;

        Assert.True(ClientMessageValidator.TryDecodeInputMessage(document.RootElement, type, out var command));
        Assert.Equal(expected, command.Context?.ToString());
    }

    [Theory]
    [InlineData("""{ "type": "pointer.move", "dx": 1, "dy": 2, "inputContext": "dictation" }""")]
    [InlineData("""{ "type": "keyboard.text", "text": "private", "inputContext": "gyro-mouse" }""")]
    [InlineData("""{ "type": "keyboard.special", "key": "Enter", "inputContext": "trackpad" }""")]
    [InlineData("""{ "type": "screen.pointer.move", "displayId": "display-1", "x": 0.5, "y": 0.5, "inputContext": "trackpad" }""")]
    public void RejectsInputContextsThatDoNotMatchTheCommandKind(string json)
    {
        using var document = JsonDocument.Parse(json);
        var type = document.RootElement.GetProperty("type").GetString()!;

        Assert.False(ClientMessageValidator.TryDecodeInputMessage(document.RootElement, type, out _));
    }

    [Fact]
    public void RejectsAudioContextThatDoesNotDescribeItsFunctionalSource()
    {
        using var document = JsonDocument.Parse(
            """{ "type": "audio.mute.toggle", "inputContext": "keyboard" }""");

        Assert.False(ClientMessageValidator.IsValidAuthenticatedMessage(
            document.RootElement,
            "audio.mute.toggle"));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    public void RejectsUnknownInputContexts(string inputContext)
    {
        using var document = JsonDocument.Parse($$"""{ "type": "pointer.move", "dx": 1, "dy": 2, "inputContext": "{{inputContext}}" }""");

        Assert.False(ClientMessageValidator.TryDecodeInputMessage(document.RootElement, "pointer.move", out _));
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
