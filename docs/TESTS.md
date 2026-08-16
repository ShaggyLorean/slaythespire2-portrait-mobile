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
| Game-independent layer compiles after dialog/ZIndex/copy changes | T2 | see round notes | recorded after each harness run |
| PortraitHudMetrics math (band geometry, monotonicity, reference-canvas headroom) | T2 | see round notes | `LauncherPcHarness` exit code |
| Java changes (orientation coercion, cover sizing) | - | REVIEW ONLY | no Android SDK on this PC; compiles at the next APK build |

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

## Handoff: restoring T3 + APK builds on this PC

Blocked on game files only. Once Slay the Spire 2 is installed (or its files restored) on the PC:

1. Copy or reference `data_sts2_windows_x86_64` and run:
   `dotnet build src/STS2Mobile/STS2Mobile.csproj -c Release -p:GameReferenceDir="<path>/data_sts2_windows_x86_64"`
2. The APK additionally needs the toolchain from README (custom Godot 4.5.1 runtime via `scripts/setup-godot-source.ps1` + `scripts/build-godot.ps1`, Android SDK/NDK, JDK, gradle), then `scripts/build-android-local.ps1`.
