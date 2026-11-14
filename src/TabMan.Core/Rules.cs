using System.Text.RegularExpressions;

namespace TabMan.Core;

public static class GlobMatcher
{
    public static bool IsMatch(string? text, string glob)
    {
        text ??= "";
        try
        {
            return Regex.IsMatch(
                text,
                "^" + ToRegexBody(glob) + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(50));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string ToRegexBody(string glob)
    {
        var builder = new System.Text.StringBuilder(glob.Length * 2);
        foreach (var character in glob)
        {
            switch (character)
            {
                case '*':
                    builder.Append(".*");
                    break;
                case '?':
                    builder.Append('.');
                    break;
                default:
                    builder.Append(Regex.Escape(character.ToString()));
                    break;
            }
        }

        return builder.ToString();
    }
}

public static class RuleText
{
    public static bool Matches(WindowInfo window, string? processName, string? appUserModelId, string? titleGlob)
    {
        if (!string.IsNullOrWhiteSpace(processName)
            && !string.Equals(ProcessNames.Normalize(processName), ProcessNames.Normalize(window.ProcessName), StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(appUserModelId))
        {
            if (string.IsNullOrWhiteSpace(window.AppUserModelId)
                || !string.Equals(appUserModelId.Trim(), window.AppUserModelId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(titleGlob) && !GlobMatcher.IsMatch(window.Title, titleGlob.Trim()))
        {
            return false;
        }

        return true;
    }
}

public static class RuleMatcher
{
    public static PersistentMatchRule? Best(WindowInfo window, IReadOnlyList<PersistentMatchRule> rules)
    {
        PersistentMatchRule? best = null;
        foreach (var rule in rules)
        {
            if (!rule.Enabled || !rule.HasMatcher)
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

    private static int Compare(PersistentMatchRule candidate, PersistentMatchRule current)
    {
        var priority = candidate.Priority.CompareTo(current.Priority);
        if (priority != 0)
        {
            return priority;
        }

        var specificity = Specificity(candidate).CompareTo(Specificity(current));
        return specificity != 0 ? specificity : candidate.Id.CompareTo(current.Id);
    }

    private static int Specificity(PersistentMatchRule rule)
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

public static class ProjectAssignment
{
    public static Guid? Resolve(TemporaryAssignment? temporary, PersistentMatchRule? rule)
    {
        if (temporary is { AppliesProject: true })
        {
            return temporary.ProjectGroupId;
        }

        if (rule is { AppliesProject: true })
        {
            return rule.ProjectGroupId;
        }

        return null;
    }
}

public static class ColorValidator
{
    public static bool IsValid(string? hex) => Normalize(hex) is not null;

    public static string? Normalize(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        var text = hex.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length != 6)
        {
            return null;
        }

        foreach (var character in text)
        {
            var isHex = character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex)
            {
                return null;
            }
        }

        return "#" + text.ToUpperInvariant();
    }
}

public static class ColorPalette
{
    public static readonly string[] Presets =
    [
        "#4C8DFF",
        "#7C5CFF",
        "#E85D75",
        "#F5A524",
        "#3DDC97",
        "#2BBAC0",
        "#F07D4A",
        "#5AC8FA",
        "#BF5AF2",
        "#8E8E93"
    ];

    public static string ForIndex(int index) => Presets[Math.Abs(index) % Presets.Length];
}

public static class ColorResolver
{
    public static string? Resolve(TemporaryAssignment? temporary, PersistentMatchRule? rule, ProjectGroup? project)
    {
        if (temporary is { HasColorOverride: true })
        {
            var windowColor = ColorValidator.Normalize(temporary.ColorHex);
            if (windowColor is not null)
            {
                return windowColor;
            }
        }
        else if (ColorValidator.Normalize(rule?.ColorHex) is { } ruleColor)
        {
            return ruleColor;
        }

        return project is null ? null : ColorValidator.Normalize(project.ColorHex);
    }
}
