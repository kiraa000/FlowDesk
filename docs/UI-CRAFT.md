# UI craft rules

Rules every FlowDesk screen follows so the app looks crafted and native to Windows 11. Based on Microsoft's
Windows design guidance (learn.microsoft.com/windows/apps/design), Fluent 2 (fluent2.microsoft.design) and the
Windows Community Toolkit docs.

## 1. What "AI slop" looks like (avoid all of these)

- Indigo/purple gradients, glow, glassmorphism used as decoration.
- Everything in a rounded card; identical card grids; "three feature cards" rows.
- Emoji or random icon sets used as UI icons; decorative badges and pills with no meaning.
- A centred hero with big vague copy ("Supercharge your productivity") on a tool screen.
- Uniform padding everywhere, so nothing has hierarchy; faint shadows on every surface.
- Several accent colours competing; colour carrying meaning with no text/icon backup.
- Filler copy, exclamation marks, marketing tone inside the app; lorem-style sample data.
- Only the happy path: no empty, loading, error or disabled states.
- Custom-drawn controls that imitate native ones badly (fake title bars, fake toggles).

## 2. What crafted UI does instead

- **Let the task define the layout.** The first screen shows the user's real objects (workspaces,
  timer state) and the one or two primary actions, not a welcome banner.
- **Hierarchy from type and spacing, not boxes.** Use the type ramp; group by proximity (8 px inside a
  group, 12-16 px between related groups, 24-32 px between sections). Cards only where a card groups
  a thing and its actions.
- **One accent.** The system accent (`AccentFillColor*`) marks selection and the primary button per
  view (one `AccentButtonStyle` per view at most). Everything else uses neutral theme brushes.
- **Density for a tool.** Desktop tools can be compact; align everything to a 4 px grid and to shared
  left edges. Consistent column widths; numbers right-aligned or tabular.
- **Every state designed:** empty (says what to do + one action), loading (ProgressRing/ProgressBar,
  skeleton only for long lists), error (InfoBar with what happened + what to do), disabled (with a
  reason via tooltip), hover/pressed/focus (built-in control styles give these for free - don't
  override templates without reason).
- **Keyboard-first:** logical tab order, `AccessKey`s on main commands, Enter/Escape on dialogs,
  `KeyboardAccelerator`s for frequent actions (shown in tooltips).
- **Microcopy:** sentence case, verbs on buttons ("Save workspace", not "OK"), specific nouns, no
  "Oops!", no exclamation marks, numbers with units ("25 min").
- **Motion explains change only:** built-in theme transitions, connected animation for item-to-detail;
  ~150-300 ms; nothing loops or bounces. Respect "Animation effects" off (UISettings.AnimationsEnabled).
- **Real sample/seed data** that looks like a real person's (plausible apps, file names, times).

## 3. Windows 11 / WinUI 3 specifics

**Type ramp** (size/line-height epx; XAML style): Caption 12/16 `CaptionTextBlockStyle`; Body 14/20
`BodyTextBlockStyle`; Body Strong 14/20 semibold `BodyStrongTextBlockStyle`; Body Large 18/24
`BodyLargeTextBlockStyle`; Subtitle 20/28 `SubtitleTextBlockStyle`; Title 28/36 `TitleTextBlockStyle`;
Title Large 40/52 `TitleLargeTextBlockStyle`; Display 68/92 `DisplayTextBlockStyle`. Font is Segoe UI
Variable (default). Page title = Title; section header = BodyStrong (convention: 24-32 top margin,
8 bottom); secondary text = Body/Caption with `TextFillColorSecondaryBrush`. Never set FontSize ad hoc.

**Geometry:** 4 px base grid. `ControlCornerRadius` = 4, `OverlayCornerRadius` = 8 (cards, dialogs,
flyouts). Use the resources, never literal radii. 16 px between a surface edge and its text; 8 between
adjacent buttons; 12 between content areas. Page content margin (convention, WinUI Gallery/PowerToys):
36-56 left/right on wide windows, 16-24 on narrow; content max width ~1000 px for settings-like pages.
Pointer targets >= 32 px tall for buttons (standard control height), 40x40 for touch.

**Materials:** main window `SystemBackdrop = new MicaBackdrop()` (Mica Alt: `MicaBackdrop { Kind =
MicaKind.BaseAlt }` for tabbed/title-bar-heavy windows). Acrylic only for transient surfaces
(flyouts/menus - they get it automatically). Layer brushes: content area on Mica =
`LayerFillColorDefaultBrush` with `CardStrokeColorDefaultBrush` border (convention: the NavigationView
content "layer" with an 8 px top-left radius); cards = `CardBackgroundFillColorDefaultBrush` +
`CardStrokeColorDefaultBrush`, 1 px border, `OverlayCornerRadius`. Never a solid custom background over
Mica.

**Title bar:** `ExtendsContentIntoTitleBar = true` + `SetTitleBar(element)` (or the `TitleBar` control,
Windows App SDK 1.7+). Show app icon (16 px) + app name in Caption style; leave the caption-button area
clear (use `AppWindow.TitleBar.RightInset`); height 48 when it holds a search box or back button, 32
otherwise. Set `AppWindow.TitleBar.PreferredHeightOption` to match.

**Navigation:** `NavigationView` `PaneDisplayMode="Left"` for 5-10 destinations, `Top` for <= 5 equal
ones, `LeftCompact` when icons are self-explanatory. Use the built-in Settings item
(`IsSettingsVisible`). Back button only if there is real back navigation. `BreadcrumbBar` for nested
detail pages (e.g. Workspaces > Study).

**Colour:** always `{ThemeResource ...}` brushes: `TextFillColorPrimary/Secondary/TertiaryBrush`,
`AccentFillColorDefaultBrush`, `SystemFillColorSuccess/Caution/CriticalBrush` (+ matching
`...BackgroundBrush`) for status. Support Light, Dark and High Contrast; never hard-code hex colours
in XAML except a user-picked tag colour (and then give it a text label too).

**Icons:** Segoe Fluent Icons via `FontIcon Glyph="&#xE...;"` or `SymbolIcon`; 16 px in lists/buttons,
20 in nav, 24-32 for empty-state illustrations at most. App icons of real programs: extract the exe icon
(Shell) rather than a generic glyph. No emoji.

**Controls to reach for:** `InfoBar` (inline status/errors), `TeachingTip` with `Target` (onboarding
steps; chain them yourself - one tip open at a time, Back/Next/Skip in its buttons/content),
`ContentDialog` (confirm destructive actions only), `SettingsCard`/`SettingsExpander` from
`CommunityToolkit.WinUI.Controls.SettingsControls` 8.2.x (settings rows; group them under section
headers with 4 px between cards - convention), `ItemsView`/`GridView`/`ListView`, `ProgressRing`,
`NumberBox`, `ToggleSwitch` (binary settings, label says what On means), `SegmentedControl`/`RadioButtons`
for 2-4 exclusive options, `CommandBar` for page commands, `MenuFlyout` for overflow ("..." button).

**Accessibility:** visible focus (default `FocusVisual` - don't remove), `AutomationProperties.Name` on
icon-only buttons, tooltips on icon-only buttons, contrast >= 4.5:1 (theme brushes give this), works
at 200 % text scaling without clipping.

## 4. Self-review checklist (run on every screen before calling it done)

1. Is the main task obvious in 3 seconds? Is there exactly one primary (accent) action?
2. Does every text use a ramp style and a theme text brush? Any ad-hoc FontSize/hex colour?
3. Alignment: do elements share left edges on the 4 px grid? Any uniform-padding mush?
4. Is any card just a decoration? Remove it or merge it.
5. Empty, loading, error, disabled states present and worded well?
6. Keyboard: tab through the whole screen; Enter/Escape; accelerators on frequent commands.
7. Light, Dark and High Contrast all look right (switch in Windows settings).
8. Resize to 500 px wide and to full screen: nothing clipped, nothing stretched absurdly.
9. Copy: sentence case, specific verbs, no marketing tone, no emoji.
10. Anything from section 1 left? Then it is not done.

Verify by looking: take a screenshot of the running app (both themes) and review it against this
list; do not judge from XAML alone.
