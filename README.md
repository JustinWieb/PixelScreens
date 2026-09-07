# Pixelscreens (working name)

Display templates plus DPI-correct cursor travel for multi-monitor Windows setups,
in a dark, minimal, pixel-accented UI.

Two mature engines, one new front end:

- **Display profiles** come from [DisplayMagician](https://github.com/terrymacdonald/DisplayMagician)
  (`upstream/DisplayMagician`, GPL-3.0). Capture a monitor arrangement, save it as a profile,
  apply it later with one click. Talks to NVIDIA, AMD, Intel, and Windows CCD directly.
- **Cursor layout** comes from [LittleBigMouse](https://github.com/mgth/LittleBigMouse)
  (`upstream/LittleBigMouse`, GPL-3.0). Lay screens out by physical size and position so the
  pointer crosses edges where it should, even across mixed DPI. The hook is a Rust daemon.
- **App** is `src/Pixelscreens.App`, an Avalonia 12 desktop app. It replaces both upstream UIs.

## Layout

```
src/Pixelscreens.App/        new Avalonia UI (the only place with our own screens)
upstream/DisplayMagician/    vendored with history via git subtree
upstream/LittleBigMouse/     vendored with history via git subtree (HLab.* squashed)
```

Both engines are referenced as projects, not copied, so upstream fixes can be pulled with
`git subtree pull`.

## Build (Windows)

Requirements: .NET 10 SDK, Visual Studio Build Tools 2026 with the .NET desktop and C++
workloads (DisplayMagician needs full MSBuild for a COM reference; the Rust hook needs the
MSVC linker), Rust 1.94 via rustup.

```powershell
# Rust cursor hook
cd upstream\LittleBigMouse\LittleBigMouse-Hook-Rust; cargo build --release

# App
msbuild Pixelscreens.slnx /t:Restore,Build /p:Configuration=Debug /p:Platform=AnyCPU /m
```

The app manifest requests administrator rights, which both engines need.

## License

GPL-3.0. See `LICENSE`. Upstream credits are in `CREDITS.md`.
