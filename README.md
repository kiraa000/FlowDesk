# FlowDesk

FlowDesk is a native Windows 11 app that saves the windows you have open as a **workspace** and puts them back
with one click: it relaunches the apps, files, folders and websites, and moves every window to where it was
(including other monitors and maximized windows). It also arranges real windows into **layouts** and runs
**focus sessions** that close distracting apps.

Built with C#, .NET 10 and WinUI 3 (Windows App SDK), unpackaged and self-contained.

Current release: **FlowDesk 1.0.0**.

## Features

- **Workspaces:** save what's open now, edit items (apps, files, folders, website links), open a workspace and
  watch each window come back into place. Switching A -> B -> A returns A exactly as you left it.
- **Layouts:** two columns, primary + sidebar, grid or cascade on any monitor, then save the result as the
  workspace's positions.
- **Focus:** 25 / 50 / 90 minute sessions with blocked and allowed apps. Gentle (reminder), Normal (closes the
  app, "Allow 5 minutes" from the notification) or Strict (closes it every time). Keeps running in the tray.
- **Onboarding tour:** six teaching tips through workspaces, layouts, focus and settings. It opens on first run
  and can be replayed from Settings; Back, Next, Skip and Escape work throughout.
- **Settings:** system/light/dark theme, minimize on switch, 10-60 second launch timeout, start with Windows,
  the data folder and the app version.
- Mica, light/dark theme, keyboard support, Windows notifications.

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
.\tools\Publish.ps1 -Output builds\FlowDesk-1.0.0-task007-<timestamp>
```

Pass `-Output <folder>` to choose the destination. The folder must be new or empty: the script refuses to write
into a folder that already contains files, so an existing build is never overwritten or deleted. It publishes
self-contained win-x64 with trimming, single-file and ReadyToRun explicitly off, prints the measured file count,
size and app version, and fails if `VirtualDesktopAccessor.dll` appears in the output. The .NET 10 SDK is required
on the build machine.

## Build the installer

`tools\Build-Installer.ps1` publishes first, then compiles `installer\FlowDesk.iss` with Inno Setup 6's `ISCC.exe`:

```powershell
.\tools\Build-Installer.ps1 `
    -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
    -PublishOutput "builds\FlowDesk-1.0.0-task007-<timestamp>" `
    -InstallerOutput "builds\installer-1.0.0-task007-<timestamp>"
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
- **VirtualDesktopAccessor.dll:** this third-party DLL is not shipped. If you use the virtual-desktop feature,
  supply it yourself through Settings.

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
- Windows on other virtual desktops aren't captured.
- Suspended Store apps can't be identified until they've been opened once.
- FlowDesk never kills processes: focus closes windows politely (like clicking X).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
