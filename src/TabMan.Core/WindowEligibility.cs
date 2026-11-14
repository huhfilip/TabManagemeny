namespace TabMan.Core;

/// <summary>
/// Decides which top-level windows belong in the switcher.
/// Mirrors the usual Alt+Tab filters: visible or minimized, not a tool window,
/// not an owned popup, not cloaked, not shell chrome, and not this process.
/// </summary>
public static class WindowEligibility
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "DV2ControlHost",
        "MsgrIMEWindowClass",
        "SysShadow",
        "MultitaskingViewFrame",
        "XamlExplorerHostIslandWindow",
        "Windows.Internal.Shell.TabProxyWindow"
    };

    public static bool IsCandidate(in WindowTraits traits)
    {
        if (traits.IsOwnProcess || traits.IsCloaked)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(traits.ClassName) || ShellClasses.Contains(traits.ClassName))
        {
            return false;
        }

        if (!traits.HasTitle)
        {
            return false;
        }

        if (traits.IsToolWindow && !traits.IsAppWindow)
        {
            return false;
        }

        if (traits.HasOwner && !traits.IsAppWindow)
        {
            return false;
        }

        if (traits.IsNoActivate && !traits.IsAppWindow)
        {
            return false;
        }

        return traits.IsVisible || traits.IsIconic;
    }
}
