using VolturaAir.Host;
using System.Text.Json.Nodes;

namespace VolturaAir.Host.Tests;

public sealed class PowerPointPresentationSessionServiceTests : WebHostServiceTestBase
{
    [Fact]
    public void SemanticallyInvalidDraftIsRejected()
    {
        using var directory = new TemporaryDirectory("VolturaAir-PowerPointInvalidDraft-");
        var reportStore = new PresentationReportStore(directory.Path);
        var automation = new FakePowerPointAutomationService(Presenting(1, "running"));
        using (var session = new PowerPointPresentationSessionService(
            automation,
            reportStore))
        {
            Assert.True(session.Start(
                "owner",
                "Owner phone",
                Assert.Single(automation.Snapshot.Presentations)).Succeeded);
        }

        var draftPath = Path.Combine(
            directory.Path,
            "Drafts",
            "powerpoint-session.draft");
        var draft = JsonNode.Parse(File.ReadAllText(draftPath))!.AsObject();
        draft["visits"] = null;
        File.WriteAllText(draftPath, draft.ToJsonString());

        using var recovered = new PowerPointPresentationSessionService(
            automation,
            reportStore);

        Assert.Equal("inactive", recovered.Snapshot.State);
    }

    [Fact]
    public void SlideshowExitRollsBackWhenAutomaticDraftPersistenceFails()
    {
        using var directory = new TemporaryDirectory("VolturaAir-PowerPointExitPersistence-");
        var reportStore = new PresentationReportStore(directory.Path);
        var automation = new FakePowerPointAutomationService(Presenting(1, "running"));
        using var session = new PowerPointPresentationSessionService(
            automation,
            reportStore);
        Assert.True(session.Start(
            "owner",
            "Owner phone",
            Assert.Single(automation.Snapshot.Presentations)).Succeeded);
        BlockDraftDirectory(directory.Path);

        automation.Publish(new(PowerPointDiscoveryState.Ready, []));

        Assert.Equal("tracking", session.Snapshot.State);
    }

    [Fact]
    public void AutomaticRecoveryRollsBackWhenDraftPersistenceFails()
    {
        using var directory = new TemporaryDirectory("VolturaAir-PowerPointRecoveryPersistence-");
        var reportStore = new PresentationReportStore(directory.Path);
        var presenting = Presenting(1, "running");
        var automation = new FakePowerPointAutomationService(presenting);
        using var session = new PowerPointPresentationSessionService(
            automation,
            reportStore);
        Assert.True(session.Start(
            "owner",
            "Owner phone",
            Assert.Single(presenting.Presentations)).Succeeded);
        automation.Publish(new(PowerPointDiscoveryState.Ready, []));
        Assert.Equal("pending-review", session.Snapshot.State);
        BlockDraftDirectory(directory.Path);

        automation.Publish(presenting);

        Assert.Equal("pending-review", session.Snapshot.State);
    }

    [Fact]
    public async Task DraftRecoversAndSavesOrderedVisitsAfterSlideshowExit()
    {
        const string sourcePath = @"C:\Presentations\Quarterly update.pptx";
        using var directory = new TemporaryDirectory("VolturaAir-PowerPointSession-");
        var reportStore = new PresentationReportStore(directory.Path);
        var initial = Presenting(slide: 1, state: "running");
        var presentation = Assert.Single(initial.Presentations) with
        {
            SourcePath = sourcePath
        };
        var automation = new FakePowerPointAutomationService(
            initial with { Presentations = [presentation] });

        using (var session = new PowerPointPresentationSessionService(
            automation,
            reportStore))
        {
            Assert.True(session.Start("client-a", "Presenter phone", presentation).Succeeded);
            session.PrepareCommand("goto", presentation.RuntimePresentationId);
            automation.Publish(Presenting(slide: 7, state: "running"));
            automation.Publish(Presenting(slide: 9, state: "running"));
            automation.Publish(Presenting(slide: 9, state: "black"));

            Assert.Equal("tracking", session.Snapshot.State);
            Assert.False(session.Snapshot.BreakActive);

            automation.Publish(new(PowerPointDiscoveryState.Ready, []));
            Assert.Equal("pending-review", session.Snapshot.State);
        }

        using (var recovered = new PowerPointPresentationSessionService(
            automation,
            reportStore))
        {
            Assert.Equal("pending-review", recovered.Snapshot.State);
            Assert.True(recovered.Snapshot.OwnerClientId == "client-a");

            var completed = await recovered.CompleteAsync(
                save: true,
                CancellationToken.None);

            Assert.True(completed.Succeeded);
            Assert.Equal("inactive", recovered.Snapshot.State);
        }

        var report = Assert.Single(reportStore.ReadAll().Reports);
        Assert.Equal("powerpoint", report.Target);
        Assert.Equal("Quarterly update.pptx", report.Title);
        Assert.Equal(sourcePath, report.PresentationFilePath);
        Assert.Equal("Presenter phone", report.DeviceName);
        Assert.Equal([1, 7, 9], report.Slides.Select(slide => slide.SlideNumber));
        Assert.Equal(
            ["voltura-air", "voltura-air", "powerpoint"],
            report.SlideVisits!.Select(visit => visit.Origin));
    }

    [Fact]
    public void HostRestartContinuesTrackingTheSameRunningPresentation()
    {
        const string sourcePath = @"C:\Presentations\Quarterly update.pptx";
        using var directory = new TemporaryDirectory("VolturaAir-PowerPointRestart-");
        var reportStore = new PresentationReportStore(directory.Path);
        var initialPresentation = Presenting(4, "running").Presentations[0] with
        {
            SourcePath = sourcePath
        };
        var initialAutomation = new FakePowerPointAutomationService(
            new(PowerPointDiscoveryState.Ready, [initialPresentation]));
        using (var session = new PowerPointPresentationSessionService(
            initialAutomation,
            reportStore))
        {
            Assert.True(session.Start(
                "owner",
                "Owner phone",
                initialPresentation).Succeeded);
        }

        var resumedPresentation = initialPresentation with
        {
            RuntimePresentationId = "presentation-after-restart",
            CurrentSlideIndex = 5,
            CurrentShowPosition = 5
        };
        var resumedAutomation = new FakePowerPointAutomationService(
            new(PowerPointDiscoveryState.Ready, [resumedPresentation]));
        using var recovered = new PowerPointPresentationSessionService(
            resumedAutomation,
            reportStore);

        Assert.Equal("tracking", recovered.Snapshot.State);
        Assert.Equal(
            "presentation-after-restart",
            recovered.Snapshot.RuntimePresentationId);
        Assert.Equal(5, recovered.Snapshot.CurrentSlideIndex);
        Assert.Equal("owner", recovered.Snapshot.OwnerClientId);
    }

    [Fact]
    public void PausedSessionResumesWhenTheSamePresentationStartsAgain()
    {
        const string sourcePath = @"C:\Presentations\Quarterly update.pptx";
        using var directory = new TemporaryDirectory("VolturaAir-PowerPointCompleted-");
        var reportStore = new PresentationReportStore(directory.Path);
        var presentation = Presenting(4, "running").Presentations[0] with
        {
            SourcePath = sourcePath
        };
        var automation = new FakePowerPointAutomationService(
            new(PowerPointDiscoveryState.Ready, [presentation]));
        using (var session = new PowerPointPresentationSessionService(
            automation,
            reportStore))
        {
            Assert.True(session.Start("owner", "Owner phone", presentation).Succeeded);
            automation.Publish(new(PowerPointDiscoveryState.Ready, []));
            Assert.Equal("pending-review", session.Snapshot.State);
        }

        var restartedAutomation = new FakePowerPointAutomationService(
            new(PowerPointDiscoveryState.Ready, [presentation]));
        using var recovered = new PowerPointPresentationSessionService(
            restartedAutomation,
            reportStore);

        Assert.Equal("tracking", recovered.Snapshot.State);
        Assert.Equal("owner", recovered.Snapshot.OwnerClientId);
    }

    [Fact]
    public async Task DifferentPresentationAutomaticallySavesPausedSession()
    {
        var automation = new FakePowerPointAutomationService(Presenting(4, "running"));
        var reportStore = new InMemoryPresentationReportStore();
        using var session = new PowerPointPresentationSessionService(
            automation,
            reportStore);
        var original = Assert.Single(automation.Snapshot.Presentations);
        Assert.True(session.Start("owner", "Owner phone", original).Succeeded);
        automation.Publish(new(PowerPointDiscoveryState.Ready, []));

        var sameNameOnly = original with
        {
            RuntimePresentationId = "different-runtime"
        };
        automation.Publish(new(PowerPointDiscoveryState.Ready, [sameNameOnly]));

        Assert.Equal("pending-review", session.Snapshot.State);
        var prepared = await session.PrepareForStartAsync(
            sameNameOnly.RuntimePresentationId,
            sameNameOnly.SourcePath,
            CancellationToken.None);
        Assert.True(prepared.Succeeded);
        Assert.Equal("inactive", session.Snapshot.State);
        Assert.Equal("Owner phone", Assert.Single(reportStore.ReadAll().Reports).DeviceName);
    }

    [Fact]
    public async Task StartingSameActivePresentationTransfersControlAndPreservesTiming()
    {
        var automation = new FakePowerPointAutomationService(Presenting(4, "running"));
        var reportStore = new InMemoryPresentationReportStore();
        var time = new ManualTimeProvider();
        using var session = new PowerPointPresentationSessionService(
            automation,
            reportStore,
            time);
        var presentation = Assert.Single(automation.Snapshot.Presentations);
        Assert.True(session.Start("owner", "Owner phone", presentation).Succeeded);
        time.Advance(TimeSpan.FromSeconds(5));

        var takeover = session.StartOrResume("other", "Other phone", presentation);
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.True(takeover.Succeeded);
        Assert.Equal("other", session.Snapshot.OwnerClientId);
        Assert.Equal("Other phone", session.Snapshot.OwnerDeviceName);
        Assert.True((await session.CompleteAsync(
            save: true,
            CancellationToken.None)).Succeeded);
        var report = Assert.Single(reportStore.ReadAll().Reports);
        Assert.Equal("Other phone", report.DeviceName);
        Assert.Equal(10, report.PresentationDurationSeconds);
    }

    [Fact]
    public async Task AutomaticSaveFailureLeavesDraftManageableFromMobile()
    {
        var automation = new FakePowerPointAutomationService(Presenting(4, "running"));
        var reportStore = new ThrowingOnceReportStore();
        using var session = new PowerPointPresentationSessionService(
            automation,
            reportStore);
        Assert.True(session.Start(
            "owner",
            "Owner phone",
            Assert.Single(automation.Snapshot.Presentations)).Succeeded);

        var prepared = await session.PrepareForStartAsync(
            "different-runtime",
            sourcePath: null,
            CancellationToken.None);

        Assert.False(prepared.Succeeded);
        Assert.Equal("session-save-failed", prepared.Code);
        Assert.Equal("pending-review", session.Snapshot.State);
        Assert.True((await session.CompleteAsync(
            save: false,
            CancellationToken.None)).Succeeded);
        Assert.Equal("inactive", session.Snapshot.State);
    }

    [Fact]
    public async Task AutomaticSaveRollsBackWhenDraftCannotBeFinalized()
    {
        using var directory = new TemporaryDirectory("VolturaAir-PowerPointTakeoverDraft-");
        var reportStore = new PresentationReportStore(directory.Path);
        var automation = new FakePowerPointAutomationService(Presenting(4, "running"));
        using var session = new PowerPointPresentationSessionService(
            automation,
            reportStore);
        Assert.True(session.Start(
            "owner",
            "Owner phone",
            Assert.Single(automation.Snapshot.Presentations)).Succeeded);
        BlockDraftDirectory(directory.Path);

        var prepared = await session.PrepareForStartAsync(
            "different-runtime",
            sourcePath: null,
            CancellationToken.None);

        Assert.False(prepared.Succeeded);
        Assert.Equal("session-persistence-failed", prepared.Code);
        Assert.Equal("tracking", session.Snapshot.State);
        Assert.Empty(reportStore.ReadAll().Reports);
        Assert.True((await session.CompleteAsync(
            save: false,
            CancellationToken.None)).Succeeded);
        Assert.Equal("inactive", session.Snapshot.State);
    }

    [Fact]
    public async Task TakeoverAttemptsAreSerialized()
    {
        using var session = new PowerPointPresentationSessionService(
            new FakePowerPointAutomationService(Presenting(1, "running")),
            new InMemoryPresentationReportStore());
        using var first = await session.AcquireStartAsync(CancellationToken.None);

        var secondTask = session.AcquireStartAsync(CancellationToken.None);
        await Task.Yield();
        Assert.False(secondTask.IsCompleted);

        first.Dispose();
        using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task HeldTakeoverLeaseReleasesSafelyDuringShutdown()
    {
        var session = new PowerPointPresentationSessionService(
            new FakePowerPointAutomationService(Presenting(1, "running")),
            new InMemoryPresentationReportStore());
        var lease = await session.AcquireStartAsync(CancellationToken.None);

        session.Dispose();
        lease.Dispose();
    }

    [Fact]
    public async Task QueuedTakeoverDoesNotStartAfterShutdown()
    {
        var session = new PowerPointPresentationSessionService(
            new FakePowerPointAutomationService(Presenting(1, "running")),
            new InMemoryPresentationReportStore());
        var heldLease = await session.AcquireStartAsync(CancellationToken.None);
        var queuedLease = session.AcquireStartAsync(CancellationToken.None);
        await Task.Yield();
        Assert.False(queuedLease.IsCompleted);

        session.Dispose();
        heldLease.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await queuedLease.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task TakeoverCannotPrepareOrStartAfterShutdown()
    {
        var automation = new FakePowerPointAutomationService(Presenting(1, "running"));
        var session = new PowerPointPresentationSessionService(
            automation,
            new InMemoryPresentationReportStore());
        var presentation = Assert.Single(automation.Snapshot.Presentations);
        session.Dispose();

        var prepared = await session.PrepareForStartAsync(
            presentation.RuntimePresentationId,
            presentation.SourcePath,
            CancellationToken.None);
        var started = session.StartOrResume("client", "Phone", presentation);

        Assert.False(prepared.Succeeded);
        Assert.Equal("session-unavailable", prepared.Code);
        Assert.False(started.Succeeded);
        Assert.Equal("session-unavailable", started.Code);
        Assert.Equal("inactive", session.Snapshot.State);
    }

    [Fact]
    public async Task ResumingPausedSessionPreservesReportAndExcludesPausedTime()
    {
        const string sourcePath = @"C:\Presentations\Quarterly update.pptx";
        using var directory = new TemporaryDirectory("VolturaAir-PowerPointPaused-");
        var reportStore = new PresentationReportStore(directory.Path);
        var time = new ManualTimeProvider();
        var initial = Presenting(2, "running").Presentations[0] with
        {
            SourcePath = sourcePath
        };
        var automation = new FakePowerPointAutomationService(
            new(PowerPointDiscoveryState.Ready, [initial]));
        using var session = new PowerPointPresentationSessionService(
            automation,
            reportStore,
            time);
        Assert.True(session.Start("owner", "Owner phone", initial).Succeeded);

        time.Advance(TimeSpan.FromSeconds(10));
        automation.Publish(new(
            PowerPointDiscoveryState.Ready,
            [initial with
            {
                IsPresenting = false,
                CurrentSlideIndex = null,
                CurrentShowPosition = null,
                SlideShowState = "ready"
            }]));
        Assert.Equal("pending-review", session.Snapshot.State);
        time.Advance(TimeSpan.FromMinutes(5));

        var restarted = initial with
        {
            RuntimePresentationId = "presentation-restarted",
            CurrentSlideIndex = 4,
            CurrentShowPosition = 4
        };
        automation.Publish(new(PowerPointDiscoveryState.Ready, [restarted]));
        Assert.Equal("tracking", session.Snapshot.State);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.True((await session.CompleteAsync(
            save: true,
            CancellationToken.None)).Succeeded);

        var report = Assert.Single(reportStore.ReadAll().Reports);
        Assert.Equal(15, report.PresentationDurationSeconds);
        Assert.Equal([2, 4], report.SlideVisits!.Select(visit => visit.SlideNumber));
        Assert.StartsWith("host-", report.OperationId);
    }

    [Fact]
    public async Task PausedSessionReconcilesToTheCurrentEditorSlideBeforeContinuing()
    {
        const string sourcePath = @"C:\Presentations\Quarterly update.pptx";
        using var directory = new TemporaryDirectory("VolturaAir-PowerPointReconcile-");
        var reportStore = new PresentationReportStore(directory.Path);
        var presenting = Presenting(5, "running").Presentations[0] with
        {
            SourcePath = sourcePath
        };
        var automation = new FakePowerPointAutomationService(
            new(PowerPointDiscoveryState.Ready, [presenting]));
        using var session = new PowerPointPresentationSessionService(
            automation,
            reportStore);
        Assert.True(session.Start("owner", "Owner phone", presenting).Succeeded);

        var readyOnEditorSlide = presenting with
        {
            IsPresenting = false,
            CurrentSlideIndex = 3,
            CurrentShowPosition = null,
            SlideShowState = "ready"
        };
        automation.Publish(new(
            PowerPointDiscoveryState.Ready,
            [readyOnEditorSlide]));

        Assert.Equal("pending-review", session.Snapshot.State);
        Assert.Equal(3, session.Snapshot.CurrentSlideIndex);

        var presentingAgain = readyOnEditorSlide with
        {
            IsPresenting = true,
            CurrentShowPosition = 3,
            SlideShowState = "running"
        };
        Assert.True(session.StartOrResume(
            "owner",
            "Owner phone",
            presentingAgain).Succeeded);
        Assert.True((await session.CompleteAsync(
            save: true,
            CancellationToken.None)).Succeeded);

        var report = Assert.Single(reportStore.ReadAll().Reports);
        Assert.Equal(
            [5, 3],
            report.SlideVisits!.Select(visit => visit.SlideNumber));
    }

    [Fact]
    public async Task AnyAuthorizedCallerCanManageBreaksOrCompleteDraft()
    {
        using var directory = new TemporaryDirectory("VolturaAir-PowerPointOwner-");
        var reportStore = new PresentationReportStore(directory.Path);
        var automation = new FakePowerPointAutomationService(Presenting(2, "running"));
        var overlay = new FakePresentationBreakOverlay();
        using var session = new PowerPointPresentationSessionService(
            automation,
            reportStore,
            breakOverlay: overlay);
        _ = session.Start(
            "owner",
            "Owner phone",
            Assert.Single(automation.Snapshot.Presentations));

        Assert.True(session.SetBreak(true).Succeeded);
        Assert.True(session.Snapshot.BreakActive);
        Assert.True(overlay.IsVisible);
        Assert.InRange(
            overlay.GetElapsed!(),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));
        Assert.True((await session.ResumeAsync(
            CancellationToken.None)).Succeeded);
        Assert.False(session.Snapshot.BreakActive);
        Assert.False(overlay.IsVisible);
        Assert.Equal(
            "activate",
            Assert.Single(automation.Commands).Action);
        Assert.True((await session.CompleteAsync(
            save: false,
            CancellationToken.None)).Succeeded);
    }

    [Fact]
    public void BreakDoesNotStartWhenOverlayCannotBeShown()
    {
        var automation = new FakePowerPointAutomationService(Presenting(2, "running"));
        var overlay = new FakePresentationBreakOverlay { ShowSucceeds = false };
        using var session = new PowerPointPresentationSessionService(
            automation,
            new InMemoryPresentationReportStore(),
            breakOverlay: overlay);
        _ = session.Start(
            "owner",
            "Owner phone",
            Assert.Single(automation.Snapshot.Presentations));

        var result = session.SetBreak(true);

        Assert.False(result.Succeeded);
        Assert.Equal("session-break-overlay-failed", result.Code);
        Assert.False(session.Snapshot.BreakActive);
    }

    [Fact]
    public async Task ResumeRestartsEndedSlideshowAtTrackedSlideAndActivatesIt()
    {
        var automation = new FakePowerPointAutomationService(Presenting(7, "running"));
        using var session = new PowerPointPresentationSessionService(
            automation,
            new InMemoryPresentationReportStore());
        _ = session.Start(
            "owner",
            "Owner phone",
            Assert.Single(automation.Snapshot.Presentations));
        _ = session.SetBreak(true);
        automation.Publish(new(
            PowerPointDiscoveryState.Ready,
            [new(
                "presentation-a",
                "Quarterly update.pptx",
                false,
                20,
                null,
                null,
                "ready")]));
        var activationAttempts = 0;
        automation.ExecuteHandler = command =>
        {
            if (command.Action == "activate" && activationAttempts++ == 0)
            {
                return new(
                    false,
                    "powerpoint-not-presenting",
                    "Not presenting.",
                    automation.Snapshot);
            }

            return new(true, null, "Done.", automation.Snapshot);
        };

        Assert.Equal("tracking", session.Snapshot.State);
        var result = await session.ResumeAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(session.Snapshot.BreakActive);
        Assert.Equal(
            ["activate", "start", "goto", "activate"],
            automation.Commands.Select(command => command.Action));
        Assert.Equal(7, automation.Commands[2].SlideNumber);
    }

    [Fact]
    public async Task ResumeRestartsWhenReadyActivationSuccessfullyForegroundsEditor()
    {
        var presenting = Presenting(7, "running");
        var automation = new FakePowerPointAutomationService(presenting);
        using var session = new PowerPointPresentationSessionService(
            automation,
            new InMemoryPresentationReportStore());
        _ = session.Start(
            "owner",
            "Owner phone",
            Assert.Single(presenting.Presentations));
        _ = session.SetBreak(true);
        var ready = presenting.Presentations[0] with
        {
            IsPresenting = false,
            CurrentSlideIndex = 3,
            CurrentShowPosition = null,
            SlideShowState = "ready"
        };
        automation.Publish(new(PowerPointDiscoveryState.Ready, [ready]));
        automation.ExecuteHandler = command =>
        {
            if (command.Action == "activate" && automation.Commands.Count == 1)
            {
                return new(true, null, "Editor activated.", automation.Snapshot, ready);
            }

            return new(true, null, "Done.", automation.Snapshot, ready with { IsPresenting = true });
        };

        var result = await session.ResumeAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(session.Snapshot.BreakActive);
        Assert.Equal(
            ["activate", "start", "goto", "activate"],
            automation.Commands.Select(command => command.Action));
        Assert.Equal(7, automation.Commands[2].SlideNumber);
    }

    [Fact]
    public async Task CompletionRejectsResumeWhileReportSaveIsInFlight()
    {
        var automation = new FakePowerPointAutomationService(Presenting(4, "running"));
        var reportStore = new DelayedReportStore();
        using var session = new PowerPointPresentationSessionService(automation, reportStore);
        var presentation = Assert.Single(automation.Snapshot.Presentations);
        Assert.True(session.Start("owner", "Owner phone", presentation).Succeeded);

        var completion = session.CompleteAsync(save: true, CancellationToken.None);
        await reportStore.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var resume = session.StartOrResume("owner", "Owner phone", presentation);
        Assert.False(resume.Succeeded);
        Assert.Equal("session-saving", resume.Code);

        reportStore.ReleaseSave.SetResult();
        Assert.True((await completion).Succeeded);
        Assert.Equal("inactive", session.Snapshot.State);
    }

    [Fact]
    public async Task CanceledSaveReleasesTheSessionForARecoverableRetry()
    {
        var automation = new FakePowerPointAutomationService(Presenting(4, "running"));
        var reportStore = new DelayedReportStore();
        using var session = new PowerPointPresentationSessionService(automation, reportStore);
        Assert.True(session.Start(
            "owner",
            "Owner phone",
            Assert.Single(automation.Snapshot.Presentations)).Succeeded);
        using var cancellation = new CancellationTokenSource();

        var completion = session.CompleteAsync(save: true, cancellation.Token);
        await reportStore.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);

        Assert.Equal("pending-review", session.Snapshot.State);
        Assert.True((await session.CompleteAsync(
            save: false,
            CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task UnexpectedSaveFailureReleasesTheSessionForRetry()
    {
        var automation = new FakePowerPointAutomationService(Presenting(4, "running"));
        var reportStore = new ThrowingOnceReportStore();
        using var session = new PowerPointPresentationSessionService(
            automation,
            reportStore);
        Assert.True(session.Start(
            "owner",
            "Owner phone",
            Assert.Single(automation.Snapshot.Presentations)).Succeeded);

        var failed = await session.CompleteAsync(
            save: true,
            CancellationToken.None);

        Assert.False(failed.Succeeded);
        Assert.Equal("session-save-failed", failed.Code);
        Assert.Equal("pending-review", session.Snapshot.State);
        Assert.True((await session.CompleteAsync(
            save: true,
            CancellationToken.None)).Succeeded);
        Assert.Equal("inactive", session.Snapshot.State);
    }

    [Fact]
    public async Task ResumeBlocksCompletionUntilItsPowerPointWorkFinishes()
    {
        var automation = new FakePowerPointAutomationService(Presenting(4, "running"));
        using var session = new PowerPointPresentationSessionService(
            automation,
            new InMemoryPresentationReportStore());
        Assert.True(session.Start(
            "owner",
            "Owner phone",
            Assert.Single(automation.Snapshot.Presentations)).Succeeded);
        Assert.True(session.SetBreak(true).Succeeded);
        var activationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseActivation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        automation.ExecuteAsyncHandler = async (command, cancellationToken) =>
        {
            activationStarted.SetResult();
            await releaseActivation.Task.WaitAsync(cancellationToken);
            return new(true, null, "Activated.", automation.Snapshot, automation.Snapshot.Presentations[0]);
        };

        var resume = session.ResumeAsync(CancellationToken.None);
        await activationStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var completion = await session.CompleteAsync(
            save: false,
            CancellationToken.None);

        Assert.False(completion.Succeeded);
        Assert.Equal("session-busy", completion.Code);
        releaseActivation.SetResult();
        Assert.True((await resume).Succeeded);
        Assert.False(session.Snapshot.BreakActive);
    }

    [Fact]
    public async Task DisposeDuringSaveStillUnsubscribesAndDisposesLifecycleState()
    {
        var automation = new FakePowerPointAutomationService(Presenting(4, "running"));
        var reportStore = new DelayedReportStore();
        var session = new PowerPointPresentationSessionService(automation, reportStore);
        Assert.Equal(1, automation.SnapshotSubscriberCount);
        Assert.True(session.Start(
            "owner",
            "Owner phone",
            Assert.Single(automation.Snapshot.Presentations)).Succeeded);

        var completion = session.CompleteAsync(save: true, CancellationToken.None);
        await reportStore.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        session.Dispose();

        Assert.Equal(0, automation.SnapshotSubscriberCount);
        reportStore.ReleaseSave.SetResult();
        Assert.True((await completion).Succeeded);
    }

    [Fact]
    public void StartRollsBackAndReturnsExplicitFailureWhenDraftCannotBeWritten()
    {
        using var directory = new TemporaryDirectory("VolturaAir-PowerPointDraftFailure-");
        var blockedDirectory = Path.Combine(directory.Path, "blocked");
        File.WriteAllText(blockedDirectory, "not a directory");
        var automation = new FakePowerPointAutomationService(Presenting(2, "running"));
        using var session = new PowerPointPresentationSessionService(
            automation,
            new TestReportStore(blockedDirectory));

        var result = session.Start(
            "owner",
            "Owner phone",
            Assert.Single(automation.Snapshot.Presentations));

        Assert.False(result.Succeeded);
        Assert.Equal("session-persistence-failed", result.Code);
        Assert.Equal("inactive", session.Snapshot.State);
    }

    [Fact]
    public async Task ResumeReopensClosedTrackedFileBeforeRestoringSlide()
    {
        const string sourcePath = @"C:\Presentations\Quarterly update.pptx";
        var presenting = Presenting(7, "running").Presentations[0] with
        {
            SourcePath = sourcePath
        };
        var automation = new FakePowerPointAutomationService(
            new(PowerPointDiscoveryState.Ready, [presenting]));
        using var session = new PowerPointPresentationSessionService(
            automation,
            new InMemoryPresentationReportStore());
        _ = session.Start("owner", "Owner phone", presenting);
        _ = session.SetBreak(true);
        automation.Publish(new(PowerPointDiscoveryState.Ready, []));
        var reopened = presenting with
        {
            RuntimePresentationId = "presentation-reopened",
            IsPresenting = false,
            CurrentSlideIndex = null,
            CurrentShowPosition = null,
            SlideShowState = "ready"
        };
        automation.ExecuteHandler = command => command.Action switch
        {
            "activate" when command.RuntimePresentationId == "presentation-a" =>
                new(false, "powerpoint-target-stale", "Closed.", automation.Snapshot),
            "open" => new(
                true,
                null,
                "Reopened.",
                new(PowerPointDiscoveryState.Ready, [reopened]),
                reopened),
            _ => new(true, null, "Done.", automation.Snapshot, reopened)
        };

        Assert.Equal("tracking", session.Snapshot.State);
        var result = await session.ResumeAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("presentation-reopened", session.Snapshot.RuntimePresentationId);
        Assert.Equal(
            ["activate", "open", "start", "goto", "activate"],
            automation.Commands.Select(command => command.Action));
        Assert.Equal(sourcePath, automation.Commands[1].SourcePath);
        Assert.Equal(7, automation.Commands[3].SlideNumber);
    }

    [Fact]
    public void NonReadyDiscoveryDoesNotEndTracking()
    {
        var automation = new FakePowerPointAutomationService(Presenting(2, "running"));
        using var session = new PowerPointPresentationSessionService(
            automation,
            new InMemoryPresentationReportStore());
        _ = session.Start(
            "owner",
            "Owner phone",
            Assert.Single(automation.Snapshot.Presentations));

        automation.Publish(PowerPointAutomationSnapshot.Unavailable);
        Assert.Equal("tracking", session.Snapshot.State);

        automation.Publish(new(PowerPointDiscoveryState.Inaccessible, []));
        Assert.Equal("tracking", session.Snapshot.State);

        automation.Publish(Presenting(3, "running"));
        Assert.Equal("tracking", session.Snapshot.State);
        Assert.Equal(3, session.Snapshot.CurrentSlideIndex);

        automation.Publish(new(PowerPointDiscoveryState.Ready, []));
        Assert.Equal("pending-review", session.Snapshot.State);
    }

    [Fact]
    public async Task PresentationAndVisitDurationsUseMonotonicTimeAndExcludeBreaks()
    {
        using var directory = new TemporaryDirectory("VolturaAir-PowerPointClock-");
        var reportStore = new PresentationReportStore(directory.Path);
        var automation = new FakePowerPointAutomationService(Presenting(2, "running"));
        var time = new ManualTimeProvider();
        using var session = new PowerPointPresentationSessionService(
            automation,
            reportStore,
            time);
        _ = session.Start(
            "owner",
            "Owner phone",
            Assert.Single(automation.Snapshot.Presentations));

        time.Advance(TimeSpan.FromSeconds(10));
        _ = session.SetBreak(true);
        time.Advance(TimeSpan.FromSeconds(20));
        _ = session.SetBreak(false);
        time.Advance(TimeSpan.FromSeconds(5));
        _ = await session.CompleteAsync(
            save: true,
            CancellationToken.None);

        var report = Assert.Single(reportStore.ReadAll().Reports);
        Assert.Equal(15, report.PresentationDurationSeconds);
        Assert.Equal(20, Assert.Single(report.Breaks).BreakDurationSeconds);
        Assert.Equal(15, report.Slides.Sum(slide => slide.DurationSeconds ?? 0));
    }

    private static PowerPointAutomationSnapshot Presenting(int slide, string state) =>
        new(
            PowerPointDiscoveryState.Ready,
            [new(
                "presentation-a",
                "Quarterly update.pptx",
                true,
                20,
                slide,
                slide,
                state)]);

    private static void BlockDraftDirectory(string reportDirectory)
    {
        var draftDirectory = Path.Combine(reportDirectory, "Drafts");
        Directory.Delete(draftDirectory, recursive: true);
        File.WriteAllText(draftDirectory, "blocked");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory(string prefix)
        {
            Path = Directory.CreateTempSubdirectory(prefix).FullName;
        }

        internal string Path { get; }

        public void Dispose()
        {
            var directory = new DirectoryInfo(Path);
            if (directory.Exists)
            {
                var draftBlocker = System.IO.Path.Combine(Path, "Drafts");
                if (File.Exists(draftBlocker))
                {
                    File.Delete(draftBlocker);
                }

                directory.Delete(recursive: true);
            }
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 7, 24, 8, 0, 0, TimeSpan.Zero);
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        internal void Advance(TimeSpan duration)
        {
            _utcNow += duration;
            _timestamp += duration.Ticks;
        }
    }

    private sealed class FakePresentationBreakOverlay : IPresentationBreakOverlay
    {
        internal bool ShowSucceeds { get; init; } = true;

        internal bool IsVisible { get; private set; }

        internal Func<TimeSpan>? GetElapsed { get; private set; }

        public SystemPowerExecutionResult TryShowPresentationBreak(Func<TimeSpan> getElapsed)
        {
            GetElapsed = getElapsed;
            IsVisible = ShowSucceeds;
            return new(ShowSucceeds);
        }

        public bool DismissPresentationBreakIfActive()
        {
            var wasVisible = IsVisible;
            IsVisible = false;
            return wasVisible;
        }
    }

    private class TestReportStore(string reportDirectory) : IPresentationReportStore
    {
        public string ReportDirectory { get; } = reportDirectory;

        public event EventHandler? ReportsChanged
        {
            add { }
            remove { }
        }

        public virtual Task<PresentationReportSaveResult> SaveAsync(
            PresentationReportSaveRequest request,
            string clientId,
            string deviceName,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PresentationReportSaveResult(
                true,
                null,
                "Saved.",
                request.ReportId));

        public PresentationReportReadResult ReadAll() => new(true, []);

        public PresentationReportMutationResult Rename(string reportId, string title) =>
            new(false, "Not supported.");

        public PresentationReportMutationResult Delete(string reportId) =>
            new(false, "Not supported.");

        public PresentationReportMutationResult DeleteMany(IReadOnlyCollection<string> reportIds) =>
            new(false, "Not supported.");

        public PresentationReportMutationResult DeleteAll() =>
            new(false, "Not supported.");

        public PresentationReportMutationResult SetPresentationFile(string reportId, string? path) =>
            new(false, "Not supported.");

        public PresentationReportMutationResult SetPresentationUrl(string reportId, string? url) =>
            new(false, "Not supported.");

    }

    private sealed class DelayedReportStore() : TestReportStore(string.Empty)
    {
        internal TaskCompletionSource SaveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource ReleaseSave { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<PresentationReportSaveResult> SaveAsync(
            PresentationReportSaveRequest request,
            string clientId,
            string deviceName,
            CancellationToken cancellationToken)
        {
            SaveStarted.SetResult();
            await ReleaseSave.Task.WaitAsync(cancellationToken);
            return new(true, null, "Saved.", request.ReportId);
        }
    }

    private sealed class ThrowingOnceReportStore() : TestReportStore(string.Empty)
    {
        private bool _hasThrown;

        public override Task<PresentationReportSaveResult> SaveAsync(
            PresentationReportSaveRequest request,
            string clientId,
            string deviceName,
            CancellationToken cancellationToken)
        {
            if (!_hasThrown)
            {
                _hasThrown = true;
                throw new InvalidOperationException("Unexpected report subscriber failure.");
            }

            return base.SaveAsync(
                request,
                clientId,
                deviceName,
                cancellationToken);
        }
    }
}
