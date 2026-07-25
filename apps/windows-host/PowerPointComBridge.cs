using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace VolturaAir.Host;

internal sealed partial class PowerPointComBridge : IDisposable
{
    private const int PpSlideShowRunning = 1;
    private const int PpSlideShowPaused = 2;
    private const int PpSlideShowBlackScreen = 3;
    private const int PpSlideShowWhiteScreen = 4;
    private const int PpSlideShowPointerArrow = 1;
    private const int PpSlideShowPointerAutoArrow = 4;
    private const int MaxOpenPresentations = 32;
    private static readonly Guid PowerPointApplicationEvents =
        new("914934C2-5A91-11CF-8700-00AA0060263B");
    private static readonly int[] RefreshEventIds =
    [
        2004, // PresentationClose
        2006, // PresentationOpen
        2007, // NewPresentation
        2011, // SlideShowBegin
        2013, // SlideShowNextSlide
        2014  // SlideShowEnd
    ];

    private readonly Dictionary<string, string> _runtimeIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _stateBeforeBlank = new(StringComparer.Ordinal);
    private readonly HashSet<int> _attachedEventIds = [];
    private readonly Action<object> _refreshEvent;
    private readonly IWindowsWindowActivator _windowActivator;
    private object? _application;
    private bool _eventsAttached;

    internal string? LastFailureDiagnostic { get; private set; }

    internal PowerPointComBridge(
        Action requestRefresh,
        IWindowsWindowActivator? windowActivator = null)
    {
        _refreshEvent = _ => requestRefresh();
        _windowActivator = windowActivator ?? new WindowsWindowActivator();
    }

    internal bool TryAttach()
    {
        if (_application is not null)
        {
            return true;
        }

        var type = Type.GetTypeFromProgID("PowerPoint.Application");
        if (type is null || !TryGetActiveObject(type.GUID, out _application))
        {
            _application = null;
            return false;
        }

        AttachEvents();
        return true;
    }

    internal PowerPointAutomationSnapshot ReadSnapshot()
    {
        LastFailureDiagnostic = null;
        if (!TryAttach())
        {
            LastFailureDiagnostic = "PowerPoint active object was not found.";
            return PowerPointAutomationSnapshot.Unavailable;
        }

        object? presentations = null;
        object? slideShowWindows = null;
        var windowsByPresentation = new Dictionary<string, object>(
            StringComparer.OrdinalIgnoreCase);
        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshots = new List<PowerPointPresentationSnapshot>();
        var stage = "read application collections";
        try
        {
            presentations = GetProperty(_application!, "Presentations");
            slideShowWindows = GetProperty(_application!, "SlideShowWindows");
            stage = "index slideshow windows";
            IndexSlideShowWindows(slideShowWindows, windowsByPresentation);
            stage = "enumerate presentations";
            var count = GetIntProperty(presentations, "Count");
            for (var index = 1; index <= Math.Min(count, MaxOpenPresentations); index++)
            {
                object? presentation = null;
                try
                {
                    presentation = GetIndexedProperty(presentations, "Item", index);
                    var presentationKey = GetPresentationKey(presentation);
                    activeKeys.Add(presentationKey);
                    if (!_runtimeIds.TryGetValue(presentationKey, out var runtimeId))
                    {
                        runtimeId = Guid.NewGuid().ToString("N");
                        _runtimeIds.Add(presentationKey, runtimeId);
                    }

                    windowsByPresentation.TryGetValue(
                        presentationKey,
                        out var slideShowWindow);
                    stage = "read presentation";
                    snapshots.Add(ReadPresentation(
                        presentation,
                        slideShowWindow,
                        runtimeId));
                }
                finally
                {
                    ReleaseCom(presentation);
                }
            }

            foreach (var staleKey in _runtimeIds.Keys.Where(
                key => !activeKeys.Contains(key)).ToArray())
            {
                _stateBeforeBlank.Remove(_runtimeIds[staleKey]);
                _runtimeIds.Remove(staleKey);
            }

            return new(
                PowerPointDiscoveryState.Ready,
                [.. snapshots.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)]);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            LastFailureDiagnostic = $"{stage}: {FormatDiagnostic(exception)}";
            return new(PowerPointDiscoveryState.Inaccessible, []);
        }
        catch (Exception exception) when (IsAutomationFailure(exception))
        {
            LastFailureDiagnostic = $"{stage}: {FormatDiagnostic(exception)}";
            Detach();
            return PowerPointAutomationSnapshot.Unavailable;
        }
        finally
        {
            foreach (var window in windowsByPresentation.Values)
            {
                ReleaseCom(window);
            }

            ReleaseCom(slideShowWindows);
            ReleaseCom(presentations);
        }
    }

    internal PowerPointAutomationResult Execute(PowerPointCommand command)
    {
        if (command.Action == "open")
        {
            return OpenPresentation(command.SourcePath);
        }

        var snapshot = ReadSnapshot();
        if (snapshot.State != PowerPointDiscoveryState.Ready)
        {
            return Failure(
                snapshot.State == PowerPointDiscoveryState.Inaccessible
                    ? "powerpoint-inaccessible"
                    : "powerpoint-unavailable",
                snapshot.State == PowerPointDiscoveryState.Inaccessible
                    ? "PowerPoint denied access to its open presentations."
                    : "PowerPoint is not available. Open it on the PC and refresh.",
                snapshot);
        }

        var selected = ResolveSelection(snapshot, command.RuntimePresentationId);
        if (selected is null)
        {
            return snapshot.Presentations.Count > 1 && string.IsNullOrEmpty(command.RuntimePresentationId)
                ? Failure(
                    "powerpoint-selection-required",
                    "Choose which open PowerPoint presentation to control.",
                    snapshot)
                : Failure(
                    "powerpoint-target-stale",
                    "That PowerPoint presentation is no longer open. Refresh and choose another.",
                    snapshot);
        }

        object? presentation = null;
        try
        {
            presentation = FindPresentation(selected.RuntimePresentationId);
            if (presentation is null)
            {
                return Failure(
                    "powerpoint-target-stale",
                    "That PowerPoint presentation is no longer open. Refresh and choose another.",
                    ReadSnapshot());
            }

            ExecuteCore(presentation, selected, command);
            if (command.Action == "pointer")
            {
                return new(
                    true,
                    null,
                    ResultMessage(command.Action),
                    snapshot,
                    selected);
            }

            var updated = ReadSnapshot();
            var updatedPresentation = updated.Presentations.FirstOrDefault(
                item => string.Equals(
                    item.RuntimePresentationId,
                    selected.RuntimePresentationId,
                    StringComparison.Ordinal));
            return new(
                true,
                null,
                ResultMessage(command.Action),
                updated,
                updatedPresentation);
        }
        catch (PowerPointCommandException exception)
        {
            var failure = Failure(
                exception.Code,
                exception.Message,
                command.Action == "pointer" ? snapshot : ReadSnapshot());
            LastFailureDiagnostic = $"{exception.Code}: {exception.Message}";
            return failure;
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            var failure = Failure(
                "powerpoint-inaccessible",
                "PowerPoint denied access to that presentation.",
                command.Action == "pointer" ? snapshot : ReadSnapshot());
            LastFailureDiagnostic = FormatDiagnostic(exception);
            return failure;
        }
        catch (Exception exception) when (IsAutomationFailure(exception))
        {
            var failure = Failure(
                "powerpoint-automation-failed",
                command.Action == "activate"
                    ? "PowerPoint is open, but Voltura Air could not bring its window forward."
                    : $"PowerPoint could not complete the {CommandLabel(command.Action)} command.",
                command.Action == "pointer" ? snapshot : ReadSnapshot());
            LastFailureDiagnostic = FormatDiagnostic(exception);
            return failure;
        }
        finally
        {
            ReleaseCom(presentation);
        }
    }

    public void Dispose()
    {
        Detach();
        _runtimeIds.Clear();
        _stateBeforeBlank.Clear();
    }

    private static void IndexSlideShowWindows(
        object slideShowWindows,
        IDictionary<string, object> windowsByPresentation)
    {
        var count = GetIntProperty(slideShowWindows, "Count");
        for (var index = 1; index <= count; index++)
        {
            object? window = null;
            object? presentation = null;
            try
            {
                window = Invoke(slideShowWindows, "Item", index);
                presentation = GetProperty(window, "Presentation");
                var presentationKey = GetPresentationKey(presentation);
                if (!windowsByPresentation.TryAdd(presentationKey, window))
                {
                    ReleaseCom(window);
                }

                window = null;
            }
            finally
            {
                ReleaseCom(presentation);
                ReleaseCom(window);
            }
        }
    }

    private static PowerPointPresentationSnapshot ReadPresentation(
        object presentation,
        object? slideShowWindow,
        string runtimeId)
    {
        var name = NormalizeName(GetStringProperty(presentation, "Name"));
        var sourcePath = NormalizeSourcePath(GetStringProperty(presentation, "FullName"));
        object? slides = null;
        object? view = null;
        object? slide = null;
        try
        {
            slides = GetProperty(presentation, "Slides");
            var slideCount = GetIntProperty(slides, "Count");
            if (slideShowWindow is null)
            {
                return new(
                    runtimeId,
                    name,
                    false,
                    slideCount,
                    TryReadEditorSlide(presentation),
                    null,
                    "ready",
                    sourcePath);
            }

            view = GetProperty(slideShowWindow, "View");
            slide = TryGetProperty(view, "Slide");
            return new(
                runtimeId,
                name,
                true,
                slideCount,
                slide is null ? null : GetIntProperty(slide, "SlideIndex"),
                TryGetIntProperty(view, "CurrentShowPosition"),
                ToProtocolState(GetIntProperty(view, "State")),
                sourcePath);
        }
        finally
        {
            ReleaseCom(slide);
            ReleaseCom(view);
            ReleaseCom(slides);
        }
    }

    private PowerPointAutomationResult OpenPresentation(string? sourcePath)
    {
        var path = NormalizeSourcePath(sourcePath);
        if (path is null || !File.Exists(path))
        {
            return Failure(
                "powerpoint-source-missing",
                "The tracked PowerPoint file is no longer available.",
                ReadSnapshot());
        }

        var before = ReadSnapshot();
        if (before.State != PowerPointDiscoveryState.Ready)
        {
            return Failure(
                "powerpoint-unavailable",
                "PowerPoint is not available to reopen the tracked presentation.",
                before);
        }

        object? presentations = null;
        object? presentation = null;
        try
        {
            presentations = GetProperty(_application!, "Presentations");
            presentation = Invoke(presentations, "Open", path);
            var updated = ReadSnapshot();
            var opened = updated.Presentations.FirstOrDefault(item =>
                string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase));
            return opened is null
                ? Failure(
                    "powerpoint-open-failed",
                    "PowerPoint opened the file but Voltura Air could not identify it.",
                    updated)
                : new(true, null, "Tracked PowerPoint presentation reopened.", updated, opened);
        }
        catch (Exception exception) when (IsAutomationFailure(exception))
        {
            LastFailureDiagnostic = FormatDiagnostic(exception);
            return Failure(
                "powerpoint-open-failed",
                "PowerPoint could not reopen the tracked presentation.",
                ReadSnapshot());
        }
        finally
        {
            ReleaseCom(presentation);
            ReleaseCom(presentations);
        }
    }

    private void ExecuteCore(
        object presentation,
        PowerPointPresentationSnapshot selected,
        PowerPointCommand command)
    {
        if (command.Action is "start" or "start-current")
        {
            Start(presentation, command.Action == "start-current");
            return;
        }

        if (command.Action == "activate" && !selected.IsPresenting)
        {
            ActivatePresentationWindow(presentation);
            return;
        }

        object? slideShowWindow = null;
        object? view = null;
        try
        {
            slideShowWindow = FindSlideShowWindow(presentation);
            if (slideShowWindow is null)
            {
                throw new PowerPointCommandException(
                    "powerpoint-not-presenting",
                    "Start the selected PowerPoint slideshow before using that control.");
            }

            if (RequiresForegroundActivation(command))
            {
                ActivateSlideShowWindow(slideShowWindow, selected.Name);
            }

            if (command.Action == "activate") return;

            view = GetProperty(slideShowWindow, "View");
            switch (command.Action)
            {
                case "next":
                    Invoke(view, "Next");
                    break;
                case "previous":
                    Invoke(view, "Previous");
                    break;
                case "first":
                    Invoke(view, "First");
                    break;
                case "last":
                    Invoke(view, "Last");
                    break;
                case "goto":
                    GoToSlide(view, command.SlideNumber, selected.SlideCount);
                    break;
                case "black":
                    ToggleBlank(view, selected.RuntimePresentationId, PpSlideShowBlackScreen);
                    break;
                case "white":
                    ToggleBlank(view, selected.RuntimePresentationId, PpSlideShowWhiteScreen);
                    break;
                case "pause":
                    SetProperty(
                        view,
                        "State",
                        command.Enabled == true ? PpSlideShowPaused : PpSlideShowRunning);
                    break;
                case "pointer":
                    SetProperty(
                        view,
                        "PointerType",
                        command.Enabled == true
                            ? PpSlideShowPointerArrow
                            : PpSlideShowPointerAutoArrow);
                    break;
                case "end":
                    Invoke(view, "Exit");
                    _stateBeforeBlank.Remove(selected.RuntimePresentationId);
                    break;
                default:
                    throw new PowerPointCommandException(
                        "unsupported-action",
                        "That PowerPoint control is not supported.");
            }
        }
        finally
        {
            ReleaseCom(view);
            ReleaseCom(slideShowWindow);
        }
    }

    private object? FindSlideShowWindow(object presentation)
    {
        var expectedKey = GetPresentationKey(presentation);
        object? windows = null;
        try
        {
            windows = GetProperty(_application!, "SlideShowWindows");
            var count = GetIntProperty(windows, "Count");
            for (var index = 1; index <= count; index++)
            {
                object? window = null;
                object? candidatePresentation = null;
                try
                {
                    window = Invoke(windows, "Item", index);
                    candidatePresentation = GetProperty(window, "Presentation");
                    if (string.Equals(
                            GetPresentationKey(candidatePresentation),
                            expectedKey,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        var result = window;
                        window = null;
                        return result;
                    }
                }
                finally
                {
                    ReleaseCom(candidatePresentation);
                    ReleaseCom(window);
                }
            }

            return null;
        }
        finally
        {
            ReleaseCom(windows);
        }
    }

    private void Start(object presentation, bool fromCurrentSlide)
    {
        var currentSlide = fromCurrentSlide ? ReadEditorSlide(presentation) : null;
        object? settings = null;
        object? slideShowWindow = null;
        object? view = null;
        try
        {
            settings = GetProperty(presentation, "SlideShowSettings");
            slideShowWindow = Invoke(settings, "Run");
            ActivateSlideShowWindow(
                slideShowWindow,
                NormalizeName(GetStringProperty(presentation, "Name")));
            if (currentSlide is not null)
            {
                view = GetProperty(slideShowWindow, "View");
                Invoke(view, "GotoSlide", currentSlide.Value);
            }
        }
        finally
        {
            ReleaseCom(view);
            ReleaseCom(slideShowWindow);
            ReleaseCom(settings);
        }
    }

    private void ActivateSlideShowWindow(
        object slideShowWindow,
        string presentationName)
    {
        Invoke(slideShowWindow, "Activate");
        if (!_windowActivator.TryActivatePowerPointSlideShow(presentationName))
        {
            throw new PowerPointCommandException(
                "powerpoint-focus-failed",
                "Windows could not bring the selected PowerPoint slideshow to the foreground.");
        }
    }

    private void ActivatePresentationWindow(object presentation)
    {
        object? windows = null;
        object? window = null;
        try
        {
            windows = GetProperty(presentation, "Windows");
            if (GetIntProperty(windows, "Count") == 0)
            {
                throw new PowerPointCommandException(
                    "powerpoint-focus-failed",
                    "PowerPoint could not find the selected presentation window.");
            }

            window = GetIndexedProperty(windows, "Item", 1);
            Invoke(window, "Activate");
            var caption = GetStringProperty(window, "Caption");
            if (string.IsNullOrWhiteSpace(caption) ||
                !_windowActivator.TryBringPowerPointDocumentWindowForward(caption))
            {
                throw new PowerPointCommandException(
                    "powerpoint-focus-failed",
                    "PowerPoint is open, but Windows could not bring its window forward.");
            }
        }
        finally
        {
            ReleaseCom(window);
            ReleaseCom(windows);
        }
    }

    internal static bool RequiresForegroundActivation(PowerPointCommand command) =>
        command.Action != "pointer";

    private static int? ReadEditorSlide(object presentation)
    {
        object? windows = null;
        object? window = null;
        object? view = null;
        object? slide = null;
        try
        {
            windows = GetProperty(presentation, "Windows");
            if (GetIntProperty(windows, "Count") == 0)
            {
                throw new PowerPointCommandException(
                    "powerpoint-invalid-state",
                    "PowerPoint could not determine the current editor slide.");
            }

            window = GetIndexedProperty(windows, "Item", 1);
            view = GetProperty(window, "View");
            slide = GetProperty(view, "Slide");
            return GetIntProperty(slide, "SlideIndex");
        }
        finally
        {
            ReleaseCom(slide);
            ReleaseCom(view);
            ReleaseCom(window);
            ReleaseCom(windows);
        }
    }

    private static int? TryReadEditorSlide(object presentation)
    {
        try
        {
            return ReadEditorSlide(presentation);
        }
        catch (PowerPointCommandException)
        {
            return null;
        }
        catch (Exception exception) when (
            IsAccessFailure(exception) ||
            IsAutomationFailure(exception))
        {
            return null;
        }
    }

    private static void GoToSlide(object view, int? slideNumber, int slideCount)
    {
        if (slideNumber is null || slideNumber < 1 || slideNumber > slideCount)
        {
            throw new PowerPointCommandException(
                "powerpoint-invalid-slide",
                $"Enter a slide number from 1 to {slideCount}.");
        }

        Invoke(view, "GotoSlide", slideNumber.Value);
    }

    private void ToggleBlank(object view, string runtimeId, int requestedState)
    {
        var currentState = GetIntProperty(view, "State");
        var priorState = _stateBeforeBlank.GetValueOrDefault(runtimeId);
        var transition = ResolveBlankTransition(
            currentState,
            requestedState,
            _stateBeforeBlank.ContainsKey(runtimeId) ? priorState : null);
        if (transition.StateBeforeBlank is { } stateBeforeBlank)
        {
            _stateBeforeBlank[runtimeId] = stateBeforeBlank;
        }
        else
        {
            _stateBeforeBlank.Remove(runtimeId);
        }

        SetProperty(view, "State", transition.NextState);
    }

    internal static (int NextState, int? StateBeforeBlank) ResolveBlankTransition(
        int currentState,
        int requestedState,
        int? stateBeforeBlank)
    {
        if (currentState == requestedState)
        {
            return (stateBeforeBlank ?? PpSlideShowRunning, null);
        }

        if (currentState is PpSlideShowBlackScreen or PpSlideShowWhiteScreen)
        {
            return (requestedState, stateBeforeBlank ?? PpSlideShowRunning);
        }

        return (requestedState, currentState);
    }

    private object? FindPresentation(string runtimeId)
    {
        object? presentations = null;
        try
        {
            presentations = GetProperty(_application!, "Presentations");
            var count = GetIntProperty(presentations, "Count");
            for (var index = 1; index <= count; index++)
            {
                object? presentation = null;
                try
                {
                    presentation = GetIndexedProperty(presentations, "Item", index);
                    var presentationKey = GetPresentationKey(presentation);
                    if (_runtimeIds.TryGetValue(presentationKey, out var candidate) &&
                        string.Equals(candidate, runtimeId, StringComparison.Ordinal))
                    {
                        var match = presentation;
                        presentation = null;
                        return match;
                    }

                    ReleaseCom(presentation);
                    presentation = null;
                }
                finally
                {
                    ReleaseCom(presentation);
                }
            }

            return null;
        }
        finally
        {
            ReleaseCom(presentations);
        }
    }

    private static PowerPointPresentationSnapshot? ResolveSelection(
        PowerPointAutomationSnapshot snapshot,
        string? runtimeId)
    {
        if (!string.IsNullOrEmpty(runtimeId))
        {
            return snapshot.Presentations.FirstOrDefault(
                item => string.Equals(item.RuntimePresentationId, runtimeId, StringComparison.Ordinal));
        }

        return snapshot.Presentations.Count == 1 ? snapshot.Presentations[0] : null;
    }

    private void AttachEvents()
    {
        if (_application is null || _eventsAttached)
        {
            return;
        }

        try
        {
            foreach (var eventId in RefreshEventIds)
            {
                ComEventsHelper.Combine(
                    _application,
                    PowerPointApplicationEvents,
                    eventId,
                    _refreshEvent);
                _attachedEventIds.Add(eventId);
            }

            _eventsAttached = true;
        }
        catch (Exception exception) when (IsAutomationFailure(exception))
        {
            RemoveAttachedEvents();
            _eventsAttached = false;
        }
    }

    private void Detach()
    {
        if (_application is null)
        {
            return;
        }

        RemoveAttachedEvents();
        _eventsAttached = false;
        ReleaseCom(_application);
        _application = null;
    }

    private void RemoveAttachedEvents()
    {
        if (_application is null)
        {
            _attachedEventIds.Clear();
            return;
        }

        foreach (var eventId in _attachedEventIds.ToArray())
        {
            try
            {
                ComEventsHelper.Remove(
                    _application,
                    PowerPointApplicationEvents,
                    eventId,
                    _refreshEvent);
            }
            catch (Exception exception) when (IsAutomationFailure(exception))
            {
            }
        }

        _attachedEventIds.Clear();
    }

    private static bool TryGetActiveObject(Guid classId, out object? value)
    {
        value = null;
        var result = GetActiveObject(classId, nint.Zero, out var unknown);
        if (result < 0 || unknown == nint.Zero)
        {
            return false;
        }

        try
        {
            value = Marshal.GetObjectForIUnknown(unknown);
            return value is not null;
        }
        finally
        {
            _ = Marshal.Release(unknown);
        }
    }

    private static string GetPresentationKey(object presentation)
    {
        var sourcePath = NormalizeSourcePath(
            GetStringProperty(presentation, "FullName"));
        if (sourcePath is not null)
        {
            return $"path:{sourcePath}";
        }

        return $"unsaved:{NormalizeName(GetStringProperty(presentation, "Name"))}";
    }

    private static object GetProperty(object value, string name) =>
        value.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty,
            null,
            value,
            null,
            CultureInfo.InvariantCulture)
        ?? throw new InvalidOperationException($"PowerPoint returned no value for {name}.");

    private static object? TryGetProperty(object value, string name)
    {
        try
        {
            return value.GetType().InvokeMember(
                name,
                BindingFlags.GetProperty,
                null,
                value,
                null,
                CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (IsAutomationFailure(exception))
        {
            return null;
        }
    }

    private static object GetIndexedProperty(object value, string name, int index) =>
        value.GetType().InvokeMember(
            name,
            BindingFlags.InvokeMethod,
            null,
            value,
            [index],
            CultureInfo.InvariantCulture)
        ?? throw new InvalidOperationException($"PowerPoint returned no value for {name}.");

    private static object Invoke(object value, string name, params object[] arguments) =>
        value.GetType().InvokeMember(
            name,
            BindingFlags.InvokeMethod,
            null,
            value,
            arguments.Length == 0 ? null : arguments,
            CultureInfo.InvariantCulture)
        ?? value;

    private static void SetProperty(object value, string name, object propertyValue) =>
        value.GetType().InvokeMember(
            name,
            BindingFlags.SetProperty,
            null,
            value,
            [propertyValue],
            CultureInfo.InvariantCulture);

    private static int GetIntProperty(object value, string name) =>
        Convert.ToInt32(GetProperty(value, name), CultureInfo.InvariantCulture);

    private static int? TryGetIntProperty(object value, string name)
    {
        try
        {
            return GetIntProperty(value, name);
        }
        catch (Exception exception) when (IsAutomationFailure(exception))
        {
            return null;
        }
    }

    private static string GetStringProperty(object value, string name) =>
        Convert.ToString(GetProperty(value, name), CultureInfo.InvariantCulture) ?? string.Empty;

    private static string NormalizeName(string name)
    {
        var normalized = new string(
            [.. name.Where(character => !char.IsControl(character)).Take(120)]).Trim();
        return normalized.Length == 0 ? "Untitled presentation" : normalized;
    }

    private static string? NormalizeSourcePath(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !Path.IsPathFullyQualified(sourcePath))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(sourcePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string ToProtocolState(int state) => state switch
    {
        PpSlideShowPaused => "paused",
        PpSlideShowBlackScreen => "black",
        PpSlideShowWhiteScreen => "white",
        _ => "running"
    };

    private static string ResultMessage(string action) => action switch
    {
        "start" => "PowerPoint slideshow started.",
        "start-current" => "PowerPoint slideshow started from the current slide.",
        "next" => "Next slide shown.",
        "previous" => "Previous slide shown.",
        "first" => "First slide shown.",
        "last" => "Last slide shown.",
        "goto" => "Requested slide shown.",
        "black" => "PowerPoint black screen changed.",
        "white" => "PowerPoint white screen changed.",
        "pause" => "PowerPoint pause state changed.",
        "pointer" => "PowerPoint pointer visibility changed.",
        "activate" => "PowerPoint window brought forward.",
        "end" => "PowerPoint slideshow ended.",
        _ => "PowerPoint command completed."
    };

    private static string CommandLabel(string action) => action switch
    {
        "start" => "Start from beginning",
        "start-current" => "Start from current",
        "next" => "Next",
        "previous" => "Previous",
        "first" => "First",
        "last" => "Last",
        "goto" => "Go to slide",
        "black" => "Black screen",
        "white" => "White screen",
        "pause" => "Pause auto-play",
        "pointer" => "Laser pointer",
        "activate" => "Bring PowerPoint forward",
        "end" => "End slideshow",
        _ => action
    };

    private static PowerPointAutomationResult Failure(
        string code,
        string message,
        PowerPointAutomationSnapshot snapshot) =>
        new(false, code, message, snapshot);

    private static bool IsAutomationFailure(Exception exception) =>
        exception is COMException or InvalidComObjectException or InvalidOperationException or
            TargetInvocationException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException;

    private static bool IsAccessFailure(Exception exception) =>
        exception is UnauthorizedAccessException ||
        exception is COMException { HResult: unchecked((int)0x80070005) } ||
        exception is TargetInvocationException { InnerException: { } inner } &&
        IsAccessFailure(inner);

    private static string FormatDiagnostic(Exception exception)
    {
        var root = exception.GetBaseException();
        return $"{root.GetType().Name} HRESULT=0x{root.HResult:X8}: {root.Message}";
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.ReleaseComObject(value);
        }
    }

    [LibraryImport("oleaut32.dll", EntryPoint = "GetActiveObject")]
    private static partial int GetActiveObject(
        in Guid classId,
        nint reserved,
        out nint unknown);

    private sealed class PowerPointCommandException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
