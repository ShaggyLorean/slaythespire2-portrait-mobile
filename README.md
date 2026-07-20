# Slay the Spire 2 Portrait Mobile

An experimental portrait-first Android launcher and runtime patch set for Slay the Spire 2.

This project uses [StS2 Launcher Overhaul](https://github.com/SocialHummingbird/StS2-Launcher-Overhaul) as its launcher, Android runtime, Steam, download, recovery, and cloud foundation. The portrait UI, layout, and on-device work is integrated directly into that foundation instead of being injected into a prebuilt APK.

> This is an unofficial community project. It does not include game files or game assets. You need your own legitimate copy of Slay the Spire 2.

## What changed

- Android and Godot are locked to sensor portrait.
- The game uses a phone-width virtual canvas with safe-area handling.
- Combat, top bar, hand, energy, End Turn, map, events, shops, rest sites, Neow, character selection, and common edge buttons receive portrait layouts.
- The launcher is a single-column, touch-first portrait interface.
- Startup now begins with an explicit choice between Offline and Steam Online.
- The overhaul's Steam authentication, depot downloader, Steam Guard, Cloud controls, diagnostics, startup recovery, LAN, and shader warmup remain available.

The design audit and the mapping from the supplied before/after references live in [docs/portrait-design-audit.md](docs/portrait-design-audit.md).

## Launcher modes

### Offline

Offline mode never signs in to Steam and disables Steam Cloud injection for that launch.

1. On the phone, create `/storage/emulated/0/StS2Portrait/Offline/`.
2. From your own PC installation, copy these into it:
   - `SlayTheSpire2.pck`
   - the complete `data_sts2_windows_x86_64` directory
3. Open the launcher, choose **Offline**, grant file access, and tap **Import Local Files**.
4. The importer copies into a staging directory, validates the staged PCK and required assembly, then swaps it into place. The previous live installation is kept as `game-backup`.

Offline mode has no Steam downloads, achievements, ownership checks, or Steam Cloud. Updating is a manual reimport.

### Steam Online

Steam Online keeps the overhaul workflow:

- Steam sign-in and Steam Guard
- ownership verification
- game download/update from Steam
- explicit Cloud pull and push controls
- encrypted refresh-token storage through Android Keystore
- offline fallback for an already verified cached installation if the connection times out

Cloud push is never automatic. Pull is deliberately shown before Push because Push can overwrite remote data.

## Architecture

```text
Android launcher / custom Godot runtime
               |
               v
        STS2Mobile managed entry
               |
       +-------+----------------+
       |                        |
       v                        v
Launcher MVC              Harmony startup groups
  Offline importer          platform / saves
  Steam + download          portrait layouts
  Cloud controls            touch / lifecycle
  diagnostics               mods / LAN
```

Important directories:

```text
src/STS2Mobile/Portrait/       Portrait viewport and game UI patches
src/STS2Mobile/Launcher/       Offline/online launcher MVC and recovery flow
src/STS2Mobile/Steam/          SteamKit, depot download, and Cloud implementation
src/STS2Mobile/Patches/        Non-portrait Android compatibility patches
android/                       Android wrapper and custom Godot Gradle project
scripts/                       Build, device test, and release tooling
```

The old portrait repository's reflection-based `PortraitLauncher`, APK injector, runtime tuning bridge, and duplicate landscape/mobile patches are intentionally not used. The portrait layer is now a normal startup patch group in `STS2Mobile.dll`.

## Building the managed project

Requirements:

- .NET 9 SDK
- your own Slay the Spire 2 managed assembly directory for compile-time references

```bash
dotnet build src/STS2Mobile/STS2Mobile.csproj \
  -c Release \
  -p:GameReferenceDir="/path/to/Slay the Spire 2/data_sts2_windows_x86_64"
```

No referenced game DLL is copied into the repository by this build.

## Building Android

The Android build still requires the overhaul's custom Godot 4.5.1 Android/.NET runtime, Android SDK/NDK, and the licensed runtime dependencies described in [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).

On a configured checkout:

```bash
./scripts/setup-godot-source.sh
./scripts/build-godot.sh
./scripts/build.sh
```

Local development defaults to package `com.sts2portrait.mobile.dev`. Production builds should use `com.sts2portrait.mobile` and a persistent release keystore.

## Verification status

- Managed Release build: passing against the current local game API used during development.
- Portrait and launcher patch types: compile-verified.
- Android manifest: sensor portrait for launcher, game, and fallback activities.
- Offline importer: staged-copy and rollback behavior implemented; physical-device validation is still required.
- Full Android APK/device regression: requires the custom engine/runtime dependencies and ARM64 hardware.

See [docs/portrait-design-audit.md](docs/portrait-design-audit.md) for the required device test matrix.

## Safety and licensing

- Never commit or publish `SlayTheSpire2.pck`, game DLLs, Steam credentials, refresh tokens, save files, FMOD/Spine binaries, APK signing keys, or proprietary game assets.
- Offline import only reads files the user places in shared storage.
- Steam passwords are not persisted by the launcher.
- Preserve the notices in [LICENSE](LICENSE) and [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).

## Credits

- [SocialHummingbird/StS2-Launcher-Overhaul](https://github.com/SocialHummingbird/StS2-Launcher-Overhaul) — launcher/runtime foundation.
- [Ekyso/StS2-Launcher](https://github.com/Ekyso/StS2-Launcher) — original launcher foundation.
- Mega Crit — Slay the Spire 2.
