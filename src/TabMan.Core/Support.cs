using System.Text.Json;

namespace TabMan.Core;

public static class OverlayPlacement
{
    public static PixelRect PreferredSize(PixelRect workArea, int margin = 24)
    {
        var availableWidth = Math.Max(0, workArea.Width - (margin * 2));
        var availableHeight = Math.Max(0, workArea.Height - (margin * 2));
        var width = Math.Clamp((int)(workArea.Width * 0.62), 560, 1200);
        var height = Math.Clamp((int)(workArea.Height * 0.58), 380, 760);
        width = Math.Min(width, availableWidth);
        height = Math.Min(height, availableHeight);
        return new PixelRect(0, 0, Math.Max(0, width), Math.Max(0, height));
    }

    public static PixelRect Center(PixelRect workArea, int overlayWidth, int overlayHeight, int margin = 16)
    {
        var width = Math.Min(Math.Max(0, overlayWidth), Math.Max(0, workArea.Width - (margin * 2)));
        var height = Math.Min(Math.Max(0, overlayHeight), Math.Max(0, workArea.Height - (margin * 2)));
        var x = workArea.X + ((workArea.Width - width) / 2);
        var y = workArea.Y + ((workArea.Height - height) / 2);
        return new PixelRect(x, y, width, height);
    }
}

public static class HistoryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string? Serialize(IReadOnlyList<HistoryEntry> entries, bool persistHistory)
    {
        if (!persistHistory)
        {
            return null;
        }

        var safe = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.AppKey))
            .Select(entry => new HistoryEntry(entry.AppKey.Trim().ToLowerInvariant(), entry.LastActiveUtcTicks))
            .Take(50)
            .ToList();
        return JsonSerializer.Serialize(safe, Options);
    }

    public static IReadOnlyList<HistoryEntry> Load(string path, bool persistHistory)
    {
        if (!persistHistory || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(path), Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static void Save(string path, IReadOnlyList<HistoryEntry> entries, bool persistHistory)
    {
        if (!persistHistory)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        var json = Serialize(entries, true);
        if (json is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>
/// Thumbnail images are previews of whatever is on screen. They are never written to disk,
/// including when the stored flag is turned on by hand.
/// </summary>
public static class ThumbnailPersistence
{
    public static bool ShouldWriteFiles(bool setting) => false;

    public static bool ShowLive(bool showLiveThumbnails) => showLiveThumbnails;
}

public sealed class DiagnosticLog
{
    private readonly List<string> _lines = [];

    public bool Enabled { get; set; }

    public IReadOnlyList<string> Lines => _lines;

    public void Event(string code, int value = 0)
    {
        if (!Enabled || !IsCode(code))
        {
            return;
        }

        if (_lines.Count >= 200)
        {
            _lines.RemoveAt(0);
        }

        _lines.Add($"{DateTimeOffset.UtcNow:O}\t{code}\t{value}");
    }

    public void FlushTo(string path)
    {
        if (!Enabled)
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllLines(path, _lines);
    }

    private static bool IsCode(string code)
    {
        if (code.Length is < 1 or > 40)
        {
            return false;
        }

        foreach (var character in code)
        {
            var allowed = character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}

public static class StartupCommand
{
    public const string ValueName = "TabMan";

    public static string? FormatRunValue(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || exePath.Contains('"'))
        {
            return null;
        }

        return "\"" + exePath.Trim() + "\"";
    }
}

public static class AltTabPolicy
{
    public const int VkTab = 0x09;
    public const int VkMenu = 0x12;
    public const int VkLeftMenu = 0xA4;
    public const int VkRightMenu = 0xA5;

    public static AltTabDecision Evaluate(AltTabPhase phase, KeyStroke key, bool featureEnabled)
    {
        if (!featureEnabled)
        {
            return new AltTabDecision(AltTabPhase.Idle, false, false, false, false);
        }

        var altReleased = phase == AltTabPhase.Engaged && IsAlt(key.VirtualKey) && !key.IsKeyDown;
        if (altReleased)
        {
            return new AltTabDecision(AltTabPhase.Idle, false, false, true, false);
        }

        var tabDown = key.IsKeyDown && key.VirtualKey == VkTab && key.Alt && !key.Control;
        if (!tabDown)
        {
            return new AltTabDecision(phase, false, false, false, false);
        }

        return key.Shift
            ? new AltTabDecision(AltTabPhase.Engaged, false, true, false, true)
            : new AltTabDecision(AltTabPhase.Engaged, true, false, false, true);
    }

    public static bool IsAlt(int virtualKey) =>
        virtualKey is VkMenu or VkLeftMenu or VkRightMenu;
}
