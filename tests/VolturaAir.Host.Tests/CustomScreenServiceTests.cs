using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class CustomScreenServiceTests
{
    [Fact]
    public void AssignmentCleanupNeverDeletesReusableScreen()
    {
        var service = CreateService();
        var draft = CustomScreenService.CreateDraft() with
        {
            AssignedClientIds = ["phone-a", "tablet-b"]
        };
        Assert.True(service.TrySave(draft, out var saved, out var error), error);

        service.RemoveDeviceAssignments("phone-a");

        var remaining = Assert.Single(service.GetAll());
        Assert.Equal(saved.Id, remaining.Id);
        Assert.Equal(["tablet-b"], remaining.AssignedClientIds);
    }

    [Fact]
    public void MobileDefinitionContainsVisualsAndAvailabilityButNoActionPayload()
    {
        var service = CreateService();
        var draft = CustomScreenService.CreateDraft();
        var sourceButton = draft.Sections[0].Buttons[0];
        draft = draft with
        {
            AssignedClientIds = ["phone-a"],
            Sections =
            [
                draft.Sections[0] with
                {
                    Buttons =
                    [
                        sourceButton with
                        {
                            Presentation = "label",
                            Action = new CustomScreenAction(
                                "text",
                                Text: "private literal text")
                        }
                    ]
                }
            ]
        };
        Assert.True(service.TrySave(draft, out var saved, out var error), error);

        var mobile = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: false,
            canLaunchApps: false);

        var button = Assert.Single(Assert.Single(mobile!.Sections).Buttons);
        Assert.False(button.Enabled);
        Assert.Contains("Remote input", button.UnavailableReason, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private literal text",
            System.Text.Json.JsonSerializer.Serialize(mobile),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SaveRejectsDefinitionWhoseFetchEnvelopeExceedsWebSocketLimit()
    {
        var service = CreateService();
        var sections = Enumerable.Range(0, 16).Select(sectionIndex =>
            new CustomScreenSection(
                $"section.{sectionIndex}",
                new string(
                    (char)('A' + sectionIndex),
                    CustomScreenLimits.MaxSectionNameLength),
                true,
                12,
                "content",
                1,
                3,
                null,
                null,
                [.. Enumerable.Range(0, 16).Select(buttonIndex =>
                    new CustomScreenButton(
                        $"button.{sectionIndex}.{buttonIndex}",
                        new string('N', CustomScreenLimits.MaxButtonNameLength - 4) +
                            $"{sectionIndex:D2}{buttonIndex:D2}",
                        new string('L', CustomScreenLimits.MaxButtonLabelLength),
                        "command",
                        "iconLabel",
                        "fill",
                        false,
                        null,
                        null,
                        new CustomScreenAction("builtIn", BuiltIn: "navigation.enter")))])).ToArray();
        var draft = CustomScreenService.CreateDraft() with { Sections = sections };

        Assert.False(service.TrySave(draft, out _, out var error));
        Assert.Contains("too large to send", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShortcutSupportsAltGrWithOtherCurrentModifiers()
    {
        var service = CreateService();
        var draft = WithShortcut(
            CustomScreenService.CreateDraft(),
            "X",
            ["Shift", "AltGr"]);
        Assert.True(service.TrySave(draft, out _, out var validError), validError);

        var combined = WithShortcut(draft, "X", ["Control", "AltGr"]);
        Assert.True(
            service.TrySave(combined, out _, out var combinedError),
            combinedError);

        var unknown = WithShortcut(draft, "NotARealKey", ["Control"]);
        Assert.False(service.TrySave(unknown, out _, out _));

        var duplicate = WithShortcut(draft, "X", ["AltGr", "AltGr"]);
        Assert.False(service.TrySave(duplicate, out _, out _));

        var insert = WithShortcut(draft, "Insert", ["Control"]);
        Assert.True(service.TrySave(insert, out _, out var insertError), insertError);

        var digit = WithShortcut(draft, "7", ["Control", "Alt"]);
        Assert.True(service.TrySave(digit, out _, out var digitError), digitError);

        var semicolon = WithShortcut(draft, ";", ["Control"]);
        Assert.True(
            service.TrySave(semicolon, out _, out var semicolonError),
            semicolonError);

        var escape = WithShortcut(draft, "Escape", ["Control", "Alt"]);
        Assert.True(service.TrySave(escape, out _, out var escapeError), escapeError);

        var taskManager = WithShortcut(
            draft,
            "Escape",
            ["Control", "Shift"]);
        Assert.True(
            service.TrySave(taskManager, out _, out var taskManagerError),
            taskManagerError);
    }

    [Fact]
    public void ArbitraryShortcutsCannotEnableHoldRepeat()
    {
        Assert.False(CustomScreenService.IsRepeatable(
            new CustomScreenAction("shortcut", Key: "X", Modifiers: ["Control"])));
        Assert.True(CustomScreenService.IsRepeatable(
            new CustomScreenAction("builtIn", BuiltIn: "volume.up")));
    }

    [Theory]
    [InlineData("power.restart", "hold")]
    [InlineData("power.shutdown", "hold")]
    [InlineData("power.sleep", "confirm")]
    [InlineData("power.hibernate", "confirm")]
    [InlineData("display.off", "confirm")]
    public void HostActionSafetyIsDerivedByTheHost(string actionId, string confirmation)
    {
        var service = CreateService();
        var draft = CustomScreenService.CreateDraft();
        var section = draft.Sections[0];
        draft = draft with
        {
            AssignedClientIds = ["phone-a"],
            Sections = [section with
            {
                Buttons = [section.Buttons[0] with
                {
                    Action = new CustomScreenAction("hostAction", ActionId: actionId)
                }]
            }]
        };
        Assert.True(service.TrySave(draft, out var saved, out var error), error);

        var mobile = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: true,
            canLaunchApps: true,
            canControlVolume: true,
            canOpenUrls: true,
            permissions: new HostPermissionSet(
                AllowPcSleep: true,
                AllowDisplayControl: true,
                AllowPcLock: true,
                AllowRestart: true,
                AllowShutdown: true));

        var button = Assert.Single(Assert.Single(mobile!.Sections).Buttons);
        Assert.True(button.Enabled);
        Assert.Equal(confirmation, button.Confirmation);
        Assert.False(string.IsNullOrWhiteSpace(button.ConfirmationMessage));
    }

    [Fact]
    public void HostActionProjectionDisablesActionsKnownToBeUnavailable()
    {
        var service = CreateService();
        var draft = CustomScreenService.CreateDraft();
        var section = draft.Sections[0];
        draft = draft with
        {
            AssignedClientIds = ["phone-a"],
            Sections = [section with
            {
                Buttons = [section.Buttons[0] with
                {
                    Action = new CustomScreenAction("hostAction", ActionId: "power.lock")
                }]
            }]
        };
        Assert.True(service.TrySave(draft, out var saved, out var error), error);

        var mobile = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: true,
            canLaunchApps: true,
            permissions: new HostPermissionSet(AllowPcLock: true),
            unavailableHostActions: new HashSet<string>(StringComparer.Ordinal) { "power.lock" });

        var button = Assert.Single(Assert.Single(mobile!.Sections).Buttons);
        Assert.False(button.Enabled);
        Assert.Contains("unavailable", button.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MobileProjectionReadsTheCachedKnownApplicationSnapshotOnce()
    {
        var launches = new FakeAppLaunchService
        {
            KnownApplications =
            [
                new("browser", "Browser", true),
                new("vlc", "VLC", true)
            ]
        };
        var service = new CustomScreenService(new InMemoryCustomScreenStore(), launches);
        var draft = CustomScreenService.CreateDraft();
        var source = draft.Sections[0].Buttons[0];
        draft = draft with
        {
            AssignedClientIds = ["phone-a"],
            Sections = [draft.Sections[0] with
            {
                Buttons =
                [
                    source with { Action = new CustomScreenAction("knownApp", ActionId: "browser") },
                    source with { Id = "button.vlc", Action = new CustomScreenAction("knownApp", ActionId: "vlc") }
                ]
            }]
        };
        Assert.True(service.TrySave(draft, out var saved, out var error), error);

        _ = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: true,
            canLaunchApps: true,
            canControlVolume: true,
            canOpenUrls: true,
            HostPermissions.DefaultGlobal);

        Assert.Equal(1, launches.KnownApplicationQueries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleKnownApplicationDependencyGatesTheEntireProjectedScreen(bool available)
    {
        var launches = new FakeAppLaunchService
        {
            KnownApplications = [new("vlc", "VLC", available)]
        };
        var service = new CustomScreenService(new InMemoryCustomScreenStore(), launches);
        var draft = CustomScreenService.CreateVolumeSlider(
            CustomScreenService.CreateTrackpad(CustomScreenService.CreateDraft()));
        var source = draft.Sections[0].Buttons[0];
        draft = draft with
        {
            AssignedClientIds = ["phone-a"],
            Sections =
            [
                draft.Sections[0] with
                {
                    Buttons =
                    [
                        source with
                        {
                            Action = new CustomScreenAction("knownApp", ActionId: "vlc")
                        },
                        source with
                        {
                            Id = "button.play",
                            Name = "Play",
                            Label = "Play",
                            Presentation = "label",
                            Action = new CustomScreenAction("shortcut", Key: "Space", Modifiers: [])
                        }
                    ]
                },
                draft.Sections[1],
                draft.Sections[2]
            ]
        };
        Assert.True(service.TrySave(draft, out var saved, out var error), error);

        var mobile = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: true,
            canLaunchApps: true,
            canControlVolume: true,
            canOpenUrls: true,
            HostPermissions.DefaultGlobal)!;

        var expectedReason = available ? null : "This Custom Screen requires VLC on the PC.";
        Assert.All(mobile.Sections[0].Buttons, button =>
        {
            Assert.Equal(available, button.Enabled);
            Assert.Equal(expectedReason, button.UnavailableReason);
        });
        Assert.Equal(available, mobile.Sections[1].TrackpadEnabled);
        Assert.Equal(expectedReason, mobile.Sections[1].TrackpadUnavailableReason);
        Assert.Equal(available, mobile.Sections[2].VolumeEnabled);
        Assert.Equal(expectedReason, mobile.Sections[2].VolumeUnavailableReason);
    }

    [Fact]
    public void MultipleKnownApplicationTargetsRetainPerControlAvailability()
    {
        var launches = new FakeAppLaunchService
        {
            KnownApplications =
            [
                new("browser", "Browser", true),
                new("vlc", "VLC", false)
            ]
        };
        var service = new CustomScreenService(new InMemoryCustomScreenStore(), launches);
        var draft = CustomScreenService.CreateDraft();
        var source = draft.Sections[0].Buttons[0];
        draft = draft with
        {
            AssignedClientIds = ["phone-a"],
            Sections = [draft.Sections[0] with
            {
                Buttons =
                [
                    source with { Action = new CustomScreenAction("knownApp", ActionId: "browser") },
                    source with { Id = "button.vlc", Action = new CustomScreenAction("knownApp", ActionId: "vlc") },
                    source with
                    {
                        Id = "button.play",
                        Name = "Play",
                        Label = "Play",
                        Presentation = "label",
                        Action = new CustomScreenAction("shortcut", Key: "Space", Modifiers: [])
                    }
                ]
            }]
        };
        Assert.True(service.TrySave(draft, out var saved, out var error), error);

        var buttons = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: true,
            canLaunchApps: true)!.Sections[0].Buttons;

        Assert.True(buttons[0].Enabled);
        Assert.False(buttons[1].Enabled);
        Assert.True(buttons[2].Enabled);
    }

    [Fact]
    public void LiteralTextAndCustomShortcutsRequireLabelOnlyPresentation()
    {
        var service = CreateService();
        var draft = CustomScreenService.CreateDraft();
        var section = draft.Sections[0];
        var button = section.Buttons[0] with
        {
            Action = new CustomScreenAction("text", Text: "ABC"),
            Presentation = "iconLabel"
        };

        Assert.False(service.TrySave(
            draft with { Sections = [section with { Buttons = [button] }] },
            out _,
            out _));
        Assert.True(service.TrySave(
            draft with
            {
                Sections =
                [
                    section with
                    {
                        Buttons = [button with { Presentation = "label" }]
                    }
                ]
            },
            out _,
            out var error), error);
    }

    [Fact]
    public void ButtonRowIsValidatedAndPublishedToMobile()
    {
        var service = CreateService();
        var draft = CustomScreenService.CreateDraft();
        var section = draft.Sections[0] with
        {
            RowLimit = 3,
            Buttons = [draft.Sections[0].Buttons[0] with { Row = 2 }]
        };
        draft = draft with
        {
            AssignedClientIds = ["phone-a"],
            Sections = [section]
        };

        Assert.True(service.TrySave(draft, out var saved, out var error), error);
        var mobile = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: true,
            canLaunchApps: false);
        Assert.Equal(2, Assert.Single(Assert.Single(mobile!.Sections).Buttons).Row);

        var invalid = draft with
        {
            Sections = [section with
            {
                RowLimit = 1,
                Buttons = [section.Buttons[0] with { Row = 2 }]
            }]
        };
        Assert.False(service.TrySave(invalid, out _, out var invalidError));
        Assert.Contains("row", invalidError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrackpadDefinitionPublishesLayoutAndRemoteInputAvailability()
    {
        var service = CreateService();
        var draft = CustomScreenService.CreateTrackpad(CustomScreenService.CreateDraft());
        var trackpad = draft.Sections[^1] with
        {
            WidthColumns = 6,
            HeightMode = "fill",
            FillWeight = 3,
            TrackpadButtonSide = "left",
            TrackpadFullscreenControl = true
        };
        draft = draft with
        {
            AssignedClientIds = ["phone-a"],
            Sections = [trackpad]
        };

        Assert.True(service.TrySave(draft, out var saved, out var error), error);
        var mobile = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: false,
            canLaunchApps: false);

        var section = Assert.Single(mobile!.Sections);
        Assert.Equal("trackpad", section.Kind);
        Assert.Equal(6, section.WidthColumns);
        Assert.Equal("fill", section.HeightMode);
        Assert.Equal(3, section.FillWeight);
        Assert.Equal("left", section.TrackpadButtonSide);
        Assert.True(section.TrackpadFullscreenControl);
        Assert.False(section.TrackpadEnabled);
        Assert.Contains("Remote input", section.TrackpadUnavailableReason);
    }

    [Fact]
    public void CollapsibleTrackpadPublishesSharedSizingAndFoldingState()
    {
        var service = CreateService();
        var draft = CustomScreenService.CreateCollapsibleTrackpad(
            CustomScreenService.CreateDraft());
        var trackpad = draft.Sections[^1] with
        {
            WidthColumns = 8,
            FillWeight = 2,
            InitiallyExpanded = false,
            Portrait = new CustomScreenLayoutOverride(2, true, 12),
            Landscape = new CustomScreenLayoutOverride(0, true, 6)
        };
        draft = draft with
        {
            AssignedClientIds = ["phone-a"],
            OrientationLayoutsEnabled = true,
            Sections = [trackpad]
        };

        Assert.True(service.TrySave(draft, out var saved, out var error), error);
        var mobile = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: true,
            canLaunchApps: false);

        var section = Assert.Single(mobile!.Sections);
        Assert.Equal("trackpad", section.Kind);
        Assert.True(section.Collapsible);
        Assert.False(section.InitiallyExpanded);
        Assert.Equal(12, section.Portrait!.WidthColumns);
        Assert.Equal(6, section.Landscape!.WidthColumns);
        Assert.Equal(2, section.FillWeight);
    }

    [Fact]
    public void SaveEnforcesShortEditorAndVisibleNames()
    {
        var service = CreateService();
        var draft = CustomScreenService.CreateDraft();
        Assert.False(service.TrySave(
            draft with
            {
                Name = new string('S', CustomScreenLimits.MaxScreenNameLength + 1)
            },
            out _,
            out _));
        Assert.False(service.TrySave(
            draft with
            {
                Sections =
                [
                    draft.Sections[0] with
                    {
                        Name = new string(
                            'P',
                            CustomScreenLimits.MaxSectionNameLength + 1)
                    }
                ]
            },
            out _,
            out _));
        Assert.False(service.TrySave(
            draft with
            {
                Sections =
                [
                    draft.Sections[0] with
                    {
                        Buttons =
                        [
                            draft.Sections[0].Buttons[0] with
                            {
                                Name = new string(
                                    'B',
                                    CustomScreenLimits.MaxButtonNameLength + 1),
                                Label = new string(
                                    'L',
                                    CustomScreenLimits.MaxButtonLabelLength + 1)
                            }
                        ]
                    }
                ]
            },
            out _,
            out _));
    }

    [Fact]
    public void CollapsibleSectionKeepsRegularLayoutAndPublishesItsButtons()
    {
        var service = CreateService();
        var draft = CustomScreenService.CreateCollapsibleSection(
            CustomScreenService.CreateDraft());
        var collapsible = draft.Sections[^1] with
        {
            WidthColumns = 6,
            HeightMode = "fill",
            FillWeight = 3,
            RowLimit = 2,
            InitiallyExpanded = false
        };
        draft = CustomScreenService.CreateButton(
            draft with
            {
                AssignedClientIds = ["phone-a"],
                Sections = [collapsible]
            },
            collapsible.Id,
            row: 2);

        Assert.True(service.TrySave(draft, out var saved, out var error), error);
        var mobile = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: true,
            canLaunchApps: false);

        var section = Assert.Single(mobile!.Sections);
        Assert.Equal("buttons", section.Kind);
        Assert.True(section.Collapsible);
        Assert.False(section.InitiallyExpanded);
        Assert.True(section.ShowHeader);
        Assert.Equal(6, section.WidthColumns);
        Assert.Equal("fill", section.HeightMode);
        Assert.Equal(3, section.FillWeight);
        Assert.Equal(2, section.RowLimit);
        Assert.Equal(2, Assert.Single(section.Buttons).Row);
    }

    [Fact]
    public void ButtonPlacementAndVolumeSliderPublishWithVolumePermission()
    {
        var service = CreateService();
        var draft = CustomScreenService.CreateVolumeSlider(
            CustomScreenService.CreateDraft());
        draft = draft with
        {
            AssignedClientIds = ["phone-a"],
            Sections =
            [
                draft.Sections[0] with
                {
                    ButtonAlignment = "space-between"
                },
                draft.Sections[^1] with
                {
                    WidthColumns = 6
                }
            ]
        };

        Assert.True(service.TrySave(draft, out var saved, out var error), error);
        var mobile = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: true,
            canLaunchApps: true,
            canControlVolume: false);

        Assert.Equal("space-between", mobile!.Sections[0].ButtonAlignment);
        var volume = mobile.Sections[1];
        Assert.Equal("volume", volume.Kind);
        Assert.Equal(6, volume.WidthColumns);
        Assert.Equal("content", volume.HeightMode);
        Assert.Empty(volume.Buttons);
        Assert.False(volume.VolumeEnabled);
        Assert.Contains(
            "Volume control",
            volume.VolumeUnavailableReason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReorderMovesScreenBeforeOrAfterTheDropTarget()
    {
        var service = CreateService();
        var first = SaveNamed(service, "First");
        var second = SaveNamed(service, "Second");
        var third = SaveNamed(service, "Third");

        Assert.True(service.TryReorder(first.Id, third.Id, true, out var afterError), afterError);
        Assert.Equal(
            [second.Id, third.Id, first.Id],
            service.GetAll().Select(screen => screen.Id));

        Assert.True(service.TryReorder(first.Id, second.Id, false, out var beforeError), beforeError);
        Assert.Equal(
            [first.Id, second.Id, third.Id],
            service.GetAll().Select(screen => screen.Id));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("red")]
    [InlineData("green")]
    [InlineData("blue")]
    public void LaserPointerPublishesConfiguredColorAndPresentationPermission(
        string color)
    {
        var service = CreateService();
        var draft = CustomScreenService.CreateDraft();
        draft = CustomScreenService.CreateLaserPointer(
            draft,
            draft.Sections[0].Id);
        var laser = draft.Sections[0].Buttons[^1];
        draft = ReplaceButton(
            draft,
            laser with
            {
                Action = new CustomScreenAction("laserPointer", Color: color)
            }) with
        {
            AssignedClientIds = ["phone-a"]
        };

        Assert.True(service.TrySave(draft, out var saved, out var error), error);
        var allowed = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: false,
            canLaunchApps: false,
            permissions: new HostPermissionSet(AllowPresentationControl: true));
        var blocked = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: true,
            canLaunchApps: true,
            permissions: new HostPermissionSet(AllowPresentationControl: false));

        var allowedLaser = allowed!.Sections.SelectMany(section => section.Buttons)
            .Single(button => button.Id == laser.Id);
        var blockedLaser = blocked!.Sections.SelectMany(section => section.Buttons)
            .Single(button => button.Id == laser.Id);
        Assert.True(allowedLaser.Enabled);
        Assert.Equal(color, allowedLaser.LaserPointerColor);
        Assert.False(blockedLaser.Enabled);
        Assert.Contains("Presentation control", blockedLaser.UnavailableReason);
    }

    [Fact]
    public void LaserPointerRejectsMissingInvalidUnrelatedAndRepeatData()
    {
        var service = CreateService();
        var draft = CustomScreenService.CreateDraft();
        draft = CustomScreenService.CreateLaserPointer(draft, draft.Sections[0].Id);
        var laser = draft.Sections[0].Buttons[^1];

        Assert.False(service.TrySave(
            ReplaceButton(draft, laser with
            {
                Action = new CustomScreenAction("laserPointer")
            }),
            out _,
            out _));
        Assert.False(service.TrySave(
            ReplaceButton(draft, laser with
            {
                Action = new CustomScreenAction("laserPointer", Text: "bad", Color: "red")
            }),
            out _,
            out _));
        Assert.False(service.TrySave(
            ReplaceButton(draft, laser with
            {
                Action = new CustomScreenAction("laserPointer", Color: "purple")
            }),
            out _,
            out _));
        Assert.False(service.TrySave(
            ReplaceButton(draft, laser with { Repeat = true }),
            out _,
            out _));

        var ordinary = draft.Sections[0].Buttons[0];
        Assert.False(service.TrySave(
            ReplaceButton(draft, ordinary with
            {
                Action = ordinary.Action with { Color = "red" }
            }),
            out _,
            out _));
    }

    private static CustomScreenDefinition SaveNamed(
        CustomScreenService service,
        string name)
    {
        var draft = CustomScreenService.CreateDraft() with { Name = name };
        Assert.True(service.TrySave(draft, out var saved, out var error), error);
        return saved;
    }

    private static CustomScreenDefinition WithShortcut(
        CustomScreenDefinition draft,
        string key,
        IReadOnlyList<string> modifiers)
    {
        var section = draft.Sections[0];
        var button = section.Buttons[0] with
        {
            Presentation = "label",
            Action = new CustomScreenAction("shortcut", Key: key, Modifiers: modifiers)
        };
        return draft with
        {
            Sections = [section with { Buttons = [button] }]
        };
    }

    private static CustomScreenDefinition ReplaceButton(
        CustomScreenDefinition draft,
        CustomScreenButton replacement) =>
        draft with
        {
            Sections = [.. draft.Sections.Select(section => section with
            {
                Buttons = [.. section.Buttons.Select(button =>
                    button.Id == replacement.Id ? replacement : button)]
            })]
        };

    private static CustomScreenService CreateService() =>
        new(new InMemoryCustomScreenStore(), new FakeAppLaunchService());
}

internal sealed class FakeAppLaunchService : IAppLaunchService
{
    public IReadOnlyList<KnownAppProfileSummary> KnownApplications { get; init; } = [];

    public int KnownApplicationQueries { get; private set; }

    public IReadOnlyList<AppLaunchActionSummary> GetActions() =>
        [new("app.notes", "Notes", "custom")];

    public AppLaunchExecutionResult Execute(string actionId) =>
        new(true, "started", "Started.");

    public AppLaunchExecutionResult ExecutePowerPointFile(string path) =>
        new(false, "not-configured", "Unavailable.");

    public IReadOnlyList<KnownAppProfileSummary> GetKnownApplications()
    {
        KnownApplicationQueries++;
        return KnownApplications;
    }
}
