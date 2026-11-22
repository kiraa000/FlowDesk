# FlowDesk 1.2.1

This release adds in-app updates, resizing that keeps snapped windows fitting together, and a game mode fix.

**Install or upgrade:** run `FlowDesk-1.2.1-win-x64-setup.exe`. The installer upgrades 1.2.0 in place and keeps your workspaces and settings. FlowDesk 1.2.0 cannot update itself, so install 1.2.1 by hand once; later versions can be installed from inside FlowDesk. Builds are unsigned; see [GETTING-STARTED](../GETTING-STARTED.md) for setup and SmartScreen guidance.

## New
- **In-app updates.** FlowDesk checks for a new version once a day and shows **Install now / Release notes / Not now** when one is available (also in the tray menu). The update downloads only from this project's GitHub releases, is checked against the release's `SHA256SUMS.txt`, installs silently and restarts FlowDesk. Nothing installs without your click. **Not now** hides that version until a newer one appears. Turn checks off with **Check for updates** in Settings > About.
- **Resize touching windows together.** Alt+Ctrl+Left/Right/Up/Down move the active window's border and resize the windows that touch it, so snapped layouts keep fitting without overlap. The setting **Resize touching windows together** (Settings > Window shortcuts) is on by default and also applies to Alt+Shift+Wheel.
- Dragging a shared border with the mouse resizes the touching windows when you release the mouse button.

## Fixed
- After a shortcut that game mode blocks, the next Ctrl+Alt+G that turns game mode off was ignored and needed a second press. It now works on the first press.

## Privacy
- The daily version check is an anonymous request to the public GitHub releases API. It sends no user data and can be turned off. Downloaded update files are removed automatically after FlowDesk restarts.
