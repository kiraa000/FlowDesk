# Notification diagnostics (first-close tray notice)

Live status: **root cause undetermined until the diagnostics below are run on a real desktop.** This page
records what the code does, the facts established by the TASK-010 code review, and the exact steps Opus runs.
It does not assert a cause.

## Code path

- Close handler: `src/FlowDesk.App/MainWindow.xaml.cs` (`OnClosing`) asks `UxDecisions.Close`, then calls
  `UxDecisions.CanHideWithTrayNotice` (`src/FlowDesk.App/Services/UxDecisions.cs`).
- Delivery is synchronous and happens before hiding. Only `true` from `FocusNotifications.ShowMessage` marks
  `TrayCloseNoticeShown`; a failed delivery leaves the notice pending and the window visible.
- `FocusNotifications.Show` (`src/FlowDesk.App/Services/FocusNotifications.cs`) returns `false` when the manager
  is unregistered, when `AppNotificationManager.Setting` is not `Enabled`, when `Show` yields ID 0, or when an
  exception is thrown. `Failure(ex)` keeps only the exception type and HRESULT
  (`TypeName (0xXXXXXXXX)`), never messages or paths.
- Second close after a successful handoff hides without repeating the notice.

## Diagnose registration and handoff (Opus, exact commands)

Start from the repository root. The desktop must be free; `StartDiagnostics` launches the Debug build and waits
5 seconds (the tool refuses to start while any `FlowDesk.App` process is running).

```powershell
Set-Location 'D:\Workspace\_projects\FlowDesk\flowdesk-app'
.\tools\Verify-Task002.ps1 -Action StartDiagnostics
.\tools\Verify-Task002.ps1 -Action ProbeTrayNotice
.\tools\Verify-Task002.ps1 -Action Snapshot
```

`ProbeTrayNotice` delivers the same tray text through the notification service, without calling the close
handler: it does **not** hide the window or mark any setting. It prints JSON:

| Field | Meaning |
| --- | --- |
| `Error` | Diagnostic-level error (e.g. the `GetAllAsync` query failed); `ProbeTrayNotice` and `Snapshot` print it instead of throwing. |
| `Supported` | `AppNotificationManager.IsSupported()` result. |
| `Registered` | `Register()` completed without throwing. |
| `RegistrationFailure` | Literal `Unsupported`, or `TypeName (0xHRESULT)` when registration threw. |
| `Setting` | `AppNotificationManager.Setting` at probe time (`Enabled`, `DisabledForApplication`, ...). |
| `Accepted` | `ShowMessage` returned success (registration, setting, and `Show` all passed). |
| `Id` | App notification ID after `Show`; nonzero is required for a handoff. |
| `Failure` | Last failure classifier, e.g. `NotificationsNotEnabled`, `NoPlatformId`, or `TypeName (0xHRESULT)`. |
| `InHistory` | Whether `Id` was present in `GetAllAsync()` 500 ms after `Show`. |
| `PlatformNotificationIds` | IDs returned by that `GetAllAsync()` call. |
| `TrayCloseNoticeShown` | Settings value observed during this probe; must not change because of the probe. |

`Snapshot` re-reads the notification count and platform history for a later check (see evidence limits). Every
response is also saved under `%LOCALAPPDATA%\FlowDesk\task002-debug\evidence\<id>.json`.

## First-close live tests

Stop FlowDesk fully first (X only hides it; use Exit in the tray menu), then back up settings. Settings live in
`%LOCALAPPDATA%\FlowDesk\app-settings.json`.

```powershell
$settings = Join-Path $env:LOCALAPPDATA 'FlowDesk\app-settings.json'
Copy-Item -LiteralPath $settings -Destination "$settings.bak-before-notice-test"
```

With FlowDesk still stopped, open `app-settings.json` and set `"TrayCloseNoticeShown": false` by hand. Keep the
backup until both tests are done. Launch with `StartDiagnostics` whenever the run may need `ProbeTrayNotice` or
`Snapshot`; launching the exe directly does not set `FLOWDESK_TASK002_DIAGNOSTICS`:

```powershell
.\tools\Verify-Task002.ps1 -Action StartDiagnostics
```

1. **Delivery available.** With the app running, click X once. Expected: the tray notice is handed to Windows
   (`Accepted=true`, nonzero `Id`) and the window hides; `TrayCloseNoticeShown` becomes `true`. Reopen and click
   X again: it hides without a second notice.
2. **Unavailable path.** If `ProbeTrayNotice` reports `Supported=false`, `Registered=false`,
   `RegistrationFailure`, `Setting` not `Enabled`, or `Accepted=false`, repeat the reset while stopped, launch,
   and click X. Expected: the window stays visible with the native informational InfoBar
   ("FlowDesk stays running in the tray... Use Minimize ... or Exit in the tray menu...") and
   `TrayCloseNoticeShown` stays `false`, so the notice remains pending for the next close. Minimize keeps the
   app running; Exit in the tray menu closes it.
3. **Handoff without a visible banner.** If `Accepted=true` and `Id` is nonzero but no banner appears, check
   Do not disturb / notification settings. A successful handoff is not proof that a banner was rendered.

Restore the backup while FlowDesk is stopped:

```powershell
Copy-Item -LiteralPath "$settings.bak-before-notice-test" -Destination $settings -Force
```

## Evidence limits

- `Show` plus a nonzero `Id` means Windows accepted the handoff, not that a banner was drawn. Do not disturb or
  notification policy can suppress the banner; Notification Center may still hold it.
- `GetAllAsync()` history can lag behind `Show`. The probe waits 500 ms; if `InHistory` is false, run
  `Snapshot` again after a few seconds before concluding the notification is missing.
- `Id=0` with `Failure=NoPlatformId` is the silent-no-op case: registration and setting looked fine but no
  notification was created.
- A failure string is only an exception type and HRESULT. A moved folder or an empty history snapshot alone is
  not proof of the failure mechanism.

## Facts established from code review (not live root cause)

- Before the TASK-010 fix, `TakeTrayNotice` set `TrayCloseNoticeShown` **before** calling `void Show`; `Show`
  returned silently when the manager was unregistered and swallowed exceptions, so the flag could be true with
  nothing delivered. That explains why the failure was invisible, but the original live cause cannot be recovered
  from the old void path: only a fresh `ProbeTrayNotice` shows current registration and setting behavior.
  `CanHideWithTrayNotice` fixes the ordering and the fallback; it does not identify which condition occurred.
- In unsupported or elevated contexts the SDK's `Register`/`Show` can no-op. With the old void call that silent
  failure was invisible; this is a known possibility, not a diagnosis, and whether elevation is the live
  mechanism still has to come from `ProbeTrayNotice`.
- This app is unpackaged (`WindowsPackageType=None`). Without an explicit AUMID, an unpackaged app's
  notification identity is derived from its executable path, so moving the folder creates a new identity. A new
  identity is not by itself proof that a toast failed: registration can still succeed under the new path.
- The project is self-contained (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`,
  `SelfContained=true` in `src/FlowDesk.App/FlowDesk.App.csproj`). Current self-contained deployment docs do
  not require `Singleton`; missing package identity/registration is not established as the root cause.

## References

- `AppNotificationManager.Register`:
  https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register
- `AppNotificationManager.Show`:
  https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.show
- Self-contained deployment:
  https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps
- WindowsAppSDK source:
  https://github.com/microsoft/WindowsAppSDK/blob/main/dev/AppNotifications/AppNotificationManager.cpp and
  https://github.com/microsoft/WindowsAppSDK/blob/main/dev/AppNotifications/AppNotificationUtility.cpp
