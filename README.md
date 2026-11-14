# TabMan

TabMan is a small Windows window switcher. It sits in the notification area and opens an overlay of your open windows. It does not replace Windows unless you turn that on, and it never needs an administrator account.

You can:

- See open windows with an icon, a title, and a live preview when Windows allows it.
- Switch with the keyboard or the mouse. Minimized windows are restored.
- Group windows by application, by a project you name, or as a recent list.
- Give projects and individual windows colors. A window color wins over the project color.
- Rename how an application appears in the switcher.
- Keep those choices on this PC.

TabMan does not close windows, edit documents, or read the contents of files. It only uses the window title you already see on the taskbar.

## Run it

You need 64-bit Windows 10 or Windows 11.

1. Build the portable folder (see below) or copy a `win-x64` folder someone already built.
2. Open that folder and double-click `TabMan.exe`.
3. A TabMan icon appears in the notification area (the system tray). You may need to expand the hidden icons arrow.
4. Press **Ctrl+Alt+Space**, or left-click the tray icon.

The switcher is an overlay, not a full-screen program. **Enter** or a click opens the selected window. **Esc** closes the overlay and leaves you where you were. Press the shortcut again to close it without switching.

If TabMan is already running, starting it again does nothing. Use the tray icon.

### Keys in the switcher

| Key | Action |
| --- | --- |
| Arrow keys | Move between windows |
| Tab / Shift+Tab | Next / previous window |
| Enter | Open the selected window |
| Esc | Clear the filter, then close |
| Type | Filter by title, application, or project |
| Ctrl+1 / Ctrl+2 / Ctrl+3 | Application / Project / Recent |
| Shift+F10 | Menu for the selected window |
| Ctrl+, | Settings |

Right-click a window for the same menu: move it to a project, set a color, remember that choice, or rename the application. Those actions do not open the full settings window.

## Projects and rules

**Application** groups windows by program (Excel, Chrome, and so on). Inside a group, order is recent or A to Z. Change that in Settings.

**Project** uses groups you create. Each group has a name and a color. Windows you have not assigned stay in **Ungrouped**. Inside a project, windows are still grouped by application. The dropdown can show every project or just one.

**Recent** is a most-recently-used list, similar to Alt+Tab. The highlight starts on the previous window so one Enter switches back.

A move you make from the menu lasts until that window closes. **Remember for next time** stores a rule for the application. Window handles change every time a program opens, so TabMan does not save them. It matches the application name, plus a title pattern only if you ask for one. Title patterns use `*` for any text and `?` for one character.

## Settings

Right-click the tray icon and choose **Settings**, or press the Settings button on the overlay.

- Theme: system, dark, or light.
- Shortcut: click the box and press a new shortcut. It must include Ctrl, Alt, Shift, or the Windows key. Esc in that box restores Ctrl+Alt+Space.
- Live previews can be turned off. Preview pictures are never saved.
- Run at sign-in is off until you check it.
- Alt+Tab integration is off until you check it.
- Remembering recent applications between launches is off until you check it. That file stores application ids and times only, never titles or file paths.
- Diagnostic codes are off until you check them. The log does not contain window titles.

## Turn keyboard integration on or off

Two separate switches:

1. **The TabMan shortcut** (Ctrl+Alt+Space by default). This is on when TabMan is running. Change or restore it in Settings. If Windows says the shortcut is already taken, the tray icon still opens the switcher. Pick another shortcut and save.
2. **Alt+Tab**. Off by default. Settings → General → **Also open TabMan for Alt+Tab**.

The Alt+Tab option uses a keyboard hook inside TabMan. It does not install a driver, does not edit system files, and does not inject code into other programs. When you clear the checkbox or exit TabMan, the hook is removed. If Windows will not allow the hook, normal Alt+Tab keeps working and TabMan leaves it alone. Treat this option as best-effort. The custom shortcut is the reliable way to open TabMan.

## Build from source

Install the .NET 10 SDK on Windows. In this folder:

```powershell
dotnet test TabMan.slnx -c Release
powershell -ExecutionPolicy Bypass -File .\packaging\publish-portable.ps1
```

The portable program is `artifacts\win-x64\TabMan.exe`. That folder is self-contained: the people you give it to do not need to install .NET. You can copy the whole `win-x64` folder anywhere. No installer and no administrator rights.

`build.sh` runs the same tests. It is useful on a non-Windows machine for the logic tests. Publishing the app still requires the Windows publish command above (it can be run from Linux if the Windows targeting packs download, but the resulting `.exe` only runs on Windows).

## Remove TabMan completely

1. Right-click the tray icon and choose **Exit**.
2. If you turned on "Run when I sign in", open Settings and turn it off before exiting. That deletes the `TabMan` value under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`.
3. Delete the folder that contains `TabMan.exe`.
4. Delete the data folder. Settings shows the path. It is usually:

   `%LOCALAPPDATA%\TabMan`

   That folder holds `settings.json` and, only if you enabled them, `history.json` and `diagnostics.log`.

You can also use **Delete saved settings and history** on the Data tab. That does not delete the program folder.

## Privacy

- No account, no cloud, no telemetry, and no network calls from the app.
- Settings stay in your user profile.
- Window titles are shown in the overlay so you can tell windows apart. They are not written to the diagnostic log.
- Remembered rules store a process name and a title pattern only when you type or confirm that pattern.
- Live previews use the Windows thumbnail API while the overlay is open. They are not written to disk, even if an old settings file asks for that.
- Executable paths are used in memory to load icons and are not saved.

## What was verified where

The grouping, search, keyboard navigation, project rules, and settings file format have automated tests and were run on Linux with the .NET 10 SDK.

The WPF windows and the Win32 calls were compiled with Microsoft's Windows targeting libraries. They were **not** launched on Windows in this environment, so the overlay, hotkey, tray icon, previews, and optional Alt+Tab hook still need a pass on a Windows PC.

## Notes for IT

- Per-user only. The manifest requests `asInvoker`. Nothing is written under `Program Files` or `HKLM`.
- Optional sign-in startup is a single `HKCU` Run value named `TabMan`.
- The global shortcut uses `RegisterHotKey`. The optional Alt+Tab behavior uses an in-process `WH_KEYBOARD_LL` hook and is disabled by default.
- No service, no driver, no shell extension, and no extra open ports.
- High DPI is per-monitor (PerMonitorV2). The overlay opens on the monitor under the pointer.
- The switcher's own windows are left out of the list. Cloaked, owned, and tool windows are skipped using the usual Alt+Tab filters.
- Window list updates come from `SetWinEventHook`, not a polling timer. Icons and previews are filled after the overlay is on screen.
