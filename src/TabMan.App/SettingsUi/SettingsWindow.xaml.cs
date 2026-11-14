using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabMan.App.Dialogs;
using TabMan.Core;

namespace TabMan.App.SettingsUi;

public partial class SettingsWindow : Window
{
    private readonly record struct ProjectChoice(string Label, Guid? Id)
    {
        public override string ToString() => Label;
    }

    private readonly record struct NameRow(string Key, string Name)
    {
        public override string ToString() => Name + "  (" + Key + ")";
    }

    private AppSettings _draft;
    private HotkeyChord _shortcut;
    private readonly IReadOnlyList<WindowInfo> _liveWindows;

    public SettingsWindow(AppSettings settings, IReadOnlyList<WindowInfo> liveWindows, string dataDirectory)
    {
        InitializeComponent();
        _draft = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(settings)).Settings;
        _shortcut = _draft.Shortcut;
        _liveWindows = liveWindows;
        DataPathBox.Text = dataDirectory;
        ThemeBox.ItemsSource = Enum.GetValues<AppTheme>();
        ThemeBox.SelectedItem = _draft.Theme;
        SortBox.ItemsSource = Enum.GetValues<GroupSortOrder>();
        SortBox.SelectedItem = _draft.SortOrder;
        ThumbnailsBox.IsChecked = _draft.ShowLiveThumbnails;
        StartupBox.IsChecked = _draft.RunAtStartup;
        AltTabBox.IsChecked = _draft.AltTabIntegrationEnabled;
        HistoryBox.IsChecked = _draft.PersistHistory;
        DiagnosticsBox.IsChecked = _draft.DiagnosticsEnabled;
        ShortcutBox.Text = HotkeyFormatter.Format(_shortcut);
        BuildPalette();
        RefreshLists();
    }

    public AppSettings Result { get; private set; } = SettingsSerializer.CreateDefault();
    public bool DeletePersonalData { get; private set; }

    private void BuildPalette()
    {
        foreach (var hex in ColorPalette.Presets)
        {
            var color = hex;
            var button = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 6),
                Background = Brush(color),
                ToolTip = color,
                Padding = new Thickness(0)
            };
            button.Click += (_, _) => Recolor(color);
            Palette.Children.Add(button);
        }

        var custom = new Button { Content = "Custom", Margin = new Thickness(0, 0, 0, 6) };
        custom.Click += (_, _) =>
        {
            var hex = TextPromptWindow.Ask(this, "Project color", "Enter a color such as #4C8DFF.", "#4C8DFF", value =>
                ColorValidator.IsValid(value) ? null : "Use a color like #4C8DFF.");
            if (hex is not null)
            {
                Recolor(hex);
            }
        };
        Palette.Children.Add(custom);
    }

    private void RefreshLists()
    {
        ProjectList.Items.Clear();
        foreach (var project in _draft.Projects.OrderBy(project => project.Order))
        {
            ProjectList.Items.Add(project);
        }

        ProjectList.DisplayMemberPath = nameof(ProjectGroup.Name);
        RuleList.Items.Clear();
        foreach (var rule in _draft.MatchRules)
        {
            var project = _draft.Projects.FirstOrDefault(item => item.Id == rule.ProjectGroupId);
            var target = rule.AppliesProject ? project?.Name ?? "Ungrouped" : "color only";
            RuleList.Items.Add(new RuleRow(rule.Id, $"{rule.ProcessName ?? rule.AppUserModelId ?? "any"}   {rule.TitleGlob ?? ""}   → {target}   {rule.ColorHex ?? ""}"));
        }

        RuleProject.Items.Clear();
        RuleProject.Items.Add(new ProjectChoice("Ungrouped", null));
        foreach (var project in _draft.Projects.OrderBy(project => project.Order))
        {
            RuleProject.Items.Add(new ProjectChoice(project.Name, project.Id));
        }

        if (RuleProject.SelectedIndex < 0)
        {
            RuleProject.SelectedIndex = 0;
        }

        NameList.Items.Clear();
        foreach (var pair in _draft.AppDisplayNames.OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase))
        {
            NameList.Items.Add(new NameRow(pair.Key, pair.Value));
        }

        GroupRuleList.Items.Clear();
        foreach (var rule in _draft.AppGroupingRules)
        {
            GroupRuleList.Items.Add(new RuleRow(rule.Id, $"{rule.ProcessName ?? "any"}   {rule.TitleGlob ?? ""}   → {rule.DisplayName ?? rule.GroupKey}"));
        }

        LiveList.Items.Clear();
        foreach (var window in _liveWindows.OrderBy(window => window.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            LiveList.Items.Add(window.ProcessName + "   —   " + window.Title);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = _draft with
        {
            Theme = ThemeBox.SelectedItem is AppTheme theme ? theme : AppTheme.System,
            SortOrder = SortBox.SelectedItem is GroupSortOrder sort ? sort : GroupSortOrder.Recent,
            Shortcut = _shortcut.IsValid ? _shortcut : HotkeyChord.Default,
            ShowLiveThumbnails = ThumbnailsBox.IsChecked == true,
            RunAtStartup = StartupBox.IsChecked == true,
            AltTabIntegrationEnabled = AltTabBox.IsChecked == true,
            PersistHistory = HistoryBox.IsChecked == true,
            PersistThumbnails = false,
            DiagnosticsEnabled = DiagnosticsBox.IsChecked == true
        };
        DialogResult = true;
    }

    private void ShortcutBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab)
        {
            return;
        }

        if (key is Key.Escape or Key.Back)
        {
            _shortcut = HotkeyChord.Default;
            ShortcutBox.Text = HotkeyFormatter.Format(_shortcut);
            ShortcutHint.Text = "Restored the default shortcut. Save to apply it.";
            return;
        }

        var chord = new HotkeyChord(
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Windows),
            KeyInterop.VirtualKeyFromKey(key));
        if (!chord.IsValid)
        {
            ShortcutHint.Text = "Include Ctrl, Alt, Shift, or the Windows key.";
            return;
        }

        _shortcut = chord;
        ShortcutBox.Text = HotkeyFormatter.Format(chord);
        ShortcutHint.Text = "Save to start using this shortcut.";
    }

    private ProjectGroup? SelectedProject() => ProjectList.SelectedItem as ProjectGroup;

    private void AddProject_Click(object sender, RoutedEventArgs e)
    {
        var name = TextPromptWindow.Ask(this, "New project", "Project name");
        if (name is null)
        {
            return;
        }

        (_draft, _) = SettingsEditor.AddProject(_draft, name);
        RefreshLists();
    }

    private void RenameProject_Click(object sender, RoutedEventArgs e)
    {
        var project = SelectedProject();
        if (project is null)
        {
            return;
        }

        var name = TextPromptWindow.Ask(this, "Rename project", "Project name", project.Name);
        if (name is null)
        {
            return;
        }

        _draft = SettingsEditor.RenameProject(_draft, project.Id, name);
        RefreshLists();
    }

    private void Recolor(string hex)
    {
        var project = SelectedProject();
        if (project is null)
        {
            DataStatus.Text = "Select a project before choosing a color.";
            return;
        }

        _draft = SettingsEditor.RecolorProject(_draft, project.Id, hex);
        RefreshLists();
    }

    private void MoveProjectUp_Click(object sender, RoutedEventArgs e) => MoveProject(-1);
    private void MoveProjectDown_Click(object sender, RoutedEventArgs e) => MoveProject(1);

    private void MoveProject(int direction)
    {
        if (SelectedProject() is not { } project)
        {
            return;
        }

        _draft = SettingsEditor.MoveProject(_draft, project.Id, direction);
        RefreshLists();
        ProjectList.SelectedItem = _draft.Projects.FirstOrDefault(item => item.Id == project.Id);
    }

    private void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProject() is not { } project)
        {
            return;
        }

        _draft = SettingsEditor.RemoveProject(_draft, project.Id);
        RefreshLists();
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var choice = RuleProject.SelectedItem as ProjectChoice? ?? new ProjectChoice("Ungrouped", null);
        var rule = new PersistentMatchRule
        {
            Id = Guid.NewGuid(),
            ProcessName = RuleProcess.Text,
            TitleGlob = string.IsNullOrWhiteSpace(RuleTitle.Text) ? null : RuleTitle.Text.Trim(),
            AppliesProject = true,
            ProjectGroupId = choice.Id,
            Priority = string.IsNullOrWhiteSpace(RuleTitle.Text) ? 1 : 10,
            Enabled = true
        };
        var updated = SettingsEditor.AddMatchRule(_draft, rule);
        if (updated.MatchRules.Count == _draft.MatchRules.Count)
        {
            DataStatus.Text = "Enter a process name or a title pattern.";
            return;
        }

        _draft = updated;
        RuleProcess.Clear();
        RuleTitle.Clear();
        RefreshLists();
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (RuleList.SelectedItem is RuleRow row)
        {
            _draft = SettingsEditor.RemoveMatchRule(_draft, row.Id);
            RefreshLists();
        }
    }

    private void RenameApp_Click(object sender, RoutedEventArgs e)
    {
        if (NameList.SelectedItem is not NameRow row)
        {
            return;
        }

        var name = TextPromptWindow.Ask(this, "Display name", "Name shown in the switcher", row.Name);
        if (name is null)
        {
            return;
        }

        _draft = SettingsEditor.SetAppDisplayName(_draft, row.Key, name);
        RefreshLists();
    }

    private void ClearAppName_Click(object sender, RoutedEventArgs e)
    {
        if (NameList.SelectedItem is NameRow row)
        {
            _draft = SettingsEditor.SetAppDisplayName(_draft, row.Key, null);
            RefreshLists();
        }
    }

    private void AddGroupRule_Click(object sender, RoutedEventArgs e)
    {
        var rule = new AppGroupingRule
        {
            Id = Guid.NewGuid(),
            ProcessName = GroupProcess.Text,
            GroupKey = GroupName.Text,
            DisplayName = GroupName.Text,
            TitleGlob = string.IsNullOrWhiteSpace(GroupTitle.Text) ? null : GroupTitle.Text.Trim(),
            Priority = string.IsNullOrWhiteSpace(GroupTitle.Text) ? 1 : 10,
            Enabled = true
        };
        var updated = SettingsEditor.AddAppGroupingRule(_draft, rule);
        if (updated.AppGroupingRules.Count == _draft.AppGroupingRules.Count)
        {
            DataStatus.Text = "Enter a process and a group name.";
            return;
        }

        _draft = updated;
        GroupProcess.Clear();
        GroupName.Clear();
        GroupTitle.Clear();
        RefreshLists();
    }

    private void RemoveGroupRule_Click(object sender, RoutedEventArgs e)
    {
        if (GroupRuleList.SelectedItem is RuleRow row)
        {
            _draft = SettingsEditor.RemoveAppGroupingRule(_draft, row.Id);
            RefreshLists();
        }
    }

    private void LiveList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LiveList.SelectedItem is string line)
        {
            var split = line.Split("   —   ", 2, StringSplitOptions.TrimEntries);
            RuleProcess.Text = split[0];
            GroupProcess.Text = split[0];
        }
    }

    private void DeleteData_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Delete saved projects, rules, and history on this PC? TabMan will use default settings.",
                "TabMan",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        DeletePersonalData = true;
        Result = SettingsSerializer.CreateDefault();
        DialogResult = true;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private sealed record RuleRow(Guid Id, string Text)
    {
        public override string ToString() => Text;
    }
}
