# Palette surface: frame, Acrylic and R5 Light readability

Status: R3 proved the mechanism; R4's frame fix and Dark Thin Acrylic passed live. R5's Light readability is
**not verified yet**, and corners/shadow stay conditional — Opus owns the live check. Do not stop or redirect the user's installed FlowDesk.

## What R3 proved (live, build f0c4784)

- Outline: after `SetBorderAndTitleBar(false, false)` the native style was `0x04480000`
  (WS_DLGFRAME | WS_SYSMENU | WS_CLIPSIBLINGS, no WS_POPUP); window 800x185 vs client 794x179, i.e.
  `clientOffset 3,3`. Those 3 non-client px were the bright line; DWMWA_BORDER_COLOR = NONE returned S_OK.
- Acrylic was active but near-opaque: the default `DesktopAcrylicBackdrop` uses the Base kind (Dark
  surface pixel 44,44,44). Flyouts/Start use the Thin kind.

## R4 implementation (frame and Dark Acrylic passed live; corners/shadow unverified)

- Frame: `PaletteWindowStyle.Apply` clears WS_DLGFRAME, WS_THICKFRAME, WS_BORDER, WS_CAPTION, WS_SYSMENU
  and sets WS_POPUP, then SetWindowPos with SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER |
  SWP_NOACTIVATE. It runs after the presenter call and again after show and after every resize to
  repair frame bits if these AppWindow calls restore them. The DWM corner preference is retained;
  corners and shadow availability still require live verification.
- Acrylic: `PaletteAcrylicBackdrop` (custom SystemBackdrop) drives `DesktopAcrylicController` with
  `Kind = Thin` and a `SystemBackdropConfiguration`: `IsInputActive` true while shown, `Theme` synced to
  `Root.ActualTheme`, High Contrast and its colours inherited from WinUI's default configuration.
  Surface colours still come from theme brushes, so Light/Dark/High Contrast use system colours.
- Fallback: `Root` keeps the outer Grid, stroke, radius and resources. The new inner `SurfaceContent`
  Grid carries the padding and rows; the `SolidFallback` Border (`SolidBackgroundFillColorBaseBrush`,
  system Window colour in HC) sits behind it. R5 shows it only for a missing controller, transparency off
  or High Contrast; native Fallback (opening/hidden or persistent) uses the controller's own colour/transition.
- Live R4: `clientOffset 0,0` (also after reopen), Thin Acrylic Active in Dark, quiet stroke.

## R5 implementation (runtime unverified)

- Light Thin was too transparent: over a dark window the surface read ~#717171 and secondary text
  ~#3B3B3B, about 2.3:1. The palette hint and keyboard footer now use `TextFillColorSecondaryBrush`.
- Profiles are applied after the Theme sync and both keep `Thin`; no opaque XAML sits over an active
  Acrylic. Light: tint `FC`, TintOpacity `0`, LuminosityOpacity `.85`, FallbackColor `F9` (WinUI 2.6.5
  `AcrylicBackgroundFillColorDefaultBrush`). Dark: tint `2C`, TintOpacity `.15`, LuminosityOpacity
  `.80`, FallbackColor `2C` (WinUI's Dark default is `.96`, lowered for visible variation).
- Contrast follows WinUI `AcrylicBrush`'s luminosity-then-tint colour recipe
  ([cpp](https://github.com/microsoft/microsoft-ui-xaml/blob/main/controls/dev/Materials/Acrylic/AcrylicBrush.cpp),
  [h](https://github.com/microsoft/microsoft-ui-xaml/blob/main/controls/dev/Materials/Acrylic/AcrylicBrush.h)).
  Neutral pure black/white backing gives a Light surface of 214.2 and a Dark surface of 86.2; 2%
  adversarial noise plus rounding gives 209 and 90. Secondary text (alpha `9E` black / `C5` white)
  composites to 80 and 217. WCAG ratios are emitted by the lead tests and are not claimed here.
- The main WinUI source is not proof for the closed `DesktopAcrylicController` on 2.6.5, so live
  measurement is still required.

## Capture (Debug only)

Before launching a Debug build in an approved live-test session, set (unchanged):

```powershell
$env:FLOWDESK_PALETTE_DIAGNOSTICS_LOG = 'D:\Workspace\_tmp\opencode\task015r5-live-surface.log'
```

The log is append-only, Debug-only and contains surface/native state only (no queries, results or document
names). R4 adds `kind=` / `controller=` / `state=` / `solidFallback=` and `fallbackLayer=<Visibility>/<colour>`; R5 adds the chosen tint, tint opacity, luminosity and fallback values.

## What Opus must verify live

Dark and Light, then High Contrast, with Windows Transparency effects both on and off:

- `clientOffset=0,0` and window size equal to client size at `shown` **and** after results `resized`.
- Style is WS_POPUP without DLGFRAME/SYSMENU; one quiet 1 px stroke, no bright frame; corners and shadow.
- Measure actual Light/black and Dark/white surfaces plus normal mixed-content Acrylic: it must still
  visibly tint/blur rather than read as a solid fill.
- Dismiss/reopen: transient `state=Fallback` must not add the XAML cover (`solidFallback=False`).
- Toggle transparency/HC: explicit fallback must show immediately and recover without a timer or stale reopen callback.

Do not infer visual success from a successful build or from these logs alone.
