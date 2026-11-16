# Palette surface: collect evidence before changing the frame

TASK-015 R3 has **not established the visual cause**. The installed FlowDesk instance must not be
stopped or redirected by an agent. Opus owns the live check and any temporary instance/data setup.
No WS_THICKFRAME removal, WM_NCCALCSIZE override, or custom backdrop controller has been added.

## Capture (Debug only)

Before launching the R3 Debug build in an approved live-test session, set:

```powershell
$env:FLOWDESK_PALETTE_DIAGNOSTICS_LOG = 'D:\Workspace\_tmp\opencode\task015r3-live-surface.log'
```

Use an existing writable parent directory and an absolute filename. The log is append-only and
contains surface/native state, not queries, results or document names. Release does not emit it.
The opt-in Debug backdrop derives from DesktopAcrylicBackdrop and delegates its lifecycle to base;
it only reads WinUI's default SystemBackdropConfiguration. It does not force IsInputActive or theme.

Capture a Dark-theme palette open with Win+Alt+Space, results resizing, dismissal and reopen.
Compare Light and High Contrast; also record whether Windows Transparency effects is on.
Check the quiet outline, Acrylic, corners, shadow, focus, one activation per press and no Start on
Win-up. Do not infer visual success from a successful build or from these logs alone.

## What the records distinguish

- `before-presenter`, `after-presenter`, `attributes-set`, `constructed-hidden`: actual GWL_STYLE /
  GWL_EXSTYLE and border/corner DWM HRESULTs/readback, around the presenter's native changes.
- `resized`, `shown`, activation and `activation-settled`: whether later AppWindow operations reset
  those attributes, and whether WinUI's configuration agrees with native foreground ownership.
- `window`, `client`, `clientOffset`: non-client space, alongside style bits WS_THICKFRAME `0x40000`,
  WS_DLGFRAME `0x400000`, WS_BORDER `0x800000`, WS_POPUP `0x80000000`. Style bits alone do not prove
  which component painted a visible line. A successful DWMWA_BORDER_COLOR readback of `FFFFFFFE`
  suppresses the DWM border colour; it does not prove there is no residual non-client frame.
- `surface`, `stroke`, `resultsBackground`, `strokeThickness`: resolved XAML colours/alpha. The
  normal surface and results are intended transparent; High Contrast deliberately uses solid
  system colours and a visible stroke. A white stroke must be checked against ActualTheme and HC.
- `configuration`: default backdrop IsInputActive, theme, High Contrast and configuration-change
  count, or `disconnected`. Compare the settled shown record, not the expected inactive hidden one.
- `advancedEffects`, `acrylicSupported`, `highContrast`, `batterySaver`, `remoteSession`: system
  fallback conditions. These are clues, not a claim that the native controller is rendering Acrylic.

If the shown, foreground palette has inactive/default-theme configuration, investigate WinUI's
configuration lifecycle before implementing a custom controller. If configuration and transparent
fills are correct, investigate system fallback/composition rather than forcing activation. For the
outline, first distinguish native non-client pixels from the resolved XAML stroke using the live
image and these records; only then change the responsible layer.
