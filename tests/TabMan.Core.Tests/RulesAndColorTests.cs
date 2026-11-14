namespace TabMan.Core.Tests;

public class RulesAndColorTests
{
    [Theory]
    [InlineData("Budget 2024", "Budget*", true)]
    [InlineData("Q1 Report final", "*report*", true)]
    [InlineData("Doc1.txt", "Doc?.txt", true)]
    [InlineData("Doc12.txt", "Doc?.txt", false)]
    [InlineData("a.b", "a.b", true)]
    [InlineData("ab", "a.b", false)]
    [InlineData("anything", "*", true)]
    public void GlobMatchesLiterally(string text, string glob, bool expected) =>
        Assert.Equal(expected, GlobMatcher.IsMatch(text, glob));

    [Fact]
    public void HigherPriorityRuleWinsOverMoreSpecificRule()
    {
        var broad = new PersistentMatchRule
        {
            Id = Guid.NewGuid(),
            ProcessName = "chrome",
            AppliesProject = true,
            ProjectGroupId = Guid.NewGuid(),
            Priority = 10
        };
        var specific = new PersistentMatchRule
        {
            Id = Guid.NewGuid(),
            ProcessName = "chrome",
            TitleGlob = "*GitHub*",
            AppliesProject = true,
            ProjectGroupId = Guid.NewGuid(),
            Priority = 1
        };
        var window = new WindowInfo { ProcessName = "chrome.exe", Title = "GitHub - Chrome" };
        Assert.Equal(broad.Id, RuleMatcher.Best(window, [specific, broad])!.Id);
    }

    [Fact]
    public void EqualPriorityPrefersTheTitlePattern()
    {
        var broad = new PersistentMatchRule
        {
            Id = Guid.NewGuid(),
            ProcessName = "chrome",
            AppliesProject = true,
            Priority = 1
        };
        var specific = new PersistentMatchRule
        {
            Id = Guid.NewGuid(),
            ProcessName = "chrome",
            TitleGlob = "*GitHub*",
            AppliesProject = true,
            ProjectGroupId = Guid.NewGuid(),
            Priority = 1
        };
        var window = new WindowInfo { ProcessName = "chrome", Title = "GitHub - Chrome" };
        Assert.Equal(specific.Id, RuleMatcher.Best(window, [broad, specific])!.Id);
    }

    [Fact]
    public void DisabledRulesAreIgnored()
    {
        var rule = new PersistentMatchRule
        {
            Id = Guid.NewGuid(),
            ProcessName = "notepad",
            AppliesProject = true,
            Enabled = false
        };
        Assert.Null(RuleMatcher.Best(new WindowInfo { ProcessName = "notepad", Title = "notes" }, [rule]));
    }

    [Fact]
    public void ReopenedWindowMatchesByAppAndTitleNotHandle()
    {
        var project = Guid.NewGuid();
        var rule = new PersistentMatchRule
        {
            Id = Guid.NewGuid(),
            ProcessName = "WINWORD.EXE",
            TitleGlob = "*Budget*",
            AppliesProject = true,
            ProjectGroupId = project
        };
        var settings = new AppSettings
        {
            Projects = [new ProjectGroup { Id = project, Name = "Finance", ColorHex = "#E85D75" }],
            MatchRules = [rule]
        };
        var first = new WindowInfo { Hwnd = 10, ProcessId = 4, ProcessName = "winword", Title = "Budget.docx - Word" };
        var reopened = new WindowInfo { Hwnd = 999, ProcessId = 8, ProcessName = "WINWORD.EXE", Title = "Budget draft - Word" };
        var other = new WindowInfo { Hwnd = 11, ProcessId = 4, ProcessName = "winword", Title = "Letter.docx - Word" };

        Assert.Equal(project, SwitcherLayoutBuilder.Resolve(first, settings, null).ProjectId);
        Assert.Equal(project, SwitcherLayoutBuilder.Resolve(reopened, settings, null).ProjectId);
        Assert.Null(SwitcherLayoutBuilder.Resolve(other, settings, null).ProjectId);
    }

    [Fact]
    public void TemporaryAssignmentBeatsPersistentRule()
    {
        var ruleProject = Guid.NewGuid();
        var tempProject = Guid.NewGuid();
        var settings = new AppSettings
        {
            Projects =
            [
                new ProjectGroup { Id = ruleProject, Name = "Rule", ColorHex = "#111111" },
                new ProjectGroup { Id = tempProject, Name = "Temp", ColorHex = "#222222" }
            ],
            MatchRules =
            [
                new PersistentMatchRule
                {
                    Id = Guid.NewGuid(),
                    ProcessName = "notepad",
                    AppliesProject = true,
                    ProjectGroupId = ruleProject
                }
            ]
        };
        var window = new WindowInfo { Hwnd = 5, ProcessId = 9, ProcessName = "notepad", Title = "scratch" };
        var temporary = new TemporaryAssignment
        {
            Hwnd = 5,
            ProcessId = 9,
            AppliesProject = true,
            ProjectGroupId = tempProject
        };
        var item = SwitcherLayoutBuilder.Resolve(window, settings, temporary);
        Assert.Equal(tempProject, item.ProjectId);

        var ungroup = temporary with { ProjectGroupId = null };
        Assert.Null(SwitcherLayoutBuilder.Resolve(window, settings, ungroup).ProjectId);
    }

    [Fact]
    public void WindowColorOverridesRuleAndGroupColor()
    {
        var project = new ProjectGroup { Id = Guid.NewGuid(), Name = "P", ColorHex = "#112233" };
        var rule = new PersistentMatchRule { ColorHex = "#AABBCC", AppliesProject = true, ProjectGroupId = project.Id };
        var temporary = new TemporaryAssignment { HasColorOverride = true, ColorHex = "#010203" };
        Assert.Equal("#010203", ColorResolver.Resolve(temporary, rule, project));

        var cleared = temporary with { ColorHex = null };
        Assert.Equal("#112233", ColorResolver.Resolve(cleared, rule, project));

        Assert.Equal("#AABBCC", ColorResolver.Resolve(null, rule, project));
        Assert.Equal("#112233", ColorResolver.Resolve(null, null, project));
    }

    [Theory]
    [InlineData("#abc", null)]
    [InlineData("4c8dff", "#4C8DFF")]
    [InlineData("#4C8DFF", "#4C8DFF")]
    [InlineData("zzzzzz", null)]
    public void ColorNormalization(string input, string? expected) =>
        Assert.Equal(expected, ColorValidator.Normalize(input));
}
