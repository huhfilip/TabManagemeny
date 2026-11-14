namespace TabMan.Core.Tests;

public class GroupingFilterNavigationTests
{
    [Fact]
    public void ApplicationModeGroupsByAppAndSortsInsideTheGroup()
    {
        var windows = new List<WindowInfo>
        {
            Window(1, "excel", "B sheet", ticks: 50),
            Window(2, "excel", "A sheet", ticks: 10),
            Window(3, "chrome", "Mail", ticks: 40)
        };
        var recent = SwitcherLayoutBuilder.Build(windows, new AppSettings { SortOrder = GroupSortOrder.Recent }, EmptyTemp(), null);
        Assert.Equal(["Excel", "Google Chrome"], recent.Rows.Select(row => row.SectionTitle).ToArray());
        Assert.Equal(["B sheet", "A sheet"], recent.Rows[0].Items.Select(item => item.Title).ToArray());

        var alpha = SwitcherLayoutBuilder.Build(
            windows,
            new AppSettings { SortOrder = GroupSortOrder.Alphabetical },
            EmptyTemp(),
            null);
        Assert.Equal(["A sheet", "B sheet"], alpha.Rows.Single(row => row.SectionTitle == "Excel").Items.Select(item => item.Title).ToArray());
        Assert.Equal(["Excel", "Google Chrome"], alpha.Rows.Select(row => row.SectionTitle).ToArray());
    }

    [Fact]
    public void InitialSelectionIsThePreviousWindowNotTheForeground()
    {
        var windows = new List<WindowInfo>
        {
            Window(1, "excel", "Current", ticks: 300),
            Window(2, "chrome", "Previous", ticks: 200),
            Window(3, "notepad", "Older", ticks: 100)
        };
        var layout = SwitcherLayoutBuilder.Build(windows, new AppSettings(), EmptyTemp(), null);
        var selection = NavigationEngine.Initial(layout);
        Assert.Equal("Previous", NavigationEngine.Get(layout, selection)!.Title);
    }

    [Fact]
    public void ProjectModeGroupsByApplicationInsideTheProjectAndKeepsUngroupedDistinct()
    {
        var design = Guid.NewGuid();
        var finance = Guid.NewGuid();
        var settings = new AppSettings
        {
            LastMode = SwitcherMode.Project,
            SortOrder = GroupSortOrder.Alphabetical,
            Projects =
            [
                new ProjectGroup { Id = finance, Name = "Finance", ColorHex = "#E85D75", Order = 1 },
                new ProjectGroup { Id = design, Name = "Design", ColorHex = "#4C8DFF", Order = 0 }
            ],
            MatchRules =
            [
                new PersistentMatchRule { Id = Guid.NewGuid(), ProcessName = "figma", AppliesProject = true, ProjectGroupId = design },
                new PersistentMatchRule { Id = Guid.NewGuid(), ProcessName = "excel", TitleGlob = "*Budget*", AppliesProject = true, ProjectGroupId = finance }
            ]
        };
        var windows = new List<WindowInfo>
        {
            Window(1, "figma", "Mock"),
            Window(2, "chrome", "Reference", processId: 7),
            Window(3, "excel", "Budget"),
            Window(4, "excel", "Other book"),
            Window(5, "chrome", "Design specs", processId: 8)
        };
        var temporary = new Dictionary<long, TemporaryAssignment>
        {
            [5] = new() { Hwnd = 5, ProcessId = 8, AppliesProject = true, ProjectGroupId = design }
        };
        var layout = SwitcherLayoutBuilder.Build(windows, settings, temporary, null);

        Assert.Equal(["Design", "Design", "Finance", "Ungrouped", "Ungrouped"], layout.Rows.Select(row => row.SectionTitle).ToArray());
        Assert.Equal("Figma", layout.Rows[0].SubsectionTitle);
        Assert.Equal("Google Chrome", layout.Rows[1].SubsectionTitle);
        Assert.True(layout.Rows[0].ShowSectionHeader);
        Assert.False(layout.Rows[1].ShowSectionHeader);
        Assert.Equal("#4C8DFF", layout.Rows[0].SectionColor);
        Assert.Contains(layout.Rows.Single(row => row.SectionTitle == "Finance").Items, item => item.Title == "Budget");
        Assert.Contains(layout.Rows.Where(row => row.IsUngrouped).SelectMany(row => row.Items), item => item.Title == "Other book");
        Assert.Contains(layout.Rows.Where(row => row.IsUngrouped).SelectMany(row => row.Items), item => item.Title == "Reference");
    }

    [Fact]
    public void ProjectScopeCanShowOneGroup()
    {
        var design = Guid.NewGuid();
        var settings = new AppSettings
        {
            LastMode = SwitcherMode.Project,
            ProjectScope = ProjectScopeMode.Single,
            VisibleProjectId = design,
            Projects = [new ProjectGroup { Id = design, Name = "Design", ColorHex = "#4C8DFF" }],
            MatchRules = [new PersistentMatchRule { Id = Guid.NewGuid(), ProcessName = "figma", AppliesProject = true, ProjectGroupId = design }]
        };
        var layout = SwitcherLayoutBuilder.Build(
            [Window(1, "figma", "Mock"), Window(2, "notepad", "Loose")],
            settings,
            EmptyTemp(),
            null);
        Assert.Single(layout.Items);
        Assert.Equal("Mock", layout.Items.Single().Title);

        var ungrouped = SwitcherLayoutBuilder.Build(
            [Window(1, "figma", "Mock"), Window(2, "notepad", "Loose")],
            settings with { ProjectScope = ProjectScopeMode.Ungrouped, VisibleProjectId = null },
            EmptyTemp(),
            null);
        Assert.Equal("Loose", ungrouped.Items.Single().Title);
    }

    [Fact]
    public void RecentModeIsMostRecentlyUsed()
    {
        var layout = SwitcherLayoutBuilder.Build(
            [
                Window(1, "notepad", "Old", ticks: 1, z: 2),
                Window(2, "excel", "New", ticks: 9, z: 0),
                Window(3, "chrome", "Mid", ticks: 5, z: 1)
            ],
            new AppSettings { LastMode = SwitcherMode.Recent },
            EmptyTemp(),
            null);
        Assert.Single(layout.Rows);
        Assert.Equal(["New", "Mid", "Old"], layout.Rows[0].Items.Select(item => item.Title).ToArray());
    }

    [Fact]
    public void SearchMatchesTitleAppAndProjectAndDropsEmptyRows()
    {
        var project = Guid.NewGuid();
        var settings = new AppSettings
        {
            LastMode = SwitcherMode.Project,
            Projects = [new ProjectGroup { Id = project, Name = "Alpha Launch", ColorHex = "#4C8DFF" }],
            MatchRules = [new PersistentMatchRule { Id = Guid.NewGuid(), ProcessName = "excel", AppliesProject = true, ProjectGroupId = project }]
        };
        var windows = new[]
        {
            Window(1, "excel", "Quarterly"),
            Window(2, "chrome", "News"),
            Window(3, "notepad", "scratch")
        };
        var byTitle = SwitcherLayoutBuilder.Build(windows, settings, EmptyTemp(), "quart");
        Assert.Equal("Quarterly", byTitle.Items.Single().Title);

        var byApp = SwitcherLayoutBuilder.Build(windows, settings with { LastMode = SwitcherMode.Application }, EmptyTemp(), "CHROME");
        Assert.Equal("News", byApp.Items.Single().Title);
        Assert.Single(byApp.Rows);

        var byProject = SwitcherLayoutBuilder.Build(windows, settings, EmptyTemp(), "launch");
        Assert.Equal("Quarterly", byProject.Items.Single().Title);

        var none = SwitcherLayoutBuilder.Build(windows, settings, EmptyTemp(), "zzz");
        Assert.Empty(none.Rows);
    }

    [Fact]
    public void ArrowsTabsAndPreferredColumnMoveThroughRows()
    {
        var layout = new SwitcherLayout(SwitcherMode.Application,
        [
            Row("r0", "A", "B", "C"),
            Row("r1", "D", "E")
        ]);
        var onB = Selection.At(0, 1);
        Assert.Equal(Selection.At(0, 0), NavigationEngine.Move(onB, NavigationDirection.Left, layout));
        Assert.Equal(Selection.At(0, 2), NavigationEngine.Move(onB, NavigationDirection.Right, layout));
        Assert.Equal(new Selection(1, 1, 1), NavigationEngine.Move(onB, NavigationDirection.Down, layout));

        var onC = Selection.At(0, 2);
        var downFromC = NavigationEngine.Move(onC, NavigationDirection.Down, layout);
        Assert.Equal(new Selection(1, 1, 2), downFromC);
        var back = NavigationEngine.Move(downFromC, NavigationDirection.Down, layout);
        Assert.Equal(new Selection(0, 2, 2), back);

        Assert.Equal(Selection.At(1, 0), NavigationEngine.Move(onC, NavigationDirection.Next, layout));
        Assert.Equal(Selection.At(0, 2), NavigationEngine.Move(Selection.At(1, 0), NavigationDirection.Previous, layout));
        Assert.Equal(Selection.At(1, 1), NavigationEngine.Move(Selection.At(0, 0), NavigationDirection.Left, layout));
        Assert.Equal(Selection.At(0, 0), NavigationEngine.Move(onB, NavigationDirection.First, layout));
        Assert.Equal(Selection.At(1, 1), NavigationEngine.Move(onB, NavigationDirection.Last, layout));
        Assert.Equal(Selection.None, NavigationEngine.Move(onB, NavigationDirection.Next, new SwitcherLayout(SwitcherMode.Recent, [])));
    }

    [Fact]
    public void SessionKeepsSelectionAcrossFilterAndDropsClosedWindows()
    {
        var session = new SwitcherSession(new AppSettings());
        session.Open(
        [
            Window(1, "excel", "Budget", ticks: 20),
            Window(2, "notepad", "Notes", ticks: 10),
            Window(3, "chrome", "Mail", ticks: 5)
        ]);
        Assert.Equal("Notes", session.SelectedItem!.Title);

        session.SelectHwnd(2);
        session.SetQuery("note");
        Assert.Equal(2, session.SelectedItem!.Hwnd);

        session.Type('z');
        Assert.Null(session.SelectedItem);
        Assert.False(session.HandleEscape());
        Assert.Equal("", session.Query);
        Assert.NotNull(session.SelectedItem);

        Assert.True(session.Backspace() == false);
        session.Type('n');
        Assert.True(session.Backspace());
        Assert.Equal("", session.Query);

        session.ReplaceWindows([Window(1, "excel", "Budget", ticks: 20), Window(3, "chrome", "Mail", ticks: 5)]);
        Assert.DoesNotContain(session.Layout.Items, item => item.Hwnd == 2);
    }

    [Fact]
    public void EscapeClosesOnlyWhenTheQueryIsEmpty()
    {
        var session = new SwitcherSession(new AppSettings());
        session.Open([Window(1, "notepad", "A", ticks: 2), Window(2, "notepad", "B", ticks: 1)]);
        session.SetQuery("a");
        Assert.False(session.HandleEscape());
        Assert.True(session.IsOpen);
        Assert.True(session.HandleEscape());
        Assert.False(session.IsOpen);
    }

    [Fact]
    public void TemporaryAssignmentDoesNotStickToAReusedHandle()
    {
        var project = Guid.NewGuid();
        var session = new SwitcherSession(new AppSettings
        {
            LastMode = SwitcherMode.Project,
            Projects = [new ProjectGroup { Id = project, Name = "P", ColorHex = "#4C8DFF" }]
        });
        session.Open([Window(50, "notepad", "One", processId: 1)]);
        session.AssignTemporary(50, true, project);
        Assert.Equal(project, session.Layout.Items.Single().ProjectId);

        session.ReplaceWindows([Window(50, "notepad", "Two", processId: 2)]);
        Assert.Null(session.Layout.Items.Single().ProjectId);
        Assert.Empty(session.TemporaryAssignments);
    }

    [Fact]
    public void ModeChangeIsRememberedByTheSession()
    {
        var session = new SwitcherSession(new AppSettings());
        session.Open([Window(1, "notepad", "A", ticks: 1)]);
        session.SetMode(SwitcherMode.Recent);
        Assert.Equal(SwitcherMode.Recent, session.Settings.LastMode);
        Assert.Equal(SwitcherMode.Recent, session.Layout.Mode);
    }

    private static SwitcherRow Row(string id, params string[] titles) => new()
    {
        Id = id,
        SectionTitle = id,
        ShowSectionHeader = true,
        Items = titles.Select((title, index) => new SwitcherItem
        {
            Hwnd = HashCode.Combine(id, index),
            Title = title,
            AppDisplayName = id
        }).ToList()
    };

    private static Dictionary<long, TemporaryAssignment> EmptyTemp() => [];

    private static WindowInfo Window(long hwnd, string process, string title, long ticks = 0, int z = 0, int processId = 100) => new()
    {
        Hwnd = hwnd,
        ProcessId = processId,
        ProcessName = process,
        Title = title,
        ClassName = "Test",
        LastActiveUtcTicks = ticks,
        ZOrder = z
    };
}
