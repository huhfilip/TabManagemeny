using System.Text.Json;

namespace TabMan.Core.Tests;

public class SettingsAndPolicyTests
{
    [Fact]
    public void DefaultsLeaveIntegrationHistoryThumbnailsAndStartupOff()
    {
        var settings = SettingsSerializer.CreateDefault();
        Assert.Equal(HotkeyChord.Default, settings.Shortcut);
        Assert.Equal("Ctrl+Alt+Space", HotkeyFormatter.Format(settings.Shortcut));
        Assert.False(settings.AltTabIntegrationEnabled);
        Assert.False(settings.RunAtStartup);
        Assert.False(settings.PersistHistory);
        Assert.False(settings.PersistThumbnails);
        Assert.False(settings.DiagnosticsEnabled);
        Assert.True(settings.ShowLiveThumbnails);
        Assert.Equal(SwitcherMode.Application, settings.LastMode);
    }

    [Fact]
    public void SettingsRoundTripAndRejectsAShortcutWithNoModifier()
    {
        var (withProject, project) = SettingsEditor.AddProject(SettingsSerializer.CreateDefault(), "Design", "#4c8dff");
        var rule = new PersistentMatchRule
        {
            Id = Guid.NewGuid(),
            ProcessName = "Figma.exe",
            TitleGlob = "*Mock*",
            AppliesProject = true,
            ProjectGroupId = project.Id,
            ColorHex = "e85d75",
            Priority = 3
        };
        var grouping = new AppGroupingRule
        {
            Id = Guid.NewGuid(),
            ProcessName = "chrome.exe",
            GroupKey = "Browsers",
            DisplayName = "Browsers",
            Priority = 2
        };
        var edited = SettingsEditor.AddAppGroupingRule(SettingsEditor.AddMatchRule(withProject, rule), grouping);
        edited = SettingsEditor.SetAppDisplayName(edited, "proc:notepad", "Scratch pad");
        edited = edited with
        {
            LastMode = SwitcherMode.Project,
            SortOrder = GroupSortOrder.Alphabetical,
            Shortcut = new HotkeyChord(true, false, true, false, 0x20),
            Theme = AppTheme.Dark,
            ProjectScope = ProjectScopeMode.Single,
            VisibleProjectId = project.Id,
            ShowLiveThumbnails = false
        };

        var json = SettingsSerializer.Serialize(edited);
        Assert.DoesNotContain("hwnd", json, StringComparison.OrdinalIgnoreCase);
        var loaded = SettingsSerializer.Deserialize(json);
        Assert.False(loaded.UsedDefaults);
        Assert.Equal(SwitcherMode.Project, loaded.Settings.LastMode);
        Assert.Equal(GroupSortOrder.Alphabetical, loaded.Settings.SortOrder);
        Assert.Equal(new HotkeyChord(true, false, true, false, 0x20), loaded.Settings.Shortcut);
        Assert.Equal(AppTheme.Dark, loaded.Settings.Theme);
        Assert.Equal("Design", loaded.Settings.Projects.Single().Name);
        Assert.Equal("#4C8DFF", loaded.Settings.Projects.Single().ColorHex);
        Assert.Equal("figma", loaded.Settings.MatchRules.Single().ProcessName);
        Assert.Equal("#E85D75", loaded.Settings.MatchRules.Single().ColorHex);
        Assert.Equal(project.Id, loaded.Settings.VisibleProjectId);
        Assert.Equal("group:browsers", loaded.Settings.AppGroupingRules.Single().GroupKey);
        Assert.Equal("Scratch pad", loaded.Settings.AppDisplayNames["proc:notepad"]);

        var invalid = loaded.Settings with { Shortcut = new HotkeyChord(false, false, false, false, 0x41) };
        var repaired = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(invalid));
        Assert.Equal(HotkeyChord.Default, repaired.Settings.Shortcut);
    }

    [Fact]
    public void CorruptAndPartialJsonFallBackWithoutThrowing()
    {
        var corrupt = SettingsSerializer.Deserialize("{ this is not json");
        Assert.True(corrupt.UsedDefaults);
        Assert.Equal(HotkeyChord.Default, corrupt.Settings.Shortcut);
        Assert.False(corrupt.Settings.AltTabIntegrationEnabled);

        var partial = SettingsSerializer.Deserialize("{\"lastMode\":\"recent\",\"projects\":[{\"id\":\"00000000-0000-0000-0000-000000000000\",\"name\":\"\",\"colorHex\":\"nope\"}]}");
        Assert.False(partial.UsedDefaults);
        Assert.Equal(SwitcherMode.Recent, partial.Settings.LastMode);
        Assert.Empty(partial.Settings.Projects);
        Assert.False(partial.Settings.RunAtStartup);
    }

    [Fact]
    public void RepositoryRoundTripDoesNotStoreLiveWindowTitles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tabman-tests", Guid.NewGuid().ToString("N"));
        var repository = new SettingsRepository(directory);
        var (settings, project) = SettingsEditor.AddProject(SettingsSerializer.CreateDefault(), "Alpha", "#4C8DFF");
        repository.Save(settings);

        var session = new SwitcherSession(repository.Load().Settings);
        session.Open([new WindowInfo { Hwnd = 424242, ProcessId = 7, ProcessName = "excel", Title = "Secret Quarterly Budget" }]);
        session.AssignTemporary(424242, true, project.Id);
        session.SetTemporaryColor(424242, true, "#010203");
        repository.Save(session.Settings);

        var json = File.ReadAllText(repository.SettingsPath);
        Assert.DoesNotContain("Secret Quarterly Budget", json);
        Assert.DoesNotContain("424242", json);
        Assert.DoesNotContain("hwnd", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(project.Id, session.Layout.Items.Single().ProjectId);
        Assert.Equal("#010203", session.Layout.Items.Single().AccentColor);

        File.WriteAllText(repository.SettingsPath, "{ broken");
        var reloaded = repository.Load();
        Assert.True(reloaded.UsedDefaults);
        Assert.True(File.Exists(repository.SettingsPath + ".bad.json"));

        repository.DeletePersonalData();
        Assert.False(File.Exists(repository.SettingsPath));
    }

    [Fact]
    public void RemovingAProjectDetachesRulesAndClearsItsFilter()
    {
        var (settings, project) = SettingsEditor.AddProject(new AppSettings(), "Design", "#4C8DFF");
        settings = settings with { ProjectScope = ProjectScopeMode.Single, VisibleProjectId = project.Id };
        settings = SettingsEditor.AddMatchRule(settings, new PersistentMatchRule
        {
            Id = Guid.NewGuid(),
            ProcessName = "figma",
            AppliesProject = true,
            ProjectGroupId = project.Id,
            ColorHex = "#AABBCC"
        });
        var removed = SettingsEditor.RemoveProject(settings, project.Id);
        Assert.Empty(removed.Projects);
        Assert.Equal(ProjectScopeMode.All, removed.ProjectScope);
        Assert.Null(removed.VisibleProjectId);
        var rule = Assert.Single(removed.MatchRules);
        Assert.False(rule.AppliesProject);
        Assert.Equal("#AABBCC", rule.ColorHex);
    }

    [Fact]
    public void HistoryAndThumbnailsStayOffTheDiskByDefault()
    {
        var entries = new List<HistoryEntry> { new("proc:excel", 10), new("proc:chrome", 9) };
        Assert.Null(HistoryStore.Serialize(entries, persistHistory: false));

        var path = Path.Combine(Path.GetTempPath(), "tabman-tests", Guid.NewGuid().ToString("N"), "history.json");
        File.WriteAllText(EnsureParent(path), "{\"title\":\"should be removed\"}");
        HistoryStore.Save(path, entries, persistHistory: false);
        Assert.False(File.Exists(path));

        HistoryStore.Save(path, entries, persistHistory: true);
        var json = File.ReadAllText(path);
        Assert.Contains("proc:excel", json);
        Assert.DoesNotContain("title", json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(HistoryStore.Load(path, persistHistory: false));
        Assert.Equal("proc:excel", HistoryStore.Load(path, persistHistory: true)[0].AppKey);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.False(ThumbnailPersistence.ShouldWriteFiles(true));
        Assert.False(ThumbnailPersistence.ShouldWriteFiles(false));
        Assert.True(ThumbnailPersistence.ShowLive(true));
    }

    [Fact]
    public void DiagnosticsAcceptCodesOnlyAndStayQuietWhenDisabled()
    {
        var log = new DiagnosticLog();
        log.Event("HOTKEY_OK", 1);
        log.Event("Secret Quarterly Budget");
        Assert.Empty(log.Lines);

        log.Enabled = true;
        log.Event("ENUM_OK", 12);
        log.Event("window title");
        log.Event("lowercase");
        Assert.Single(log.Lines);
        Assert.Contains("ENUM_OK", log.Lines[0]);
        Assert.Contains("\t12", log.Lines[0]);

        var path = Path.Combine(Path.GetTempPath(), "tabman-tests", Guid.NewGuid().ToString("N"), "diagnostics.log");
        log.Enabled = false;
        log.FlushTo(path);
        Assert.False(File.Exists(path));
        log.Enabled = true;
        log.FlushTo(path);
        Assert.Contains("ENUM_OK", File.ReadAllText(path));
    }

    [Fact]
    public void StartupCommandQuotesTheExecutableAndRejectsQuotes()
    {
        Assert.Equal("\"C:\\Tools\\TabMan.exe\"", StartupCommand.FormatRunValue("C:\\Tools\\TabMan.exe"));
        Assert.Null(StartupCommand.FormatRunValue("C:\\bad\"quote\\TabMan.exe"));
        Assert.Null(StartupCommand.FormatRunValue("  "));
        Assert.Equal("TabMan", StartupCommand.ValueName);
    }

    [Fact]
    public void OverlayCentersOnAMonitorLeftOfThePrimary()
    {
        var work = new PixelRect(-1920, 0, 1920, 1080);
        var placed = OverlayPlacement.Center(work, 800, 600);
        Assert.Equal(new PixelRect(-1360, 240, 800, 600), placed);

        var tiny = new PixelRect(0, 0, 400, 300);
        var clamped = OverlayPlacement.Center(tiny, 900, 700, margin: 16);
        Assert.Equal(16, clamped.X);
        Assert.Equal(16, clamped.Y);
        Assert.Equal(368, clamped.Width);
        Assert.Equal(268, clamped.Height);
    }

    [Theory]
    [InlineData(false, 0x09, true, false, false, true, false, false, false, false)]
    [InlineData(true, 0x09, true, false, false, true, true, true, false, true)]
    [InlineData(true, 0x09, true, true, false, true, true, false, true, true)]
    [InlineData(true, 0x09, true, false, true, true, false, false, false, false)]
    public void AltTabPolicyStaysOffUnlessEnabledAndDoesNotStealCtrlAltTab(
        bool enabled,
        int virtualKey,
        bool alt,
        bool shift,
        bool control,
        bool keyDown,
        bool engaged,
        bool forward,
        bool backward,
        bool swallow)
    {
        var decision = AltTabPolicy.Evaluate(
            AltTabPhase.Idle,
            new KeyStroke(virtualKey, keyDown, alt, control, shift),
            enabled);
        Assert.Equal(engaged, decision.NextPhase == AltTabPhase.Engaged);
        Assert.Equal(forward, decision.CycleForward);
        Assert.Equal(backward, decision.CycleBackward);
        Assert.Equal(swallow, decision.Swallow);
        Assert.False(decision.Activate);
    }

    [Fact]
    public void AltReleaseActivatesAndDoesNotSwallow()
    {
        var decision = AltTabPolicy.Evaluate(
            AltTabPhase.Engaged,
            new KeyStroke(AltTabPolicy.VkLeftMenu, false, false, false, false),
            true);
        Assert.Equal(AltTabPhase.Idle, decision.NextPhase);
        Assert.True(decision.Activate);
        Assert.False(decision.Swallow);
    }

    [Fact]
    public void TabKeyUpDoesNotCycleTwice()
    {
        var decision = AltTabPolicy.Evaluate(
            AltTabPhase.Engaged,
            new KeyStroke(AltTabPolicy.VkTab, false, true, false, false),
            true);
        Assert.False(decision.CycleForward);
        Assert.False(decision.Swallow);
        Assert.Equal(AltTabPhase.Engaged, decision.NextPhase);
    }

    private static string EnsureParent(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
