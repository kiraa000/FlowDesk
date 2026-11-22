# FlowDesk

FlowDesk is a native Windows 11 app that saves the windows you have open as a **workspace** and puts them back
with one click: it relaunches the apps, files, folders and websites, and moves every window to where it was
(including other monitors and maximized windows). It also arranges real windows into **layouts** and runs
**focus sessions** that close distracting apps.

Built with C#, .NET 10 and WinUI 3 (Windows App SDK), unpackaged and self-contained.

Current version: **FlowDesk 1.2.1**. New here? Read [GETTING-STARTED.md](GETTING-STARTED.md) (install, use, develop, release).
See [release notes](docs/RELEASE-NOTES-1.2.1.md) for what changed and important behaviour changes.

## Features

- **Workspaces:** save what's open now, including windows on other virtual desktops and the desktop each one
  is on, edit items (apps, files, folders, website links), open a workspace and watch each window come back
  into place. Switching A -> B -> A returns A exactly as you left it.
- **Layouts:** two columns, primary + sidebar, grid or cascade on any monitor, then save the result as the
  workspace's positions.
- **Focus:** 25 / 50 / 90 minute sessions with blocked and allowed apps. Gentle (reminder), Normal (closes the
  app, "Allow 5 minutes" from the notification) or Strict (closes it every time). Keeps running in the tray.
- **Onboarding tour:** eight teaching tips through workspaces, layouts, focus, the palette and Calm Windows. It opens on first run
  and can be replayed from Settings; Back, Next, Skip and Escape work throughout.
- **Settings:** system/light/dark theme, minimize on switch, 10-60 second launch timeout, start with Windows,
  the terminal program for Alt+Enter, the data folder and the app version.
- **Window shortcuts (optional, off by default):** snap and cycle window sizes, move focus, switch and move between
  virtual desktops, open a terminal, reload or pause the shortcut engine, and use Win+wheel for desktop switching,
  desktop moves and window search.
- **Quick Look (optional, off by default):** select a file in File Explorer or on the desktop and press **Space** to
  preview it; **Space** or **Esc** closes the preview, **Enter** opens the file, and arrow keys keep moving the
  selection while the preview follows.
- **Command palette:** search apps, windows, files and bookmarks; calculate, convert units, run commands, search the web or explicitly submit an AI question.
- **Hot corners:** choose actions for screen corners, with a dwell delay and full-screen safeguards.
- **Hotkeys:** view, enable and rebind shortcuts, with conflict and live availability information.
- **Calm Windows:** check Windows suggestions and distractions, apply chosen changes and undo them from a local journal.
- **Advanced:** opt-in tool detection and installation with explicit consent.
- **Contexts:** attach audio output, Do Not Disturb and Focus preferences to a workspace.
- **Clipboard history:** opt-in, memory-only text history available through the palette, with guarded paste-back.
- **Settings transfer:** export and preview imports, choose Add or Replace, and undo an import before the next change.
- **Game mode:** suspend shortcuts other than the palette, with a Ctrl+Alt+G toggle and on-screen status.
- **Desktop conveniences:** friendly monitor names, workspace delete undo and item editing, Focus extensions, and tray workspace/Focus actions.
- Mica, light/dark theme, keyboard support, Windows notifications.

## Window shortcuts (optional)

Turn them on in Settings > **Window shortcuts**. The same section sets the terminal program used by Alt+Enter:
leave the field blank to use Windows Terminal when it is available, otherwise Command Prompt (FlowDesk adds no
arguments); **Use default** clears a custom program. **Resize touching windows together** (on by default) makes a
resize also move the windows that share the border, so snapped layouts keep fitting; turn it off to resize only the
active window. Dragging a shared border with the mouse updates the touching windows when you release the button.

| Shortcut | Action |
| --- | --- |
| Alt+1 … Alt+5 | Go to virtual desktop 1–5 |
| Alt+Shift+1 … Alt+Shift+5 | Move the active window to desktop 1–5 and follow it |
| Alt+W / A / S / D (or arrows) | Snap up / left / down / right; repeated presses cycle 1/2 -> 1/3 -> 2/3 of the height or width |
| Alt+Shift+W / A / S / D (or arrows) | Focus the window above / left / below / right |
| Alt+F | Maximize or restore the active window |
| Alt+M | Minimize the active window |
| Alt+Q | Minimize the active window and focus the next one |
| Alt+Shift+Space | Center the active window |
| Alt+Enter | Open a terminal in your profile folder |
| Alt+Shift+R | Reload the shortcut engine and re-check the desktop library |
| Alt+Shift+E | Pause window shortcuts |
| Alt+Shift+0 | Toggle pause/resume for window shortcuts |
| Alt+Ctrl+Left / Right / Up / Down | Move the active window's border left / right / up / down; touching windows resize with it |
| Alt+Shift+Wheel | Resize the active window (touching windows resize with it) |
| Win+Wheel | Switch to the previous/next virtual desktop (wheel up/down) |
| Win+Shift+Wheel | Move the active window to the previous/next desktop and follow it |
| Win+Alt+Wheel | Search open windows: wheel up cycles, wheel down undoes or minimizes |

The Alt desktop shortcuts need `VirtualDesktopAccessor.dll`; see [GETTING-STARTED.md](GETTING-STARTED.md) for the
supported Windows versions.

## Quick Look (optional)

Turn it on in Settings > **Quick Look**. Select a file in File Explorer or on the desktop and press **Space**. **Space**
or **Esc** closes the preview, **Enter** opens the file in its default app, and arrow keys still move the Explorer
selection while the preview follows.

It previews images (jpg/jpeg/png/gif/bmp/webp/ico/tif/tiff; HEIC and AVIF when Windows has the codec), PDF, video and
audio, text and code files (first 1 MB), and folders. Other files show a thumbnail with name, type, size and dates
plus an **Open** button. Office documents are not previewed in this version. If QuickLook or PowerToys Peek is
running, the Settings card shows a short note (QuickLook uses Space; Peek uses Ctrl+Space by default).

Playback uses whichever codecs Windows has installed. Quick Look does not take Space with a Chinese, Japanese or
Korean keyboard layout active (v1 limit).

Turn it off in the same Settings card; while it is off FlowDesk does not take the Space key.

## Requirements

- Windows 11 (Windows 10 19041+ should work but is not tested).
- [.NET 10 SDK](https://dotnet.microsoft.com/download) 10.0.400 or newer.
- Visual Studio 2022 (workload ".NET desktop development" + Windows App SDK C# templates) **or** Build Tools 2022
  with the Windows 10/11 SDK (10.0.26100 or newer). NuGet restores everything else (Windows App SDK 2.6.5,
  CommunityToolkit, H.NotifyIcon).
- Only to build the installer: [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`ISCC.exe`). It is not needed to
  build, publish or run FlowDesk, and the repo does not install or download it.

## Build, test, run

```powershell
git clone https://github.com/kiraa000/FlowDesk.git
cd FlowDesk
dotnet build FlowDesk.slnx -c Debug
dotnet test tests/FlowDesk.Core.Tests
dotnet test tests/FlowDesk.App.Tests
```

Run the app:

```powershell
.\src\FlowDesk.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\FlowDesk.App.exe
```

Or open `FlowDesk.slnx` in Visual Studio, set `FlowDesk.App` as the startup project (platform x64) and press F5.

Data is stored in `%LOCALAPPDATA%\FlowDesk\` (`flowdesk.json` for workspaces and focus settings,
`app-settings.json` for app settings). Settings has an "Open folder" button for it. Delete the folder to start
fresh. Unhandled errors are logged to `%LOCALAPPDATA%\FlowDesk\logs\flowdesk-YYYYMMDD.log`, keeping the last 7
daily files.

## Publish

`tools\Publish.ps1` produces a folder that runs on a PC without .NET or the Windows App SDK installed:

```powershell
.\tools\Publish.ps1 -Output builds\FlowDesk-<version>
```

Pass `-Output <folder>` to choose the destination. The folder must be new or empty: the script refuses to write
into a folder that already contains files, so an existing build is never overwritten or deleted. It publishes
self-contained win-x64 with trimming, single-file and ReadyToRun explicitly off, prints the measured file count,
size and app version, and fails if the bundled `VirtualDesktopAccessor.dll` or its licence is missing. The .NET 10 SDK is required
on the build machine.

## Build the installer

`tools\Build-Installer.ps1` publishes first, then compiles `installer\FlowDesk.iss` with Inno Setup 6's `ISCC.exe`:

```powershell
.\tools\Build-Installer.ps1 `
    -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
    -PublishOutput "builds\FlowDesk-<version>" `
    -InstallerOutput "builds\installer-<version>"
```

`-IsccPath` is required and is checked, with a clear error, before anything is published. `-PublishOutput` and
`-InstallerOutput` default to `builds\FlowDesk-publish` and `builds\installer`. Both folders must be new or empty;
the script never overwrites or deletes a build. It reads the version from the published `FlowDesk.App.exe`
metadata, passes `/DSourceDir` and `/DOutputDirectory` to ISCC, and expects
`FlowDesk-<version>-win-x64-setup.exe` in the installer folder.

## Install, upgrade, uninstall

- **Install:** run `FlowDesk-<version>-win-x64-setup.exe` (for 1.0.0, `FlowDesk-1.0.0-win-x64-setup.exe`). It is a
  per-user install with no administrator rights; program files go to `%LOCALAPPDATA%\Programs\FlowDesk`, with a
  Start menu shortcut and an optional desktop shortcut.
- **Unsigned build:** neither the app nor the installer is code-signed, so SmartScreen may show "Windows protected
  your PC". Choose **More info > Run anyway**.
- **Upgrade:** run the newer installer. Installs share one application identity, so it upgrades in place and keeps
  your data. It uses Windows Restart Manager to ask a running FlowDesk to close before replacing files; if a Focus
  session or a busy prompt prevents that, exit FlowDesk from the tray and run the installer again.
- **Uninstall:** Settings > Apps > Installed apps > FlowDesk (or the Start menu uninstall entry). It removes the
  program and shortcuts. It does **not** delete `%LOCALAPPDATA%\FlowDesk`, so workspaces, settings and logs are
  kept; delete that folder yourself for a clean start.
- **Start with Windows:** the Settings toggle writes one HKCU Run value pointing at the running exe
  (`Environment.ProcessPath`). Enable it from the installed app so the entry points at
  `%LOCALAPPDATA%\Programs\FlowDesk\FlowDesk.App.exe`; upgrades then keep the same path. Toggle it off and on again
  if you move FlowDesk.
- **Data and logs:** `%LOCALAPPDATA%\FlowDesk\` holds `flowdesk.json`, `app-settings.json` and
  `logs\flowdesk-YYYYMMDD.log` (unhandled errors; the last 7 daily files are kept). A log line records the timestamp,
  source, thread, exception types and method names only; it omits exception messages, stack-trace file paths,
  activation arguments, window titles and the paths of your other apps. Settings > About has "Open log folder".
  Logging is local only: FlowDesk sends no telemetry.
- **VirtualDesktopAccessor.dll:** bundled (MIT, see `third_party/VirtualDesktopAccessor`) and used for
  workspace desktop capture/restore even when the window shortcuts are off. It needs Windows 11 24H2 or newer;
  on other versions choose a matching build in Settings > Window shortcuts > Choose file... (your file takes
  precedence over the bundled one). Without a working DLL, Windows' public COM fallback can still list known
  windows on other desktops but cannot move them, and the restore report says so per item.

## Repository layout

| Path | What it is |
| --- | --- |
| `src/FlowDesk.Core` | Window capture/restore, matching, layouts, workspace switching, focus enforcement, JSON storage. No UI dependency. |
| `src/FlowDesk.App` | WinUI 3 app: XAML pages (`Views/`), view models (`ViewModels/`, CommunityToolkit.Mvvm), services. |
| `tests/FlowDesk.Core.Tests` | xUnit tests for the core, run against a fake window system (no real windows touched). |
| `tests/FlowDesk.App.Tests` | Headless xUnit tests for app logic with no WinUI dependency (the tour state machine). |
| `tools/FlowDesk.Probe` | Console harness that runs the core on the real desktop (`list`, `capture`, `restore`, `layout`, `focus`). |
| `tools/Publish.ps1` | Builds the self-contained win-x64 release folder; refuses to overwrite an existing build. |
| `tools/Build-Installer.ps1` | Publishes, then compiles the installer with Inno Setup 6 (`ISCC.exe`). |
| `installer/FlowDesk.iss` | Inno Setup 6 script: per-user install, no administrator rights. |
| `tools/*.ps1` | Desktop smoke and screenshot scripts used during development. |
| `docs/ARCHITECTURE.md` | Architecture, core API and screen-by-screen design spec. **Start here.** |
| `docs/UI-CRAFT.md` | UI rules every screen follows (Fluent / Windows 11). |

## Known limits

- Browser tabs can't be read. Website items reopen saved links in a new browser window.
- Windows of apps running as administrator can't be moved unless FlowDesk also runs as administrator.
- Workspaces capture windows on all virtual desktops and return each to its saved desktop when the desktop
  backend can move windows; without it, the read-only Windows fallback can see known other desktops but cannot
  move windows there. Layouts and focus stay on the current desktop.
- FlowDesk never kills processes: focus closes windows politely (like clicking X).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
