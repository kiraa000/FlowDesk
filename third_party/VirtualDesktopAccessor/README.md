# VirtualDesktopAccessor.dll (bundled third-party binary)

- Project: https://github.com/Ciantic/VirtualDesktopAccessor (MIT, see `LICENSE.txt` in this folder).
- Release: `2024-12-16-windows11` ("Windows 11 binary, works with 24H2, 26100.2605"), asset `VirtualDesktopAccessor.dll`.
- Size: 354,816 bytes (same as the release asset).
- SHA-256: `8740C572A1C000E3B87FFEB1E4C397EAE9AF3BD4A2ABDC3BCFFACAB4493F8FF5`
- Works on: Windows 11 24H2 (build 26100.2605) or newer. Older Windows 11 builds and Windows 10 need a different
  build of the DLL from the project's releases.

FlowDesk copies this file next to `FlowDesk.App.exe` (build output, publish folder and installer). At runtime it
first uses a DLL the user chose in Settings > Window shortcuts > "Choose file..." (stored in
`%LOCALAPPDATA%\FlowDesk\native\`), then this bundled copy. If the DLL does not work on the running Windows build,
Settings shows that, and only the virtual-desktop shortcuts are unavailable.

To update: replace the DLL, update the release, size and SHA-256 above, and keep `LICENSE.txt`.
