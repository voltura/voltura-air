using System.Text.Json;
using Microsoft.Win32;

namespace VolturaAir.Host;

public enum TextDestinationMode { Focused, Clipboard, Managed }

public enum TextDestinationPreset { Notepad, NotepadPlusPlus, Word, VisualStudioCode, Excel, DefaultMail, Outlook, Custom, DefaultTextFile }

public sealed record TextDestinationSettings(TextDestinationMode Mode, TextDestinationPreset Preset, Dictionary<TextDestinationPreset, string>? ExecutableOverrides)
{
    public static TextDestinationSettings Default { get; } = new(TextDestinationMode.Focused, TextDestinationPreset.Notepad, null);
}

public static class AppTextDestinationSettings
{
    private const string SettingsKeyPath = @"Software\VolturaAir";
    private const string ValueName = "TextDestination";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static event EventHandler? Changed;

    public static TextDestinationSettings Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        try
        {
            return Normalize(JsonSerializer.Deserialize<TextDestinationSettings>(key?.GetValue(ValueName) as string ?? string.Empty, JsonOptions));
        }
        catch (JsonException) { return TextDestinationSettings.Default; }
    }

    public static string? GetExecutableOverride(TextDestinationSettings settings, TextDestinationPreset preset)
    {
        return settings.ExecutableOverrides is not null && settings.ExecutableOverrides.TryGetValue(preset, out var path) ? path : null;
    }

    public static bool SupportsExecutableOverride(TextDestinationPreset preset) => preset is not (TextDestinationPreset.DefaultTextFile or TextDestinationPreset.DefaultMail or TextDestinationPreset.Outlook);

    public static bool TrySave(TextDestinationMode mode, TextDestinationPreset preset, Dictionary<TextDestinationPreset, string>? executableOverrides, out string error)
    {
        var candidate = new TextDestinationSettings(mode, preset, executableOverrides);
        if (!TryValidate(candidate, out error)) return false;
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(ValueName, JsonSerializer.Serialize(candidate, JsonOptions), RegistryValueKind.String);
        Changed?.Invoke(null, EventArgs.Empty);
        return true;
    }

    public static bool TryValidate(TextDestinationSettings settings, out string error)
    {
        error = string.Empty;
        if (!Enum.IsDefined(settings.Mode) || !Enum.IsDefined(settings.Preset)) { error = "Choose a supported text destination."; return false; }
        foreach (var (preset, path) in settings.ExecutableOverrides ?? [])
        {
            if (!Enum.IsDefined(preset) || !SupportsExecutableOverride(preset) || string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                error = "Each executable override must be an existing absolute .exe file.";
                return false;
            }
        }
        return true;
    }

    private static TextDestinationSettings Normalize(TextDestinationSettings? settings) => settings is not null && TryValidate(settings, out _) ? settings : TextDestinationSettings.Default;
}

public sealed record TextDeliveryResult(bool Succeeded, string Kind, string? Code, string Message);
public sealed record TextDestinationMetadata(string Mode, string DisplayName, bool Available);
public sealed record TextDestinationProfile(
    string DisplayName,
    string[] ExecutableNames,
    string StartArguments,
    string NewItemKey,
    IReadOnlyList<string> NewItemModifiers,
    bool CreateNewItemAfterStart = false,
    int StartupNewItemDelayMilliseconds = 0,
    string? StartupNewItemKey = null,
    bool SupportsStartedCompose = true)
{
    public static TextDestinationProfile For(TextDestinationPreset preset) => preset switch
    {
        TextDestinationPreset.Notepad => new("Windows Notepad", ["notepad.exe"], "", "N", ["Control"]),
        TextDestinationPreset.NotepadPlusPlus => new("Notepad++", ["notepad++.exe"], "-nosession", "N", ["Control"]),
        TextDestinationPreset.Word => new("Microsoft Word", ["winword.exe"], "", "N", ["Control"], SupportsStartedCompose: false),
        TextDestinationPreset.VisualStudioCode => new("Visual Studio Code", ["code.exe"], "--new-window", "N", ["Control"], CreateNewItemAfterStart: true, StartupNewItemDelayMilliseconds: 2000),
        TextDestinationPreset.Excel => new("Microsoft Excel", ["excel.exe"], "", "N", ["Control"], SupportsStartedCompose: false),
        TextDestinationPreset.DefaultTextFile => new("Default text-file app", [], "", "", []),
        TextDestinationPreset.DefaultMail => new("Default email client", [], "", "", []),
        TextDestinationPreset.Outlook => new("Outlook compose", ["outlook.exe"], "/c ipm.note", "M", ["Control", "Shift"]),
        TextDestinationPreset.Custom => new("Custom executable", [], "", "N", ["Control"], CreateNewItemAfterStart: true, StartupNewItemDelayMilliseconds: 1000),
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };
}

public interface ITextDestinationService
{
    TextDestinationMetadata GetMetadata();
    Task<TextDeliveryResult> DeliverAsync(string text, bool sendEnter, CancellationToken cancellationToken);
}

internal sealed class FocusedTextDestinationService(InputDispatcher inputDispatcher) : ITextDestinationService
{
    public TextDestinationMetadata GetMetadata() => new("focused", "Currently focused application", true);
    public Task<TextDeliveryResult> DeliverAsync(string text, bool sendEnter, CancellationToken cancellationToken)
    {
        var outcome = inputDispatcher.TransferText(text, sendEnter);
        return Task.FromResult(outcome == InputDispatchOutcome.Blocked
            ? new TextDeliveryResult(false, "typed", "VAIR-TEXT-HOST-FOCUSED", "Text was not sent because the Voltura Air host window has focus. Select the destination application and try again.")
            : new TextDeliveryResult(true, "typed", null, "Text sent successfully."));
    }
}

public sealed class TextDestinationService(InputDispatcher inputDispatcher, IInputInjector inputInjector, ITextDestinationPlatform? platform = null, Func<TextDestinationSettings>? loadSettings = null) : ITextDestinationService
{
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(8);
    private readonly InputDispatcher _inputDispatcher = inputDispatcher;
    private readonly IInputInjector _inputInjector = inputInjector;
    private readonly ITextDestinationPlatform _platform = platform ?? new WindowsTextDestinationPlatform();
    private readonly Func<TextDestinationSettings> _loadSettings = loadSettings ?? AppTextDestinationSettings.Load;

    public TextDestinationMetadata GetMetadata()
    {
        var settings = _loadSettings();
        return settings.Mode switch
        {
            TextDestinationMode.Clipboard => new("clipboard", "Windows clipboard", true),
            TextDestinationMode.Managed when settings.Preset == TextDestinationPreset.DefaultTextFile => new("configured", "Default text-file app", true),
            TextDestinationMode.Managed when settings.Preset == TextDestinationPreset.DefaultMail => new("configured", "Default email client", true),
            TextDestinationMode.Managed => new("configured", TextDestinationProfile.For(settings.Preset).DisplayName, _platform.ResolveExecutable(TextDestinationProfile.For(settings.Preset), AppTextDestinationSettings.GetExecutableOverride(settings, settings.Preset)) is not null),
            _ => new("focused", "Currently focused application", true)
        };
    }

    public async Task<TextDeliveryResult> DeliverAsync(string text, bool sendEnter, CancellationToken cancellationToken)
    {
        var settings = _loadSettings();
        if (settings.Mode == TextDestinationMode.Focused)
        {
            var outcome = _inputDispatcher.TransferText(text, sendEnter);
            return outcome == InputDispatchOutcome.Blocked
                ? new(false, "typed", "VAIR-TEXT-HOST-FOCUSED", "Text was not sent because the Voltura Air host window has focus. Select the destination application and try again.")
                : new(true, "typed", null, "Text sent successfully.");
        }

        if (settings.Mode == TextDestinationMode.Clipboard)
            return _platform.TrySetClipboardText(text)
                ? new(true, "clipboard", null, "Text copied to the Windows clipboard.")
                : ClipboardFailure();

        if (settings.Preset == TextDestinationPreset.DefaultTextFile)
        {
            try
            {
                var path = PlainTextDraft.Create(text, sendEnter);
                return PlainTextDraft.TryOpen(path)
                    ? new(true, "pasted", null, "Text was added to a new text file.")
                    : ClipboardFallback(text, "Windows could not open the new text file. Text was copied to the Windows clipboard.");
            }
            catch (IOException)
            {
                return ClipboardFallback(text, "Windows could not prepare a new text file. Text was copied to the Windows clipboard.");
            }
        }

        if (settings.Preset == TextDestinationPreset.DefaultMail)
        {
            return DefaultMailCompose.TryCreate(text, sendEnter)
                ? new(true, "pasted", null, "Text was added to a new message in the default email client.")
                : ClipboardFallback(text, "The default email client could not create a message body. Text was copied to the Windows clipboard.");
        }

        if (settings.Preset == TextDestinationPreset.Outlook)
        {
            return OutlookCompose.TryCreate(text, sendEnter)
                ? new(true, "pasted", null, "Text was added to a new Outlook message body.")
                : ClipboardFallback(text, "Outlook could not create a message body. Text was copied to the Windows clipboard.");
        }

        var profile = TextDestinationProfile.For(settings.Preset);
        var executable = _platform.ResolveExecutable(profile, AppTextDestinationSettings.GetExecutableOverride(settings, settings.Preset));
        if (executable is null)
            return ClipboardFallback(text, $"{profile.DisplayName} is not available. Text was copied to the Windows clipboard.");

        var window = _platform.FindRunningWindow(executable);
        var useNewItemShortcut = window is not null;
        var timeout = window is null ? StartupTimeout : ActivationTimeout;
        var startedWithPreparedDraft = false;
        var prepareDraft = settings.Preset == TextDestinationPreset.NotepadPlusPlus ||
            (window is null && settings.Preset is TextDestinationPreset.Excel or TextDestinationPreset.Word);
        if (!prepareDraft && !_platform.TrySetClipboardText(text)) return ClipboardFailure();
        if (window is null || prepareDraft)
        {
            var startArguments = profile.StartArguments;
            if (prepareDraft)
            {
                try
                {
                    startArguments = QuoteArgument(settings.Preset switch
                    {
                        TextDestinationPreset.Excel => ExcelDraftWorkbook.Create(text, sendEnter),
                        TextDestinationPreset.Word => WordDraftDocument.Create(text, sendEnter),
                        TextDestinationPreset.NotepadPlusPlus => PlainTextDraft.Create(text, sendEnter),
                        _ => throw new InvalidOperationException("Unsupported text destination preset.")
                    });
                    startedWithPreparedDraft = true;
                }
                catch (IOException)
                {
                    return ClipboardFallback(text, $"{profile.DisplayName} could not prepare a new document. Text was copied to the Windows clipboard.");
                }
            }

            if (!_platform.Start(executable, startArguments))
                return ClipboardFallback(text, $"{profile.DisplayName} could not be started. Text was copied to the Windows clipboard.");
        }

        window = await _platform.WaitForWindowAsync(executable, timeout, cancellationToken);
        var foregroundReady = window is not null && (_platform.IsForeground(window.Value) || _platform.TryActivate(window.Value));
        if (window is null || !foregroundReady || !_platform.IsForeground(window.Value) || _platform.IsElevatedAboveHost(window.Value))
            return ClipboardFallback(text, $"Text was copied to the Windows clipboard. Paste it into {profile.DisplayName} manually.");

        if (!useNewItemShortcut && !startedWithPreparedDraft && !profile.SupportsStartedCompose)
            return ClipboardFallback(text, $"{profile.DisplayName} was started. Text was copied to the Windows clipboard; create a new document, then paste manually.");

        if (startedWithPreparedDraft)
            return new(true, "pasted", null, settings.Preset switch
            {
                TextDestinationPreset.Excel => "Text was added to cell A1 in a new Excel workbook.",
                TextDestinationPreset.Word => "Text was added to a new Word document.",
                TextDestinationPreset.NotepadPlusPlus => "Text was added to a new Notepad++ document.",
                _ => throw new InvalidOperationException("Unsupported text destination preset.")
            });

        await Task.Delay(150, cancellationToken);
        if (!_platform.IsForeground(window.Value) || _platform.IsElevatedAboveHost(window.Value))
            return ClipboardFallback(text, $"Text was copied to the Windows clipboard. Resolve the {profile.DisplayName} dialog, then paste manually.");

        var needsNewItemShortcut = useNewItemShortcut || profile.CreateNewItemAfterStart;
        if (needsNewItemShortcut)
        {
            if (!useNewItemShortcut && profile.StartupNewItemDelayMilliseconds > 0)
            {
                await Task.Delay(profile.StartupNewItemDelayMilliseconds, cancellationToken);
                if (!_platform.IsForeground(window.Value) || _platform.IsElevatedAboveHost(window.Value))
                    return ClipboardFallback(text, $"Text was copied to the Windows clipboard. Paste it into {profile.DisplayName} manually.");
            }
            _inputInjector.SpecialKey(useNewItemShortcut ? profile.NewItemKey : profile.StartupNewItemKey ?? profile.NewItemKey, useNewItemShortcut ? profile.NewItemModifiers : []);
            await Task.Delay(100, cancellationToken);
        }
        if (!_platform.IsForeground(window.Value) || _platform.IsElevatedAboveHost(window.Value))
            return ClipboardFallback(text, $"Text was copied to the Windows clipboard. Paste it into {profile.DisplayName} manually.");
        _inputInjector.SpecialKey("V", ["Control"]);
        if (sendEnter) _inputInjector.SpecialKey("Enter", []);
        return new(true, "pasted", null, $"Text pasted into {profile.DisplayName}.");
    }

    private TextDeliveryResult ClipboardFallback(string text, string message) => _platform.TrySetClipboardText(text)
        ? new(true, "clipboard", null, message)
        : ClipboardFailure();

    private static TextDeliveryResult ClipboardFailure() => new(false, "clipboard", "VAIR-TEXT-CLIPBOARD-FAILED", "Windows could not copy the text to the clipboard. Try again.");

    private static string QuoteArgument(string value) => $"\"{value.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";
}
