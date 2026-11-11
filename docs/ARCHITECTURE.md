# FlowDesk architecture

FlowDesk is a native Windows 11 app (C#, .NET 10, WinUI 3 / Windows App SDK, unpackaged) that saves the
windows you have open as a workspace and puts them back with one click, applies layouts to real windows,
and runs focus sessions that close distracting apps.

## Projects

| Project | What it is | Who writes it |
| --- | --- | --- |
| `src/FlowDesk.Core` | Window capture/restore, matching, layouts, A->B->A switching, focus enforcement, JSON storage. No UI dependency. | Lead (done, tested) |
| `src/FlowDesk.App` | WinUI 3 app: shell, pages, view models, notifications, tray, onboarding tour. | Sol |
| `tests/FlowDesk.Core.Tests` | xUnit tests for the core against a fake window system. | Lead; Sol adds view-model tests |
| `tools/FlowDesk.Probe` | Console harness to run the core on the real desktop (`list`, `capture`, `restore`, `layout`, `focus`). | Lead |

Build/test: `dotnet build FlowDesk.slnx`, `dotnet test tests/FlowDesk.Core.Tests`.

## Core API (what the app calls)

All in `FlowDesk.Core`. Create ONE of each at startup (DI or a small `AppServices` class):

```
var windows  = new Win32WindowSystem();              // needs Per-Monitor-V2 DPI awareness (WinUI default)
var tracker  = new SessionTracker();                 // runtime-only: item -> live window, session snapshots
var capture  = new WorkspaceCapture(windows, tracker);
var restorer = new WorkspaceRestorer(windows, new ShellLauncher(), tracker);
var switcher = new WorkspaceSwitcher(windows, tracker, capture, restorer);
var layouts  = new LayoutApplier(windows);
var focus    = new FocusWatcher(windows);
var store    = new JsonStore();                      // %LOCALAPPDATA%\FlowDesk\flowdesk.json
```

| Need | Call | Notes |
| --- | --- | --- |
| "Save what's open" picker | `capture.CaptureOpenWindows()` -> `CaptureCandidate[]` | Each has a ready `Item` (with window position). `Problem != null` -> show disabled with the reason. `IsBrowser` -> ask for links (FlowDesk can't read tabs); with links the item becomes `Kind=Website, Urls=[...]`, `App` = that browser. Blocking (COM + process queries): call on a background thread. |
| Open / switch workspace | `await switcher.SwitchToAsync(target, current, leave, progress)` | Returns `RestoreReport` (per-item `ItemOutcome` + `Detail`, user-readable). `progress` reports items as they finish; marshal to UI thread. Runs launches and waits (up to 20 s); run off the UI thread (`Task.Run`). |
| Save current positions | `capture.SaveLayout(ws)` -> ids updated | Then persist. Items whose window is closed keep their old position. |
| Reset to saved layout | `switcher.ResetSession(ws.Id)` | Next open uses the saved layout instead of "as you left it". |
| Apply a layout | `layouts.Apply(preset, orderedWindows, monitorDevice)` | Windows from `capture.LiveWindowsFor(ws)`; first = primary. Monitors: `windows.GetMonitors()`. |
| Focus session | `focus.Start(settings)`, `Pause/Resume/Stop(force)`, `AllowTemporarily(window)`, `Remaining`, `State` | Events `AppBlocked` (Warned/Closed + `CanAllowTemporarily`) and `SessionEnded` fire on a thread-pool thread. Strict: `Stop()` returns false; ask the user, then `Stop(force: true)`. |
| Persist | `store.Load(out recovered)`, `await store.SaveAsync(data)` | `recovered != null` -> show an InfoBar "Your data file was damaged; restored the last good copy". |

Model types: `Workspace`, `WorkspaceItem` (`Kind` App/Website/File/Folder, `App`, `Path`, `Urls`, `Window`),
`SavedWindow`, `FocusSettings`, `FocusAppRule`, `FlowDeskData` (root of the JSON file, incl. `TourCompleted`).

Rules the app must keep:
- Never call core methods that enumerate/launch on the UI thread (they block: COM, process queries, waits).
- Core events and progress arrive off the UI thread -> `DispatcherQueue.TryEnqueue`.
- Persist after every user change (debounced ~500 ms); never hold the only copy in memory.
- FlowDesk never kills processes; focus closes windows politely. Keep it that way.

## App structure (`src/FlowDesk.App`)

- Unpackaged: `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, `SelfContained=true`,
  RuntimeIdentifier `win-x64`; `app.manifest` with PerMonitorV2. Single instance via
  `AppInstance.FindOrRegisterForKey("FlowDesk")` (second launch activates the first window).
- MVVM with `CommunityToolkit.Mvvm` (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
  Views in `Views/`, view models in `ViewModels/`, services in `Services/` (AppServices, Notifications, Icons).
- Shell: `MainWindow` with `MicaBackdrop`, extended title bar (icon + "FlowDesk"), `NavigationView`
  (Left): Workspaces, Layouts, Focus; Settings in the footer. Default 1100x720, min 720x520, remembers its
  own size/position.
- Styling: follow `docs/UI-CRAFT.md` exactly (type ramp styles, theme brushes, 4 px grid, one accent action
  per view, Segoe Fluent Icons, no emoji, every empty/loading/error state).

## Screens (design spec)

**Workspaces** (home). Title "Workspaces". Command row: accent `SplitButton` "New workspace" (main =
"From open windows"; menu: "Empty workspace"). List rows (~64 px): colour tag + Fluent glyph, name
(BodyStrong), secondary line "6 items · Last used 2 hours ago" (Caption/Secondary), "Active" tag on the
running one, right side "Open" button + "..." `MenuFlyout` (Edit, Save current positions, Reset to saved
layout, Duplicate, Delete -> `ContentDialog` confirm). Empty state: one glyph, "No workspaces yet", one
sentence, button "Save open windows". While opening: the row shows a `ProgressRing` and "Opening 3 of 6";
when done an `InfoBar` summarises ("Study is ready - 5 of 6 windows in place. Discord wasn't found.") with
"Details" listing per-item outcomes.

**Save open windows** (page or large `ContentDialog`). Name box (default "Workspace N"), list of
candidates with checkboxes: real app icon (from the exe), name, window title as secondary text, monitor +
state ("Monitor 2 · Maximized"). Browsers: expandable row with a links box ("Add the pages to reopen, one
per line") and a hint that tabs can't be read. Disabled rows show their `Problem`. Primary "Save
workspace".

**Workspace details** (`BreadcrumbBar` Workspaces > Name). Editable name, description, colour/glyph
picker. Items list (icon, name, detail = path/URLs/program, position summary, remove). "Add" `MenuFlyout`:
App (browse .exe or pick a running app), Website (links), File, Folder (pickers). Buttons: "Save current
positions", accent "Open".

**Layouts.** Monitor selector (`SegmentedControl`/`RadioButtons` when >1 monitor, showing name +
resolution). Preset tiles drawn as small schematic rectangles (Two columns, Primary + sidebar, Grid,
Cascade). Window list: the active workspace's open windows in order (drag to reorder; first = primary).
Accent "Apply layout"; "Save as workspace positions" afterwards.

**Focus.** Idle: duration `RadioButtons` 25/50/90 min; strictness `RadioButtons` with one-line explanations
(Gentle - remind me; Normal - close blocked apps, allow 5 minutes on request; Strict - close them until
the session ends); Blocked apps list and Allowed apps list (add from running apps or browse); toggle
"Block every app that isn't allowed"; accent "Start focus". Running: large remaining time
(Display/TitleLarge, tabular digits), progress ring/bar, Pause (hidden in Strict), "End session" (Strict:
confirmation dialog). Blocked events: Windows app notification (`AppNotificationManager`) with "Allow 5
minutes" button in Normal mode, plus an in-app `InfoBar`. Closing the main window during a session keeps
FlowDesk running in the tray (H.NotifyIcon.WinUI) with time remaining in the tooltip.

**Settings.** `SettingsCard`s: Theme (system/light/dark), "When switching workspaces, minimize the previous
one's windows" (toggle, default on), Launch timeout, Start with Windows (toggle, HKCU Run key), "Show the
tour again", data folder link, About/version.

**Onboarding tour** (first run, and from Settings). A chain of `TeachingTip`s targeting real controls, one
open at a time, "Step 2 of 6", Back / Next / Skip tour; ends on "Restart tour" in Settings. Steps: welcome
(no target) -> New workspace button -> a workspace's Open button -> Layouts nav item -> Focus nav item ->
Settings. State: `FlowDeskData.TourCompleted`. Port the step/state-machine idea from
`flowdesk-blazor/Services/TutorialService.cs` (no Blazor code).

## Known limits (by design, tell the user in UI copy where relevant)
- Browser tabs can't be read; websites are reopened from saved links in a new browser window.
- Windows of apps running as administrator can't be moved unless FlowDesk runs as administrator.
- Windows on other virtual desktops are not captured.
- Suspended Store apps can't be identified until they are opened once.
