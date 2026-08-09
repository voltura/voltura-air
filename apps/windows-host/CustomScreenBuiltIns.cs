namespace VolturaAir.Host;

public sealed record CustomScreenBuiltIn(
    string Id,
    string Label,
    string Icon,
    string Key,
    IReadOnlyList<string> Modifiers,
    bool Repeatable);

public static class CustomScreenBuiltIns
{
    private static readonly CustomScreenBuiltIn[] Items =
    [
        new("media.previous", "Previous", "skip-back", "MediaPreviousTrack", [], false),
        new("media.playPause", "Play / pause", "play", "MediaPlayPause", [], false),
        new("media.next", "Next", "skip-forward", "MediaNextTrack", [], false),
        new("media.stop", "Stop", "square-x", "MediaStop", [], false),
        new("media.seekBack", "Seek back", "arrow-left", "ArrowLeft", [], true),
        new("media.seekForward", "Seek forward", "arrow-right", "ArrowRight", [], true),
        new("volume.down", "Volume down", "volume-1", "VolumeDown", [], true),
        new("volume.mute", "Mute", "volume-x", "VolumeMute", [], false),
        new("volume.up", "Volume up", "volume-2", "VolumeUp", [], true),
        new("navigation.up", "Up", "arrow-up", "ArrowUp", [], true),
        new("navigation.down", "Down", "arrow-down", "ArrowDown", [], true),
        new("navigation.left", "Left", "arrow-left", "ArrowLeft", [], true),
        new("navigation.right", "Right", "arrow-right", "ArrowRight", [], true),
        new("navigation.enter", "Enter", "corner-down-left", "Enter", [], false),
        new("navigation.escape", "Escape", "escape", "Escape", [], false),
        new("browser.back", "Browser back", "arrow-left", "BrowserBack", [], false),
        new("browser.forward", "Browser forward", "arrow-right", "BrowserForward", [], false),
        new("browser.reload", "Reload", "refresh", "R", ["Control"], false),
        new("browser.fullscreen", "Fullscreen", "maximize", "F11", [], false),
        new("windows.start", "Start", "search", "Win", [], false),
        new("windows.previousApp", "Previous app", "app-window", "Tab", ["Alt"], false),
        new("windows.taskView", "Task view", "app-window", "Tab", ["Win"], false),
        new("windows.showDesktop", "Show desktop", "monitor", "D", ["Win"], false),
        new("windows.minimize", "Minimize", "minimize", "ArrowDown", ["Win"], false),
        new("windows.maximize", "Maximize", "maximize", "ArrowUp", ["Win"], false),
        new("windows.snapLeft", "Snap left", "arrow-left", "ArrowLeft", ["Win"], false),
        new("windows.snapRight", "Snap right", "arrow-right", "ArrowRight", ["Win"], false),
        new("windows.explorer", "File Explorer", "app-window", "E", ["Win"], false),
        new("windows.run", "Run", "command", "R", ["Win"], false),
        new("windows.close", "Close window", "square-x", "F4", ["Alt"], false)
    ];

    public static IReadOnlyList<CustomScreenBuiltIn> All => Items;

    public static CustomScreenBuiltIn? Find(string? id) =>
        Items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public static bool IsSupported(string? id) => Find(id) is not null;
}
