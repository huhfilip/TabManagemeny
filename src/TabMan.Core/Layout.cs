namespace TabMan.Core;

public static class SwitcherSearch
{
    public static bool Matches(SwitcherItem item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var needle = query.Trim();
        return Contains(item.Title, needle)
            || Contains(item.AppDisplayName, needle)
            || Contains(item.ProjectName, needle)
            || Contains(item.ProcessName, needle);
    }

    private static bool Contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

public static class SwitcherLayoutBuilder
{
    public static SwitcherLayout Build(
        IReadOnlyList<WindowInfo> windows,
        AppSettings settings,
        IReadOnlyDictionary<long, TemporaryAssignment> temporary,
        string? query)
    {
        var resolved = new List<SwitcherItem>(windows.Count);
        foreach (var window in windows)
        {
            temporary.TryGetValue(window.Hwnd, out var assignment);
            if (assignment is not null && assignment.ProcessId != window.ProcessId)
            {
                assignment = null;
            }

            var item = Resolve(window, settings, assignment);
            if (SwitcherSearch.Matches(item, query) && InScope(item, settings))
            {
                resolved.Add(item);
            }
        }

        MarkInitialTarget(resolved);
        var rows = settings.LastMode switch
        {
            SwitcherMode.Application => BuildApplicationRows(resolved, settings.SortOrder),
            SwitcherMode.Project => BuildProjectRows(resolved, settings),
            _ => BuildRecentRows(resolved)
        };

        return new SwitcherLayout(settings.LastMode, rows);
    }

    public static SwitcherItem Resolve(WindowInfo window, AppSettings settings, TemporaryAssignment? temporary)
    {
        var app = AppIdentityResolver.Resolve(window, settings.AppGroupingRules, settings.AppDisplayNames);
        var rule = RuleMatcher.Best(window, settings.MatchRules);
        var projectId = ProjectAssignment.Resolve(temporary, rule);
        var project = projectId is Guid id
            ? settings.Projects.FirstOrDefault(candidate => candidate.Id == id)
            : null;
        if (project is null)
        {
            projectId = null;
        }

        return new SwitcherItem
        {
            Hwnd = window.Hwnd,
            ProcessId = window.ProcessId,
            Title = window.Title,
            ProcessName = ProcessNames.Normalize(window.ProcessName),
            AppKey = app.Key,
            AppDisplayName = app.DisplayName,
            ProjectId = project?.Id,
            ProjectName = project?.Name,
            AccentColor = ColorResolver.Resolve(temporary, rule, project),
            IsMinimized = window.IsMinimized,
            LastActiveUtcTicks = window.LastActiveUtcTicks,
            ZOrder = window.ZOrder
        };
    }

    private static bool InScope(SwitcherItem item, AppSettings settings)
    {
        if (settings.LastMode != SwitcherMode.Project || settings.ProjectScope == ProjectScopeMode.All)
        {
            return true;
        }

        if (settings.ProjectScope == ProjectScopeMode.Ungrouped)
        {
            return item.ProjectId is null;
        }

        return settings.VisibleProjectId is Guid id && item.ProjectId == id;
    }

    private static void MarkInitialTarget(List<SwitcherItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var ordered = items
            .OrderByDescending(item => item.LastActiveUtcTicks)
            .ThenBy(item => item.ZOrder)
            .ThenBy(item => item.Hwnd)
            .ToList();
        var chosen = ordered.Count >= 2 ? ordered[1] : ordered[0];
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            items[index] = item with
            {
                IsInitialTarget = item.Hwnd == chosen.Hwnd && item.ProcessId == chosen.ProcessId
            };
        }
    }

    private static IReadOnlyList<SwitcherRow> BuildApplicationRows(
        IReadOnlyList<SwitcherItem> items,
        GroupSortOrder sortOrder)
    {
        var rows = items
            .GroupBy(item => item.AppKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = SortItems(group.ToList(), sortOrder);
                var header = ordered[0].AppDisplayName;
                return new SwitcherRow
                {
                    Id = "app:" + group.Key,
                    SectionTitle = header,
                    SubsectionTitle = null,
                    SectionColor = null,
                    ShowSectionHeader = true,
                    Items = ordered
                };
            })
            .ToList();

        rows.Sort(CompareApplicationRows(sortOrder));
        return rows;
    }

    private static Comparison<SwitcherRow> CompareApplicationRows(GroupSortOrder sortOrder)
    {
        return (left, right) =>
        {
            if (sortOrder == GroupSortOrder.Alphabetical)
            {
                var byName = string.Compare(SortLabel(left), SortLabel(right), StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : string.Compare(left.Id, right.Id, StringComparison.Ordinal);
            }

            var leftActivity = left.Items.Max(item => item.LastActiveUtcTicks);
            var rightActivity = right.Items.Max(item => item.LastActiveUtcTicks);
            var byRecent = rightActivity.CompareTo(leftActivity);
            if (byRecent != 0)
            {
                return byRecent;
            }

            var leftZ = left.Items.Min(item => item.ZOrder);
            var rightZ = right.Items.Min(item => item.ZOrder);
            var byZ = leftZ.CompareTo(rightZ);
            return byZ != 0 ? byZ : string.Compare(left.Id, right.Id, StringComparison.Ordinal);
        };
    }

    private static string SortLabel(SwitcherRow row) => row.SubsectionTitle ?? row.SectionTitle;

    private static IReadOnlyList<SwitcherRow> BuildProjectRows(IReadOnlyList<SwitcherItem> items, AppSettings settings)
    {
        var rows = new List<SwitcherRow>();
        foreach (var project in settings.Projects.OrderBy(project => project.Order).ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase))
        {
            var members = items.Where(item => item.ProjectId == project.Id).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            AppendAppSubrows(rows, project.Id.ToString("N"), project.Name, project.ColorHex, false, members, settings.SortOrder);
        }

        var ungrouped = items.Where(item => item.ProjectId is null).ToList();
        if (ungrouped.Count > 0)
        {
            AppendAppSubrows(rows, "ungrouped", "Ungrouped", null, true, ungrouped, settings.SortOrder);
        }

        return rows;
    }

    private static void AppendAppSubrows(
        List<SwitcherRow> rows,
        string sectionId,
        string sectionTitle,
        string? color,
        bool ungrouped,
        List<SwitcherItem> members,
        GroupSortOrder sortOrder)
    {
        var apps = members
            .GroupBy(item => item.AppKey, StringComparer.Ordinal)
            .Select(group => new SwitcherRow
            {
                Id = sectionId + ":" + group.Key,
                SectionTitle = sectionTitle,
                SubsectionTitle = group.First().AppDisplayName,
                SectionColor = ColorValidator.Normalize(color),
                IsUngrouped = ungrouped,
                Items = SortItems(group.ToList(), sortOrder)
            })
            .ToList();

        apps.Sort(CompareApplicationRows(sortOrder));
        var first = true;
        foreach (var row in apps)
        {
            rows.Add(row with { ShowSectionHeader = first });
            first = false;
        }
    }

    private static IReadOnlyList<SwitcherRow> BuildRecentRows(IReadOnlyList<SwitcherItem> items)
    {
        if (items.Count == 0)
        {
            return [];
        }

        return
        [
            new SwitcherRow
            {
                Id = "recent",
                SectionTitle = "Recent",
                ShowSectionHeader = true,
                Items = SortItems(items.ToList(), GroupSortOrder.Recent)
            }
        ];
    }

    private static List<SwitcherItem> SortItems(List<SwitcherItem> items, GroupSortOrder sortOrder)
    {
        if (sortOrder == GroupSortOrder.Alphabetical)
        {
            return items
                .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Hwnd)
                .ToList();
        }

        return items
            .OrderByDescending(item => item.LastActiveUtcTicks)
            .ThenBy(item => item.ZOrder)
            .ThenBy(item => item.Hwnd)
            .ToList();
    }
}
