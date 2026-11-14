using System.Text.Json;
using System.Text.Json.Serialization;

namespace TabMan.Core;

public static class HotkeyFormatter
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0D] = "Enter",
        [0x13] = "Pause",
        [0x14] = "Caps Lock",
        [0x1B] = "Esc",
        [0x20] = "Space",
        [0x21] = "Page Up",
        [0x22] = "Page Down",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2C] = "Print Screen",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x60] = "Num 0",
        [0x61] = "Num 1",
        [0x62] = "Num 2",
        [0x63] = "Num 3",
        [0x64] = "Num 4",
        [0x65] = "Num 5",
        [0x66] = "Num 6",
        [0x67] = "Num 7",
        [0x68] = "Num 8",
        [0x69] = "Num 9",
        [0x6A] = "Num *",
        [0x6B] = "Num +",
        [0x6D] = "Num -",
        [0x6E] = "Num .",
        [0x6F] = "Num /",
        [0x70] = "F1",
        [0x71] = "F2",
        [0x72] = "F3",
        [0x73] = "F4",
        [0x74] = "F5",
        [0x75] = "F6",
        [0x76] = "F7",
        [0x77] = "F8",
        [0x78] = "F9",
        [0x79] = "F10",
        [0x7A] = "F11",
        [0x7B] = "F12",
        [0xBA] = ";",
        [0xBB] = "=",
        [0xBC] = ",",
        [0xBD] = "-",
        [0xBE] = ".",
        [0xBF] = "/",
        [0xC0] = "`",
        [0xDB] = "[",
        [0xDC] = "\\",
        [0xDD] = "]",
        [0xDE] = "'"
    };

    public static string Format(HotkeyChord chord)
    {
        var parts = new List<string>(5);
        if (chord.Control)
        {
            parts.Add("Ctrl");
        }

        if (chord.Alt)
        {
            parts.Add("Alt");
        }

        if (chord.Shift)
        {
            parts.Add("Shift");
        }

        if (chord.Win)
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(chord.VirtualKey));
        return string.Join('+', parts);
    }

    public static string KeyName(int virtualKey)
    {
        if (Names.TryGetValue(virtualKey, out var name))
        {
            return name;
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        return "Key " + virtualKey.ToString("X2");
    }
}

public static class SettingsEditor
{
    public static (AppSettings Settings, ProjectGroup Project) AddProject(AppSettings settings, string? name, string? color = null)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim();
        if (trimmed.Length > 80)
        {
            trimmed = trimmed[..80];
        }

        var order = settings.Projects.Count == 0 ? 0 : settings.Projects.Max(project => project.Order) + 1;
        var project = new ProjectGroup
        {
            Id = Guid.NewGuid(),
            Name = trimmed,
            ColorHex = ColorValidator.Normalize(color) ?? ColorPalette.ForIndex(settings.Projects.Count),
            Order = order
        };
        return (settings with { Projects = settings.Projects.Append(project).ToList() }, project);
    }

    public static AppSettings RenameProject(AppSettings settings, Guid id, string? name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim();
        if (trimmed.Length > 80)
        {
            trimmed = trimmed[..80];
        }

        return settings with
        {
            Projects = settings.Projects
                .Select(project => project.Id == id ? project with { Name = trimmed } : project)
                .ToList()
        };
    }

    public static AppSettings RecolorProject(AppSettings settings, Guid id, string? color)
    {
        var normalized = ColorValidator.Normalize(color);
        if (normalized is null)
        {
            return settings;
        }

        return settings with
        {
            Projects = settings.Projects
                .Select(project => project.Id == id ? project with { ColorHex = normalized } : project)
                .ToList()
        };
    }

    public static AppSettings MoveProject(AppSettings settings, Guid id, int direction)
    {
        var ordered = settings.Projects.OrderBy(project => project.Order).ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var index = ordered.FindIndex(project => project.Id == id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= ordered.Count)
        {
            return settings;
        }

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i] = ordered[i] with { Order = i };
        }

        return settings with { Projects = ordered };
    }

    public static AppSettings RemoveProject(AppSettings settings, Guid id)
    {
        var projects = settings.Projects.Where(project => project.Id != id).ToList();
        var rules = settings.MatchRules
            .Select(rule => rule.AppliesProject && rule.ProjectGroupId == id
                ? rule with { AppliesProject = false, ProjectGroupId = null }
                : rule)
            .Where(rule => rule.HasMatcher && (rule.AppliesProject || !string.IsNullOrWhiteSpace(rule.ColorHex)))
            .ToList();
        var scope = settings.ProjectScope;
        var visible = settings.VisibleProjectId;
        if (settings.VisibleProjectId == id)
        {
            scope = ProjectScopeMode.All;
            visible = null;
        }

        return settings with
        {
            Projects = projects,
            MatchRules = rules,
            ProjectScope = scope,
            VisibleProjectId = visible
        };
    }

    public static AppSettings AddMatchRule(AppSettings settings, PersistentMatchRule rule)
    {
        if (!rule.HasMatcher)
        {
            return settings;
        }

        var color = ColorValidator.Normalize(rule.ColorHex);
        var stored = rule with { ColorHex = color, ProcessName = NormalizeOptionalProcess(rule.ProcessName) };
        return settings with { MatchRules = settings.MatchRules.Append(stored).ToList() };
    }

    public static AppSettings RemoveMatchRule(AppSettings settings, Guid id) =>
        settings with { MatchRules = settings.MatchRules.Where(rule => rule.Id != id).ToList() };

    public static AppSettings AddAppGroupingRule(AppSettings settings, AppGroupingRule rule)
    {
        if (!rule.HasMatcher || string.IsNullOrWhiteSpace(rule.GroupKey))
        {
            return settings;
        }

        var stored = rule with
        {
            GroupKey = AppKeys.NormalizeGroupKey(rule.GroupKey),
            ProcessName = NormalizeOptionalProcess(rule.ProcessName),
            DisplayName = string.IsNullOrWhiteSpace(rule.DisplayName) ? null : rule.DisplayName.Trim()
        };
        if (stored.GroupKey.Length == 0)
        {
            return settings;
        }

        return settings with { AppGroupingRules = settings.AppGroupingRules.Append(stored).ToList() };
    }

    public static AppSettings RemoveAppGroupingRule(AppSettings settings, Guid id) =>
        settings with { AppGroupingRules = settings.AppGroupingRules.Where(rule => rule.Id != id).ToList() };

    public static AppSettings SetAppDisplayName(AppSettings settings, string appKey, string? name)
    {
        var key = appKey.Trim().ToLowerInvariant();
        var names = new Dictionary<string, string>(settings.AppDisplayNames, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(name))
        {
            names.Remove(key);
        }
        else
        {
            var trimmed = name.Trim();
            names[key] = trimmed.Length > 80 ? trimmed[..80] : trimmed;
        }

        return settings with { AppDisplayNames = names };
    }

    private static string? NormalizeOptionalProcess(string? processName)
    {
        var normalized = ProcessNames.Normalize(processName);
        return normalized.Length == 0 ? null : normalized;
    }
}

public sealed record SettingsLoadResult(AppSettings Settings, bool UsedDefaults, string? Warning);

public static class SettingsSerializer
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static AppSettings CreateDefault() => new();

    public static string Serialize(AppSettings settings)
    {
        var normalized = SettingsNormalizer.Normalize(settings);
        return JsonSerializer.Serialize(normalized, Options);
    }

    public static SettingsLoadResult Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SettingsLoadResult(CreateDefault(), true, "Settings were empty.");
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AppSettings>(json, Options);
            if (parsed is null)
            {
                return new SettingsLoadResult(CreateDefault(), true, "Settings could not be read.");
            }

            return new SettingsLoadResult(SettingsNormalizer.Normalize(parsed), false, null);
        }
        catch (JsonException)
        {
            return new SettingsLoadResult(CreateDefault(), true, "Settings file was not valid JSON.");
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public static class SettingsNormalizer
{
    public static AppSettings Normalize(AppSettings settings)
    {
        var projects = settings.Projects
            .Where(project => project.Id != Guid.Empty)
            .GroupBy(project => project.Id)
            .Select(group => group.First())
            .Select(project => project with
            {
                Name = string.IsNullOrWhiteSpace(project.Name) ? "Untitled" : project.Name.Trim(),
                ColorHex = ColorValidator.Normalize(project.ColorHex) ?? ColorPalette.Presets[0]
            })
            .OrderBy(project => project.Order)
            .ToList();
        var projectIds = projects.Select(project => project.Id).ToHashSet();

        var rules = settings.MatchRules
            .Where(rule => rule.HasMatcher)
            .Select(rule => rule with
            {
                ProcessName = EmptyToNull(ProcessNames.Normalize(rule.ProcessName)),
                AppUserModelId = EmptyToNull(rule.AppUserModelId?.Trim()),
                TitleGlob = EmptyToNull(rule.TitleGlob?.Trim()),
                ColorHex = ColorValidator.Normalize(rule.ColorHex),
                AppliesProject = rule.AppliesProject && (rule.ProjectGroupId is null || projectIds.Contains(rule.ProjectGroupId.Value)),
                ProjectGroupId = rule.AppliesProject && rule.ProjectGroupId is Guid id && projectIds.Contains(id) ? id : null
            })
            .Where(rule => rule.HasMatcher && (rule.AppliesProject || rule.ColorHex is not null))
            .ToList();

        var appRules = settings.AppGroupingRules
            .Where(rule => rule.HasMatcher && !string.IsNullOrWhiteSpace(rule.GroupKey))
            .Select(rule => rule with
            {
                ProcessName = EmptyToNull(ProcessNames.Normalize(rule.ProcessName)),
                AppUserModelId = EmptyToNull(rule.AppUserModelId?.Trim()),
                TitleGlob = EmptyToNull(rule.TitleGlob?.Trim()),
                GroupKey = AppKeys.NormalizeGroupKey(rule.GroupKey),
                DisplayName = EmptyToNull(rule.DisplayName?.Trim())
            })
            .Where(rule => rule.GroupKey.Length > 0 && rule.HasMatcher)
            .ToList();

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in settings.AppDisplayNames)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            names[pair.Key.Trim().ToLowerInvariant()] = pair.Value.Trim();
        }

        var shortcut = settings.Shortcut.IsValid ? settings.Shortcut : HotkeyChord.Default;
        var scope = settings.ProjectScope;
        var visible = settings.VisibleProjectId;
        if (scope == ProjectScopeMode.Single && (visible is null || !projectIds.Contains(visible.Value)))
        {
            scope = ProjectScopeMode.All;
            visible = null;
        }

        if (scope != ProjectScopeMode.Single)
        {
            visible = null;
        }

        return new AppSettings
        {
            Version = SettingsSerializer.CurrentVersion,
            LastMode = settings.LastMode,
            SortOrder = settings.SortOrder,
            Shortcut = shortcut,
            AltTabIntegrationEnabled = settings.AltTabIntegrationEnabled,
            RunAtStartup = settings.RunAtStartup,
            PersistHistory = settings.PersistHistory,
            PersistThumbnails = settings.PersistThumbnails,
            ShowLiveThumbnails = settings.ShowLiveThumbnails,
            DiagnosticsEnabled = settings.DiagnosticsEnabled,
            Theme = settings.Theme,
            ProjectScope = scope,
            VisibleProjectId = visible,
            Projects = projects,
            AppGroupingRules = appRules,
            MatchRules = rules,
            AppDisplayNames = names
        };
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

public sealed class SettingsRepository
{
    private readonly string _settingsPath;
    private readonly string _historyPath;

    public SettingsRepository(string directory)
    {
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "settings.json");
        _historyPath = Path.Combine(directory, "history.json");
    }

    public string SettingsPath => _settingsPath;

    public string HistoryPath => _historyPath;

    public SettingsLoadResult Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new SettingsLoadResult(SettingsSerializer.CreateDefault(), false, null);
        }

        string json;
        try
        {
            json = File.ReadAllText(_settingsPath);
        }
        catch (IOException ex)
        {
            return new SettingsLoadResult(SettingsSerializer.CreateDefault(), true, ex.GetType().Name);
        }

        var result = SettingsSerializer.Deserialize(json);
        if (result.UsedDefaults)
        {
            var backup = _settingsPath + ".bad.json";
            try
            {
                File.Copy(_settingsPath, backup, overwrite: true);
            }
            catch (IOException)
            {
                // The unreadable file stays in place. The next successful save replaces it.
            }
        }

        return result;
    }

    public void Save(AppSettings settings)
    {
        var json = SettingsSerializer.Serialize(settings);
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _settingsPath, overwrite: true);
    }

    public void DeletePersonalData()
    {
        TryDelete(_settingsPath);
        TryDelete(_settingsPath + ".tmp");
        TryDelete(_settingsPath + ".bad.json");
        TryDelete(_historyPath);
        TryDelete(_historyPath + ".tmp");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort. Removal instructions also cover deleting the folder by hand.
        }
    }
}

public static class AppPaths
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabMan");
}
