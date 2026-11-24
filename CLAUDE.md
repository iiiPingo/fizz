# Fizz

Minimal in-game performance overlay for Windows (FPS, temps, load, RAM, VRAM) that replaces MSI Afterburner/RivaTuner OSD. C# WinForms, .NET Framework 4.8. The overlay is a transparent, click-through, always-on-top window. Nothing is injected into games.

## Build and test
- `./build.sh` builds `out/Fizz.exe` with Mono's `mcs` against the .NET Framework 4.8 reference assemblies and the LibreHardwareMonitor DLLs in `lib/lhm096`. Unit tests are in `tests/Tests.cs`.
- The version is set in `build.sh` (the last argument of `tools/make_res.py`, e.g. `1.2.0.0`), which writes the VS_VERSIONINFO resource. Bump it every release. Minibar compares it with the GitHub tag.
- Tray icon comes from `tools/make_icon.py` into `src/fizz-tray.ico`.
- Outfit (SIL Open Font License, `src/fonts/OFL.txt`) is built into `Fizz.exe` by the `-resource` lines in `build.sh` and loaded at startup in `Theme.cs`. Nothing is installed on the PC. The app window and tray menu use it with Segoe UI as the fallback; the overlay keeps its own font.
- Release zip: `Fizz-x.y.z.zip` with `Fizz/Fizz.exe`, `Fizz.exe.config`, the LHM DLLs, `tools/PresentMon`, and the PawnIO setup.

## Gotchas
- The manifest is `asInvoker` and Fizz elevates itself. It also creates a logon scheduled task named `Fizz` so it starts with Windows without a UAC prompt.
- `Fizz.exe --exit` closes it cleanly: the launcher posts the message `Fizz.Exit` to the message-only hub, which accepts it from non-admin processes (ChangeWindowMessageFilterEx). Stop must not need a UAC prompt. A watchdog restarts Fizz after a crash, so a clean close has to go through `--exit`, not a kill.
- CPU temperature needs the PawnIO driver (installed separately). Some anti-cheats (e.g. FACEIT) may block that driver. Minibar's Fizz card shows this notice.
- Exclusive fullscreen can't be drawn over without injection. Games must run borderless.
- With "Always on", the overlay position is checked against the game window. Alt-tabbing away from a windowed game used to make it bounce back and forth; a test in `tests/Tests.cs` covers it, so keep it passing.
- Never add game-injection or hooking.

## Releasing
When a change is ready to ship, leave it fully done, so the release can go out without rebuilding or repackaging:
1. Bump the version (see Build and test), add the "what's new" to the README and to `README.txt` in the zip (a "NEW IN x.y.z" section at the top, plain words), and run the tests.
2. Build the exe from the final source, last, after the final change. The exe you leave is the one that ships. Copy it to `C:\Claude Projects\Fizz\Fizz.exe` so it can be tried.
3. Build the full release zip `Fizz-x.y.z.zip`: one top folder `Fizz/` with `Fizz.exe`, `Fizz.exe.config`, `README.txt`, the LibreHardwareMonitor DLLs and the System.* helper DLLs, `tools/PresentMon-*.exe` and `tools/PawnIO_setup-*.exe`. Copy the DLLs and tools from the previous release zip if they did not change. Save it to `C:\Claude Projects\Releases\Fizz-x.y.z\`. A zip with only the exe is not a release zip.
4. Push the branch. Push to `main` when the owner says push or release. Never force-push. Tags are plain versions (`vX.Y.Z`) and are created on GitHub when the owner publishes.
5. Do not create the GitHub release or tag yourself. The owner attaches the zip and clicks Publish. The "My project apps" chat prepares the release page and checks it afterwards; it does not rebuild.
6. Finish with one message: what changed in short plain bullets (these become the release notes, no dashes), the version, the exe's SHA256, the zip path, the tests run, and what only real Windows can check.

## Working rules (all Pingo apps)
- Apps: Echoa, Fizz, Juice, Pour, Pulp, Minibar. Minibar is the hub that finds, installs, starts, stops and updates the others from GitHub releases (iiiPingo/<app>). Don't change how an app is named, found, installed, updated or closed without checking it still works with Minibar.
- Never rely on the exe's file name. Discord and browsers rename downloads to `Fizz (2).exe`. Identify an app by its version resource (ProductName), not by `<App>.exe`.
- Every release needs a zip with the app name in its file name, containing `<App>.exe`. Bump the exe's version every release.
- Never touch user data (settings.ini, logs, history, databases).
- This repo is public: never commit secrets, tokens, personal paths, build output (`out/`), crash logs, settings.ini or nuget.config.
- Work on a branch. Don't push, open a PR or publish a release until the owner says so. "Push", "release" and "update GitHub" count as that go-ahead: then push to `main`.
- Add a test for each fix that can be tested off Windows. At the end, list what can only be checked on real Windows (sensors, driver, UAC, overlay on a real game).
- If unsure a fix is safe, report it instead of changing it.
- Keep README text in plain words; mention what's new each version.
- Explain changes simply, in plain language.
