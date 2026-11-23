# Fizz

Minimal in-game performance overlay for Windows (FPS, temps, load, RAM, VRAM) that replaces MSI Afterburner/RivaTuner OSD. C# WinForms, .NET Framework 4.8. The overlay is a transparent, click-through, always-on-top window. Nothing is injected into games.

## Build and test
- `./build.sh` builds `out/Fizz.exe` with Mono's `mcs` against the .NET Framework 4.8 reference assemblies and the LibreHardwareMonitor DLLs in `lib/lhm096`. Unit tests are in `tests/Tests.cs`.
- The version is set in `build.sh` (the last argument of `tools/make_res.py`, e.g. `1.1.1.0`), which writes the VS_VERSIONINFO resource. Bump it every release. Minibar compares it with the GitHub tag.
- Tray icon comes from `tools/make_icon.py` into `src/fizz-tray.ico`.
- Release zip: `Fizz-x.y.z.zip` with `Fizz/Fizz.exe`, `Fizz.exe.config`, the LHM DLLs, `tools/PresentMon`, and the PawnIO setup.

## Gotchas
- The manifest is `asInvoker` and Fizz elevates itself. It also creates a logon scheduled task named `Fizz` so it starts with Windows without a UAC prompt.
- `Fizz.exe --exit` closes it cleanly: the launcher posts the message `Fizz.Exit` to the message-only hub, which accepts it from non-admin processes (ChangeWindowMessageFilterEx). Stop must not need a UAC prompt. A watchdog restarts Fizz after a crash, so a clean close has to go through `--exit`, not a kill.
- CPU temperature needs the PawnIO driver (installed separately). Some anti-cheats (e.g. FACEIT) may block that driver. Minibar's Fizz card shows this notice.
- Exclusive fullscreen can't be drawn over without injection. Games must run borderless.
- Never add game-injection or hooking.

## Working rules (all Pingo apps)
- Apps: Echoa, Fizz, Juice, Pour, Pulp, Minibar. Minibar is the hub that finds, installs, starts, stops and updates the others from GitHub releases (iiiPingo/<app>). Don't change how an app is named, found, installed, updated or closed without checking it still works with Minibar.
- Never rely on the exe's file name. Discord and browsers rename downloads to `Fizz (2).exe`. Identify an app by its version resource (ProductName), not by `<App>.exe`.
- Every release needs a zip with the app name in its file name, containing `<App>.exe`. Bump the exe's version every release.
- Never touch user data (settings.ini, logs, history, databases).
- This repo is public: never commit secrets, tokens, personal paths, build output (`out/`), crash logs, settings.ini or nuget.config.
- Work on a branch. Don't push, open a PR or publish a release until the owner says so.
- Add a test for each fix that can be tested off Windows. At the end, list what can only be checked on real Windows (sensors, driver, UAC, overlay on a real game).
- If unsure a fix is safe, report it instead of changing it.
- Keep README text in plain words; mention what's new each version.
- Explain changes simply, in plain language.
