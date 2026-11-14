# FlowDesk — Getting started (for the group)

This guide is for everyone in the group. Part 1 is for **using** FlowDesk. Part 2 is for **working on the code**.
Part 3 explains how we **make a release**. If something here is wrong or unclear, fix this file in your next PR.

---

## Part 1 — Use FlowDesk

### 1.1 What FlowDesk does

FlowDesk remembers which windows you have open (apps, files, folders, website links) and **where** each window
is. Later you click **Open** and FlowDesk starts what is missing and moves every window back to its place, on the
right monitor, maximized or not. It can also arrange windows into **layouts** and run **focus sessions** that
close distracting apps.

### 1.2 Install

1. Open the [Releases page](https://github.com/kiraa000/FlowDesk/releases) and download
   `FlowDesk-1.0.0-win-x64-setup.exe` from the newest release.
2. Run it. You do **not** need administrator rights.
3. Windows may show **"Windows protected your PC"** (SmartScreen). This is normal: the app is not code-signed.
   Click **More info**, then **Run anyway**.
4. Keep the default folder (`%LOCALAPPDATA%\Programs\FlowDesk`). Tick "desktop shortcut" if you want one.
5. Click **Finish**. FlowDesk starts and shows a short tour (6 steps). You can replay it later from Settings.

Requirements: Windows 11, 64-bit (Windows 10 version 2004 or newer should work but is not tested). Nothing else
needs to be installed; the setup contains everything.

### 1.3 Your first workspace

1. Open the apps, files and folders you use for one task (for example: VS Code, a project folder, a browser).
2. Put the windows where you like them.
3. In FlowDesk: **Workspaces → Save open windows**.
4. Tick the windows you want, give the workspace a name (for example "Coding"), and save.
5. Close those windows. Click **Open** on the workspace: everything comes back to the same place.

Tips:
- Make a second workspace (for example "Study"). Switching **Coding → Study → Coding** puts Coding back exactly as
  you left it. By default the windows of the workspace you leave are minimized (Settings → "Minimize on switch").
- FlowDesk **autosaves** the active workspace: if you move a window, the new position is kept.
- Click a workspace to edit it: rename it, remove items, or add apps, files, folders and website links.
- Browsers: FlowDesk cannot read your tabs. A browser item reopens the browser; turn on the browser's own
  "Continue where you left off" setting to get your tabs back. Website items open their saved links.

### 1.4 Layouts

**Layouts** arranges the windows of a workspace on one monitor: two columns, primary + sidebar, grid or cascade.
Pick the monitor, pick a layout, then **Save positions** if you want the workspace to remember it.

### 1.5 Focus sessions

**Focus** runs a 25, 50 or 90 minute session. Add apps to **Blocked** (or use **Allowed** only). Modes:
- **Gentle:** a reminder when you open a blocked app.
- **Normal:** the blocked app's window is closed; the notification offers "Allow 5 minutes".
- **Strict:** the blocked app is closed every time until the session ends.

FlowDesk closes windows politely (like clicking X). It never kills processes, so you are asked to save if needed.
During a session FlowDesk keeps running in the system tray.

### 1.6 Window shortcuts (optional, off by default)

Settings → **Window shortcuts**. Turn it on to use:

| Shortcut | Action |
| --- | --- |
| Alt+1 … Alt+5 | Go to virtual desktop 1–5 |
| Alt+Shift+1 … 5 | Move the active window to desktop 1–5 and follow it |
| Alt+W / A / S / D (or arrows) | Snap the active window to the top / left / bottom / right half |
| Alt+Shift+W / A / S / D (or arrows) | Focus the window above / left / below / right |
| Alt+F | Maximize or restore the active window |
| Alt+M | Minimize the active window |
| Alt+Q | Minimize the active window and focus the next one |
| Alt+Shift+Space | Center the active window |
| Alt+Shift+Wheel | Resize the active window |
| Alt+Shift+0 | Pause or resume all window shortcuts |

The virtual-desktop shortcuts need the third-party file `VirtualDesktopAccessor.dll` (not included, for licence
reasons). Download the build that matches your Windows version from the VirtualDesktopAccessor project on GitHub,
then use Settings → Window shortcuts → **Choose file…**. Everything else works without it.

### 1.7 Where your data is

- Workspaces and settings: `%LOCALAPPDATA%\FlowDesk\` (Settings → Data → **Open folder**).
- Crash logs: `%LOCALAPPDATA%\FlowDesk\logs\` (Settings → About → **Open log folder**). Logs contain only error
  types and code locations, never window titles or your file paths. Nothing is sent anywhere.
- To start fresh: quit FlowDesk (tray icon → Exit) and delete `%LOCALAPPDATA%\FlowDesk`.

### 1.8 Update and uninstall

- **Update:** run the newer setup. It replaces the program and keeps your workspaces. If FlowDesk is running,
  the setup closes it first; if that fails, exit FlowDesk from the tray and run the setup again.
- **Uninstall:** Windows Settings → Apps → Installed apps → FlowDesk → Uninstall. Your data folder is kept on
  purpose; delete it yourself if you want everything gone.

### 1.9 Known limits

- Apps running **as administrator** cannot be moved unless FlowDesk also runs as administrator.
- Windows on **other virtual desktops** are not captured.
- **Chrome** sometimes insists on its own window size. FlowDesk then says "The browser controls its window size"
  instead of pretending it worked.
- FlowDesk cannot restore what is inside an app (open tabs, unsaved documents) — only the windows.

### 1.10 Report a problem

Open an issue on GitHub with:
1. What you did, what you expected, what happened (a screenshot helps).
2. The FlowDesk version (Settings → About).
3. The newest file from the log folder, if there is one.

---

## Part 2 — Work on the code

### 2.1 Install the tools (once)

1. **Git** and a GitHub account with access to the repo (ask the repo owner to add you as a collaborator).
2. **.NET 10 SDK** (10.0.400 or newer): <https://dotnet.microsoft.com/download>. Check with `dotnet --version`.
3. **Visual Studio 2022** with the workload ".NET desktop development" and the "Windows App SDK C# Templates"
   component, **or** "Build Tools for Visual Studio 2022" with the Windows 11 SDK (10.0.26100 or newer).
4. Optional, only to build the setup file: **Inno Setup 6** (`winget install JRSoftware.InnoSetup`).

### 2.2 Get the code and run it

```powershell
git clone https://github.com/kiraa000/FlowDesk.git
cd FlowDesk
dotnet build FlowDesk.slnx -c Debug
dotnet test FlowDesk.slnx
.\src\FlowDesk.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\FlowDesk.App.exe
```

In Visual Studio: open `FlowDesk.slnx`, set **FlowDesk.App** as startup project, platform **x64**, press **F5**.

Note: the app you run from the code uses the same data folder (`%LOCALAPPDATA%\FlowDesk`) as an installed
FlowDesk. Copy that folder somewhere safe before you test changes that save data. Only one FlowDesk can run at a
time: a second start just brings the running one to the front.

### 2.3 Find your way around

| Folder | What is in it |
| --- | --- |
| `src/FlowDesk.Core` | The engine: reading open windows, matching them to saved items, starting apps, moving windows, layouts, switching workspaces, focus enforcement, JSON storage. No UI code. |
| `src/FlowDesk.App` | The WinUI 3 app: pages in `Views/` (XAML), view models in `ViewModels/`, helpers in `Services/`. |
| `tests/FlowDesk.Core.Tests` | Unit tests for the engine. They use a fake window system, so they never touch real windows. |
| `tests/FlowDesk.App.Tests` | Unit tests for app logic that does not need WinUI (tour, single instance, crash log, …). |
| `tools/FlowDesk.Probe` | A console tool that runs the engine on your real desktop (`list`, `capture`, `restore`, `layout`, …). |
| `tools/RealApp-Test.ps1` | Full end-to-end test with real Notepad/Paint/Calculator/Explorer/Chrome windows. |
| `installer/` + `tools/Build-Installer.ps1` | The Inno Setup installer. |
| `docs/ARCHITECTURE.md` | How everything fits together. **Read this first.** |
| `docs/UI-CRAFT.md` | The UI rules every screen follows (Windows 11 / Fluent look). |

### 2.4 How we work

1. **Never commit straight to `main`.** Make a branch per change:
   ```powershell
   git switch main
   git pull
   git switch -c feature/short-name
   ```
2. Keep changes small and focused. Follow the existing style (MVVM with CommunityToolkit, nullable on,
   0 build warnings).
3. Before you push, both must pass:
   ```powershell
   dotnet build FlowDesk.slnx
   dotnet test FlowDesk.slnx
   ```
4. **Add a unit test** for every bug fix or new rule in `FlowDesk.Core`.
5. If you changed how windows are restored, switched or arranged, also try it on the real desktop (Probe, or
   `tools\RealApp-Test.ps1 -Mode Full` — it starts and closes its own test apps; do not touch the mouse while it
   runs, and close your other work first).
6. If you changed a screen, check it in **light and dark** theme and follow `docs/UI-CRAFT.md`.
7. Push your branch and open a **Pull Request** to `main`. Someone else reviews it before it is merged.
   ```powershell
   git push -u origin feature/short-name
   ```
8. **Never commit** personal data: your name in file paths (`C:\Users\<you>\…`), e-mail addresses, keys or
   passwords, or the `builds/` folder.

---

## Part 3 — Make a release (repo owner)

1. Make sure `main` builds with 0 warnings and all tests pass.
2. Raise the version in `src/FlowDesk.App/FlowDesk.App.csproj` (`<Version>1.0.1</Version>`) and commit.
3. Build the setup into **new** folders (the scripts refuse to overwrite an existing build):
   ```powershell
   .\tools\Build-Installer.ps1 `
       -IsccPath "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" `
       -PublishOutput "builds\FlowDesk-1.0.1" `
       -InstallerOutput "builds\installer-1.0.1"
   ```
4. Install the new setup over the old version once and check that your workspaces are still there.
5. Tag, push and publish the release:
   ```powershell
   git tag -a v1.0.1 -m "FlowDesk 1.0.1"
   git push origin main
   git push origin v1.0.1
   gh release create v1.0.1 "builds\installer-1.0.1\FlowDesk-1.0.1-win-x64-setup.exe" --title "1.0.1" --notes "What changed..."
   ```
