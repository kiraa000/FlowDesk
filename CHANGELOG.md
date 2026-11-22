# Changelog

Notable changes to FlowDesk, written for users. This file follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### New
- Alt+Ctrl+Left/Right/Up/Down move the active window's border and resize the windows that touch it, so snapped layouts keep fitting. **Resize touching windows together** in Settings > Window shortcuts is on by default and also applies to Alt+Shift+Wheel.

## [1.2.0] - 2026-11-22

### New
- Command palette with modes for apps, windows, files, bookmarks, web search, calculations, unit conversions and time zones; Windows actions, custom commands and explicit run mode.
- Optional AI answers in the palette: questions are sent only on explicit submission, with securely stored keys and cancellable streaming.
- Quick Look previews for selected Explorer/desktop files, including images, PDF, media, text and folders; palette files can use the same preview window.
- Configurable hot corners with dwell and full-screen safeguards.
- Hotkeys page to view, enable and rebind shortcuts, with conflict checks and live status.
- Calm Windows: a Windows check-up and opt-in changes to suggestions and distractions, with a durable undo journal.
- Advanced page for optional tools, detection and consent-based installation.
- Workspace Contexts for audio output, Do Not Disturb and Focus preferences.
- Optional memory-only text clipboard history in the palette, with guarded paste-back.
- Settings export/import with preview, Add/Replace choices, backups and undo before the next change.
- Game mode suspends input features other than the command palette; toggle it with Ctrl+Alt+G and see on-screen status, or enable automatic full-screen detection.
- Tray actions to open a workspace or start a Focus session; Settings > About now links to release notes and updates.

### Improved
- Physical monitor identities and friendly display names make saved positions and monitor choices clearer.
- Undo workspace deletion, edit individual workspace items, start an empty workspace, and extend a Focus session by five minutes.
- Focus shows its expected finish time; Calm Windows shows when it last checked Windows.
- Settings are grouped by task, shortcut status is easier to read, and the eight-step tour covers newer features.
- Sign-in launches can stay in the tray; the tray menu follows the app theme.
- The navigation label is now **Calm Windows**. **Hotkeys** lists all shortcuts; **Window shortcuts** remains the separate feature switch, with an Open Hotkeys link when enabled.

### Fixed
- Stopped the Hotkeys page repeatedly raising unchanged row properties, which caused long-running memory growth.
- Fixed a display-enumeration startup failure that prevented window resume, game-mode checks, the first Windows check-up and the tour from running.
- Fixed nested Settings expanders crashing when opened, and improved tour target positioning and running-app picker rows.
- Hardened workspace switching, window identity and saved layout/show-state restoration, including apps that clamp restored rectangles.
- Improved dialog/shutdown race handling, shortcut hook cleanup and warnings, and the in-app fallback for Focus's Allow 5 minutes action.
- Bounded Quick Look PDF rendering and hardened corrupt-store recovery and settings import validation.
- Accumulated high-resolution wheel input correctly and limited workspace restore to creating at most one adjacent virtual desktop.

### Behaviour changes
- Clicking a palette result no longer confirms **Shut down**, **Restart**, **Sign out** or **Empty Recycle Bin**. Confirm with Enter instead.
- Import keeps this PC's local AI provider and endpoint. Import Undo expires after the first later change.
- If an unreadable data/settings file cannot be safely set aside, saving is disabled for that run with a warning rather than overwriting it.
- Imported workspace items with network/UNC or reparse-point paths are skipped with a warning.

[1.2.0]: https://github.com/kiraa000/FlowDesk/releases/tag/v1.2.0
