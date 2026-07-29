namespace VolturaAir.Host.Features.CustomScreens;

internal static class CustomScreenDemoData
{
    public static void AddTo(CustomScreenService service)
    {
        var screen = new CustomScreenDefinition(
            "screen.public-demo",
            "Living room controls",
            "public-demo",
            [],
            true,
            true,
            [
                new CustomScreenSection(
                    "section.media",
                    "Media",
                    true,
                    6,
                    "content",
                    1,
                    2,
                    new(0, true, 12),
                    new(0, true, 6),
                    [
                        Button("previous", "Previous", "skip-back", "media.previous", 1),
                        Button("play", "Play / pause", "play", "media.playPause", 1),
                        Button("next", "Next", "skip-forward", "media.next", 2),
                        Button("mute", "Mute", "volume-x", "volume.mute", 2)
                    ]),
                new CustomScreenSection(
                    "section.windows",
                    "Windows",
                    true,
                    6,
                    "content",
                    1,
                    2,
                    new(1, true, 12),
                    new(1, true, 6),
                    [
                        Button("task-view", "Task view", "app-window", "windows.taskView", 1),
                        Button("desktop", "Desktop", "monitor", "windows.showDesktop", 1),
                        Button("minimize", "Minimize", "minimize", "windows.minimize", 2),
                        Button("close", "Close", "square-x", "windows.close", 2)
                    ]),
                new CustomScreenSection(
                    "section.trackpad",
                    "Trackpad",
                    true,
                    12,
                    "fill",
                    1,
                    0,
                    new(2, true, 12),
                    new(2, true, 12),
                    [],
                    Kind: "trackpad",
                    TrackpadFullscreenControl: true)
            ]);

        if (!service.TrySave(screen, out _, out var error))
        {
            throw new InvalidOperationException(
                $"Could not create Custom screens screenshot data: {error}");
        }
    }

    private static CustomScreenButton Button(
        string id,
        string label,
        string icon,
        string builtIn,
        int row) =>
        new(
            $"button.{id}",
            label,
            label,
            icon,
            "iconLabel",
            "standard",
            false,
            null,
            null,
            new CustomScreenAction("builtIn", BuiltIn: builtIn),
            row);
}
