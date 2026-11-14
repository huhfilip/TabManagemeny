namespace TabMan.Core;

public enum SwitcherMode
{
    Application,
    Project,
    Recent
}

public enum GroupSortOrder
{
    Recent,
    Alphabetical
}

public enum AppTheme
{
    System,
    Dark,
    Light
}

public enum ProjectScopeMode
{
    All,
    Single,
    Ungrouped
}

/// <summary>
/// A top-level window observed in memory. The handle is not a stable identity
/// and is never written to the settings file.
/// </summary>
public sealed record WindowInfo
{
    public long Hwnd { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string? AppUserModelId { get; init; }
    public string Title { get; init; } = "";
    public string ClassName { get; init; } = "";
    public bool IsMinimized { get; init; }

    /// <summary>Zero is the front-most enumerated window.</summary>
    public int ZOrder { get; init; }

    public long LastActiveUtcTicks { get; init; }
}

public readonly record struct WindowTraits(
    bool IsVisible,
    bool IsIconic,
    bool IsCloaked,
    bool HasOwner,
    bool IsToolWindow,
    bool IsAppWindow,
    bool IsNoActivate,
    bool HasTitle,
    bool IsOwnProcess,
    string ClassName);

public readonly record struct HotkeyChord(bool Control, bool Alt, bool Shift, bool Win, int VirtualKey)
{
    public static HotkeyChord Default { get; } = new(true, true, false, false, 0x20);

    public bool HasModifier => Control || Alt || Shift || Win;

    public bool IsValid =>
        HasModifier
        && VirtualKey is > 0 and < 255
        && VirtualKey is not (
            0x10 or 0x11 or 0x12
            or 0x5B or 0x5C
            or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5);

    /// <summary>RegisterHotKey modifier flags, including MOD_NOREPEAT.</summary>
    public int Win32Modifiers
    {
        get
        {
            var flags = 0x4000;
            if (Alt)
            {
                flags |= 0x0001;
            }

            if (Control)
            {
                flags |= 0x0002;
            }

            if (Shift)
            {
                flags |= 0x0004;
            }

            if (Win)
            {
                flags |= 0x0008;
            }

            return flags;
        }
    }
}

public sealed record ProjectGroup
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string ColorHex { get; init; } = "#4C8DFF";
    public int Order { get; init; }
}

public sealed record AppGroupingRule
{
    public Guid Id { get; init; }
    public string? ProcessName { get; init; }
    public string? AppUserModelId { get; init; }
    public string? TitleGlob { get; init; }
    public string GroupKey { get; init; } = "";
    public string? DisplayName { get; init; }
    public int Priority { get; init; }
    public bool Enabled { get; init; } = true;

    public bool HasMatcher =>
        !string.IsNullOrWhiteSpace(ProcessName)
        || !string.IsNullOrWhiteSpace(AppUserModelId)
        || !string.IsNullOrWhiteSpace(TitleGlob);
}

public sealed record PersistentMatchRule
{
    public Guid Id { get; init; }
    public string? ProcessName { get; init; }
    public string? AppUserModelId { get; init; }
    public string? TitleGlob { get; init; }
    public bool AppliesProject { get; init; }
    public Guid? ProjectGroupId { get; init; }
    public string? ColorHex { get; init; }
    public int Priority { get; init; }
    public bool Enabled { get; init; } = true;

    public bool HasMatcher =>
        !string.IsNullOrWhiteSpace(ProcessName)
        || !string.IsNullOrWhiteSpace(AppUserModelId)
        || !string.IsNullOrWhiteSpace(TitleGlob);
}

/// <summary>
/// Session-only assignment. Dropped when the window closes. Never serialized.
/// </summary>
public sealed record TemporaryAssignment
{
    public long Hwnd { get; init; }
    public int ProcessId { get; init; }
    public bool AppliesProject { get; init; }
    public Guid? ProjectGroupId { get; init; }
    public bool HasColorOverride { get; init; }
    public string? ColorHex { get; init; }
}

public sealed record AppSettings
{
    public int Version { get; init; } = SettingsSerializer.CurrentVersion;
    public SwitcherMode LastMode { get; init; } = SwitcherMode.Application;
    public GroupSortOrder SortOrder { get; init; } = GroupSortOrder.Recent;
    public HotkeyChord Shortcut { get; init; } = HotkeyChord.Default;
    public bool AltTabIntegrationEnabled { get; init; }
    public bool RunAtStartup { get; init; }
    public bool PersistHistory { get; init; }
    public bool PersistThumbnails { get; init; }
    public bool ShowLiveThumbnails { get; init; } = true;
    public bool DiagnosticsEnabled { get; init; }
    public AppTheme Theme { get; init; } = AppTheme.System;
    public ProjectScopeMode ProjectScope { get; init; } = ProjectScopeMode.All;
    public Guid? VisibleProjectId { get; init; }
    public IReadOnlyList<ProjectGroup> Projects { get; init; } = [];
    public IReadOnlyList<AppGroupingRule> AppGroupingRules { get; init; } = [];
    public IReadOnlyList<PersistentMatchRule> MatchRules { get; init; } = [];
    public IReadOnlyDictionary<string, string> AppDisplayNames { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record SwitcherItem
{
    public long Hwnd { get; init; }
    public int ProcessId { get; init; }
    public string Title { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string AppKey { get; init; } = "";
    public string AppDisplayName { get; init; } = "";
    public Guid? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public string? AccentColor { get; init; }
    public bool IsMinimized { get; init; }
    public long LastActiveUtcTicks { get; init; }
    public int ZOrder { get; init; }
    public bool IsInitialTarget { get; init; }
}

public sealed record SwitcherRow
{
    public string Id { get; init; } = "";
    public string SectionTitle { get; init; } = "";
    public string? SubsectionTitle { get; init; }
    public string? SectionColor { get; init; }
    public bool IsUngrouped { get; init; }
    public bool ShowSectionHeader { get; init; }
    public IReadOnlyList<SwitcherItem> Items { get; init; } = [];
}

public sealed record SwitcherLayout(SwitcherMode Mode, IReadOnlyList<SwitcherRow> Rows)
{
    public int TotalCount => Rows.Sum(row => row.Items.Count);

    public IEnumerable<SwitcherItem> Items => Rows.SelectMany(row => row.Items);
}

public enum NavigationDirection
{
    Left,
    Right,
    Up,
    Down,
    Next,
    Previous,
    First,
    Last
}

public readonly record struct Selection(int Row, int Column, int PreferredColumn)
{
    public static Selection None { get; } = new(-1, -1, 0);

    public bool IsNone => Row < 0 || Column < 0;

    public static Selection At(int row, int column) => new(row, column, column);
}

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public readonly record struct HistoryEntry(string AppKey, long LastActiveUtcTicks);

public enum AltTabPhase
{
    Idle,
    Engaged
}

public readonly record struct KeyStroke(int VirtualKey, bool IsKeyDown, bool Alt, bool Control, bool Shift);

public readonly record struct AltTabDecision(
    AltTabPhase NextPhase,
    bool CycleForward,
    bool CycleBackward,
    bool Activate,
    bool Swallow);
