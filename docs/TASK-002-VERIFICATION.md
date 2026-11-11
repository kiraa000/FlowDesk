# TASK-002 verification (2026-11-10)

Branch: `task-002-layouts-focus`, local only. Core source unchanged from `c661dea`.
All commands below ran in the repository root. Each shell invocation was a single command.

## Build and tests

| Exact command | Exit | Actual summary |
| --- | --- | --- |
| `dotnet build FlowDesk.slnx -c Debug` | 0 | Build succeeded. 0 Warning(s), 0 Error(s). |
| `dotnet test tests/FlowDesk.Core.Tests` | 0 | Passed! - Failed: 0, Passed: 29, Skipped: 0, Total: 29, Duration: 85 ms. |
| `git diff c661dea -- src/FlowDesk.Core` | 0 | No output; core source untouched. |

The full core suite ran once during resumption, before the verification commit. Subsequent edits only preserve diagnostic responses in the verification script; application binaries are unchanged.

## Desktop and notification checks

Only disposable `tools/smoke-fixture/note.txt` Notepad and the exact `tools/smoke-fixture` Explorer window were targeted. No other app was blocked. The existing two-item `FlowDesk smoke test` workspace was reused.

| Exact command | Exit | Actual result |
| --- | --- | --- |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action EndSession` | 0 | Ended previous session; also confirmed the Strict end dialog in later runs. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action StartDiagnostics` | 0 | Launched opt-in Debug diagnostics, no session started automatically. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action StartShortNormal` | 0 | One-minute Normal session; hard-coded notepad-only rule, block-all false. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action OpenNotepad` | 0 | Opened only disposable note.txt. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action AssertClosed` | 0 | Disposable Notepad window closed. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action Allow` | 1 | UI control not found: Allow 5 minutes. Windows toast button not reachable by UIA. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action AllowLogged` | 0 | Show returned nonzero notification ID; platform history present=True; exact payload argument replayed through real activation handler. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action AssertAllowed` | 0 | Reopened Notepad stayed open across multiple watcher polls. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action HideReopen` | 0 | Close hid live app; second launch reopened same process/window. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action AssertCompletion` | 0 | Session ended naturally; Focus session complete notification submitted; platform history present=True. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action ClearNotifications` | 0 | Cleared only FlowDesk platform notifications and in-memory evidence; archived diagnostic files remain. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action StartShortStrict` | 0 | One-minute Strict session, only notepad blocked. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action AssertLoggedStrict` | 0 | Nonzero Strict notification ID; payload has no allowance action. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action AssertStrict` | 0 | Strict screen exposes no Pause; no temporary allowance button found. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action Snapshot` | 0 | Running=False after confirmed Strict end. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action PauseResume` | 0 | Normal Pause froze remaining time; Resume restarted it. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Smoke-App.ps1 -Action Click -Name 'Apply layout'` | 0 | Applied Two columns to the fixture workspace. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Smoke-App.ps1 -Action Click -Name 'Save as workspace positions'` | 0 | Saved live fixture placements. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Task002.ps1 -Action Layout` | 0 | Both live/saved outer-rect pixel deltas 0,0,0,0; equal widths/aligned/non-overlapping columns. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Smoke-App.ps1 -Action CloseFixtures` | 0 | Closed only named Notepad and exact fixture Explorer. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Smoke-App.ps1 -Action AssertNoFixtures` | 0 | Both disposable fixture windows closed. |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Smoke-App.ps1 -Action CloseApp` | 0 | FlowDesk closed normally and flushed persistence. |

Normal/Strict notification checks were repeated after adding archive retention. Exact notification payloads, nonzero IDs, platform-history IDs and activation results are retained outside git in `%LOCALAPPDATA%/FlowDesk/task002-debug/evidence/*.json`. `response.json` is a live mailbox, not the archive. Diagnostics require the launcher opt-in environment variable and are absent from Release builds. Old mailbox commands are not replayed at startup. No executable paths, arbitrary rules or user-defined code can be supplied to the diagnostic session commands.

This verifies Windows notification submission/history, action XML/arguments, shared activation dispatch and actual temporary allowance enforcement. **It does not claim a visible popup or a real Windows button click.** No system notification settings were changed.

## Screenshots and craft review

Commands (each exit 0):

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Review-Task002Themes.ps1 -Theme Light -EvidenceTag reviewed
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Review-Task002Themes.ps1 -Theme Dark -EvidenceTag reviewed
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Review-Task002Themes.ps1 -Theme Light -EvidenceTag monitorfix -LayoutsOnly
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Review-Task002Themes.ps1 -Theme Dark -EvidenceTag monitorfix -LayoutsOnly
```

Both full runs: `PASS: captured seven views ...`. Layout-only runs also assert `PASS: exactly one monitor is visibly selected.`

Authoritative images in `../sol-tasks/shots-002/`:
- Layouts: `layouts-{light,dark}-monitorfix.png` (supersedes older Layouts images).
- Focus: `focus-idle-{top,bottom}-{light,dark}-reviewed.png`, `focus-running-{light,dark}-reviewed.png`.
- Capture: `save-open-windows-{light,dark}-reviewed.png`, additional `save-open-windows-compact-{light,dark}-reviewed.png`.
- Details: `details-{light,dark}-reviewed.png`.

Lead visually reviewed Light/Dark shots for Layouts, Focus idle/running, Capture and Details. Checks: obvious primary task; one accent action in idle views; theme/ramp/neutral surfaces; schematic previews visible in both themes; compact capture rows, labelled colour dots, native dropdowns, tabular timer; empty/loading/error/disabled states in markup. Capture also rendered at the shell minimum 720x520. Layouts monitor selector uses native grouped RadioButtons with per-item checked state: the RadioButtons ItemsSource control lost selection when revisiting, found by screenshot/UIA and replaced. Light preview brush resolution was likewise fixed after viewing screenshots.

Not fully exercised: manual full keyboard tab traversal, drag-reorder gesture, High Contrast, 200% text scaling, maximum resize, tray popup menu click-through and physical toast activation. The shell's pre-existing minimum prevents 500-pixel-wide testing. Browsing arbitrary executables and block-all enforcement were intentionally not exercised on this desktop.

## Guard and harness corrections

Supervisor authorized renaming the script action `End` to `EndSession` after the raw-command parser treated the old literal as a keyword; no guard settings changed. A screenshot-helper attempt selected the caption Close instead of InfoBar Close and exited the idle app; fixed by scoping automation to the InfoBar's stable `AppStatusBar` ID, then both theme runs passed. Earlier evidence is preserved.

Final state: no focus session; disposable windows and app closed; test blocked rule removed from user settings; no push, no core edits, no system-setting changes.
