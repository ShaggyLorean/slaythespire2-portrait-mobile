# sts2-portrait

A **portrait (vertical) UI mod** for *Slay the Spire 2* (Early Access). It re-lays-out the game's 1920×1080 landscape UI so a full run — menus, map, combat, shop, rewards, deck — is playable on a phone held upright or on a vertical monitor.

Philosophy (borrowed from [balatro-portrait-mobile](https://github.com/ShaggyLorean/balatro-portrait-mobile)): **this repo contains only original patch code — you bring your own game files.** Nothing from the game is redistributed here.

## What it does

- True portrait orientation: the mod calls `DisplayServer.ScreenSetOrientation(Portrait)` at runtime, so on Android you get real portrait, not a rotated landscape frame (a manifest-only change is **not** enough — Godot resets orientation from project settings at startup).
- **Full-screen shop**: the merchant's wide wares strip becomes a screen-filling 3-column grid (cards + card-removal service) with a relics/potions band. Re-flows live after purchases; open/close animations respected.
- **Two-row dynamic top bar**: row 1 (portrait/HP/gold/potions/floor/boss) auto-scales as content grows — a 6-potion belt fits; row 2 holds your run relics (left, auto-scaling — verified with 11 relics) and Map/Deck/Settings (right). The version watermark is shrunk and moved below the icons. The whole bar re-applies itself periodically, so *any* content change reflows automatically.
- Portrait re-layouts for the map (parchment fills the screen, legend clamped on-screen), combat (background covers the vertical screen, hand raised and fan-compressed, energy/end-turn reachable), main menu, character select, settings, pause, events and reward screens.
- **Input**: the game's own pointer model is already touch-shaped — drag a card up/onto an enemy to play, long-press End Turn to confirm — and Godot's touch→mouse emulation drives it on Android. Verified end-to-end with synthetic pointer gestures (drag-to-play attack and skill cards, pile views, long-press End Turn).

Implemented as a standard mod for the official mod loader: `Sts2Portrait.dll` + manifest json, with [Harmony](https://github.com/pardeike/Harmony) patches (the game ships `0Harmony.dll`).

## Requirements

- Your own copy of *Slay the Spire 2* (Steam, Early Access).
- To build from source: .NET 9 SDK.

## Install — PC

1. Build the mod (see [Building](#building)) or grab a release DLL matching your game version.
2. Copy into the game's mods folder:

   ```
   <game install>/mods/Sts2Portrait/
     ├── Sts2Portrait.dll
     └── Sts2Portrait.json
   ```

3. Launch the game normally.

**Windows:** works as-is.

**Linux:** the game (and the mod) run fine under Proton; plain Wine did not work in our testing. Developed and tested with **GE-Proton**. Steam installs can just use Steam's Proton; for a non-Steam launch, point your Proton wrapper at `SlayTheSpire2.exe` with its own prefix.

## Install — Android (⚠ experimental, not yet device-verified)

There is **no official Android version** of Slay the Spire 2. Two community launchers wrap the game's managed code (`sts2.dll`) and assets (`SlayTheSpire2.pck`) in an ARM-native Godot runtime. **Nothing below has been verified on a real device yet**, and there is a hard version constraint — read [Version compatibility](#version-compatibility--read-this) first: the launchers lag behind the PC game, so the mod DLL must be **rebuilt against the `sts2.dll` of whatever game version your launcher actually runs** (and the manifest's `min_game_version` adjusted), or it will not load.

### Path A: Ekyso StS2-Launcher

[Ekyso/StS2-Launcher](https://github.com/Ekyso/StS2-Launcher) (MIT) obtains the game by **logging into your Steam account with an unofficial client (SteamKit2)** and downloading the depot. Be aware what that means before using it: you are giving your Steam credentials to a third-party app, which is against Steam's ToS and carries account risk. This repo deliberately ships no Steam-login mechanism of its own.

If you use it, the mod goes to the launcher's mods folder:

```
/storage/emulated/0/StS2Launcher/Mods/Sts2Portrait/
  ├── Sts2Portrait.dll
  └── Sts2Portrait.json
```

The mod flips orientation at runtime, so no APK changes are needed for portrait. Note: Android mod loading has been flaky in some launcher releases (upstream issue #31).

### Path B: darthalex2014 NoSteam launcher

With the [darthalex2014 fork](https://github.com/darthalex2014/StS2-Launcher) you copy **your own** game files (`SlayTheSpire2.pck` + `data_sts2_windows_x86_64/`) from your PC install to `/storage/emulated/0/StS2/` — no credentials involved. **Whether this fork loads mods at all (and from which folder) is unverified.** If it does not, this path currently cannot deliver the portrait mod: the APK-manifest repack alone does **not** produce portrait (Godot resets orientation at startup; the mod's runtime call is required).

`scripts/android-repack.sh` patches **your own** launcher APK's manifest to `sensorPortrait` (apktool → zipalign → sign, input never modified, nothing downloaded). Useful *in addition to* a working mod load — never *instead of* it.

## Bring your own game files

- **In this repo:** original patch source code, a manifest, and build/helper scripts. Nothing else.
- **Never in this repo:** game assets, `sts2.dll`, `SlayTheSpire2.pck`, any game executables/data, rehosted launcher APKs, or any Steam-download automation.

You must own the game and copy your own files where needed. The mod does not touch DRM and does not download anything on your behalf.

## Version compatibility — read this

Harmony patches are compiled **against a specific `sts2.dll`**. This mod currently targets game **v0.107.1**. Slay the Spire 2 is Early Access and updates often; any game update can break the mod — expect to rebuild per game version. The mod's initializer applies each patch class independently, so when an update renames one method, the rest of the portrait UI keeps working and the log tells you exactly which patch was skipped.

## Building

```bash
dotnet build src/Sts2Portrait/Sts2Portrait.csproj -c Release \
  -p:GameDir="/path/to/Slay the Spire 2"
```

`GameDir` (or the `STS2_GAME_DIR` env var) must point at your own game install; the build references `sts2.dll`, `GodotSharp.dll` and `0Harmony.dll` from its `data_sts2_windows_x86_64/` folder — referenced only, never copied into the output. The built DLL lands in `src/Sts2Portrait/bin/Release/net9.0/`; pair it with the manifest json.

For the dev helper scripts (`scripts/`), create a git-ignored `scripts/local.env`:

```bash
GAME="/path/to/Slay the Spire 2"
GAME_DIR="$GAME"
```

## Status

Playable end-to-end in portrait on PC: menu → character select → map → combat (incl. Act 1 boss and Act 2) → rewards → shop → deck/pile views. See `STATUS.md` for screen-by-screen detail and `ANDROID-DELIVERY.md` for the Android strategy research.
