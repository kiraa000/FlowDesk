# Contributing to FlowDesk

## Workflow

1. Create a branch from `main` (`feature/<short-name>` or `fix/<short-name>`).
2. Keep changes focused; commit in small steps that each build.
3. Before opening a pull request:
   - `dotnet build FlowDesk.slnx -c Debug` - 0 errors, no new warnings.
   - `dotnet test tests/FlowDesk.Core.Tests` - all tests pass.
   - For UI changes: run the app, check the screen in **Light and Dark** theme, and go through the checklist in
     `docs/UI-CRAFT.md` (section 4). Add before/after screenshots to the pull request.
4. Open a pull request against `main`; at least one teammate reviews it before merge.

## Where code goes

- **Desktop logic** (anything that enumerates, launches, moves or closes windows, matching rules, storage) belongs
  in `src/FlowDesk.Core`, behind `IWindowSystem` so it can be tested with the fake in
  `tests/FlowDesk.Core.Tests/Fakes.cs`. Add a test for every behaviour change there.
- **UI** goes in `src/FlowDesk.App`: XAML in `Views/`, state and commands in `ViewModels/` (CommunityToolkit.Mvvm
  `[ObservableProperty]` / `[RelayCommand]`), page code-behind only for view-only concerns.
- Never call core methods that enumerate or launch on the UI thread (they block): use `Task.Run`, and marshal
  results and core events back with `DispatcherQueue`.
- Follow `docs/UI-CRAFT.md`: theme brushes and type-ramp styles only, 4 px grid, one accent action per view,
  Segoe Fluent Icons, sentence case, every empty / loading / error state designed.

## Testing on a real desktop - be careful

The probe tool and the smoke scripts act on **real windows**. Test only with windows you opened for the test
(e.g. Notepad and one File Explorer folder), never with a workspace containing your own work, and never block
apps other than a disposable one (e.g. `notepad`) in focus tests.

```powershell
dotnet run --project tools/FlowDesk.Probe -- list
dotnet run --project tools/FlowDesk.Probe -- capture my-test.json
dotnet run --project tools/FlowDesk.Probe -- restore my-test.json
```

`tools/Smoke-App.ps1`, `Verify-FixtureRestore.ps1` and `Verify-Task002.ps1` drive the app through UI Automation
using the fixture in `tools/smoke-fixture/`. `Verify-Task002.ps1` needs a Debug build started with
`FLOWDESK_TASK002_DIAGNOSTICS=1` (debug-only hooks, not compiled into Release). Screenshot scripts write to
`artifacts/screenshots/` (git-ignored).

## Privacy

Don't commit personal data: no user names, e-mail addresses, `C:\Users\...` paths, or saved workspace files.
