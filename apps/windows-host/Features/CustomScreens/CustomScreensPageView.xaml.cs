using System.Windows;
using System.Windows.Controls;
using System.Diagnostics.CodeAnalysis;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.CustomScreens;

public partial class CustomScreensPageView : UserControl
{
    private readonly Window _owner;
    private readonly CustomScreenService _service;
    private readonly Action<string> _showToast;
    private readonly Action _openCommunityLibrary;
    private readonly CustomScreenLibraryController _libraryController;
    private readonly CustomScreenPropertiesPanelController _propertiesController;
    private readonly CustomScreenPreviewController _previewController;
    private readonly CustomScreenPreviewDeviceController _previewDevices;
    private readonly CustomScreenDeleteConfirmationController _deleteConfirmations;
    private readonly CustomScreenEditorActivityLog _activityLog;
    private readonly CustomScreenComponentDeletionController _componentDeletion;
    private readonly CustomScreenComponentCreationController _componentCreation;
    private readonly CustomScreenComponentMovementController _movement;
    private readonly CustomScreenPaletteDragController _paletteDrag;
    private readonly CustomScreenHiddenControlsController _hiddenControls;
    private readonly CustomScreenEditorPreviewController _editorPreview;
    private readonly Func<CustomScreenDefinition, CancellationToken,
        Task<CustomScreenValidationReport>> _validateDraft;
    private readonly Stack<CustomScreenDefinition> _undo = new();
    private readonly Stack<CustomScreenDefinition> _redo = new();
    private CustomScreenDefinition? _draft;
    private string? _selectedSectionId;
    private string? _selectedButtonId;
    private int? _selectedRow;
    private bool _synchronizing;
    private bool _dirty;
    private bool _invalidDataPromptShown;
    internal CustomScreensPageView(
        Window owner,
        CustomScreenService service,
        PairingManager pairingManager,
        Action<string>? showToast = null,
        Func<string, UrlOpenExecutionResult>? openPreview = null,
        Func<string, CustomScreenViewport, bool, string?, UrlOpenExecutionResult>? openSizedPreview = null,
        CustomScreenEditorActivityLog? activityLog = null,
        Action? openCommunityLibrary = null,
        Func<CustomScreenDefinition, CancellationToken,
            Task<CustomScreenValidationReport>>? validateDraft = null)
    {
        _owner = owner;
        _service = service;
        _showToast = showToast ?? (static _ => { });
        _openCommunityLibrary = openCommunityLibrary ?? ProductWebsite.OpenCustomScreenLibrary;
        _activityLog = activityLog ?? new CustomScreenEditorActivityLog(NullAppLog.Instance);
        _validateDraft = validateDraft ?? ((draft, _) => Task.FromResult(
            CustomScreenValidationAnalyzer.Analyze(
                draft,
                service.GetKnownAppProfiles(),
                service.GetApprovedAppActions(),
                layoutIssues: null,
                layoutFailure: "The real mobile preview renderer is unavailable in this host context.")));
        var preview = openPreview ??
            (static _ => new(false, "preview-unavailable", "Preview is unavailable."));
        InitializeComponent();
        CustomScreenEditorColumnController.Attach(ComponentPaletteColumn,
            PropertiesPanelColumn, ComponentPaletteSplitter, PropertiesPanelSplitter);
        _deleteConfirmations = new(
            LibraryConfirmDeletesCheckBox,
            LibraryConfirmHidesCheckBox,
            EditorConfirmDeletesCheckBox,
            EditorConfirmHidesCheckBox);
        _hiddenControls = new(
            HiddenControlsRoot,
            HiddenControlsHint,
            HiddenControlsList,
            Brush,
            ShowHiddenComponent);
        _ = new CustomScreenLayoutSettingsController(
            OrientationLayoutsCheckBox, NavigationHeaderCheckBox,
            () => _draft, () => _synchronizing, ApplyDraft);
        _componentDeletion = new(
            owner,
            () => _draft,
            GetPreviewOrientation,
            DeleteComponent,
            _showToast);
        _componentCreation = new(
            () => _draft,
            () => _selectedSectionId,
            () => _selectedRow,
            GetPreviewOrientation,
            edit => ApplyPreviewDraft(
                edit.Draft,
                edit.SelectedSectionId,
                edit.SelectedButtonId,
                edit.SelectedRow));
        _movement = new(
            () => _draft,
            GetPreviewOrientation,
            ApplyDraft);
        _propertiesController = new CustomScreenPropertiesPanelController(
            PropertiesPanel,
            PropertiesHint,
            service,
            Brush,
            UpdateSection,
            UpdateButton,
            MoveSelectedSection,
            MoveSelectedButton,
            MoveButtonToSection,
            _componentDeletion.Request,
            _componentDeletion.RequestEverywhere);
        _libraryController = new CustomScreenLibraryController(
            owner,
            LibraryList,
            service,
            pairingManager,
            Brush,
            OpenEditor,
            preview,
            _activityLog,
            _showToast);
        _previewController = new CustomScreenPreviewController(
            PreviewSections,
            PreviewWorkspace,
            EditorRoot,
            Brush,
            SelectPreviewComponent,
            ApplyPreviewDraft,
            _componentDeletion.Request,
            _componentDeletion.RequestEverywhere);
        _paletteDrag = new(
            EditorRoot,
            PreviewWorkspace,
            SectionPaletteDragHandle,
            SectionPaletteItem,
            CollapsibleSectionPaletteDragHandle,
            CollapsibleSectionPaletteItem,
            ButtonPaletteDragHandle,
            ButtonPaletteItem,
            VolumePaletteDragHandle,
            VolumePaletteItem,
            TrackpadPaletteDragHandle,
            TrackpadPaletteItem,
            CollapsibleTrackpadPaletteDragHandle,
            CollapsibleTrackpadPaletteItem,
            NavigationRingPaletteDragHandle,
            NavigationRingPaletteItem,
            DPadPaletteDragHandle,
            DPadPaletteItem);
        _previewDevices = new CustomScreenPreviewDeviceController(
            PreviewDeviceCombo,
            PreviewOrientationCombo,
            DeviceFrame,
            pairingManager,
            RenderEditorOrientation);
        var sizedPreview = openSizedPreview ??
            ((string screenId, CustomScreenViewport _, bool _, string? _) =>
                preview(screenId));
        _editorPreview = new(
            owner, EditorPreviewButton, service,
            () => _draft, () => _dirty,
            screenId => sizedPreview(
                screenId,
                _previewDevices.Viewport,
                _previewDevices.ControlDepth,
                _previewDevices.ClientId),
            _activityLog, _showToast);
        _deleteConfirmations.Synchronize();
        _previewDevices.Load();
        RefreshLibrary();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_invalidDataPromptShown || _service.LoadError is not { } loadError)
        {
            return;
        }

        _invalidDataPromptShown = true;
        if (!ThemedConfirmationDialog.Show(
                _owner,
                "Invalid Custom Screens data",
                $"{loadError}\n\nDelete the invalid Custom Screens file and start with an empty library?",
                "Delete invalid file",
                "Keep file",
                ConfirmationTone.Warning))
        {
            return;
        }

        if (!_service.TryDeleteInvalidData(out var error))
        {
            ThemedConfirmationDialog.ShowInformation(
                _owner,
                "Invalid Custom Screens data",
                error,
                ConfirmationTone.Warning);
            return;
        }

        _showToast("Invalid Custom Screens file deleted");
        RefreshLibrary();
    }
    internal bool TryLeave()
    {
        if (!_dirty)
        {
            return true;
        }

        return ThemedConfirmationDialog.Show(
            _owner,
            "Discard custom-screen changes",
            "Discard the unsaved changes to this custom screen?",
            "Discard",
            "Keep editing",
            ConfirmationTone.Warning);
    }
    private void RefreshLibrary()
    {
        _deleteConfirmations.Synchronize();
        _libraryController.Refresh();
    }

    internal void ImportBytes(byte[] bytes) => _libraryController.ImportBytes(bytes);
    internal void OpenEditor(CustomScreenDefinition screen)
    {
        _draft = screen;
        _selectedSectionId = screen.Sections.Count == 0 ? null : screen.Sections[0].Id;
        _selectedButtonId = null;
        _selectedRow = null;
        _dirty = false;
        _undo.Clear();
        _redo.Clear();
        LibraryRoot.Visibility = Visibility.Collapsed;
        EditorRoot.Visibility = Visibility.Visible;
        SynchronizeEditor();
    }
    private void SynchronizeEditor()
    {
        if (_draft is null)
        {
            return;
        }

        _synchronizing = true;
        NormalizeSelection();
        ScreenNameInput.Text = _draft.Name;
        OrientationLayoutsCheckBox.IsChecked = _draft.OrientationLayoutsEnabled;
        NavigationHeaderCheckBox.IsChecked = _draft.ShowNavigationHeader;
        PreviewTitle.Text = _draft.Name;
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
        _editorPreview.Refresh();
        _synchronizing = false;
        RenderPreview();
        RenderProperties();
        RenderHiddenControls();
    }
    private void NormalizeSelection()
    {
        if (_draft is null)
        {
            return;
        }
        (_selectedSectionId, _selectedButtonId, _selectedRow) =
            CustomScreenSelection.Normalize(
                _draft,
                _selectedSectionId,
                _selectedButtonId,
                _selectedRow);
    }
    private void RenderPreview()
    {
        _previewController.Render(
            _draft,
            _selectedSectionId,
            _selectedButtonId,
            _selectedRow,
            _previewDevices.Orientation);
    }
    private void RenderEditorOrientation()
    {
        RenderPreview();
        RenderProperties();
        RenderHiddenControls();
    }
    private void SelectPreviewComponent(string sectionId, string? buttonId, int? row)
    {
        _selectedSectionId = sectionId;
        _selectedButtonId = buttonId;
        _selectedRow = row;
        RenderPreview();
        RenderProperties();
    }
    private void ApplyPreviewDraft(
        CustomScreenDefinition draft,
        string sectionId,
        string? buttonId,
        int? row)
    {
        _selectedSectionId = sectionId;
        _selectedButtonId = buttonId;
        _selectedRow = row;
        ApplyDraft(draft);
    }
    private void RenderProperties()
    {
        _propertiesController.Render(
            _draft,
            _selectedSectionId,
            _selectedButtonId,
            _selectedRow,
            _previewDevices.Orientation);
    }

    private void RenderHiddenControls()
        => _hiddenControls.Render(_draft, GetPreviewOrientation());

    private void ShowHiddenComponent(
        CustomScreenDefinition draft,
        string sectionId,
        string? buttonId)
    {
        _selectedSectionId = sectionId;
        _selectedButtonId = buttonId;
        _selectedRow = null;
        ApplyDraft(draft);
    }

    private void UpdateSection(CustomScreenSection updated)
    {
        ApplyDraft(_draft! with
        {
            Sections = [.. _draft!.Sections.Select(section => section.Id == updated.Id ? updated : section)]
        });
    }

    private void UpdateButton(CustomScreenButton updated)
    {
        var section = _draft!.Sections.First(item => item.Id == _selectedSectionId);
        var previous = section.Buttons.First(button => button.Id == updated.Id);
        var orientation = GetPreviewOrientation();
        var previousRow = _draft.OrientationLayoutsEnabled
            ? CustomScreenOrientationEditing.ButtonOverride(previous, orientation).Row ??
                previous.Row
            : previous.Row;
        var updatedRow = _draft.OrientationLayoutsEnabled
            ? CustomScreenOrientationEditing.ButtonOverride(updated, orientation).Row ??
                updated.Row
            : updated.Row;
        if (previousRow != updatedRow)
        {
            _selectedRow = updatedRow > 0 ? updatedRow : null;
        }
        UpdateSection(section with
        {
            Buttons = [.. section.Buttons.Select(button => button.Id == updated.Id ? updated : button)]
        });
    }

    private void ApplyDraft(CustomScreenDefinition next)
    {
        if (_draft is null || _synchronizing || Equals(_draft, next))
        {
            return;
        }

        _undo.Push(_draft);
        _redo.Clear();
        _draft = next;
        _dirty = true;
        SynchronizeEditor();
    }

    private void MoveSelectedSection(int direction)
        => _movement.MoveSection(_selectedSectionId, direction);

    private void MoveSelectedButton(int direction)
        => _movement.MoveButton(
            _selectedSectionId,
            _selectedButtonId,
            direction);

    private void MoveButtonToSection(string buttonId, string targetSectionId)
    {
        if (_draft is null)
        {
            return;
        }

        var edit = CustomScreenPreviewDraftEditing.MoveButtonToSection(
            _draft,
            buttonId,
            targetSectionId,
            targetRow: null);
        if (edit is null)
        {
            return;
        }

        ApplyPreviewDraft(
            edit.Draft,
            edit.SelectedSectionId,
            edit.SelectedButtonId,
            edit.SelectedRow);
    }

    private void DeleteSelectedComponent(bool deleteEverywhere)
    {
        if (_draft is null || _selectedSectionId is null)
        {
            return;
        }

        var buttonId = _selectedButtonId;
        var next = CustomScreenComponentDeletionEditing.Delete(
            _draft,
            _selectedSectionId,
            buttonId,
            GetPreviewOrientation(),
            deleteEverywhere);
        _selectedButtonId = null;
        if (buttonId is null)
        {
            _selectedSectionId = null;
            _selectedRow = null;
        }
        ApplyDraft(next);
    }

    private void DeleteComponent(
        string sectionId,
        string? buttonId,
        bool deleteEverywhere)
    {
        _selectedSectionId = sectionId;
        _selectedButtonId = buttonId;
        _selectedRow = null;
        DeleteSelectedComponent(deleteEverywhere);
    }
    private Brush Brush(string name) => (Brush)_owner.FindResource(name);

    private string GetPreviewOrientation() => _previewDevices.Orientation;

    private void OnNewScreen(object sender, RoutedEventArgs e) => OpenEditor(CustomScreenService.CreateDraft());

    private void OnImportScreen(object sender, RoutedEventArgs e) => _libraryController.Import();

    private void OnBrowseLibrary(object sender, RoutedEventArgs e) => _openCommunityLibrary();

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (!TryLeave())
        {
            return;
        }

        _dirty = false;
        _draft = null;
        EditorRoot.Visibility = Visibility.Collapsed;
        LibraryRoot.Visibility = Visibility.Visible;
        RefreshLibrary();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_draft is null)
        {
            return;
        }

        if (!_service.TrySave(_draft, out var saved, out var error))
        {
            _activityLog.Write("save", succeeded: false);
            MessageBox.Show(_owner, error, "Custom screens", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _activityLog.Write("save", succeeded: true);
        _draft = saved;
        _dirty = false;
        _undo.Clear();
        _redo.Clear();
        SynchronizeEditor();
        _showToast("Custom screen saved");
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This async WPF command boundary must convert every validator failure into themed feedback instead of terminating the host.")]
    private async void OnValidate(object sender, RoutedEventArgs e)
    {
        if (_draft is null || !ValidateButton.IsEnabled)
        {
            return;
        }

        var draft = _draft;
        ValidateButton.IsEnabled = false;
        ValidateButton.Content = "Validating…";
        try
        {
            var report = await _validateDraft(draft, CancellationToken.None);
            _activityLog.Write("validate", succeeded: true);
            var dialog = new CustomScreenValidationReportDialog(
                report,
                finding =>
                {
                    if (finding.SectionId is not null)
                    {
                        SelectPreviewComponent(
                            finding.SectionId,
                            finding.ButtonId,
                            row: null);
                    }
                })
            {
                Owner = _owner
            };
            _ = dialog.ShowDialog();
        }
        catch (Exception)
        {
            _activityLog.Write("validate", succeeded: false);
            ThemedConfirmationDialog.ShowInformation(
                _owner,
                "Custom Screen validation",
                "Validation could not finish. Try again; Save remains available.",
                ConfirmationTone.Warning);
        }
        finally
        {
            ValidateButton.Content = "Validate";
            ValidateButton.IsEnabled = true;
        }
    }

    private void OnScreenNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_draft is not null && !_synchronizing && !string.IsNullOrWhiteSpace(ScreenNameInput.Text))
        {
            ApplyDraft(_draft with { Name = ScreenNameInput.Text.Trim() });
        }
    }

    private void OnAddSection(object sender, RoutedEventArgs e) => _componentCreation.AddSection("buttons");

    private void OnAddButton(object sender, RoutedEventArgs e) => _componentCreation.AddButton();

    private void OnAddVolume(object sender, RoutedEventArgs e) => _componentCreation.AddSection("volume");

    private void OnAddCollapsibleSection(object sender, RoutedEventArgs e) => _componentCreation.AddSection("collapsible");

    private void OnAddTrackpad(object sender, RoutedEventArgs e) => _componentCreation.AddSection("trackpad");

    private void OnAddCollapsibleTrackpad(object sender, RoutedEventArgs e) => _componentCreation.AddSection("collapsibleTrackpad");

    private void OnAddNavigationRing(object sender, RoutedEventArgs e) => _componentCreation.AddSection("navigationRing");

    private void OnAddDPad(object sender, RoutedEventArgs e) => _componentCreation.AddSection("dpad");

    private void OnConfirmDeletesChanged(object sender, RoutedEventArgs e)
        => _deleteConfirmations?.HandleDeleteChanged(sender);

    private void OnConfirmHidesChanged(object sender, RoutedEventArgs e)
        => _deleteConfirmations?.HandleHideChanged(sender);

    private void OnCollapseAllProperties(object sender, RoutedEventArgs e) =>
        _propertiesController.SetAllExpanded(false);

    private void OnExpandAllProperties(object sender, RoutedEventArgs e) =>
        _propertiesController.SetAllExpanded(true);

    private void OnCollapseAllComponentSections(object sender, RoutedEventArgs e) =>
        SetAllComponentSectionsExpanded(false);

    private void OnExpandAllComponentSections(object sender, RoutedEventArgs e) =>
        SetAllComponentSectionsExpanded(true);

    private void SetAllComponentSectionsExpanded(bool expanded)
    {
        AvailableComponentsExpander.IsExpanded = expanded;
        LayoutOptionsExpander.IsExpanded = expanded;
        HiddenControlsRoot.IsExpanded = expanded;
        EditingOptionsExpander.IsExpanded = expanded;
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (_draft is null || _undo.Count == 0)
        {
            return;
        }
        _redo.Push(_draft);
        _draft = _undo.Pop();
        _dirty = true;
        SynchronizeEditor();
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        if (_draft is null || _redo.Count == 0)
        {
            return;
        }
        _undo.Push(_draft);
        _draft = _redo.Pop();
        _dirty = true;
        SynchronizeEditor();
    }

    private void OnPreviewDeviceChanged(object sender, SelectionChangedEventArgs e) =>
        _previewDevices?.ApplySize();

    private void OnPreviewOrientationChanged(object sender, SelectionChangedEventArgs e) =>
        _previewDevices?.ApplySize();
}
