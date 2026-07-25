using VolturaAir.Host;
using System.Diagnostics;
using System.Windows.Threading;

namespace VolturaAir.Host.Tests;

public sealed class PowerPointComBridgeSmokeTests
{
    [Theory]
    [InlineData(1, 3, null, 3, 1)]
    [InlineData(2, 4, null, 4, 2)]
    [InlineData(3, 4, 1, 4, 1)]
    [InlineData(4, 3, 2, 3, 2)]
    [InlineData(4, 4, 1, 1, null)]
    [InlineData(3, 3, 2, 2, null)]
    public void BlankTransitionsPreserveTheOriginalNonBlankState(
        int currentState,
        int requestedState,
        int? stateBeforeBlank,
        int expectedNextState,
        int? expectedStateBeforeBlank)
    {
        var result = PowerPointComBridge.ResolveBlankTransition(
            currentState,
            requestedState,
            stateBeforeBlank);

        Assert.Equal(expectedNextState, result.NextState);
        Assert.Equal(expectedStateBeforeBlank, result.StateBeforeBlank);
    }

    [Fact]
    [Trait("Category", "ManualMicrosoft365")]
    public void RunsControlledReadyPresentationAndOverlaySequence()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("VOLTURA_AIR_REAL_POWERPOINT_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            PowerPointComBridge? bridge = null;
            WindowsDisplayActionController? overlays = null;
            string? runtimePresentationId = null;
            try
            {
                bridge = new PowerPointComBridge(() => { });
                var initial = bridge.ReadSnapshot();
                Assert.Equal(PowerPointDiscoveryState.Ready, initial.State);
                var presentation = Assert.Single(initial.Presentations);
                runtimePresentationId = presentation.RuntimePresentationId;
                if (presentation.IsPresenting)
                {
                    var normalized = bridge.Execute(
                        new("end", runtimePresentationId));
                    Assert.True(
                        normalized.Succeeded,
                        bridge.LastFailureDiagnostic ?? normalized.Message);
                    initial = bridge.ReadSnapshot();
                    presentation = Assert.Single(initial.Presentations);
                }

                Assert.False(presentation.IsPresenting);
                Assert.True(
                    presentation.SlideCount >= 3,
                    "The controlled PowerPoint test requires at least three slides.");
                Assert.NotNull(presentation.CurrentSlideIndex);

                var started = bridge.Execute(new("start", runtimePresentationId));
                Assert.True(started.Succeeded, bridge.LastFailureDiagnostic ?? started.Message);
                Assert.True(started.Presentation?.IsPresenting);
                Assert.Equal(1, started.Presentation?.CurrentSlideIndex);
                AssertForegroundPowerPoint();

                var pointerVisible = bridge.Execute(
                    new("pointer", runtimePresentationId, Enabled: true));
                Assert.True(
                    pointerVisible.Succeeded,
                    bridge.LastFailureDiagnostic ?? pointerVisible.Message);
                Assert.True(pointerVisible.Presentation?.IsPresenting);
                Assert.Equal(1, pointerVisible.Presentation?.CurrentSlideIndex);
                var pointerAutomatic = bridge.Execute(
                    new("pointer", runtimePresentationId, Enabled: false));
                Assert.True(
                    pointerAutomatic.Succeeded,
                    bridge.LastFailureDiagnostic ?? pointerAutomatic.Message);
                Assert.True(pointerAutomatic.Presentation?.IsPresenting);
                Assert.Equal(1, pointerAutomatic.Presentation?.CurrentSlideIndex);

                var navigated = bridge.Execute(new("goto", runtimePresentationId, SlideNumber: 3));
                Assert.True(navigated.Succeeded, bridge.LastFailureDiagnostic ?? navigated.Message);
                Assert.True(navigated.Presentation?.IsPresenting);
                Assert.Equal(3, navigated.Presentation?.CurrentSlideIndex);

                var black = bridge.Execute(new("black", runtimePresentationId));
                Assert.True(black.Succeeded, bridge.LastFailureDiagnostic ?? black.Message);
                Assert.Equal("black", black.Presentation?.SlideShowState);
                var blackRestored = bridge.Execute(new("black", runtimePresentationId));
                Assert.True(
                    blackRestored.Succeeded,
                    bridge.LastFailureDiagnostic ?? blackRestored.Message);
                Assert.Equal("running", blackRestored.Presentation?.SlideShowState);

                var white = bridge.Execute(new("white", runtimePresentationId));
                Assert.True(white.Succeeded, bridge.LastFailureDiagnostic ?? white.Message);
                Assert.Equal("white", white.Presentation?.SlideShowState);
                var whiteRestored = bridge.Execute(new("white", runtimePresentationId));
                Assert.True(
                    whiteRestored.Succeeded,
                    bridge.LastFailureDiagnostic ?? whiteRestored.Message);
                Assert.Equal("running", whiteRestored.Presentation?.SlideShowState);

                var ended = bridge.Execute(new("end", runtimePresentationId));
                Assert.True(ended.Succeeded, bridge.LastFailureDiagnostic ?? ended.Message);
                Assert.False(ended.Presentation?.IsPresenting);

                var currentStarted = bridge.Execute(new("start-current", runtimePresentationId));
                Assert.True(
                    currentStarted.Succeeded,
                    bridge.LastFailureDiagnostic ?? currentStarted.Message);
                Assert.True(currentStarted.Presentation?.IsPresenting);
                Assert.Equal(3, currentStarted.Presentation?.CurrentSlideIndex);
                AssertForegroundPowerPoint();

                var currentEnded = bridge.Execute(new("end", runtimePresentationId));
                Assert.True(
                    currentEnded.Succeeded,
                    bridge.LastFailureDiagnostic ?? currentEnded.Message);
                Assert.False(currentEnded.Presentation?.IsPresenting);
                Assert.Equal(3, currentEnded.Presentation?.CurrentSlideIndex);

                var readyNextStarted = bridge.Execute(
                    new("start-current", runtimePresentationId));
                Assert.True(
                    readyNextStarted.Succeeded,
                    bridge.LastFailureDiagnostic ?? readyNextStarted.Message);
                Assert.Equal(3, readyNextStarted.Presentation?.CurrentSlideIndex);
                var readyNext = bridge.Execute(new("next", runtimePresentationId));
                Assert.True(
                    readyNext.Succeeded,
                    bridge.LastFailureDiagnostic ?? readyNext.Message);
                Assert.Equal(4, readyNext.Presentation?.CurrentSlideIndex);
                var readyNextEnded = bridge.Execute(new("end", runtimePresentationId));
                Assert.True(
                    readyNextEnded.Succeeded,
                    bridge.LastFailureDiagnostic ?? readyNextEnded.Message);
                Assert.False(readyNextEnded.Presentation?.IsPresenting);
                Assert.Equal(4, readyNextEnded.Presentation?.CurrentSlideIndex);

                var readyPreviousStarted = bridge.Execute(
                    new("start-current", runtimePresentationId));
                Assert.True(
                    readyPreviousStarted.Succeeded,
                    bridge.LastFailureDiagnostic ?? readyPreviousStarted.Message);
                Assert.Equal(4, readyPreviousStarted.Presentation?.CurrentSlideIndex);
                var readyPrevious = bridge.Execute(new("previous", runtimePresentationId));
                Assert.True(
                    readyPrevious.Succeeded,
                    bridge.LastFailureDiagnostic ?? readyPrevious.Message);
                Assert.Equal(3, readyPrevious.Presentation?.CurrentSlideIndex);
                var readyPreviousEnded = bridge.Execute(
                    new("end", runtimePresentationId));
                Assert.True(
                    readyPreviousEnded.Succeeded,
                    bridge.LastFailureDiagnostic ?? readyPreviousEnded.Message);
                Assert.False(readyPreviousEnded.Presentation?.IsPresenting);
                Assert.Equal(3, readyPreviousEnded.Presentation?.CurrentSlideIndex);

                overlays = new WindowsDisplayActionController(
                    Dispatcher.CurrentDispatcher,
                    NullAppLog.Instance);
                Assert.True(overlays.TryShowBlackout().Succeeded);
                PumpDispatcher(TimeSpan.FromSeconds(2));
                Assert.True(overlays.DismissBlackoutIfActive());
                Assert.True(overlays.TryShowWhiteout().Succeeded);
                PumpDispatcher(TimeSpan.FromSeconds(2));
                Assert.True(overlays.DismissBlackoutIfActive());

                var final = bridge.ReadSnapshot();
                Assert.Equal(PowerPointDiscoveryState.Ready, final.State);
                var finalPresentation = Assert.Single(final.Presentations);
                Assert.Equal(runtimePresentationId, finalPresentation.RuntimePresentationId);
                Assert.False(finalPresentation.IsPresenting);
                Assert.Equal(3, finalPresentation.CurrentSlideIndex);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                overlays?.Dispose();
                if (bridge is not null && runtimePresentationId is not null)
                {
                    var current = bridge.ReadSnapshot().Presentations.FirstOrDefault(
                        candidate => string.Equals(
                            candidate.RuntimePresentationId,
                            runtimePresentationId,
                            StringComparison.Ordinal));
                    if (current?.IsPresenting == true)
                    {
                        _ = bridge.Execute(new("end", runtimePresentationId));
                    }
                }

                bridge?.Dispose();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(30)),
            "The controlled PowerPoint test did not finish within 30 seconds.");
        Assert.Null(failure);
    }

    [Theory]
    [InlineData("pointer", false, false)]
    [InlineData("pointer", true, false)]
    [InlineData("activate", null, true)]
    [InlineData("next", null, true)]
    public void ActivatesOnlyForInteractivePowerPointCommands(
        string action,
        bool? enabled,
        bool expected)
    {
        Assert.Equal(
            expected,
            PowerPointComBridge.RequiresForegroundActivation(
                new(action, "presentation-1", Enabled: enabled)));
    }

    [Fact]
    [Trait("Category", "ManualMicrosoft365")]
    public void ActivatesEditorWindowForOpenPresentation()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("VOLTURA_AIR_REAL_POWERPOINT_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        PowerPointAutomationSnapshot? snapshot = null;
        PowerPointAutomationResult? activation = null;
        string? foregroundProcessName = null;
        string? diagnostic = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var bridge = new PowerPointComBridge(() => { });
                snapshot = bridge.ReadSnapshot();
                diagnostic = bridge.LastFailureDiagnostic;
                var presentation = snapshot.Presentations.FirstOrDefault(
                    candidate => !candidate.IsPresenting);
                if (presentation is null)
                {
                    return;
                }

                activation = bridge.Execute(new(
                    "activate",
                    presentation.RuntimePresentationId));
                diagnostic ??= bridge.LastFailureDiagnostic;
                Thread.Sleep(250);
                var foregroundWindow = WindowNativeMethods.GetForegroundWindow();
                _ = WindowNativeMethods.GetWindowThreadProcessId(
                    foregroundWindow,
                    out var processId);
                using var foregroundProcess = Process.GetProcessById(
                    checked((int)processId));
                foregroundProcessName = foregroundProcess.ProcessName;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(5)),
            "PowerPoint editor activation timed out.");
        Assert.Null(failure);

        var result = Assert.IsType<PowerPointAutomationSnapshot>(snapshot);
        Assert.Equal(PowerPointDiscoveryState.Ready, result.State);
        Assert.Contains(result.Presentations, presentation => !presentation.IsPresenting);
        Assert.True(
            activation?.Succeeded == true,
            diagnostic ?? activation?.Message ?? "PowerPoint editor activation failed.");
        Assert.Equal("POWERPNT", foregroundProcessName, ignoreCase: true);
    }

    [Fact]
    [Trait("Category", "ManualMicrosoft365")]
    public void DiscoversPresentationFromRunningPowerPoint()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("VOLTURA_AIR_REAL_POWERPOINT_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        PowerPointAutomationSnapshot? snapshot = null;
        PowerPointAutomationResult? start = null;
        PowerPointAutomationResult? activation = null;
        PowerPointAutomationResult? cleanup = null;
        string? diagnostic = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var bridge = new PowerPointComBridge(() => { });
                snapshot = bridge.ReadSnapshot();
                diagnostic = bridge.LastFailureDiagnostic;
                if (snapshot.State == PowerPointDiscoveryState.Ready &&
                    snapshot.Presentations.Count > 0)
                {
                    var discovered = snapshot.Presentations[0];
                    var startedByTest = !discovered.IsPresenting;
                    if (startedByTest)
                    {
                        start = bridge.Execute(new(
                            "start",
                            discovered.RuntimePresentationId));
                        discovered = start.Presentation ?? discovered;
                    }

                    activation = bridge.Execute(new(
                        "activate",
                        discovered.RuntimePresentationId));
                    if (startedByTest && activation.Presentation is { } running)
                    {
                        cleanup = bridge.Execute(new(
                            "end",
                            running.RuntimePresentationId));
                    }

                    diagnostic ??= bridge.LastFailureDiagnostic;
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "PowerPoint discovery timed out.");
        Assert.Null(failure);

        var result = Assert.IsType<PowerPointAutomationSnapshot>(snapshot);
        Assert.True(
            result.State == PowerPointDiscoveryState.Ready,
            diagnostic ?? $"Unexpected discovery state: {result.State}.");
        Assert.NotEmpty(result.Presentations);
        Assert.True(
            start is null || start.Succeeded,
            diagnostic ?? start?.Message ?? "PowerPoint slideshow did not start.");
        Assert.True(
            activation?.Succeeded == true,
            diagnostic ?? activation?.Message ?? "PowerPoint activation did not run.");
        Assert.True(
            cleanup is null || cleanup.Succeeded,
            diagnostic ?? cleanup?.Message ?? "PowerPoint smoke-test cleanup failed.");
    }

    private static void AssertForegroundPowerPoint()
    {
        Thread.Sleep(250);
        var foregroundWindow = WindowNativeMethods.GetForegroundWindow();
        _ = WindowNativeMethods.GetWindowThreadProcessId(
            foregroundWindow,
            out var processId);
        using var foregroundProcess = Process.GetProcessById(checked((int)processId));
        Assert.Equal("POWERPNT", foregroundProcess.ProcessName, ignoreCase: true);
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            duration,
            DispatcherPriority.Send,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }
}
