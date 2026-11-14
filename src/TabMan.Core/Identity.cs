namespace TabMan.Core;

public readonly record struct ResolvedApp(string Key, string DisplayName);

public static class ProcessNames
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        var file = name.Trim().Replace('/', '\\');
        var slash = file.LastIndexOf('\\');
        if (slash >= 0)
        {
            file = file[(slash + 1)..];
        }

        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            file = file[..^4];
        }

        return file.ToLowerInvariant();
    }
}

public static class AppKeys
{
    public static string ForProcess(string? processName)
    {
        var normalized = ProcessNames.Normalize(processName);
        return normalized.Length == 0 ? "proc:unknown" : "proc:" + normalized;
    }

    public static string ForAppUserModelId(string appUserModelId) =>
        "aumid:" + appUserModelId.Trim().ToLowerInvariant();

    public static string NormalizeGroupKey(string? groupKey)
    {
        if (string.IsNullOrWhiteSpace(groupKey))
        {
            return "";
        }

        var key = groupKey.Trim().ToLowerInvariant();
        if (key.StartsWith("proc:", StringComparison.Ordinal)
            || key.StartsWith("aumid:", StringComparison.Ordinal)
            || key.StartsWith("group:", StringComparison.Ordinal))
        {
            return key;
        }

        return "group:" + key;
    }
}

public static class KnownApps
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Google Chrome",
        ["msedge"] = "Microsoft Edge",
        ["firefox"] = "Firefox",
        ["winword"] = "Word",
        ["excel"] = "Excel",
        ["powerpnt"] = "PowerPoint",
        ["outlook"] = "Outlook",
        ["onenote"] = "OneNote",
        ["notepad"] = "Notepad",
        ["windowsterminal"] = "Terminal",
        ["wt"] = "Terminal",
        ["code"] = "Visual Studio Code",
        ["devenv"] = "Visual Studio",
        ["explorer"] = "File Explorer",
        ["spotify"] = "Spotify",
        ["slack"] = "Slack",
        ["teams"] = "Teams",
        ["discord"] = "Discord"
    };

    public static bool TryGet(string processOrKey, out string displayName)
    {
        var raw = ProcessNames.Normalize(processOrKey);
        var colon = raw.IndexOf(':');
        if (colon >= 0)
        {
            raw = raw[(colon + 1)..];
        }

        return Names.TryGetValue(raw, out displayName!);
    }
}

public static class DisplayNames
{
    public static string Prettify(string keyOrProcess)
    {
        if (KnownApps.TryGet(keyOrProcess, out var known))
        {
            return known;
        }

        var raw = keyOrProcess;
        var colon = raw.IndexOf(':');
        if (colon >= 0)
        {
            raw = raw[(colon + 1)..];
        }

        if (raw.Length == 0)
        {
            return "Unknown";
        }

        if (raw.StartsWith("pid", StringComparison.Ordinal) && raw.Length > 3)
        {
            return "Unknown app";
        }

        return char.ToUpperInvariant(raw[0]) + raw[1..];
    }
}

public static class AppIdentityResolver
{
    public static ResolvedApp Resolve(
        WindowInfo window,
        IReadOnlyList<AppGroupingRule> rules,
        IReadOnlyDictionary<string, string> displayNames)
    {
        var rule = AppGroupingMatcher.Best(window, rules);
        if (rule is not null)
        {
            var key = AppKeys.NormalizeGroupKey(rule.GroupKey);
            var name = Lookup(displayNames, key)
                ?? (!string.IsNullOrWhiteSpace(rule.DisplayName) ? rule.DisplayName.Trim() : DisplayNames.Prettify(key));
            return new ResolvedApp(key, name);
        }

        var fallback = !string.IsNullOrWhiteSpace(window.AppUserModelId)
            ? AppKeys.ForAppUserModelId(window.AppUserModelId)
            : AppKeys.ForProcess(window.ProcessName);
        var resolved = Lookup(displayNames, fallback)
            ?? (KnownApps.TryGet(window.ProcessName, out var known) ? known : DisplayNames.Prettify(fallback));
        return new ResolvedApp(fallback, resolved);
    }

    private static string? Lookup(IReadOnlyDictionary<string, string> displayNames, string key)
    {
        if (displayNames.TryGetValue(key, out var custom) && !string.IsNullOrWhiteSpace(custom))
        {
            return custom.Trim();
        }

        return null;
    }
}

public static class AppGroupingMatcher
{
    public static AppGroupingRule? Best(WindowInfo window, IReadOnlyList<AppGroupingRule> rules)
    {
        AppGroupingRule? best = null;
        foreach (var rule in rules)
        {
            if (!rule.Enabled || !rule.HasMatcher || string.IsNullOrWhiteSpace(rule.GroupKey))
            {
                continue;
            }

            if (!RuleText.Matches(window, rule.ProcessName, rule.AppUserModelId, rule.TitleGlob))
            {
                continue;
            }

            if (best is null || Compare(rule, best) > 0)
            {
                best = rule;
            }
        }

        return best;
    }

    private static int Compare(AppGroupingRule candidate, AppGroupingRule current)
    {
        var priority = candidate.Priority.CompareTo(current.Priority);
        if (priority != 0)
        {
            return priority;
        }

        var specificity = Specificity(candidate).CompareTo(Specificity(current));
        return specificity != 0 ? specificity : candidate.Id.CompareTo(current.Id);
    }

    private static int Specificity(AppGroupingRule rule)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(rule.ProcessName))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(rule.AppUserModelId))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(rule.TitleGlob))
        {
            score += 3;
        }

        return score;
    }
}
