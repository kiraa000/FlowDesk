# FlowDesk 1.2.0 bug hunt

This is the team plan for the whole-app bug hunt before the 1.2.0 release. One person takes one area, works
through its checklist on a real Windows 11 PC, and files an issue for every real problem. The goal is a
reliable list, not a perfect app.

## Setup

1. Install the release build, or build from source. See [GETTING-STARTED.md](../GETTING-STARTED.md) (Part 1 for
   installing, Part 2 for building and running from Visual Studio or `dotnet`).
2. Use a **test Windows user** if you can, or at least a disposable set of windows. Read
   [CONTRIBUTING.md](../CONTRIBUTING.md) → "Testing on a real desktop - be careful" before you move or close any
   window.
3. Back up your data first. FlowDesk keeps workspaces and settings in `%LOCALAPPDATA%\FlowDesk\`; copy that
   folder somewhere safe before testing import, Calm Windows or Focus. Only one FlowDesk can run at a time.
4. Write down your **FlowDesk version** (Settings > About) and **Windows version** (`winver`) so the issue form is
   easy to fill in.
5. Keep a small note of what you did each time. The issue form asks for exact steps.
6. Agree an owner for each area before starting. After triage, assign each accepted issue to a teammate and
   record the build used to retest the fix; a second teammate should confirm it before closing the issue.

## Safety rules

- **Calm Windows is real.** Each toggle changes a per-user Windows registry value. Every change can be undone
  per row, or with **Undo all FlowDesk changes**, but only while the local undo journal exists. Use a test
  account if possible, and run the check-up again after undoing.
- **Advanced is real.** **Install** uses winget to install a real program. **Run** starts Chris Titus WinUtil,
  which can change Windows itself; FlowDesk cannot undo that. Only run it on a machine you can reset.
- **Undo is not isolation.** Calm rollback only reverses changes in its journal; FlowDesk has no undo for
  Advanced tools. Use a test Windows account for per-user changes and a disposable VM for system-wide changes.
- **Never test Focus Strict with apps you need.** Strict closes a blocked app every time and has no pause or
  five-minute allowance. Use Notepad, Calculator or another disposable app, and never test with unsaved work.
- **Do not touch windows that belong to your real work.** Test only windows you opened for the test.
- **Hot corners can lock the PC.** The **Lock** action locks Windows; make sure you can sign back in.
- **Import can replace data.** **Replace my workspaces** overwrites the current workspace list. Export first.
- **Do not test the installer over your only install.** Upgrade/uninstall tests belong on a test PC or VM.
- If something crashes or hangs, stop and report it. Do not keep testing a broken build.

## Workspaces

- [ ] Save open windows: pick several apps, files, folders and a browser; give the workspace a name; save. The
      card shows a summary and the saved item count.
- [ ] Close those windows, then **Open**. Everything relaunches and each window returns to its saved position,
      including a maximized window and a window on a second monitor.
- [ ] Switch A → B → A. With **Minimize the previous workspace's windows** on, A's windows are minimized while
      B is active and come back when A opens again.
- [ ] While a workspace is active, open a new app. The **New window** bar offers **Add**, **Not now** and
      **Always add new windows**. Adding the window makes it part of the workspace.
- [ ] Move and resize a window, wait for the status line to say it is saved, then restart FlowDesk and open the
      workspace. The new position is kept.
- [ ] Edit a workspace: rename it, change colour/icon, add a website, file, folder and app, remove an item,
      and toggle **Reopen my last tabs** on a browser item.
- [ ] Delete a workspace, then use **Undo** in the success bar. The workspace and its items return.
- [ ] Use the **More** menu: **Duplicate**, **Reset to saved layout**, **Save current positions**.
- [ ] Start with no windows: the empty state shows **Save open windows** and **Start empty**; starting empty
      creates a workspace with no items.
- [ ] Switch to a workspace by typing its name in Commands mode of the command palette, and from the tray
      **Workspaces** menu. The `w ` prefix searches windows, not workspaces.

## Layouts (tiling)

- [ ] Open at least four windows on one monitor and apply each layout: two columns, primary + sidebar, grid and
      cascade. The first checked window is the primary where the layout uses one.
- [ ] Reorder windows by dragging a row and with Ctrl+Up/Down, then apply the layout again. Order changes the
      result.
- [ ] On a multi-monitor PC, choose each monitor in turn. Only windows on the chosen monitor are arranged, and
      the friendly monitor name matches the Windows display name.
- [ ] Apply a layout, then **Save as workspace positions** with an active workspace. Open the workspace and check
      that the positions came back. The button is hidden without an active workspace.
- [ ] Refresh after opening or closing a window. The list and **Apply layout** enable/disable correctly.
- [ ] Try a layout with one window, and with more windows than the layout has slots. Nothing should crash; extra
      windows should be handled sensibly.
- [ ] Include a window running as administrator. FlowDesk should say it may not move rather than pretend it did.
- [ ] With no windows open, the empty state explains what to do and offers **Open a workspace**.

## Focus

- [ ] Start 25, 50 and 90 minute sessions. The countdown, progress bar, expected finish time and tray tooltip
      stay in step.
- [ ] **Gentle reminder** warns once when a blocked app opens and leaves it open. **Normal** closes the app's
      window and the notification offers **Allow 5 minutes**. **Strict** closes it every time.
- [ ] Add blocked and allowed apps with **Running app** and **Browse for .exe**; remove them with **Remove**.
      **Block every app that isn't allowed** keeps only the allowed list open.
- [ ] During a session: pause/resume where allowed, **+5 min**, and **End session**. Strict sessions have no
      pause and no five-minute allowance.
- [ ] Close the FlowDesk window during a session. FlowDesk keeps running in the tray; the session continues.
- [ ] Use **Allow 5 minutes** from the Focus page after a Normal-mode block, including when the notification's
      button was missed.
- [ ] Start a session from the tray menu. Its label shows the duration and strictness and changes with the
      current settings.
- [ ] Check the disabled Start state when no blocked apps exist and block-all is off: the reason is shown.

## Calm Windows

- [ ] Run **Run check-up**. Rows appear in groups with a caption, findings, and a last-checked line.
- [ ] Toggle one row. The Windows setting changes and the row shows its new state. Restart FlowDesk; the state
      is still there.
- [ ] Use **Undo** on one row. The value from before FlowDesk changed it comes back.
- [ ] Use **Calm all**, read the confirmation list, and accept. Startup apps are not included. Then **Undo all
      FlowDesk changes** and confirm the original values are restored.
- [ ] Search on the taskbar: choose **Hidden**, **Icon** or **Box**, press **Apply**, and check Windows after
      the suggested Explorer restart.
- [ ] Startup apps: toggle one, use its **Undo**, and check **Other startup apps** opens the Windows startup
      settings.
- [ ] Accept the **Restart File Explorer** prompt. Explorer returns and the changed settings apply.
- [ ] Rows set by policy or unavailable on this PC cannot be toggled and explain why. A custom value shows the
      "not changed by Calm all" caption.
- [ ] **System-wide changes** opens the Advanced page when Advanced mode is on.

## Hotkeys and window shortcuts

- [ ] Turn **Window shortcuts** on. Try Alt+1…5, Alt+Shift+1…5, Alt+W/A/S/D, Alt+Shift+W/A/S/D, Alt+F, Alt+M,
      Alt+Q, Alt+Shift+Space, Alt+Enter and Alt+Shift+0. Turn the feature off and check they stop.
- [ ] Turn an individual shortcut off and on from the Hotkeys page. **Edit** captures new keys, **Save** applies
      them, and Escape cancels.
- [ ] **Reset all shortcuts** restores the defaults. The filter matches by name and by key. **Turn all on** and
      **Turn all off** do what they say.
- [ ] Start another app that uses one of the same shortcuts (for example AutoHotkey or PowerToys). The conflict
      status appears, and **Refresh** updates it.
- [ ] Game mode: turn it on from Settings, the Hotkeys page, the tray or Ctrl+Alt+G. Only the command palette and
      the game-mode toggle work; the on-screen game-mode indicator appears and goes away when you turn it off.
- [ ] **Turn on automatically during full-screen games**: start and leave a full-screen game. Game mode follows
      the game, while a game mode you turned on yourself stays on.
- [ ] The Quick Look and Hot corners rows on the Hotkeys page show a summary and their **Settings** links open
      Settings.
- [ ] On a PC without a working `VirtualDesktopAccessor.dll`, use **Choose file…**. Without a mouse hook, the
      page explains which wheel shortcuts are unavailable.

## Command palette

- [ ] Open the palette with its shortcut. Search apps, workspaces and actions; the recent list and prefix hint
      are readable.
- [ ] Use each prefix: `w ` windows, `f ` files, `b ` bookmarks, `ai ` AI, `=` calculator, `? ` web and `>`
      run. Backspace on an empty prefix returns to Commands; `help` lists the modes.
- [ ] Calculator: a sum, `5 km to mi`, and a time-zone question. An invalid expression shows a clear message.
- [ ] Web: search, enter a URL, and use the fallback "Search the web for …" row. Nothing is sent until Enter.
      A custom search template is validated.
- [ ] System commands: Lock, Sleep, Sign out, Restart, Shut down, Empty Recycle Bin, theme, mute and Do Not
      Disturb. For Sign out/Restart/Shut down/Empty Recycle Bin, check that a single click only asks for
      confirmation, then cancel: do not complete these actions on the daily-use PC. Test completion only in a
      disposable VM with disposable recycle-bin contents.
- [ ] `>` runs a command and keeps the last 20 in history; **Clear history** removes them.
- [ ] Clipboard mode lists text copied during the session; **Clear** empties it.
- [ ] Custom commands: add, edit, test with an optional query, and delete. Bad input is rejected with a message.
- [ ] AI answers: nothing is sent until you submit, a missing or wrong key is explained, and streaming can be
      cancelled.
- [ ] Escape hides the palette. Check it on each monitor and with High Contrast or transparency off.

## Quick Look

- [ ] Turn Quick Look on. Select an image, a PDF, a video, an audio file, a text/code file and a folder in File
      Explorer or on the desktop; press Space for each.
- [ ] Space or Escape closes the preview. Enter opens the file in its default app. Arrow keys keep moving the
      Explorer selection while the preview follows.
- [ ] A file type that cannot be previewed (for example an Office document) shows the thumbnail, name, type,
      size, dates and an **Open** button.
- [ ] Turn Quick Look off and check that Space works normally in Explorer again.
- [ ] With QuickLook or PowerToys Peek running, the Settings card shows the conflict note.
- [ ] Try a very large PDF, a text file over 1 MB, and a corrupt or zero-byte file. Nothing should hang; the
      status line explains what happened.
- [ ] With a Chinese, Japanese or Korean keyboard layout active, Space is left to Explorer (known 1.2.0 limit).

## Hot corners

- [ ] Turn Hot corners on and set a different action in each of the four corners: Task View, Show desktop,
      Start Focus, Lock and Open command palette.
- [ ] Rest briefly in a corner. The action fires once. Leaving before the dwell time cancels it.
- [ ] Check that only outer corners work, that dragging suspends the corners, and that a full-screen app does
      not trigger them.
- [ ] Turn Hot corners off and confirm no corner action fires.
- [ ] Use the Hotkeys page summary and **Settings** link to reach the corner settings.

## Clipboard

- [ ] Turn Clipboard history on and copy text in Notepad. Open the palette in Clipboard mode; the newest item is
      first.
- [ ] Paste an item into Notepad from the palette. The text arrives in the target app.
- [ ] Use an app that explicitly marks content as excluded from history (for example by setting the clipboard
      exclude flag), and copy disposable, non-sensitive text. That text should not be captured. Not every password
      field or password app sets the exclusion, so do not rely on one for this check.
- [ ] Turn the feature off, then on. The history is cleared. **Clear** empties it; FlowDesk exit also clears it.
- [ ] Fill the history with small items and with a large text block. The 50-item and size limits hold.
- [ ] Try to paste into an elevated app or back into FlowDesk. The paste is refused with a clear message.

## Contexts

- [ ] Open a workspace's details and pick each **Preset** (Study, Coding, Gaming). The fields below change and
      can still be edited.
- [ ] Set **Focus session** to start with a duration and strictness, open the workspace, and check the session
      starts. Set it to end and check the reverse.
- [ ] Set **Do not disturb** to on and off, open the workspace, and check Windows notifications follow. On a PC
      where it is unavailable, the link opens notification settings.
- [ ] Choose a **Sound output** device and open the workspace. The default playback device changes. On a PC
      where it is unavailable, the link and **Don't change sound output** work.
- [ ] The workspace card shows a context summary when any context is set.
- [ ] A strict focus context with block-all shows the caution text before it runs.

## Advanced

- [ ] Turn **Advanced mode** on from Settings, accept the precautions, and check the **Advanced** navigation
      item appears. Turn it off and check the item disappears.
- [ ] **Chris Titus WinUtil**: read the card, use the official website link, and only on a disposable PC press
      **Run** after the consent prompt.
- [ ] Install one small tool. The install shows progress, then **Open** appears. On a PC without winget, **Get
      App Installer** is offered.
- [ ] Open an installed tool from its card and from the command palette.
- [ ] Check that a failed or cancelled install leaves a readable status and does not claim success.

## Settings, export and import

- [ ] Switch theme between system, light and dark. The app and the palette follow, and the choice survives a
      restart.
- [ ] Toggle **Start with Windows** and check the per-user Run entry points at the current exe. Turn it off
      again.
- [ ] Change **Launch timeout** (10–60 s), **Minimize the previous workspace's windows** and the
      **automatic game mode** preference; restart and confirm they stuck. Game mode you turn on yourself is
      runtime only and is not expected to survive a restart.
- [ ] **Window shortcuts**: the description ends with the Hotkeys hint, and **Open Hotkeys** appears only while
      the feature is on.
- [ ] Export a backup with each section selected. The file is created and the privacy note is visible.
- [ ] Import it with **Add to my workspaces**, then try **Replace my workspaces** on a test copy. A preview is
      shown before anything changes.
- [ ] Use **Undo import** before making another change. It restores the previous state.
- [ ] Import a tampered or invalid file. The import fails with a clear message and changes nothing.
- [ ] Settings > About: the version is 1.2.0, **Open log folder** works, and **Release notes and updates** opens
      the GitHub releases page in the default browser.

## Tray

- [ ] Close the FlowDesk window. The tray notice appears (first time only) and the app stays in the tray.
- [ ] Click the tray icon and **Open FlowDesk**. The main window comes back to the front.
- [ ] **Workspaces** lists every workspace, marks the active one, and opens one from the menu. Entries are
      disabled while FlowDesk is busy.
- [ ] Start and pause a Focus session from the tray; the labels match the current duration and strictness.
- [ ] **Command palette** and **Game mode** in the tray work.
- [ ] **Exit** closes FlowDesk and saves the current state. If a session or a busy operation is active, the
      prompt makes sense.
- [ ] The tray menu follows light/dark theme.

## Installer

- [ ] Install 1.2.0 on a clean test PC. It starts, and the eight-step tour opens on first run.
- [ ] Upgrade an installed 1.1.0 in place. The version is 1.2.0 and the existing workspaces and settings are
      kept.
- [ ] Run the installer while FlowDesk is open. It closes the app and replaces it. If a Focus session or busy
      prompt blocks the close, the documented tray-exit retry works.
- [ ] Uninstall. The app and shortcuts go; `%LOCALAPPDATA%\FlowDesk` is kept.
- [ ] On an unsigned build, SmartScreen shows "Windows protected your PC"; **More info > Run anyway** works.
- [ ] Enable **Start with Windows** from the installed app and check the entry points at the installed exe.

## Other (cross-app checks)

- [ ] Replay the tour from Settings: Back, Next, Skip and Escape work, and step 7 points at Calm Windows.
- [ ] Navigate every page using only the keyboard; focus stays visible and returns after closing a dialog.
- [ ] Resize the main window and use 200% text scaling. Labels, buttons and error messages remain readable.
- [ ] Try light, dark and High Contrast themes. Selected, disabled and warning states remain distinguishable.
- [ ] Restart after changing ordinary settings; the last page and window position return on a connected monitor.

## How to write a good issue

1. Search existing issues first. One issue per problem.
2. Use the **Bug report** form. Fill in the version, Windows version and area.
3. Steps must be repeatable: what you opened, what you clicked, and in what order. "It did not work" is not
   enough.
4. Say what you expected and what actually happened. Copy the exact message.
5. Say how often it happens and whether a restart changes it.
6. Attach the newest log from Settings > About > Open log folder if FlowDesk crashed. Add screenshots or a short
   recording for anything visual; blur personal information.
7. Keep the issue focused. Put related improvements in a separate **Feature request**.

## Severity guide

- **Crash** (`severity:crash`): FlowDesk crashes, will not start, or a window becomes unusable.
- **Data loss** (`severity:data-loss`): workspaces, settings, saved positions or clipboard text are lost or
  corrupted.
- **Wrong result** (`severity:major`): a main feature does the wrong thing and there is no reasonable
  workaround.
- **Annoyance** (`severity:minor`): rough, slow or confusing behaviour with a workaround.
- **Cosmetic** (`polish`): layout, wording or theme problem that does not block the task.

Pick one severity per bug after triage. The full label list and colours are in [LABELS.md](LABELS.md).
