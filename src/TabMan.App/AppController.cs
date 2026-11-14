using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TabMan.App.Overlay;
using TabMan.App.SettingsUi;
using TabMan.App.Theme;
using TabMan.Core;
using TabMan.Platform.Windows;

namespace TabMan.App;

public sealed class AppController : IDisposable
{
    private readonly SettingsRepository _repository = new(AppPaths.DataDirectory);
    private readonly DiagnosticLog _log = new();
    private readonly IconCache _icons = new();
    private readonly AltTabHook _altTab = new();
    private readonly SwitcherWindow _switcher = new();
    private SingleInstance? _instance;
    private WindowTracker? _tracker;
    private TrayIcon? _tray;
    private HwndSource? _messages;
    private SwitcherSession _session = new(SettingsSerializer.CreateDefault());
    private AppSettings _settings = SettingsSerializer.CreateDefault();
    private List<HistoryEntry> _history = [];
    private int _refreshQueued;
    private bool _started;

    public string? StartupFailure { get; private set; }

    public bool Start()
    {
        _instance = new SingleInstance();
        if (!_instance.IsPrimary)
        {
            return false;
        }

        var loaded = _repository.Load();
        _settings = loaded.Settings;
        _session = new SwitcherSession(_settings);
        _log.Enabled = _settings.DiagnosticsEnabled;
        if (loaded.UsedDefaults && loaded.Warning is not null)
        {
            _log.Event("SETTINGS_RESET");
        }

        _history = HistoryStore.Load(_repository.HistoryPath, _settings.PersistHistory).ToList();
        try
        {
            StartShell();
        }
        catch (Exception ex)
        {
            StartupFailure = ex.GetType().Name;
            if (_log.Enabled)
            {
                _log.Event("START_FAILED", ex.HResult);
                FlushLog();
            }

            Dispose();
            return false;
        }

        return true;
    }

    private void StartShell()
    {
        _switcher.UseDarkFrame = ThemeService.Apply(_settings.Theme);
        _switcher.ActivateRequested += ActivateSelected;
        _switcher.DismissRequested += HideSwitcher;
        _switcher.SettingsRequested += ShowSettings;
        _switcher.SettingsMutated += SaveSessionSettings;
        _tracker = new WindowTracker(Environment.ProcessId);
        _tracker.Changed += OnWindowsChanged;
        _tracker.Start();
        _messages = CreateMessageWindow();
        _tray = new TrayIcon();
        _tray.OpenSwitcher += ToggleSwitcher;
        _tray.OpenSettings += ShowSettings;
        _tray.ExitRequested += Exit;
        _tray.Attach(_messages.Handle);
        _altTab.ActionRequested += action =>
            Application.Current.Dispatcher.BeginInvoke(new Action(() => OnAltTab(action)));
        ApplyIntegrations();
        _started = true;
        _log.Event("READY");
        FlushLog();
    }

    public void LogFault(Exception exception) => _log.Event("UI_FAULT", exception.HResult);

    public void Dispose()
    {
        if (_started)
        {
            try
            {
                SaveHistory();
                FlushLog();
            }
            catch (IOException)
            {
                // Exit still removes hooks and the hotkey.
            }
        }

        if (_messages is not null)
        {
            HotkeyNative.Unregister(_messages.Handle);
            _messages.Dispose();
            _messages = null;
        }

        _altTab.Dispose();
        _tray?.Dispose();
        _tray = null;
        _tracker?.Dispose();
        _tracker = null;
        _instance?.Dispose();
        _instance = null;
        _started = false;
    }

    private HwndSource CreateMessageWindow()
    {
        // A real hidden window, not a message-only HWND. Tray icons and RegisterHotKey
        // are more reliable on a top-level window. It is a tool window so it stays out of Alt+Tab.
        var parameters = new HwndSourceParameters("TabMan")
        {
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = 0x00000080 | 0x08000000,
            PositionX = -32000,
            PositionY = -32000,
            Width = 1,
            Height = 1
        };
        var source = new HwndSource(parameters);
        source.AddHook(WndProc);
        return source;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyNative.Message && wParam.ToInt32() == HotkeyNative.Id)
        {
            handled = true;
            ToggleSwitcher();
            return IntPtr.Zero;
        }

        _tray?.Handle(msg, lParam);
        return IntPtr.Zero;
    }

    private void ToggleSwitcher()
    {
        if (_switcher.IsVisible)
        {
            HideSwitcher();
            return;
        }

        ShowSwitcher();
    }

    private void ShowSwitcher()
    {
        if (_tracker is null)
        {
            return;
        }

        var snapshot = _tracker.Capture();
        var windows = WithHistory(snapshot.Windows);
        _session.ApplySettings(_settings);
        _session.Open(windows);
        _switcher.UseDarkFrame = ThemeService.Apply(_settings.Theme);
        _switcher.Present(_session, snapshot, _icons, MonitorLocator.FromCursor());
        _log.Event("ENUM_OK", windows.Count);
    }

    private void HideSwitcher()
    {
        _altTab.Disengage();
        _session.Close();
        if (_switcher.IsVisible)
        {
            _switcher.Hide();
        }
    }

    private void ActivateSelected()
    {
        var item = _session.SelectedItem;
        if (item is null)
        {
            HideSwitcher();
            return;
        }

        var hwnd = item.Hwnd;
        var appKey = item.AppKey;
        if (_switcher.IsVisible)
        {
            _switcher.Hide();
        }

        _session.Close();
        _altTab.Disengage();
        if (!WindowActivator.IsAlive(hwnd) || !WindowActivator.TryActivate(hwnd))
        {
            _log.Event("ACTIVATE_FAILED");
            ShowSwitcher();
            return;
        }

        _log.Event("ACTIVATE_OK");
        RememberActivation(appKey);
    }

    private void ShowSettings()
    {
        HideSwitcher();
        var live = _tracker?.Capture().Windows ?? [];
        var dialog = new SettingsWindow(_session.Settings, live, AppPaths.DataDirectory);
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.DeletePersonalData)
        {
            _repository.DeletePersonalData();
            _history.Clear();
            _settings = SettingsSerializer.CreateDefault();
            _log.Event("SETTINGS_RESET");
        }
        else
        {
            _settings = dialog.Result;
            _repository.Save(_settings);
            _log.Event("SETTINGS_SAVED");
        }

        _session.ApplySettings(_settings);
        ApplyIntegrations();
    }

    private void SaveSessionSettings(AppSettings settings)
    {
        _settings = settings;
        try
        {
            _repository.Save(_settings);
            _log.Event("SETTINGS_SAVED");
        }
        catch (IOException)
        {
            _log.Event("SETTINGS_FAILED");
        }
    }

    private void ApplyIntegrations()
    {
        _switcher.UseDarkFrame = ThemeService.Apply(_settings.Theme);
        _log.Enabled = _settings.DiagnosticsEnabled;
        if (_messages is not null)
        {
            var registered = HotkeyNative.TryRegister(_messages.Handle, _settings.Shortcut);
            _log.Event(registered ? "HOTKEY_OK" : "HOTKEY_FAILED");
        }

        try
        {
            StartupRegistration.Apply(_settings.RunAtStartup, Environment.ProcessPath);
            _log.Event(_settings.RunAtStartup ? "STARTUP_ON" : "STARTUP_OFF");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _log.Event("STARTUP_FAILED");
        }

        if (_settings.AltTabIntegrationEnabled)
        {
            _log.Event(_altTab.TryInstall() ? "HOOK_OK" : "HOOK_FAILED");
        }
        else
        {
            _altTab.Remove();
        }

        if (!_settings.PersistHistory)
        {
            _history.Clear();
            HistoryStore.Save(_repository.HistoryPath, [], false);
        }

        FlushLog();
    }

    private void OnAltTab(AltTabAction action)
    {
        if (!_settings.AltTabIntegrationEnabled || !_altTab.IsInstalled)
        {
            return;
        }

        if (action.Activate)
        {
            if (_switcher.IsVisible)
            {
                ActivateSelected();
            }

            return;
        }

        var wasVisible = _switcher.IsVisible;
        if (!wasVisible)
        {
            ShowSwitcher();
            if (action.CycleBackward)
            {
                _session.Move(NavigationDirection.Last);
                _switcher.Refresh();
            }

            return;
        }

        _session.Move(action.CycleBackward ? NavigationDirection.Previous : NavigationDirection.Next);
        _switcher.Refresh();
    }

    private void OnWindowsChanged(object? sender, EventArgs e)
    {
        if (!_switcher.IsVisible)
        {
            return;
        }

        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    if (_tracker is not null && _switcher.IsVisible)
                    {
                        var snapshot = _tracker.Capture();
                        _session.ReplaceWindows(WithHistory(snapshot.Windows));
                        _switcher.UpdateSnapshot(snapshot);
                        _switcher.Refresh();
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _refreshQueued, 0);
                }
            };
            timer.Start();
        }));
    }

    private IReadOnlyList<WindowInfo> WithHistory(IReadOnlyList<WindowInfo> windows)
    {
        if (!_settings.PersistHistory || _history.Count == 0)
        {
            return windows;
        }

        var hints = _history
            .GroupBy(entry => entry.AppKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Max(entry => entry.LastActiveUtcTicks), StringComparer.OrdinalIgnoreCase);
        return windows.Select(window =>
        {
            if (window.LastActiveUtcTicks != 0)
            {
                return window;
            }

            var key = AppIdentityResolver.Resolve(window, _settings.AppGroupingRules, _settings.AppDisplayNames).Key;
            return hints.TryGetValue(key, out var ticks) ? window with { LastActiveUtcTicks = ticks } : window;
        }).ToList();
    }

    private void RememberActivation(string appKey)
    {
        if (!_settings.PersistHistory || string.IsNullOrWhiteSpace(appKey))
        {
            return;
        }

        _history.RemoveAll(entry => string.Equals(entry.AppKey, appKey, StringComparison.OrdinalIgnoreCase));
        _history.Insert(0, new HistoryEntry(appKey, DateTime.UtcNow.Ticks));
        if (_history.Count > 50)
        {
            _history.RemoveRange(50, _history.Count - 50);
        }

        HistoryStore.Save(_repository.HistoryPath, _history, true);
    }

    private void SaveHistory()
    {
        if (!_settings.PersistHistory)
        {
            HistoryStore.Save(_repository.HistoryPath, [], false);
            return;
        }

        HistoryStore.Save(_repository.HistoryPath, _history, true);
    }

    private void FlushLog()
    {
        if (!_log.Enabled)
        {
            return;
        }

        try
        {
            _log.FlushTo(Path.Combine(AppPaths.DataDirectory, "diagnostics.log"));
        }
        catch (IOException)
        {
            // Diagnostics must not stop the switcher.
        }
    }

    private void Exit()
    {
        Application.Current.Shutdown();
    }
}
