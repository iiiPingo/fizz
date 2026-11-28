<p align="center"><img src="docs/logo.png" width="96" alt="Fizz logo"></p>
<h1 align="center">Fizz</h1>
<p align="center">A minimal in-game performance overlay for Windows. FPS, temps, load, RAM and VRAM, without Afterburner/RivaTuner.</p>
<p align="center"><a href="../../releases/latest"><b>Download the latest release</b></a></p>

## Features
- **Stats** you can switch on and reorder: FPS + 1% low, GPU/CPU temperature, GPU/CPU load, RAM, VRAM, present mode.
- **Safe with anti-cheat**: the overlay is a transparent, click-through, always-on-top window. Nothing is injected into games.
- Appears by itself when a game is in front (borderless/windowed), hides on the desktop.
- Hotkeys: **Ctrl+Alt+F** show/hide, **Ctrl+Alt+L** horizontal/vertical, **Ctrl+Alt+P** next preset.
- Per-game presets, or hide it completely in competitive games.
- Session summary when a game closes (avg FPS, 1% low, max temps) and optional CSV logs.
- Sensor picker if a default temperature sensor looks wrong. A watchdog restarts it if it ever crashes.

## Install
1. Install the **PawnIO** driver from [pawnio.eu](https://pawnio.eu) (lets Fizz read CPU temperature; replaces the old WinRing0 driver).
2. Download the `Fizz-x.y.z.zip` from [Releases](../../releases/latest), unzip it into its own folder and run `Fizz.exe` (click Yes on the UAC prompt).
   Fizz creates a logon task, so after that it starts with Windows without prompts.

Exclusive fullscreen can't be drawn over by anything that isn't injected, so run games in borderless mode. Note: some anti-cheats (e.g. FACEIT) may block the PawnIO driver.

## What's new
**1.3.0**
- App theme: Dark, OLED or Light, on the Advanced page. Dark is the same as before. OLED is pure black with near-black buttons and thin outlines. Light is the Windows 11 light look.
- The theme changes the settings window, its title bar and the tray menu right away. The overlay keeps its own look.

**1.2.2**
- Fixed: the overlay could show above an app covering the game, such as Discord. A running game now keeps the overlay in its place, even while Fizz's settings window is focused.

**1.2.1**
- The overlay is now glued to your game. It stays on a running game even when the game isn't focused.
- With two games open, the overlay moves to whichever game you focus, right away.
- Other windows now cover the overlay normally, and it no longer shuts off, restarts or floats above your apps.
- Dragging a windowed game is smooth: the overlay moves with the window.
- Alt-tabbing between games no longer shows a game summary or starts a new CSV log each time.

**1.2.0**
- Fixed: after you switched away from a windowed game, the overlay could jump back and forth between the game and the screen corner (most noticeable with "Always" on).
- Games page is easier: **Add open app…** lists the apps you have open, so you pick a game (or an app that isn't a game) instead of typing its exe. The "never a game" list is now a normal list with Add and Remove.
- Update rate has a new **3x per second** option.
- New presets start vertical. Ctrl+Alt+L still switches.
- New on the Look page (and the tray's Position menu): **Screen** keeps the overlay on your main screen when you're not in a game, and **Windowed games** keeps it in the screen corner instead of inside the game window. Either one stops it moving around.
- New look for the settings window and tray menu: the Outfit font (built into Fizz, nothing to install), icons in the side menu, Windows 11 rounded menus with ticks instead of square check boxes. The overlay itself looks the same as before.
- Accent colour picker works like Minibar's: the family colour dots, plus a **+** circle for any custom colour.
- Questions like "Delete preset?" now use a dark box instead of the white Windows one.

## Build
`build.sh` builds `out/Fizz.exe` with Mono's `mcs` against the .NET Framework 4.8 reference assemblies and the LibreHardwareMonitor DLLs in `lib/lhm096`. Unit tests are in `tests/Tests.cs`.

## Third-party
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 0.9.6 (MPL-2.0), DLLs in `lib/lhm096`
- [PresentMon](https://github.com/GameTechDev/PresentMon) 2.6.0 (MIT), bundled in the release under `tools/`
- [PawnIO](https://pawnio.eu) (GPL-2.0 driver, installed separately)
- [Outfit](https://github.com/Outfitio/Outfit-Fonts) font (SIL Open Font License 1.1), in `src/fonts`, used for the settings window

## License
MIT for Fizz's own code. Third-party components keep their own licenses.
