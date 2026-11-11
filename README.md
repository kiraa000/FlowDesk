# FlowDesk

FlowDesk is a native Windows 11 app that saves the windows you have open as a **workspace** and puts them back
with one click: it relaunches the apps, files, folders and websites, and moves every window to where it was
(including other monitors and maximized windows). It also arranges real windows into **layouts** and runs
**focus sessions** that close distracting apps.

Built with C#, .NET 10 and WinUI 3 (Windows App SDK), unpackaged and self-contained.

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
fresh.

## Publish

`tools\Publish.ps1` produces a folder that runs on a PC without .NET or the Windows App SDK installed:

```powershell
.\tools\Publish.ps1
```

The output is `builds\FlowDesk-win-x64\` (about 235 MB, around 530 files, including the .NET runtime and Windows
App SDK); run `FlowDesk.App.exe` from there. Pass `-Output <folder>` to choose another destination. The .NET 10
SDK is still required on the build machine.

## Repository layout

| Path | What it is |
| --- | --- |
| `src/FlowDesk.Core` | Window capture/restore, matching, layouts, workspace switching, focus enforcement, JSON storage. No UI dependency. |
| `src/FlowDesk.App` | WinUI 3 app: XAML pages (`Views/`), view models (`ViewModels/`, CommunityToolkit.Mvvm), services. |
| `tests/FlowDesk.Core.Tests` | xUnit tests for the core, run against a fake window system (no real windows touched). |
| `tests/FlowDesk.App.Tests` | Headless xUnit tests for app logic with no WinUI dependency (the tour state machine). |
| `tools/FlowDesk.Probe` | Console harness that runs the core on the real desktop (`list`, `capture`, `restore`, `layout`, `focus`). |
| `tools/Publish.ps1` | Builds the self-contained win-x64 release folder. |
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
