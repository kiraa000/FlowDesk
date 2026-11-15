# Hotkey input: Win-menu suppression protocol

Status 2026-11-15: round 2 physical left-Win fix live-proven; round 3 modifier-state repair implemented, **not**
live-tested (no agent live tests). Evidence: `sol-tasks/LIVETEST-011.md`.

## Observed and fixed

- Round 1: Win+Wheel switched desktops but Start opened on Win release; Win+Shift/Alt passes did not prove the
  vkE8 mask (those keys provide intervening keyboard events).
- Round 2 retest (fa20ed6, physical left Win): PASS. Five gesture holds logged `upSwallowed=True`
  `maskInjected=True SendInput=3/3` (one with 214 repeats); eight plain taps passed (`accepted=False`). Right Win
  untested (no key).
- Round 3 leaves `WinKeyRelease.cs` and the release batch path unchanged. It repairs modifier tracking that stuck
  when a key-up never reached the hook (Win+L, UAC, Ctrl+Alt+Del).

## Protocol

- A `WH_KEYBOARD_LL` hook tracks left/right Win, Alt, Shift and Ctrl; FlowDesk-tagged `dwExtraInfo` events are
  ignored, other injected input is tracked.
- An accepted gesture arms the combined Win hold until both Win keys release, including overlapping presses.
- On an eligible Win-up, one `SendInput` batch inserts tagged vkE8-down, vkE8-up and the matching extended Win-up.
  The original up is suppressed only if all three succeed.
- On zero/partial insertion or exception, forward the Win-up: fail open, never leave Win stuck. Never
  defer a swallowed up to the worker; the batch contains Win-up even if stopping or unhook follows.
- Plain Win taps and non-Win keys pass through. The early mouse mask is only for Win+Alt, where Alt may release first.
- Async state lags the callback and a hook can vanish silently. Sampling is no longer only the initial seed and
  missing-hook resize fallback: it drives session reseed and stale-wheel repair. Without the hook, Win+Wheel is off;
  resize still works.

## Modifier state repair

Keyboard state timestamps each first down; repeats do not refresh it. On wheel events,
`ShortcutKeyboardState.ReconcileStale` clears a tracked down aged >=1000 ms whose `GetAsyncKeyState` high bit is up.
Fresh hook-only downs (<1000 ms) stay authoritative even if async says up, preserving injected robustness; a real
async-confirmed hold can last indefinitely. Stale clear is state correction, not key-up processing: no release is
injected; a later unmatched up cannot trigger replacement.

Limitation: genuinely injected-only holds disagreeing with async for >=1 s intentionally expire; 1 s favours fresh
hooks but bounds stuck scrolling.

Startup/session re-seed samples async before replacing tracked downs and discarding the old gesture arm. `HotkeyHost`
registers `WTSRegisterSessionNotification` (THIS_SESSION) on its own message-only window, unregistering before
destroy. It reseeds on console/remote connect/disconnect, logon/logoff, lock/unlock (reasons 1-8) and
`desktopready` (0xF), resetting search/wheel bookkeeping without synthetic releases. Registration is best-effort
(may fail before Terminal Services is ready) and logged; stale-wheel repair remains the fallback.

## Diagnostics (Debug only)

Set before launching the Debug build:

```powershell
$env:FLOWDESK_HOTKEY_DIAGNOSTICS_LOG = 'D:\Workspace\_tmp\opencode\flowdesk-hotkey-input.log'
```

Per hold the log records accepted gesture, hold IDs, repeat count, physical Win-up, swallowed, mask injected,
`SendInput` count (expected 3), cleanup count and failure flag. It adds session-registration, session-resync
and stale-resync lines. Logging runs off-hook; no private keys, titles or new log path. Release builds have no
diagnostics.

## Manual diagnosis (desktop free; Opus only; AHK stopped, restarted after)

1. Physical LWin+Wheel: desktop switches, Start stays closed; plain Win tap still opens Start.
2. Win+L lock/unlock then scroll: no desktop switch/swallow. UAC or Ctrl+Alt+Del round-trip likewise; wait >=1 s if
   no session message.
3. Release Alt+Shift on a secure desktop, return and scroll: no accidental resize.
4. Reload or off/on while Win is held: no stuck modifier or scroll; resize still works.

## Evidence limits

A passing run covers only its conditions, not every Windows/hook/elevation case. Inserted count 3 does not prove
downstream hooks or the shell received the events. `SendInput` is subject to UIPI. `GetAsyncKeyState`'s high bit is
meaningful, but zero can also mean an inactive session, secure desktop or UIPI block, not only "key up". Repeat
behaviour depends on Windows input processing.

## References

- https://www.autohotkey.com/docs/v2/lib/A_MenuMaskKey.htm
- https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification
- https://learn.microsoft.com/en-us/windows/win32/termserv/wm-wtssession-change
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate
- https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct
