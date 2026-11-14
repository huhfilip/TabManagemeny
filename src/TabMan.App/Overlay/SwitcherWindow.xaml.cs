using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TabMan.App.Dialogs;
using TabMan.Core;
using TabMan.Platform.Windows;

namespace TabMan.App.Overlay;

public partial class SwitcherWindow : Window
{
    private readonly ThumbnailMap _thumbnails = new();
    private readonly Dictionary<long, Image> _icons = [];
    private readonly Dictionary<long, Border> _thumbs = [];
    private SwitcherSession? _session;
    private WindowSnapshot? _snapshot;
    private IconCache? _iconCache;
    private PixelRect _placed;
    private bool _updatingFilter;
    private bool _menuOpen;

    public SwitcherWindow()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _thumbnails.Clear();
            }
        };
    }

    public bool UseDarkFrame { get; set; } = true;

    public event Action? ActivateRequested;
    public event Action? DismissRequested;
    public event Action? SettingsRequested;
    public event Action<AppSettings>? SettingsMutated;

    public void Present(SwitcherSession session, WindowSnapshot snapshot, IconCache icons, MonitorWorkArea monitor)
    {
        _session = session;
        _snapshot = snapshot;
        _iconCache = icons;
        var preferred = OverlayPlacement.PreferredSize(monitor.WorkArea);
        _placed = OverlayPlacement.Center(monitor.WorkArea, preferred.Width, preferred.Height);
        var scale = monitor.DpiX <= 0 ? 1d : monitor.DpiX / 96d;
        Width = Math.Max(320, _placed.Width / scale);
        Height = Math.Max(240, _placed.Height / scale);
        Left = _placed.X / scale;
        Top = _placed.Y / scale;
        Rebuild();
        if (!IsVisible)
        {
            Show();
        }

        Activate();
        FilterBox.Focus();
        Dispatcher.BeginInvoke(new Action(PlaceExact), DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(new Action(UpdateThumbnails), DispatcherPriority.Background);
    }

    public void UpdateSnapshot(WindowSnapshot snapshot) => _snapshot = snapshot;

    public void Refresh()
    {
        Rebuild();
        Dispatcher.BeginInvoke(new Action(UpdateThumbnails), DispatcherPriority.Background);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowChromeNative.SetToolWindow(hwnd);
        WindowChromeNative.ApplyRounded(hwnd, UseDarkFrame);
        PlaceExact();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        _thumbnails.Clear();
    }

    private void PlaceExact()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && _placed.Width > 0)
        {
            WindowChromeNative.Place(hwnd, _placed);
        }
    }

    private void Rebuild()
    {
        if (_session is null)
        {
            return;
        }

        _updatingFilter = true;
        if (FilterBox.Text != _session.Query)
        {
            FilterBox.Text = _session.Query;
            FilterBox.CaretIndex = FilterBox.Text.Length;
        }

        _updatingFilter = false;
        HighlightMode(_session.Settings.LastMode);
        FillProjectScope();
        RowsHost.Children.Clear();
        _icons.Clear();
        _thumbs.Clear();
        if (_session.Layout.TotalCount == 0)
        {
            RowsHost.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_session.Query) ? "No windows to show." : "No windows match that filter.",
                Margin = new Thickness(12),
                Foreground = Brush("AppMuted")
            });
            return;
        }

        foreach (var row in _session.Layout.Rows)
        {
            if (row.ShowSectionHeader)
            {
                RowsHost.Children.Add(SectionHeader(row.SectionTitle, row.SectionColor));
            }

            if (!string.IsNullOrWhiteSpace(row.SubsectionTitle))
            {
                RowsHost.Children.Add(new TextBlock
                {
                    Text = row.SubsectionTitle,
                    Margin = new Thickness(12, 4, 8, 4),
                    Foreground = Brush("AppMuted"),
                    FontSize = 12
                });
            }

            var wrap = new WrapPanel { Margin = new Thickness(8, 0, 8, 8) };
            foreach (var item in row.Items)
            {
                wrap.Children.Add(CreateCard(item));
            }

            RowsHost.Children.Add(wrap);
        }

        RequestIcons();
        Dispatcher.BeginInvoke(new Action(ScrollSelectedIntoView), DispatcherPriority.Loaded);
    }

    private UIElement SectionHeader(string title, string? color)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 8, 4) };
        if (ColorValidator.Normalize(color) is { } hex)
        {
            panel.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = HexBrush(hex),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15 });
        return panel;
    }

    private Border CreateCard(SwitcherItem item)
    {
        var selected = _session?.SelectedItem?.Hwnd == item.Hwnd;
        var card = new Border
        {
            Width = 196,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(12),
            Background = Brush(selected ? "AppSelected" : "AppCard"),
            BorderBrush = selected ? Brush("AppAccent") : Brush("AppStroke"),
            BorderThickness = new Thickness(selected ? 2 : 1),
            Padding = new Thickness(8),
            Tag = item.Hwnd,
            Cursor = Cursors.Hand,
            ToolTip = item.Title
        };
        AutomationProperties.SetName(card, item.AppDisplayName + ", " + item.Title);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(104) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var thumb = new Border
        {
            Background = Brush("AppSurface"),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true
        };
        if (ColorValidator.Normalize(item.AccentColor) is { } accent)
        {
            thumb.BorderBrush = HexBrush(accent);
            thumb.BorderThickness = new Thickness(3, 0, 0, 0);
        }

        var letter = new TextBlock
        {
            Text = Letter(item.AppDisplayName),
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("AppMuted")
        };
        var icon = new Image { Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var stack = new Grid();
        stack.Children.Add(letter);
        stack.Children.Add(icon);
        thumb.Child = stack;
        Grid.SetRow(thumb, 0);
        grid.Children.Add(thumb);
        var caption = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        caption.Children.Add(new TextBlock
        {
            Text = item.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = FontWeights.SemiBold
        });
        caption.Children.Add(new TextBlock
        {
            Text = item.IsMinimized ? item.AppDisplayName + " · Minimized" : item.AppDisplayName,
            Foreground = Brush("AppMuted"),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetRow(caption, 1);
        grid.Children.Add(caption);
        card.Child = grid;
        card.MouseLeftButtonUp += (_, _) =>
        {
            _session?.SelectHwnd(item.Hwnd);
            ActivateRequested?.Invoke();
        };
        card.MouseRightButtonUp += (_, e) =>
        {
            _session?.SelectHwnd(item.Hwnd);
            Rebuild();
            OpenMenu(item, card);
            e.Handled = true;
        };
        _icons[item.Hwnd] = icon;
        _thumbs[item.Hwnd] = thumb;
        return card;
    }

    private void OpenMenu(SwitcherItem item, FrameworkElement placement)
    {
        if (_session is null)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = placement };
        menu.Items.Add(Item("Open", () => ActivateRequested?.Invoke()));
        var projects = new MenuItem { Header = "Move to project" };
        projects.Items.Add(Item("Ungrouped", () => Assign(item.Hwnd, null)));
        foreach (var project in _session.Settings.Projects.OrderBy(project => project.Order))
        {
            var captured = project;
            projects.Items.Add(Item(captured.Name, () => Assign(item.Hwnd, captured.Id)));
        }

        projects.Items.Add(new Separator());
        projects.Items.Add(Item("New project…", () => CreateProject(item.Hwnd)));
        menu.Items.Add(projects);
        var colors = new MenuItem { Header = "Window color" };
        foreach (var hex in ColorPalette.Presets)
        {
            var captured = hex;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border { Width = 12, Height = 12, Background = HexBrush(captured), Margin = new Thickness(0, 0, 8, 0) });
            row.Children.Add(new TextBlock { Text = captured });
            colors.Items.Add(new MenuItem { Header = row, Command = new Relay(() => SetColor(item.Hwnd, captured)) });
        }

        colors.Items.Add(Item("Custom hex…", () =>
        {
            var hex = TextPromptWindow.Ask(this, "Window color", "Enter a color such as #4C8DFF.", item.AccentColor ?? "#4C8DFF", value =>
                ColorValidator.IsValid(value) ? null : "Use a color like #4C8DFF.");
            if (hex is not null)
            {
                SetColor(item.Hwnd, hex);
            }
        }));
        colors.Items.Add(Item("Use group color", () =>
        {
            _session.SetTemporaryColor(item.Hwnd, false, null);
            Publish();
        }));
        menu.Items.Add(colors);
        menu.Items.Add(Item("Remember for next time…", () => Remember(item)));
        menu.Items.Add(Item("Rename application…", () => Rename(item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings", () => SettingsRequested?.Invoke()));
        menu.Closed += (_, _) => _menuOpen = false;
        _menuOpen = true;
        menu.IsOpen = true;
    }

    private void Assign(long hwnd, Guid? projectId)
    {
        _session?.AssignTemporary(hwnd, true, projectId);
        Publish();
    }

    private void CreateProject(long hwnd)
    {
        if (_session is null)
        {
            return;
        }

        var name = TextPromptWindow.Ask(this, "New project", "Name this project.");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var (settings, project) = SettingsEditor.AddProject(_session.Settings, name);
        _session.ApplySettings(settings);
        _session.AssignTemporary(hwnd, true, project.Id);
        Publish();
    }

    private void SetColor(long hwnd, string hex)
    {
        _session?.SetTemporaryColor(hwnd, true, hex);
        Publish();
    }

    private void Rename(SwitcherItem item)
    {
        if (_session is null)
        {
            return;
        }

        var name = TextPromptWindow.Ask(this, "Rename application", "This name is only a label in TabMan.", item.AppDisplayName);
        if (name is null)
        {
            return;
        }

        _session.ApplySettings(SettingsEditor.SetAppDisplayName(_session.Settings, item.AppKey, name));
        Publish();
    }

    private void Remember(SwitcherItem item)
    {
        if (_session is null)
        {
            return;
        }

        var matchTitle = false;
        var pattern = item.Title.Replace('*', ' ').Replace('?', ' ');
        var dialog = new Window
        {
            Title = "Remember assignment",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = Background
        };
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "TabMan matches this application when the window opens again. Window handles are not saved. The title is saved only if you check the box."
        });
        var box = new CheckBox { Content = "Only when the title matches", Margin = new Thickness(0, 12, 0, 0) };
        var input = new TextBox { Text = pattern, Margin = new Thickness(0, 8, 0, 0), IsEnabled = false };
        box.Checked += (_, _) => input.IsEnabled = true;
        box.Unchecked += (_, _) => input.IsEnabled = false;
        root.Children.Add(box);
        root.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var ok = new Button { Content = "Remember", IsDefault = true, Margin = new Thickness(0) };
        ok.Click += (_, _) =>
        {
            matchTitle = box.IsChecked == true;
            pattern = input.Text.Trim();
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        dialog.Content = root;
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var projectColor = _session.Settings.Projects.FirstOrDefault(project => project.Id == item.ProjectId)?.ColorHex;
        var color = item.AccentColor is not null && !string.Equals(item.AccentColor, projectColor, StringComparison.OrdinalIgnoreCase)
            ? item.AccentColor
            : null;
        var rule = new PersistentMatchRule
        {
            Id = Guid.NewGuid(),
            ProcessName = item.ProcessName,
            TitleGlob = matchTitle ? pattern : null,
            AppliesProject = true,
            ProjectGroupId = item.ProjectId,
            ColorHex = color,
            Priority = matchTitle ? 10 : 1,
            Enabled = true
        };
        _session.ApplySettings(SettingsEditor.AddMatchRule(_session.Settings, rule));
        _session.ClearTemporary(item.Hwnd);
        Publish();
    }

    private void Publish()
    {
        if (_session is null)
        {
            return;
        }

        SettingsMutated?.Invoke(_session.Settings);
        Rebuild();
    }

    private void RequestIcons()
    {
        if (_session is null || _iconCache is null || _snapshot is null)
        {
            return;
        }

        var paths = _snapshot.ExePathsByProcessId;
        var work = _session.Layout.Items
            .Select(item => (item.Hwnd, item.ProcessId, item.ProcessName))
            .ToList();
        var cache = _iconCache;
        Task.Run(() =>
        {
            foreach (var item in work)
            {
                paths.TryGetValue(item.ProcessId, out var path);
                var pixels = cache.Get(item.ProcessName, path, item.Hwnd);
                if (pixels is null)
                {
                    continue;
                }

                Dispatcher.BeginInvoke(new Action(() => ApplyIcon(item.Hwnd, pixels)));
            }
        });
    }

    private void ApplyIcon(long hwnd, IconPixels pixels)
    {
        if (!_icons.TryGetValue(hwnd, out var image))
        {
            return;
        }

        var bitmap = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgra32, null, pixels.Bgra, pixels.Stride);
        bitmap.Freeze();
        image.Source = bitmap;
        image.Visibility = Visibility.Visible;
        if (image.Parent is Grid grid && grid.Children.Count > 0)
        {
            grid.Children[0].Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateThumbnails()
    {
        if (_session is null || !_session.Settings.ShowLiveThumbnails || !IsVisible)
        {
            _thumbnails.Clear();
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            return;
        }

        var toDevice = target.TransformToDevice;
        var alive = new HashSet<long>();
        foreach (var pair in _thumbs)
        {
            alive.Add(pair.Key);
            var host = pair.Value;
            if (!host.IsVisible || host.ActualWidth < 4)
            {
                continue;
            }

            try
            {
                var origin = host.TransformToAncestor(this).Transform(new Point(0, 0));
                var deviceOrigin = toDevice.Transform(origin);
                var size = toDevice.Transform(new Vector(host.ActualWidth, host.ActualHeight));
                _thumbnails.Update(
                    hwnd,
                    pair.Key,
                    (int)deviceOrigin.X,
                    (int)deviceOrigin.Y,
                    (int)(deviceOrigin.X + size.X),
                    (int)(deviceOrigin.Y + size.Y));
            }
            catch (InvalidOperationException)
            {
                // The card is not in the visual tree yet.
            }
        }

        _thumbnails.RetainOnly(alive);
    }

    private void ScrollSelectedIntoView()
    {
        if (_session?.SelectedItem is null)
        {
            return;
        }

        foreach (var child in RowsHost.Children.OfType<WrapPanel>())
        {
            foreach (var card in child.Children.OfType<Border>())
            {
                if (card.Tag is long hwnd && hwnd == _session.SelectedItem.Hwnd)
                {
                    card.BringIntoView();
                    return;
                }
            }
        }
    }

    private void FillProjectScope()
    {
        if (_session is null)
        {
            return;
        }

        var show = _session.Settings.LastMode == SwitcherMode.Project;
        ProjectScopeBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            return;
        }

        ProjectScopeBox.SelectionChanged -= ProjectScope_Changed;
        ProjectScopeBox.Items.Clear();
        ProjectScopeBox.Items.Add(new ScopeChoice("All projects", ProjectScopeMode.All, null));
        ProjectScopeBox.Items.Add(new ScopeChoice("Ungrouped", ProjectScopeMode.Ungrouped, null));
        foreach (var project in _session.Settings.Projects.OrderBy(project => project.Order))
        {
            ProjectScopeBox.Items.Add(new ScopeChoice(project.Name, ProjectScopeMode.Single, project.Id));
        }

        ScopeChoice? match = null;
        foreach (ScopeChoice choice in ProjectScopeBox.Items)
        {
            if (choice.Mode == _session.Settings.ProjectScope && choice.ProjectId == _session.Settings.VisibleProjectId)
            {
                match = choice;
            }
        }

        ProjectScopeBox.SelectedItem = match ?? ProjectScopeBox.Items[0];
        ProjectScopeBox.SelectionChanged += ProjectScope_Changed;
    }

    private void HighlightMode(SwitcherMode mode)
    {
        Mark(ApplicationButton, mode == SwitcherMode.Application);
        Mark(ProjectButton, mode == SwitcherMode.Project);
        Mark(RecentButton, mode == SwitcherMode.Recent);
    }

    private static void Mark(Button button, bool active)
    {
        button.BorderBrush = active ? Brush("AppAccent") : Brush("AppStroke");
        button.BorderThickness = new Thickness(active ? 2 : 1);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_session is null || _menuOpen)
        {
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.D1)
        {
            SetMode(SwitcherMode.Application);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.D2)
        {
            SetMode(SwitcherMode.Project);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.D3)
        {
            SetMode(SwitcherMode.Recent);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.OemComma)
        {
            SettingsRequested?.Invoke();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                if (_session.HandleEscape())
                {
                    DismissRequested?.Invoke();
                }
                else
                {
                    Rebuild();
                }

                e.Handled = true;
                break;
            case Key.Enter:
                ActivateRequested?.Invoke();
                e.Handled = true;
                break;
            case Key.Tab:
                _session.Move(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? NavigationDirection.Previous : NavigationDirection.Next);
                Rebuild();
                e.Handled = true;
                break;
            case Key.Left:
                Move(NavigationDirection.Left, e);
                break;
            case Key.Right:
                Move(NavigationDirection.Right, e);
                break;
            case Key.Up:
                Move(NavigationDirection.Up, e);
                break;
            case Key.Down:
                Move(NavigationDirection.Down, e);
                break;
            case Key.Home:
                Move(NavigationDirection.First, e);
                break;
            case Key.End:
                Move(NavigationDirection.Last, e);
                break;
            case Key.Apps:
            case Key.F10 when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                OpenSelectedMenu();
                e.Handled = true;
                break;
        }
    }

    private void Move(NavigationDirection direction, KeyEventArgs e)
    {
        _session?.Move(direction);
        Rebuild();
        e.Handled = true;
    }

    private void OpenSelectedMenu()
    {
        if (_session?.SelectedItem is not { } item)
        {
            return;
        }

        foreach (var panel in RowsHost.Children.OfType<WrapPanel>())
        {
            foreach (var card in panel.Children.OfType<Border>())
            {
                if (card.Tag is long hwnd && hwnd == item.Hwnd)
                {
                    OpenMenu(item, card);
                    return;
                }
            }
        }
    }

    private void SetMode(SwitcherMode mode)
    {
        _session?.SetMode(mode);
        Publish();
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFilter || _session is null || FilterBox.Text == _session.Query)
        {
            return;
        }

        _session.SetQuery(FilterBox.Text);
        Rebuild();
    }

    private void ApplicationMode_Click(object sender, RoutedEventArgs e) => SetMode(SwitcherMode.Application);
    private void ProjectMode_Click(object sender, RoutedEventArgs e) => SetMode(SwitcherMode.Project);
    private void RecentMode_Click(object sender, RoutedEventArgs e) => SetMode(SwitcherMode.Recent);

    private void ProjectScope_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_session is null || ProjectScopeBox.SelectedItem is not ScopeChoice choice)
        {
            return;
        }

        if (choice.Mode == _session.Settings.ProjectScope && choice.ProjectId == _session.Settings.VisibleProjectId)
        {
            return;
        }

        _session.SetProjectScope(choice.Mode, choice.ProjectId);
        Publish();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void Close_Click(object sender, RoutedEventArgs e) => DismissRequested?.Invoke();
    private void Scroller_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateThumbnails();

    private static MenuItem Item(string header, Action action) => new() { Header = header, Command = new Relay(action) };

    private static string Letter(string name) =>
        string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();

    private static Brush Brush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? System.Windows.Media.Brushes.Gray;

    private static Brush HexBrush(string hex)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch (FormatException)
        {
            return System.Windows.Media.Brushes.Gray;
        }
    }

    private sealed record ScopeChoice(string Label, ProjectScopeMode Mode, Guid? ProjectId)
    {
        public override string ToString() => Label;
    }

    private sealed class Relay(Action action) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
