# Test log

Rule from the project plan: nothing is called fixed until it is tested and the result is written down here. PC checks are pre-screening; the device pass is the final word.

## Verification tiers

| Tier | What it proves | What it cannot prove |
| --- | --- | --- |
| T1 `pck-verify` | `scripts/verify-bootstrap-pck.py` rebuilds the bootstrap PCK and re-parses the binary from disk | anything about the game PCK or runtime behavior |
| T2 `pc-compile` | `tools/pctest/LauncherPcHarness.csproj` compiles the game-independent layer (launcher, components, Steam client, PortraitDisplay, PortraitHudMetrics) against official GodotSharp 4.5 + SteamKit2 packages and runs the pure-math checks in `Program.cs` | files that need `sts2.dll` (Harmony patch layer), Java, visual behavior |
| T3 `managed-build` | `dotnet build src/STS2Mobile/STS2Mobile.csproj -p:GameReferenceDir=...` compiles everything including the patch layer | requires the game's `data_sts2_windows_x86_64` assemblies on the PC |
| T4 `device` | APK on ARM64 hardware, scenario below executed and observed | - |

Current environment note (2026-08-16): the dev PC has no game installation (post-format), so T3 and APK builds are blocked until the game files are restored; see the handoff section at the bottom.

## PC pre-screening configuration

Per the plan, PC checks must use real device metrics, not arbitrary window sizes.

- Reference device: OnePlus 13, Android 16. v0.3.0 was validated at 1440 x 3168.
- Before the next device round, capture and paste the authoritative values here from the device:
  - `adb shell wm size` (physical resolution)
  - `adb shell wm density` (DPI)
  - `adb shell dumpsys display | grep -i cutout` (cutout geometry)
- Canvas math for reference: PortraitDisplay clamps the virtual width to 980-1180 (1440 / 1.10 magnification, clamped to 1180), so a 1440 x 3168 screen gets a ~1180 x 2596 canvas and a physical-to-canvas factor of ~0.82.

## Result log

### 0.4.0 round 1 (2026-08-16, PC)

| Check | Tier | Result | Notes |
| --- | --- | --- | --- |
| Bootstrap PCK orientation = SENSOR_PORTRAIT (5) in project.binary and project.godot | T1 | PASS | `python scripts/verify-bootstrap-pck.py`: `OK ... (3 entries)` |
| Game-independent layer compiles after dialog/ZIndex/copy changes | T2 | PASS | `dotnet build tools/pctest -c Release`: 0 errors against GodotSharp 4.5.1 + SteamKit2 3.4.0 |
| PortraitHudMetrics math (band geometry, monotonicity, reference-canvas headroom) | T2 | PASS | `dotnet run` in tools/pctest: `OK: PortraitHudMetrics checks passed`, exit 0 |
| Java changes (orientation coercion, cover sizing) | - | REVIEW ONLY | no Android SDK on this PC; compiles at the next APK build. Engine-side call path verified against Godot 4.5 source: `GodotIO.setScreenOrientation` calls `activity.setRequestedOrientation`, which our override intercepts |
| Full managed build including the Harmony patch layer | T3 | PASS | `dotnet build src/STS2Mobile -c Release -p:GameReferenceDir="D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"`: Build succeeded, 0 errors |
| BUG-001 game-leg root cause evidence | T1 | PASS | Retail `SlayTheSpire2.pck` parsed: `display/window/handheld/orientation` absent from `project.binary` (151 settings), engine default `SCREEN_LANDSCAPE` (0) applies; confirms the activity-boundary fix choice |
| Engine + APK chain rebuild | build | PASS | Custom Godot 4.5.1 arm64 (mono + sts2 patch) built from source and injected into the AAR; `build-android-local.ps1` produced and structurally verified the APK |

**Device-round APK:** `StS2Portrait-v0.4.0-dev1-arm64-v8a.apk`, versionCode `40010`, package `com.sts2portrait.mobile.local` (installs alongside the release/dev app), local test keystore, sha256 `31c73198f632443a37e6bc62541a3d351050f9b509b4a34d514478f4e9b23a55`. Fresh package = no downloaded game inside; use Steam Online download or Offline import on device before the game-side steps.

### Device round for 0.4.0 + 0.5.0 (pending)

Run on the reference device, in this order. Every step lists what must be seen.

1. **Cold start orientation** (BUG-001): force-stop the app, launch from the icon. Expected: portrait from the first visible frame; no landscape flash, no rotation animation. Repeat 3 times.
2. **Game handoff orientation** (BUG-001): press Start game. Expected: the restart into the game shows no landscape frame at any point up to the main menu. Logcat must show `Coerced requested orientation ... to sensor portrait` at least once (proves the override intercepted the engine).
3. **Repair dialog stacking** (BUG-002): More options, then Repair game files. Expected: dimmed backdrop, dialog panel and Cancel/OK fully readable above all buttons; tapping a button area under the backdrop does nothing; Cancel closes and everything is tappable again. Do NOT confirm unless you intend to re-download.
4. **Repair dialog copy** (BUG-005): same dialog. Expected text starts with "Repair game files?" and names the active mode's behavior (delete + redownload for Steam, remove import for Offline).
5. **Cloud dialogs** (BUG-002 regression): Upload saves and Download saves confirmations render above content the same way. Cancel both.
6. **Event text clearance** (BUG-003): enter any event (map icon "?"). Expected: first text line starts below the relic row; nothing renders under HP/gold/potions. Check one long event with 4+ choices: all choices remain on screen and tappable.
7. **Combat regression** (BUG-006 removal + BUG-004 band): enter combat. Expected: unchanged combat layout (hand, energy, End Turn, piles); top band covers the cutout area completely with no raw blue-grey gutter above or below it. Capture logcat line `Combat top cover sizing: cutoutInset=...` and paste it here.
8. **Non-combat strip check** (BUG-004): on map/event/shop, confirm content clears the punch-hole (no strip is drawn outside combat by design; SafeTop handles it).
9. **Launcher smoke after dead-code removal** (BUG-006): full launcher flow: mode select, login state restore, download state, More options, Troubleshooting, Show details. Expected: identical to v0.3.0 behavior.
10. **UI Scale sanity** (BUG-009, known-open): change UI Scale in game settings; record what happens (expected today: no effect). This is evidence for the 0.6.0 decision, not a pass/fail.

Record results as: step, PASS/FAIL, device, APK version, and for failures a screenshot plus focused logcat.

### Gutter investigation (BUG-004 follow-up, same device round)

From step 7's log line: if `cutoutInset` >= the fallback on the reference device and no gutter is visible above the band, the `width / 12` floor can likely be dropped next release; if a gutter is visible on any cutout-less device, we finally have its true size. Paste the numbers here either way.

## Build environment on this PC (restored 2026-08-16)

The full chain was reconstructed post-format and is working:

- Game reference: `D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64` (also seeded into `upstream/godot-export/.godot/mono/publish/arm64` for the build script's dependency fallback: sts2, 0Harmony, GodotSharp, Steamworks.NET, Sentry).
- .NET SDK 9.0.317; JDK 17 at `C:\Program Files\Microsoft\jdk-17.0.20.8-hotspot`; Android SDK at `%LOCALAPPDATA%\Android\Sdk` (platform-tools, android-35, build-tools 35.0.0, NDK 28.1.13356709); Gradle 8.11.1 at `tmp\toolchain\gradle-8.11.1` (repo has no gradlew.bat).
- Godot 4.5.1-stable source at `vendor/godot` with `patches/godot/sts2-android-runtime.patch` applied; `scripts/build-godot.ps1 -Arches arm64` rebuilds `libgodot_android.so` and injects it into `android/libs/release/godot-lib.template_release.aar` (base AAR fetched from Maven `org.godotengine:godot:4.5.1.stable`; the engine patch touches C++ only, so the stock Java side is correct).
- `android/assets/dotnet_bcl` seeded with the .NET 9.0.7 mono-android BCL (169 assemblies + `System.Private.CoreLib.dll` from the runtime pack's `native/` dir), matching the game's 9.0.7 runtime exactly; the mono runtime pack is also cached at `tmp\nuget-runtime` for the native `.so` staging step.
- Signing: `tmp\localtest.keystore` (localtest defaults from the build script).

Full APK recipe: `dotnet publish` happens inside `scripts\build-android-local.ps1 -VersionName <v> -VersionCode <c> -AndroidHome "%LOCALAPPDATA%\Android\Sdk" -JavaHome "C:\Program Files\Microsoft\jdk-17.0.20.8-hotspot" -GradlePath "tmp\toolchain\gradle-8.11.1\bin\gradle.bat" -Abi arm64-v8a`.
