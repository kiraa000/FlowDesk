# Issue labels

Create these labels in **GitHub > Issues > Labels**. Use the exact names (including the colon) and hex colours so
the issue forms apply `bug` and `needs-triage` and the team can filter by area and severity. GitHub's colour field
takes the hex without the leading `#` (for example `d73a4a`).

Rules of use:

- Every issue gets one `area:<name>` label matching the **Area** dropdown in the issue form.
- Every bug gets one `severity:<level>` label once triaged. A purely cosmetic bug may use `polish` alone instead
  of a severity label.
- Use `needs-triage` when an issue has not been read yet; remove it after triage.
- `feature` is for feature requests; `docs` and `polish` are optional extra labels.

## Base labels

| Name | Colour | Description |
| --- | --- | --- |
| `bug` | `#d73a4a` | Something in FlowDesk does not work as expected. |
| `needs-triage` | `#fbca04` | Not reviewed by the team yet. |
| `feature` | `#a2eeef` | New feature or improvement request. |
| `docs` | `#0075ca` | Documentation, help text or release notes. |
| `polish` | `#c5def5` | Small UI, wording or consistency improvement. |
| `testing` | `#0e8a16` | A test task: check a feature that was not tested live yet. |

## Severity labels

| Name | Colour | Description |
| --- | --- | --- |
| `severity:crash` | `#b60205` | FlowDesk crashes, fails to start or the window becomes unusable. |
| `severity:data-loss` | `#e11d21` | Loses or corrupts workspaces, settings, layout positions or clipboard data. |
| `severity:major` | `#d93f0b` | A main feature is broken and there is no reasonable workaround. |
| `severity:minor` | `#fef2c0` | Wrong or rough behaviour with a workaround. |

## Area labels

One per **Area** option in the issue form.

| Name | Colour | Description |
| --- | --- | --- |
| `area:workspaces` | `#d4c5f9` | Saving, opening, editing, deleting and updating workspaces. |
| `area:layouts` | `#d4c5f9` | Layouts, tiling, monitor choice and saved positions. |
| `area:focus` | `#d4c5f9` | Focus sessions, blocked/allowed apps and strictness. |
| `area:calm-windows` | `#d4c5f9` | Calm Windows check-up, changes, undo and startup apps. |
| `area:hotkeys` | `#d4c5f9` | Hotkeys page, window shortcuts, game mode and conflicts. |
| `area:command-palette` | `#d4c5f9` | Command palette, search modes, calculator, web, AI and custom commands. |
| `area:quick-look` | `#d4c5f9` | Quick Look previews and Space handling. |
| `area:hot-corners` | `#d4c5f9` | Hot corner actions and dwell behaviour. |
| `area:clipboard` | `#d4c5f9` | Clipboard history and paste-back. |
| `area:contexts` | `#d4c5f9` | Per-workspace Focus, Do Not Disturb and sound output. |
| `area:advanced` | `#d4c5f9` | Advanced mode, tool detection, installation and WinUtil. |
| `area:settings` | `#d4c5f9` | Settings, theme, startup, export and import. |
| `area:tray` | `#d4c5f9` | Tray icon, tray menu and tray actions. |
| `area:installer` | `#d4c5f9` | Installer, upgrade, uninstall and Start with Windows. |
| `area:other` | `#d4c5f9` | Anything outside the areas above. |

`area:calm-windows` and the other slugs are deliberately lower case with hyphens; the issue form uses the
human-readable names in the dropdown.
