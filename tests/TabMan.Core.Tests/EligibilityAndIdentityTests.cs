namespace TabMan.Core.Tests;

public class EligibilityAndIdentityTests
{
    [Fact]
    public void OrdinaryVisibleWindowIsIncluded()
    {
        var traits = BaseTraits() with { IsVisible = true, HasTitle = true };
        Assert.True(WindowEligibility.IsCandidate(traits));
    }

    [Fact]
    public void MinimizedWindowIsIncluded()
    {
        var traits = BaseTraits() with { IsVisible = false, IsIconic = true };
        Assert.True(WindowEligibility.IsCandidate(traits));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void CloakedOwnedOrToolWindowsAreExcluded(bool cloaked, bool owned, bool tool)
    {
        var traits = BaseTraits() with
        {
            IsCloaked = cloaked,
            HasOwner = owned,
            IsToolWindow = tool
        };
        Assert.False(WindowEligibility.IsCandidate(traits));
    }

    [Fact]
    public void AppWindowFlagKeepsOwnedToolWindows()
    {
        var traits = BaseTraits() with { HasOwner = true, IsToolWindow = true, IsAppWindow = true };
        Assert.True(WindowEligibility.IsCandidate(traits));
    }

    [Fact]
    public void OwnProcessAndShellClassesAreExcluded()
    {
        Assert.False(WindowEligibility.IsCandidate(BaseTraits() with { IsOwnProcess = true }));
        Assert.False(WindowEligibility.IsCandidate(BaseTraits() with { ClassName = "Shell_TrayWnd" }));
        Assert.False(WindowEligibility.IsCandidate(BaseTraits() with { HasTitle = false }));
        Assert.False(WindowEligibility.IsCandidate(BaseTraits() with { IsVisible = false, IsIconic = false }));
    }

    [Fact]
    public void KnownProcessesGetFriendlyNames()
    {
        var chrome = new WindowInfo { ProcessName = "chrome.exe", Title = "Inbox" };
        var resolved = AppIdentityResolver.Resolve(chrome, [], new Dictionary<string, string>());
        Assert.Equal("proc:chrome", resolved.Key);
        Assert.Equal("Google Chrome", resolved.DisplayName);
    }

    [Fact]
    public void AppUserModelIdBeatsProcessAndCustomNameBeatsBoth()
    {
        var window = new WindowInfo
        {
            ProcessName = "chrome.exe",
            AppUserModelId = "Chrome.Aumid",
            Title = "Inbox"
        };
        var plain = AppIdentityResolver.Resolve(window, [], new Dictionary<string, string>());
        Assert.Equal("aumid:chrome.aumid", plain.Key);
        Assert.Equal("Google Chrome", plain.DisplayName);

        var names = new Dictionary<string, string> { ["aumid:chrome.aumid"] = "Work browser" };
        var custom = AppIdentityResolver.Resolve(window, [], names);
        Assert.Equal("Work browser", custom.DisplayName);
    }

    [Fact]
    public void GroupingRuleMergesProcessesAndTitleRuleSplits()
    {
        var merge = new AppGroupingRule
        {
            Id = Guid.NewGuid(),
            ProcessName = "chrome.exe",
            GroupKey = "Browsers",
            DisplayName = "Browsers",
            Priority = 1
        };
        var split = new AppGroupingRule
        {
            Id = Guid.NewGuid(),
            ProcessName = "chrome",
            TitleGlob = "*Incognito*",
            GroupKey = "private",
            DisplayName = "Private browsing",
            Priority = 5
        };
        var normal = new WindowInfo { ProcessName = "CHROME.EXE", Title = "Docs" };
        var incognito = new WindowInfo { ProcessName = "chrome", Title = "Inbox - Incognito" };
        var names = new Dictionary<string, string>();

        var normalApp = AppIdentityResolver.Resolve(normal, [merge, split], names);
        var privateApp = AppIdentityResolver.Resolve(incognito, [merge, split], names);

        Assert.Equal("group:browsers", normalApp.Key);
        Assert.Equal("Browsers", normalApp.DisplayName);
        Assert.Equal("group:private", privateApp.Key);
        Assert.Equal("Private browsing", privateApp.DisplayName);
    }

    [Fact]
    public void DisplayNameOverrideBeatsRuleName()
    {
        var rule = new AppGroupingRule
        {
            Id = Guid.NewGuid(),
            ProcessName = "excel",
            GroupKey = "sheets",
            DisplayName = "Sheets"
        };
        var window = new WindowInfo { ProcessName = "excel", Title = "Book" };
        var names = new Dictionary<string, string> { ["group:sheets"] = "Numbers" };
        var resolved = AppIdentityResolver.Resolve(window, [rule], names);
        Assert.Equal("Numbers", resolved.DisplayName);
    }

    private static WindowTraits BaseTraits() => new(
        IsVisible: true,
        IsIconic: false,
        IsCloaked: false,
        HasOwner: false,
        IsToolWindow: false,
        IsAppWindow: false,
        IsNoActivate: false,
        HasTitle: true,
        IsOwnProcess: false,
        ClassName: "Chrome_WidgetWin_1");
}
