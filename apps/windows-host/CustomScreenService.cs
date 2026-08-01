using System.Text.Json;

namespace VolturaAir.Host;

public sealed class CustomScreenService
{
    private readonly Lock _gate = new();
    private readonly ICustomScreenStore _store;
    private readonly IAppLaunchService _appLaunchService;
    private readonly CustomScreenMobileProjection _mobileProjection;
    private List<CustomScreenDefinition> _screens;
    private string _catalogRevision = NewRevision();
    private string? _loadError;

    public CustomScreenService(ICustomScreenStore store, IAppLaunchService appLaunchService)
    {
        _store = store;
        _appLaunchService = appLaunchService;
        _mobileProjection = new(appLaunchService);
        var loaded = store.Load();
        _screens = [.. loaded.Screens];
        _loadError = loaded.Error;
    }

    public event EventHandler? Changed;

    public string? LoadError
    {
        get { lock (_gate) return _loadError; }
    }

    public string CatalogRevision
    {
        get { lock (_gate) return _catalogRevision; }
    }

    public IReadOnlyList<CustomScreenDefinition> GetAll()
    {
        lock (_gate)
        {
            return [.. _screens];
        }
    }

    public IReadOnlyList<CustomScreenSummary> GetAssignedSummaries(string clientId)
    {
        lock (_gate)
        {
            return [.. _screens
                .Where(screen => screen.AssignedClientIds.Contains(clientId, StringComparer.Ordinal))
                .Select(screen => new CustomScreenSummary(screen.Id, screen.Name, screen.Revision))];
        }
    }

    public CustomScreenDefinition? Find(string screenId)
    {
        lock (_gate)
        {
            return _screens.FirstOrDefault(screen => string.Equals(screen.Id, screenId, StringComparison.Ordinal));
        }
    }

    public IReadOnlyList<AppLaunchActionSummary> GetApprovedAppActions() =>
        _appLaunchService.GetActions();

    public static CustomScreenDefinition CreateDraft() =>
        CustomScreenDraftFactory.CreateDraft();

    public static CustomScreenDefinition CreateSection(CustomScreenDefinition screen) =>
        CustomScreenDraftFactory.CreateSection(screen);

    public static CustomScreenDefinition CreateCollapsibleSection(
        CustomScreenDefinition screen) =>
        CustomScreenDraftFactory.CreateCollapsibleSection(screen);

    public static CustomScreenDefinition CreateButton(
        CustomScreenDefinition screen,
        string sectionId,
        int row = 0)
        => CustomScreenDraftFactory.CreateButton(screen, sectionId, row);

    public static CustomScreenDefinition CreateTrackpad(CustomScreenDefinition screen) =>
        CustomScreenDraftFactory.CreateTrackpad(screen);

    public static CustomScreenDefinition CreateCollapsibleTrackpad(
        CustomScreenDefinition screen) =>
        CustomScreenDraftFactory.CreateCollapsibleTrackpad(screen);

    public static CustomScreenDefinition CreateVolumeSlider(
        CustomScreenDefinition screen) =>
        CustomScreenDraftFactory.CreateVolumeSlider(screen);

    public static CustomScreenDefinition CreateNavigationRing(
        CustomScreenDefinition screen) =>
        CustomScreenDraftFactory.CreateNavigationRing(screen);

    public bool TrySave(CustomScreenDefinition draft, out CustomScreenDefinition saved, out string error)
    {
        var candidate = draft with { Revision = NewRevision() };
        saved = candidate;
        if (!CustomScreenValidator.TryValidate(candidate, out error) ||
            !FitsProtocolEnvelope(candidate, out error))
        {
            return false;
        }

        lock (_gate)
        {
            if (_loadError is not null)
            {
                error = _loadError;
                return false;
            }

            var next = _screens.ToList();
            var index = next.FindIndex(screen => string.Equals(screen.Id, candidate.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                next[index] = candidate;
            }
            else
            {
                next.Add(candidate);
            }

            if (!_store.TrySave(next, out error))
            {
                return false;
            }

            _screens = next;
            _catalogRevision = NewRevision();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool TryDelete(string screenId, out string error)
    {
        lock (_gate)
        {
            var next = _screens.Where(screen => !string.Equals(screen.Id, screenId, StringComparison.Ordinal)).ToList();
            if (next.Count == _screens.Count)
            {
                error = "The custom screen no longer exists.";
                return false;
            }

            if (!_store.TrySave(next, out error))
            {
                return false;
            }

            _screens = next;
            _catalogRevision = NewRevision();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool TryDuplicate(string screenId, out CustomScreenDefinition? duplicate, out string error)
    {
        var source = Find(screenId);
        if (source is null)
        {
            duplicate = null;
            error = "The custom screen no longer exists.";
            return false;
        }

        duplicate = CustomScreenDraftFactory.CloneWithNewIds(source) with
        {
            Name = DuplicateName(source.Name),
            AssignedClientIds = []
        };
        return TrySave(duplicate, out duplicate, out error);
    }

    public bool TryMove(string screenId, int direction, out string error)
    {
        lock (_gate)
        {
            var index = _screens.FindIndex(screen => string.Equals(screen.Id, screenId, StringComparison.Ordinal));
            var target = index + Math.Sign(direction);
            if (index < 0 || target < 0 || target >= _screens.Count)
            {
                error = string.Empty;
                return index >= 0;
            }

            var next = _screens.ToList();
            (next[index], next[target]) = (next[target], next[index]);
            if (!_store.TrySave(next, out error))
            {
                return false;
            }

            _screens = next;
            _catalogRevision = NewRevision();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool TryReorder(
        string screenId,
        string targetScreenId,
        bool insertAfter,
        out string error)
    {
        if (string.Equals(screenId, targetScreenId, StringComparison.Ordinal))
        {
            error = string.Empty;
            return true;
        }

        lock (_gate)
        {
            var sourceIndex = _screens.FindIndex(
                screen => string.Equals(screen.Id, screenId, StringComparison.Ordinal));
            var targetIndex = _screens.FindIndex(
                screen => string.Equals(screen.Id, targetScreenId, StringComparison.Ordinal));
            if (sourceIndex < 0 || targetIndex < 0)
            {
                error = "The custom screen no longer exists.";
                return false;
            }

            var next = _screens.ToList();
            var moving = next[sourceIndex];
            next.RemoveAt(sourceIndex);
            targetIndex = next.FindIndex(
                screen => string.Equals(screen.Id, targetScreenId, StringComparison.Ordinal));
            next.Insert(targetIndex + (insertAfter ? 1 : 0), moving);

            if (next.Select(screen => screen.Id).SequenceEqual(
                _screens.Select(screen => screen.Id),
                StringComparer.Ordinal))
            {
                error = string.Empty;
                return true;
            }

            if (!_store.TrySave(next, out error))
            {
                return false;
            }

            _screens = next;
            _catalogRevision = NewRevision();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool TryAssign(string screenId, IReadOnlyCollection<string> clientIds, out string error)
    {
        var existing = Find(screenId);
        if (existing is null)
        {
            error = "The custom screen no longer exists.";
            return false;
        }

        return TrySave(
            existing with { AssignedClientIds = [.. clientIds.Distinct(StringComparer.Ordinal)] },
            out _,
            out error);
    }

    public void RemoveDeviceAssignments(string clientId)
    {
        bool changed;
        lock (_gate)
        {
            var next = _screens.Select(screen => screen.AssignedClientIds.Contains(clientId, StringComparer.Ordinal)
                ? screen with
                {
                    AssignedClientIds = [.. screen.AssignedClientIds.Where(id =>
                        !string.Equals(id, clientId, StringComparison.Ordinal))]
                }
                : screen).ToList();
            changed = next.Where((screen, index) => !ReferenceEquals(screen, _screens[index])).Any();
            if (!changed || !_store.TrySave(next, out _))
            {
                return;
            }

            _screens = next;
            _catalogRevision = NewRevision();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveAssignmentsExcept(IReadOnlyCollection<string> validClientIds)
    {
        var valid = new HashSet<string>(validClientIds, StringComparer.Ordinal);
        bool changed;
        lock (_gate)
        {
            var next = _screens.Select(screen =>
            {
                var assignments = screen.AssignedClientIds.Where(valid.Contains).ToArray();
                return assignments.Length == screen.AssignedClientIds.Count
                    ? screen
                    : screen with { AssignedClientIds = assignments };
            }).ToList();
            changed = next.Where((screen, index) => !ReferenceEquals(screen, _screens[index])).Any();
            if (!changed || !_store.TrySave(next, out _))
            {
                return;
            }

            _screens = next;
            _catalogRevision = NewRevision();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public CustomScreenMobileDefinition? GetMobileDefinition(
        string clientId,
        string screenId,
        bool canUseRemoteInput,
        bool canLaunchApps,
        bool canControlVolume = true)
    {
        CustomScreenDefinition? screen;
        lock (_gate)
        {
            screen = _screens.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, screenId, StringComparison.Ordinal) &&
                candidate.AssignedClientIds.Contains(clientId, StringComparer.Ordinal));
        }

        return screen is null
            ? null
            : _mobileProjection.ToMobile(
                screen,
                canUseRemoteInput,
                canLaunchApps,
                canControlVolume);
    }

    public CustomScreenMobileDefinition? GetPreviewDefinition(string screenId)
    {
        var screen = Find(screenId);
        return screen is null
            ? null
            : _mobileProjection.ToMobile(
                screen,
                canUseRemoteInput: true,
                canLaunchApps: true,
                canControlVolume: true);
    }

    private static string DuplicateName(string sourceName)
    {
        const string suffix = " copy";
        var available = CustomScreenLimits.MaxScreenNameLength - suffix.Length;
        var baseName = sourceName.Length <= available
            ? sourceName
            : sourceName[..available].TrimEnd();
        return $"{baseName}{suffix}";
    }

    public static bool IsRepeatable(CustomScreenAction action) =>
        action.Kind == "builtIn" && CustomScreenBuiltIns.Find(action.BuiltIn)?.Repeatable == true;

    public static bool RequiresLabelOnlyPresentation(CustomScreenAction action) =>
        action.Kind is "text" or "shortcut";

    private static bool FitsProtocolEnvelope(CustomScreenDefinition screen, out string error)
    {
        var mobile = new CustomScreenMobileProjection(
            EmptyAppLaunchService.Instance)
            .ToMobile(
                screen,
                canUseRemoteInput: true,
                canLaunchApps: true,
                canControlVolume: true);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "custom.screen.get.result",
            operationId = new string('0', 64),
            succeeded = true,
            screen = mobile
        }, JsonOptions.Default);
        if (bytes.Length <= WebSocketTransport.MaxMessageBytes)
        {
            error = string.Empty;
            return true;
        }

        error = "This custom screen is too large to send to a paired device.";
        return false;
    }

    private static string NewRevision() => Guid.NewGuid().ToString("N");

    private sealed class EmptyAppLaunchService : IAppLaunchService
    {
        public static EmptyAppLaunchService Instance { get; } = new();

        public IReadOnlyList<AppLaunchActionSummary> GetActions() => [];

        public AppLaunchExecutionResult Execute(string actionId) =>
            new(false, "not-configured", "Application action unavailable.");

        public AppLaunchExecutionResult ExecutePowerPointFile(string path) =>
            new(false, "not-configured", "Application action unavailable.");
    }
}
