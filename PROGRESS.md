# Progress

## 2026-11-14

- Added TabMan, a Windows 11-style window switcher (Application, Project, and Recent modes).
- Core grouping, filtering, navigation, rules, and settings live in `src/TabMan.Core` and build on Linux.
- Win32 discovery, activation, hotkeys, tray, thumbnails, and the optional Alt+Tab hook live in `src/TabMan.Platform.Windows`.
- The WPF overlay, settings window, and tray host live in `src/TabMan.App`.
- Unit tests cover grouping, filtering, navigation, persistent rules, and settings serialization. Run with `dotnet test`.
- On this Linux machine the Windows projects compile with the Windows targeting pack. They were not run, because WPF and Win32 need Windows.
- `dotnet publish -r win-x64 --self-contained true` produced `artifacts/win-x64/TabMan.exe` (a Windows GUI executable). That folder is not committed. It was not launched.
- Packaged that folder as `TabMan-win-x64.zip` and published GitHub prerelease `v1.0.0-preview.1`. The zip is not in git.
- Portable publish command: `packaging/publish-portable.ps1` (self-contained `win-x64`).
- Not done on Windows: clicking through the real overlay, confirming hotkeys, thumbnails, and the optional Alt+Tab hook.

Resume from `README.md` section "Build from source" if a Windows PC is available.
