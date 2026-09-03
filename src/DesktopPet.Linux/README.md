# Desktop Pet — Linux (X11) Port

A native Linux port of [Adrianotiger/desktopPet](https://github.com/Adrianotiger/desktopPet), the animated desktop pet (eSheep and friends) that walks, falls, and climbs around your screen. The original app is Windows-only (WinForms + `user32.dll`); this port replaces the Windows-specific pieces with [Avalonia UI](https://avaloniaui.net/) and direct `libX11` P/Invoke calls, so it runs as a native binary on X11 desktops without Wine or emulation.

![A transparent, animated sheep walking along the bottom of the screen](../../Pets/esheep64/icon.png)

## Features

- Transparent, click-through-free animated pet windows rendered with Avalonia (no WinForms/GDI+).
- Full physics port: gravity, falling, landing on the screen edge/taskbar area, and landing on top of other application windows (via the `_NET_CLIENT_LIST_STACKING` EWMH hint).
- Drag and toss the pet with the mouse.
- System tray icon with a menu for spawning new pets, syncing pets, and opening Settings.
- A Settings window for spawning additional pets and switching between all bundled pet characters/colors (eSheep, Pingus, Neko, Pikachu, the colored sheep variants, and more) — closing it minimizes to the tray instead of quitting, Steam-style.
- Sound effects played via `ffplay` (part of `ffmpeg`) instead of the Windows-only NAudio backend.

## Requirements

- An X11 session. (Not tested under Wayland; may work via XWayland since it talks to `libX11` directly.)
- [`ffmpeg`](https://ffmpeg.org/) (for the `ffplay` sound backend). Sounds are silently skipped if it's not available.
- `libX11` (present on virtually every X11 desktop already).

## Installation

### Arch Linux

A `PKGBUILD` is available that builds a self-contained package (bundles its own .NET runtime, so no `dotnet` install is required to run it):

```bash
makepkg -si
```

This installs the `desktoppet` binary, a `.desktop` launcher entry, and an icon.

### Build from source

Requires the [.NET SDK](https://dotnet.microsoft.com/) (net10.0 or later).

```bash
cd src/DesktopPet.Linux
dotnet publish -c Release -r linux-x64 --self-contained true -p:InvariantGlobalization=true -o ./publish
./publish/desktoppet
```

For local development/testing without publishing a self-contained build:

```bash
cd src/DesktopPet.Linux
dotnet run
```

## Usage

- **Tray icon**: click it (or use its right-click menu) to open the Settings window, spawn a new pet, or sync all pets to the same animation.
- **Settings window**: pick any bundled pet character/color and click "Change Pet" to switch, or "New Pet" to spawn an additional one. Closing the window just hides it — use "Exit" (or the tray menu) to actually quit.
- **Mouse**: click and drag a pet to pick it up; release while moving to toss it.

## Known limitations

- Fullscreen-app detection (dropping topmost so a fullscreen game/video isn't obscured) is currently a stub.
- Multi-monitor behavior is implemented but not extensively tested.
- The system tray icon depends on your desktop environment supporting the StatusNotifierItem/AppIndicator protocol (most do; some minimal window managers and vanilla GNOME may need an extension).

## Credits

All original animations, pet artwork, and application design are from [Adrianotiger/desktopPet](https://github.com/Adrianotiger/desktopPet). This port only replaces the Windows-specific platform layer (rendering, window management, and audio) with cross-platform/X11-native equivalents.
