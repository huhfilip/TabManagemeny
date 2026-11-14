namespace TabMan.Core;

/// <summary>
/// Owns switcher mode, search text, temporary assignments, and selection.
/// The overlay asks this object what to draw and which window to activate.
/// </summary>
public sealed class SwitcherSession
{
    private readonly Dictionary<long, TemporaryAssignment> _temporary = [];
    private List<WindowInfo> _windows = [];

    public SwitcherSession(AppSettings settings)
    {
        Settings = settings;
        Layout = SwitcherLayoutBuilder.Build(_windows, Settings, _temporary, Query);
        Selection = Selection.None;
    }

    public AppSettings Settings { get; private set; }
    public string Query { get; private set; } = "";
    public SwitcherLayout Layout { get; private set; }
    public Selection Selection { get; private set; }
    public bool IsOpen { get; private set; }

    public SwitcherItem? SelectedItem => NavigationEngine.Get(Layout, Selection);

    public IReadOnlyList<TemporaryAssignment> TemporaryAssignments => _temporary.Values.ToList();

    public void Open(IReadOnlyList<WindowInfo> windows)
    {
        _windows = windows.ToList();
        Query = "";
        IsOpen = true;
        PruneTemporary();
        Rebuild(selectInitial: true);
    }

    public void Close() => IsOpen = false;

    public void ReplaceWindows(IReadOnlyList<WindowInfo> windows)
    {
        var selected = SelectedItem?.Hwnd;
        _windows = windows.ToList();
        PruneTemporary();
        Rebuild(selectInitial: false);
        RestoreOrInitial(selected);
    }

    public void ApplySettings(AppSettings settings)
    {
        var selected = SelectedItem?.Hwnd;
        Settings = settings;
        DropMissingProjects();
        Rebuild(selectInitial: false);
        RestoreOrInitial(selected);
    }

    public void SetMode(SwitcherMode mode)
    {
        Settings = Settings with { LastMode = mode };
        Rebuild(selectInitial: true);
    }

    public void SetSortOrder(GroupSortOrder sortOrder)
    {
        var selected = SelectedItem?.Hwnd;
        Settings = Settings with { SortOrder = sortOrder };
        Rebuild(selectInitial: false);
        RestoreOrInitial(selected);
    }

    public void SetProjectScope(ProjectScopeMode scope, Guid? projectId)
    {
        Settings = Settings with
        {
            ProjectScope = scope,
            VisibleProjectId = scope == ProjectScopeMode.Single ? projectId : null
        };
        Rebuild(selectInitial: true);
    }

    public void SetQuery(string? query)
    {
        var selected = SelectedItem?.Hwnd;
        Query = query ?? "";
        Rebuild(selectInitial: false);
        RestoreOrInitial(selected);
    }

    public void Type(char character)
    {
        if (char.IsControl(character))
        {
            return;
        }

        SetQuery(Query + character);
    }

    public bool Backspace()
    {
        if (Query.Length == 0)
        {
            return false;
        }

        SetQuery(Query[..^1]);
        return true;
    }

    /// <summary>Returns true when the overlay should close without activating.</summary>
    public bool HandleEscape()
    {
        if (Query.Length > 0)
        {
            SetQuery("");
            return false;
        }

        IsOpen = false;
        return true;
    }

    public void Move(NavigationDirection direction) =>
        Selection = NavigationEngine.Move(Selection, direction, Layout);

    public bool SelectHwnd(long hwnd)
    {
        var found = NavigationEngine.Find(Layout, hwnd);
        if (found.IsNone)
        {
            return false;
        }

        Selection = found;
        return true;
    }

    public void AssignTemporary(long hwnd, bool appliesProject, Guid? projectId)
    {
        if (!TryGetWindow(hwnd, out var window))
        {
            return;
        }

        var current = GetLiveAssignment(window) ?? new TemporaryAssignment
        {
            Hwnd = window.Hwnd,
            ProcessId = window.ProcessId
        };
        _temporary[hwnd] = current with
        {
            AppliesProject = appliesProject,
            ProjectGroupId = appliesProject ? projectId : null
        };
        var selected = SelectedItem?.Hwnd;
        Rebuild(selectInitial: false);
        RestoreOrInitial(selected);
    }

    public void SetTemporaryColor(long hwnd, bool hasOverride, string? colorHex)
    {
        if (!TryGetWindow(hwnd, out var window))
        {
            return;
        }

        var current = GetLiveAssignment(window) ?? new TemporaryAssignment
        {
            Hwnd = window.Hwnd,
            ProcessId = window.ProcessId
        };
        _temporary[hwnd] = current with
        {
            HasColorOverride = hasOverride,
            ColorHex = hasOverride ? ColorValidator.Normalize(colorHex) : null
        };
        var selected = SelectedItem?.Hwnd;
        Rebuild(selectInitial: false);
        RestoreOrInitial(selected);
    }

    public void ClearTemporary(long hwnd)
    {
        _temporary.Remove(hwnd);
        var selected = SelectedItem?.Hwnd;
        Rebuild(selectInitial: false);
        RestoreOrInitial(selected);
    }

    private void Rebuild(bool selectInitial)
    {
        Layout = SwitcherLayoutBuilder.Build(_windows, Settings, _temporary, Query);
        Selection = selectInitial ? NavigationEngine.Initial(Layout) : Selection;
        if (!selectInitial)
        {
            RestoreOrInitial(SelectedItem?.Hwnd ?? (Selection.IsNone ? null : NavigationEngine.Get(Layout, Selection)?.Hwnd));
        }
    }

    private void RestoreOrInitial(long? hwnd)
    {
        if (hwnd is long handle)
        {
            var found = NavigationEngine.Find(Layout, handle);
            if (!found.IsNone)
            {
                Selection = found;
                return;
            }
        }

        Selection = NavigationEngine.Initial(Layout);
    }

    private void PruneTemporary()
    {
        var live = _windows.ToDictionary(window => window.Hwnd, window => window.ProcessId);
        foreach (var key in _temporary.Keys.ToList())
        {
            if (!live.TryGetValue(key, out var pid) || _temporary[key].ProcessId != pid)
            {
                _temporary.Remove(key);
            }
        }
    }

    private void DropMissingProjects()
    {
        foreach (var key in _temporary.Keys.ToList())
        {
            var assignment = _temporary[key];
            if (assignment.AppliesProject
                && assignment.ProjectGroupId is Guid projectId
                && Settings.Projects.All(project => project.Id != projectId))
            {
                _temporary[key] = assignment with { ProjectGroupId = null };
            }
        }
    }

    private TemporaryAssignment? GetLiveAssignment(WindowInfo window)
    {
        if (_temporary.TryGetValue(window.Hwnd, out var assignment) && assignment.ProcessId == window.ProcessId)
        {
            return assignment;
        }

        return null;
    }

    private bool TryGetWindow(long hwnd, out WindowInfo window)
    {
        var found = _windows.FirstOrDefault(candidate => candidate.Hwnd == hwnd);
        if (found is null)
        {
            window = new WindowInfo();
            return false;
        }

        window = found;
        return true;
    }
}
