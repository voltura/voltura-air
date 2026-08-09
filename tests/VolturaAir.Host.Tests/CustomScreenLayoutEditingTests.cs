using VolturaAir.Host;
using VolturaAir.Host.Features.CustomScreens;

namespace VolturaAir.Host.Tests;

public sealed class CustomScreenLayoutEditingTests
{
    [Fact]
    public void CrossRowDropPinsDraggedAndAutomaticTargetToVisibleDestinationRow()
    {
        var draft = CustomScreenService.CreateDraft();
        var section = draft.Sections[0];
        var dragged = section.Buttons[0] with { Row = 0 };
        var target = section.Buttons[1] with { Row = 0 };
        var remaining = section.Buttons[2] with { Row = 0 };
        draft = draft with
        {
            Sections =
            [
                section with
                {
                    RowLimit = 2,
                    Buttons = [dragged, target, remaining]
                }
            ]
        };

        var updated = CustomScreenLayoutEditing.ReorderButton(
            draft,
            dragged.Id,
            section.Id,
            target.Id,
            insertAfter: true,
            targetVisualRow: 2);

        var buttons = Assert.Single(updated!.Sections).Buttons;
        Assert.Equal([target.Id, dragged.Id, remaining.Id], buttons.Select(button => button.Id));
        Assert.Equal(2, buttons.Single(button => button.Id == target.Id).Row);
        Assert.Equal(2, buttons.Single(button => button.Id == dragged.Id).Row);
        Assert.Equal(0, buttons.Single(button => button.Id == remaining.Id).Row);
    }

    [Theory]
    [InlineData(false, "first,third,second")]
    [InlineData(true, "first,second,third")]
    public void SectionDropPlacesTheDraggedSectionBeforeOrAfterItsTarget(
        bool insertAfter,
        string expectedOrder)
    {
        var firstDraft = CustomScreenService.CreateDraft();
        var secondDraft = CustomScreenService.CreateSection(firstDraft);
        var draft = CustomScreenService.CreateSection(secondDraft);
        var first = draft.Sections[0];
        var second = draft.Sections[1];
        var third = draft.Sections[2];

        var edit = CustomScreenPreviewDraftEditing.ReorderSection(
            draft,
            third.Id,
            second.Id,
            "portrait",
            insertAfter);

        Assert.NotNull(edit);
        var names = edit.Draft.Sections
            .Select(section => section.Id switch
            {
                var id when id == first.Id => "first",
                var id when id == second.Id => "second",
                _ => "third"
            });
        Assert.Equal(expectedOrder, string.Join(',', names));
    }

    [Fact]
    public void OrientationSectionDropChangesOnlyTheSelectedOrientationOrder()
    {
        var firstDraft = CustomScreenService.CreateDraft();
        var draft = CustomScreenService.CreateSection(firstDraft);
        var first = draft.Sections[0];
        var second = draft.Sections[1];
        draft = CustomScreenOrientationEditing.Enable(draft);

        var edit = CustomScreenPreviewDraftEditing.ReorderSection(
            draft,
            second.Id,
            first.Id,
            "portrait",
            insertAfter: false);

        Assert.NotNull(edit);
        var editedFirst = edit.Draft.Sections.Single(section => section.Id == first.Id);
        var editedSecond = edit.Draft.Sections.Single(section => section.Id == second.Id);
        Assert.Equal(1, editedFirst.Portrait!.Order);
        Assert.Equal(0, editedSecond.Portrait!.Order);
        Assert.Equal(0, editedFirst.Landscape!.Order);
        Assert.Equal(1, editedSecond.Landscape!.Order);
    }

    [Theory]
    [InlineData("first", "second", false)]
    [InlineData("second", "first", true)]
    [InlineData("third", "second", true)]
    public void LiveLibraryOrderResolvesToTheNearestPersistedNeighbor(
        string draggedScreenId,
        string expectedTarget,
        bool expectedInsertAfter)
    {
        var destination = CustomScreenLibraryController.ResolveOrderPersistence(
            ["first", "second", "third"],
            draggedScreenId);

        Assert.NotNull(destination);
        Assert.Equal(expectedTarget, destination.Value.TargetScreenId);
        Assert.Equal(expectedInsertAfter, destination.Value.InsertAfter);
    }

    [Fact]
    public void EnablingOrientationLayoutsCopiesCurrentGeometry()
    {
        var draft = CustomScreenService.CreateDraft();
        var section = draft.Sections[0] with { WidthColumns = 6 };
        var button = section.Buttons[0] with { Size = "wide", Row = 2 };
        draft = draft with
        {
            Sections = [section with { Buttons = [button] }]
        };

        var enabled = CustomScreenOrientationEditing.Enable(draft);
        var enabledSection = Assert.Single(enabled.Sections);
        var enabledButton = Assert.Single(enabledSection.Buttons);

        Assert.True(enabled.OrientationLayoutsEnabled);
        Assert.Equal(6, enabledSection.Portrait!.WidthColumns);
        Assert.Equal(6, enabledSection.Landscape!.WidthColumns);
        Assert.Equal("wide", enabledButton.Portrait!.Size);
        Assert.Equal("wide", enabledButton.Landscape!.Size);
        Assert.Equal(2, enabledButton.Portrait.Row);
        Assert.Equal(2, enabledButton.Landscape.Row);
        Assert.Equal(button.Action, enabledButton.Action);
    }

    [Fact]
    public void ComponentsCreatedAfterEnablingLayoutsStartOnlyInActiveOrientation()
    {
        var enabled = CustomScreenOrientationEditing.Enable(
            CustomScreenService.CreateDraft());
        var sectionEdit = CustomScreenPreviewDraftEditing.CreateSection(
            enabled,
            "buttons",
            targetSectionId: null,
            insertAfter: true,
            "portrait");
        var createdSection = sectionEdit.Draft.Sections.Single(section =>
            section.Id == sectionEdit.SelectedSectionId);
        Assert.True(createdSection.Portrait!.Visible);
        Assert.False(createdSection.Landscape!.Visible);

        var targetSection = sectionEdit.Draft.Sections[0];
        var buttonEdit = CustomScreenPreviewDraftEditing.CreateButton(
            sectionEdit.Draft,
            targetSection.Id,
            targetRow: 2,
            targetButtonId: null,
            insertAfter: true,
            "landscape");
        Assert.NotNull(buttonEdit);
        var createdButton = buttonEdit.Draft.Sections[0].Buttons.Single(button =>
            button.Id == buttonEdit.SelectedButtonId);
        Assert.False(createdButton.Portrait!.Visible);
        Assert.True(createdButton.Landscape!.Visible);
        Assert.Equal(2, createdButton.Landscape.Row);
    }

    [Fact]
    public void DroppingANewButtonOnOpenCanvasCreatesItsPanelAndButton()
    {
        var draft = CustomScreenService.CreateDraft();

        var edit = CustomScreenPreviewDraftEditing.CreatePanelForDroppedButton(
            draft,
            existingButtonId: null,
            "portrait");

        Assert.NotNull(edit);
        Assert.Equal(draft.Sections.Count + 1, edit.Draft.Sections.Count);
        var panel = edit.Draft.Sections.Single(section =>
            section.Id == edit.SelectedSectionId);
        Assert.Equal("buttons", panel.Kind);
        var button = Assert.Single(panel.Buttons);
        Assert.Equal(edit.SelectedButtonId, button.Id);
        Assert.Equal("New button", button.Name);
    }

    [Fact]
    public void DroppingALaserPointerCreatesAConfiguredNormalButton()
    {
        var draft = CustomScreenService.CreateDraft();

        var edit = CustomScreenPreviewDraftEditing.CreatePanelForDroppedButton(
            draft,
            existingButtonId: null,
            "portrait",
            laserPointer: true);

        Assert.NotNull(edit);
        var panel = edit.Draft.Sections.Single(section =>
            section.Id == edit.SelectedSectionId);
        var button = Assert.Single(panel.Buttons);
        Assert.Equal("Laser pointer", button.Name);
        Assert.Equal("Laser pointer", button.Label);
        Assert.Equal("mouse-pointer-2", button.Icon);
        Assert.Equal("iconLabel", button.Presentation);
        Assert.Equal("standard", button.Size);
        Assert.False(button.Repeat);
        Assert.Equal("laserPointer", button.Action.Kind);
        Assert.Equal("default", button.Action.Color);
    }

    [Fact]
    public void DroppingAnExistingButtonOnOpenCanvasCreatesAPanelAndMovesIt()
    {
        var draft = CustomScreenService.CreateDraft();
        var sourcePanel = draft.Sections[0];
        var movedButton = sourcePanel.Buttons[0];

        var edit = CustomScreenPreviewDraftEditing.CreatePanelForDroppedButton(
            draft,
            movedButton.Id,
            "portrait");

        Assert.NotNull(edit);
        Assert.Equal(draft.Sections.Count + 1, edit.Draft.Sections.Count);
        Assert.DoesNotContain(
            edit.Draft.Sections.Single(section => section.Id == sourcePanel.Id).Buttons,
            button => button.Id == movedButton.Id);
        var panel = edit.Draft.Sections.Single(section =>
            section.Id == edit.SelectedSectionId);
        Assert.Same(
            panel.Buttons.Single(button => button.Id == movedButton.Id),
            panel.Buttons[0]);
        Assert.Equal(
            draft.Sections.Sum(section => section.Buttons.Count),
            edit.Draft.Sections.Sum(section => section.Buttons.Count));
    }

    [Fact]
    public void HidingAndRestoringAComponentChangesOnlySelectedOrientation()
    {
        var enabled = CustomScreenOrientationEditing.Enable(
            CustomScreenService.CreateDraft());
        var section = enabled.Sections[0];
        var button = section.Buttons[0];

        var hidden = CustomScreenOrientationEditing.HideComponent(
            enabled,
            section.Id,
            button.Id,
            "landscape");
        var hiddenButton = hidden.Sections[0].Buttons[0];
        Assert.True(hiddenButton.Portrait!.Visible);
        Assert.False(hiddenButton.Landscape!.Visible);

        var restored = CustomScreenOrientationEditing.SetComponentVisibility(
            hidden,
            section.Id,
            button.Id,
            "landscape",
            visible: true);
        var restoredButton = restored.Sections[0].Buttons[0];
        Assert.True(restoredButton.Portrait!.Visible);
        Assert.True(restoredButton.Landscape!.Visible);
    }

    [Fact]
    public void RestoringAHiddenPanelAlsoRestoresItsChildrenInThatOrientation()
    {
        var enabled = CustomScreenOrientationEditing.Enable(
            CustomScreenService.CreateDraft());
        var section = enabled.Sections[0];
        var individuallyHidden = CustomScreenOrientationEditing.HideComponent(
            enabled,
            section.Id,
            section.Buttons[0].Id,
            "landscape");
        var panelHidden = CustomScreenOrientationEditing.HideComponent(
            individuallyHidden,
            section.Id,
            buttonId: null,
            "landscape");

        var restored = CustomScreenOrientationEditing.SetComponentVisibility(
            panelHidden,
            section.Id,
            buttonId: null,
            "landscape",
            visible: true);

        var restoredSection = restored.Sections[0];
        Assert.True(restoredSection.Landscape!.Visible);
        Assert.All(restoredSection.Buttons, button =>
            Assert.True(button.Landscape!.Visible));
        Assert.All(restoredSection.Buttons, button =>
            Assert.True(button.Portrait!.Visible));
    }

    [Fact]
    public void MovingAButtonToAnotherRowChangesOnlySelectedOrientation()
    {
        var draft = CustomScreenService.CreateDraft();
        var section = draft.Sections[0] with { RowLimit = 2 };
        var enabled = CustomScreenOrientationEditing.Enable(draft with
        {
            Sections = [section]
        });
        var dragged = enabled.Sections[0].Buttons[0];
        var target = enabled.Sections[0].Buttons[1];

        var moved = CustomScreenOrientationEditing.ReorderButton(
            enabled,
            dragged.Id,
            section.Id,
            target.Id,
            insertAfter: true,
            targetVisualRow: 2,
            "landscape");
        var movedButton = moved.Sections[0].Buttons.Single(button =>
            button.Id == dragged.Id);

        Assert.Equal(0, movedButton.Row);
        Assert.Equal(0, movedButton.Portrait!.Row);
        Assert.Equal(2, movedButton.Landscape!.Row);
    }

    [Fact]
    public void ReducingRowLimitNormalizesSharedAndOrientationRows()
    {
        var draft = CustomScreenService.CreateDraft();
        var section = draft.Sections[0] with { RowLimit = 3 };
        var enabled = CustomScreenOrientationEditing.Enable(draft with
        {
            Sections = [section]
        });
        var button = enabled.Sections[0].Buttons[0] with
        {
            Row = 3,
            Portrait = enabled.Sections[0].Buttons[0].Portrait! with { Row = 2 },
            Landscape = enabled.Sections[0].Buttons[0].Landscape! with { Row = 3 }
        };

        var normalized = CustomScreenOrientationEditing.SetRowLimit(
            enabled.Sections[0] with { Buttons = [button] },
            rowLimit: 1);
        var normalizedButton = Assert.Single(normalized.Buttons);

        Assert.Equal(1, normalized.RowLimit);
        Assert.Equal(0, normalizedButton.Row);
        Assert.Equal(0, normalizedButton.Portrait!.Row);
        Assert.Equal(0, normalizedButton.Landscape!.Row);
    }

    [Fact]
    public void OrientationMoveChangesOnlyTheSelectedOrientation()
    {
        var first = CustomScreenService.CreateDraft();
        var secondSection = CustomScreenService.CreateSection(first).Sections[^1];
        var enabled = CustomScreenOrientationEditing.Enable(first with
        {
            Sections = [first.Sections[0], secondSection]
        });

        var moved = CustomScreenOrientationEditing.MoveSection(
            enabled,
            secondSection.Id,
            -1,
            "portrait");

        var firstSection = moved.Sections.First(section => section.Id == first.Sections[0].Id);
        var movedSection = moved.Sections.First(section => section.Id == secondSection.Id);
        Assert.Equal(1, firstSection.Portrait!.Order);
        Assert.Equal(0, movedSection.Portrait!.Order);
        Assert.Equal(0, firstSection.Landscape!.Order);
        Assert.Equal(1, movedSection.Landscape!.Order);
    }
}
