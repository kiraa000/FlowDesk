# Hotkey input: Win-menu suppression protocol

Status 2026-11-15: target protocol and manual diagnosis only; **not** live-proven. Round 1 evidence:
`sol-tasks/LIVETEST-011.md`; the exact pre-fix failure cause on this PC is unproven because no trace was captured.

## What was observed

- Physical Win+Wheel switched desktops, but Start opened on Win release.
- Win+Shift+Wheel and Win+Alt+Wheel passed, but Shift/Alt are themselves key events during the Win hold, so those
  passes do not establish that the vkE8 mask works.
- Old code injected vkE8 once per Win hold from the mouse callback, ignored `SendInput`'s result, and its keyboard
  hook saw only key-ups, not repeat downs.
- AHK documents that Win is the last physically pressed key after a wheel gesture, so Win key-repeat can invalidate
   an earlier menu cancellation; mouse masking is normally immediate only when no keyboard hook is installed. This does
  not prove Windows ignores all injected keys from inside mouse hooks.

## Protocol

- A `WH_KEYBOARD_LL` hook tracks left/right Win, Alt, Shift and Ctrl. Events tagged with FlowDesk's own
  `dwExtraInfo` are ignored; other tools' injected input is tracked.
- An accepted Win gesture arms the combined Win hold until both Win keys are released, including overlapping presses.
- On an eligible Win-up, one `SendInput` batch inserts tagged vkE8-down, vkE8-up and the matching extended Win-up.
  The original up is suppressed only if all three succeed.
- On zero/partial insertion or exception, forward the original Win-up: fail open, never leave Win stuck. Never
  defer a swallowed up to the worker; the batch contains Win-up even if stopping or unhook follows.
- Plain Win taps and non-Win keys pass through. The early mouse mask is only for Win+Alt, where Alt may release
  first.
- Async state lags the callback, and a hook can be removed silently. Sampling is only the initial seed and
  missing-hook resize fallback; without the keyboard hook, Win wheel is off and resize still works.

## Diagnostics (Debug only)

Set the variable **before launching** the Debug build:

```powershell
$env:FLOWDESK_HOTKEY_DIAGNOSTICS_LOG = 'D:\Workspace\_tmp\opencode\flowdesk-hotkey-input.log'
```

Per hold the log records: accepted gesture, hold IDs, repeat count, physical Win-up, swallowed, mask injected,
`SendInput` inserted count (expected 3), cleanup count and failure flag. Logging runs off-hook; no typed keys or titles.
Release builds have no diagnostics.

## Manual diagnosis (desktop must be free; Opus only)

1. Quit AHK from its tray menu, then launch the Debug build with the variable above.
2. Plain LWin+Wheel and RWin+Wheel, short and past the repeat delay: desktop switches, Start stays closed.
3. Rapid repeated holds; both-Win releases in either order; plain Win taps (Start opens), Win+Shift and Win+Alt
   wheel.
4. Reload, then stop/disable FlowDesk while Win is held: no stuck modifier; resize still works.
5. Restart AHK; Start should open only for plain taps.

## Evidence limits

A passing run covers only the test conditions, not every Windows/hook/elevation case.
An insertion count of 3 does not prove downstream hooks or the shell received the events.
`SendInput` is subject to UIPI: insertion can fail while an elevated window has focus, and fail-open then forwards
the original Win-up. Repeat behaviour also depends on Windows input processing.

## References

- https://www.autohotkey.com/docs/v2/lib/A_MenuMaskKey.htm
- https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct
