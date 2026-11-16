# Palette surface: R4 frame and Acrylic fix

Status: the mechanism was proven live in R3; R4 implements it. **Runtime is not verified yet** — Opus
owns the live check. Do not stop or redirect the user's installed FlowDesk.

## What R3 proved (live, build f0c4784)

- Outline: after `SetBorderAndTitleBar(false, false)` the native style was `0x04480000`
  (WS_DLGFRAME | WS_SYSMENU | WS_CLIPSIBLINGS, no WS_POPUP); window 800x185 vs client 794x179, i.e.
  `clientOffset 3,3`. Those 3 non-client px were the bright line; DWMWA_BORDER_COLOR = NONE returned S_OK.
- Acrylic was active but near-opaque: the default `DesktopAcrylicBackdrop` uses the Base kind (Dark
  surface pixel 44,44,44). Flyouts/Start use the Thin kind.

## R4 implementation (runtime unverified)

- Frame: `PaletteWindowStyle.Apply` clears WS_DLGFRAME, WS_THICKFRAME, WS_BORDER, WS_CAPTION, WS_SYSMENU
  and sets WS_POPUP, then SetWindowPos with SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER |
  SWP_NOACTIVATE. It runs after the presenter call and again after show and after every resize to
  repair frame bits if these AppWindow calls restore them. The DWM corner preference is retained;
  corners, shadow availability and the single remaining XAML stroke still require live verification.
- Acrylic: `PaletteAcrylicBackdrop` (custom SystemBackdrop) drives `DesktopAcrylicController` with
  `Kind = Thin` and a `SystemBackdropConfiguration`: `IsInputActive` true while shown, `Theme` synced to
  `Root.ActualTheme`, High Contrast and its colours inherited from WinUI's default configuration.
  Surface colours still come from theme brushes, so Light/Dark/High Contrast use system colours.
- Fallback: `Root` keeps the outer Grid, stroke, radius and resources. The new inner `SurfaceContent`
  Grid carries the padding and rows; the `SolidFallback` Border (`SolidBackgroundFillColorBaseBrush`,
  system Window colour in HC) sits behind it and stays hidden unless code shows it. It is shown when the
  controller is missing or inactive, transparency is off, or High Contrast is on.

## Capture (Debug only)

Before launching a Debug build in an approved live-test session, set (unchanged):

```powershell
$env:FLOWDESK_PALETTE_DIAGNOSTICS_LOG = 'D:\Workspace\_tmp\opencode\task015r4-live-surface.log'
```

The log is append-only, Debug-only and contains surface/native state only (no queries, results or
document names). R4 adds `kind=` / `controller=` / `state=` / `solidFallback=` from the backdrop and
`fallbackLayer=<Visibility>/<colour>` from `SolidFallback`.

## What Opus must verify live

Dark and Light, then High Contrast, with Windows Transparency effects both on and off:

- `clientOffset=0,0` and window size equal to client size at `shown` **and** after results `resized`.
- Style is WS_POPUP without DLGFRAME/SYSMENU; one quiet 1 px stroke, no bright frame; corners and shadow.
- Acrylic Thin visible in Dark/Light; the solid fallback appears only when expected (transparency off or
  High Contrast), with system Window colours in HC.
- Dismiss and reopen, and toggle transparency, so the Acrylic/fallback switch is shown in both directions.

Do not infer visual success from a successful build or from these logs alone.
