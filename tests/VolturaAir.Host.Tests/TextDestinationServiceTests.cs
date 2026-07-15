namespace VolturaAir.Host.Tests;

public sealed class TextDestinationServiceTests
{
    [Fact]
    public async Task CustomDestinationFindsAndStartsTheApprovedExecutable()
    {
        using var injector = new FakeInputInjector();
        var approvedExecutable = @"C:\Tools\Writer.exe";
        var platform = new FakeTextDestinationPlatform(approvedExecutable);
        var settings = new TextDestinationSettings(TextDestinationMode.Managed, TextDestinationPreset.Custom, new() { [TextDestinationPreset.Custom] = approvedExecutable });
        var service = new TextDestinationService(new InputDispatcher(injector), injector, platform, () => settings);

        var result = await service.DeliverAsync("Hello", sendEnter: false, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal([approvedExecutable], platform.FindRunningWindowExecutables);
        Assert.Equal([(approvedExecutable, "")], platform.Starts);
        Assert.Contains("SpecialKey:N:", injector.Events);
        Assert.Contains("SpecialKey:V:Control", injector.Events);
    }

    [Fact]
    public async Task StartedWordDraftDoesNotRequireClipboardAccess()
    {
        using var injector = new FakeInputInjector();
        var platform = new FakeTextDestinationPlatform(@"C:\Office\WINWORD.EXE") { ClipboardAvailable = false };
        var settings = new TextDestinationSettings(TextDestinationMode.Managed, TextDestinationPreset.Word, null);
        var service = new TextDestinationService(new InputDispatcher(injector), injector, platform, () => settings);
        string? draftPath = null;
        try
        {
            var result = await service.DeliverAsync("Important number", sendEnter: false, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Empty(platform.ClipboardWrites);
            draftPath = platform.Starts.Single().Arguments.Trim('"');
            Assert.True(File.Exists(draftPath));
        }
        finally
        {
            if (draftPath is not null) File.Delete(draftPath);
        }
    }

    [Fact]
    public async Task NotepadPlusPlusAlwaysOpensAGeneratedDraftInsteadOfSendingNewItemOrPasteShortcuts()
    {
        using var injector = new FakeInputInjector();
        var platform = new FakeTextDestinationPlatform(@"C:\Tools\notepad++.exe") { ClipboardAvailable = false, RunningWindow = (nint)42 };
        var settings = new TextDestinationSettings(TextDestinationMode.Managed, TextDestinationPreset.NotepadPlusPlus, null);
        var service = new TextDestinationService(new InputDispatcher(injector), injector, platform, () => settings);
        string? draftPath = null;
        try
        {
            var result = await service.DeliverAsync("Important number", sendEnter: false, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("pasted", result.Kind);
            Assert.Empty(platform.ClipboardWrites);
            draftPath = platform.Starts.Single().Arguments.Trim('"');
            Assert.Contains("Important number", File.ReadAllText(draftPath));
            Assert.DoesNotContain(injector.Events, value => value.StartsWith("SpecialKey:N", StringComparison.Ordinal));
            Assert.DoesNotContain(injector.Events, value => value.StartsWith("SpecialKey:V", StringComparison.Ordinal));
        }
        finally
        {
            if (draftPath is not null) File.Delete(draftPath);
        }
    }

    [Fact]
    public async Task ClipboardDestinationDoesNotStartOrTargetAnApplication()
    {
        using var injector = new FakeInputInjector();
        var platform = new FakeTextDestinationPlatform(@"C:\Tools\Writer.exe");
        var settings = new TextDestinationSettings(TextDestinationMode.Clipboard, TextDestinationPreset.Notepad, null);
        var service = new TextDestinationService(new InputDispatcher(injector), injector, platform, () => settings);

        var result = await service.DeliverAsync("Clipboard only", sendEnter: false, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("clipboard", result.Kind);
        Assert.Equal(["Clipboard only"], platform.ClipboardWrites);
        Assert.Empty(platform.Starts);
        Assert.Empty(injector.Events);
    }

    [Fact]
    public async Task UnconfirmedManagedWindowFallsBackToClipboardWithoutInjectingKeys()
    {
        using var injector = new FakeInputInjector();
        var platform = new FakeTextDestinationPlatform(@"C:\Tools\Writer.exe") { IsForegroundResult = false };
        var settings = new TextDestinationSettings(TextDestinationMode.Managed, TextDestinationPreset.Custom, new() { [TextDestinationPreset.Custom] = @"C:\Tools\Writer.exe" });
        var service = new TextDestinationService(new InputDispatcher(injector), injector, platform, () => settings);

        var result = await service.DeliverAsync("Clipboard fallback", sendEnter: false, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("clipboard", result.Kind);
        Assert.Equal(["Clipboard fallback", "Clipboard fallback"], platform.ClipboardWrites);
        Assert.Empty(injector.Events);
    }

    [Fact]
    public void RejectsExecutableOverridesForNonExecutableDestinations()
    {
        var executable = Path.GetTempFileName() + ".exe";
        File.Move(Path.ChangeExtension(executable, null), executable);
        try
        {
            var settings = new TextDestinationSettings(TextDestinationMode.Managed, TextDestinationPreset.DefaultMail, new() { [TextDestinationPreset.DefaultMail] = executable });

            Assert.False(AppTextDestinationSettings.TryValidate(settings, out var error));
            Assert.Equal("Each executable override must be an existing absolute .exe file.", error);
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Theory]
    [InlineData("WRITER", @"C:\Tools\writer.exe")]
    [InlineData("notepad", "notepad.exe")]
    public void MatchesExecutableNameUsesTheResolvedExecutable(string processName, string executable)
    {
        Assert.True(WindowsTextDestinationPlatform.MatchesExecutableName(processName, executable));
    }

    private sealed class FakeTextDestinationPlatform(string executable) : ITextDestinationPlatform
    {
        public bool ClipboardAvailable { get; init; } = true;
        public bool IsForegroundResult { get; init; } = true;
        public nint? RunningWindow { get; init; }
        public List<string> ClipboardWrites { get; } = [];
        public List<string> FindRunningWindowExecutables { get; } = [];
        public List<(string Executable, string Arguments)> Starts { get; } = [];

        public string? ResolveExecutable(TextDestinationProfile profile, string? executableOverride) => executable;
        public nint? FindRunningWindow(string executable)
        {
            FindRunningWindowExecutables.Add(executable);
            return RunningWindow;
        }
        public bool Start(string executable, string arguments) { Starts.Add((executable, arguments)); return true; }
        public Task<nint?> WaitForWindowAsync(string executable, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult<nint?>((nint)1);
        public bool TryActivate(nint window) => true;
        public bool IsForeground(nint window) => IsForegroundResult;
        public bool IsElevatedAboveHost(nint window) => false;
        public bool TrySetClipboardText(string text)
        {
            ClipboardWrites.Add(text);
            return ClipboardAvailable;
        }
    }
}
