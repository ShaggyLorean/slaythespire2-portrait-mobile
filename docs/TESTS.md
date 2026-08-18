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
| PC visual rig end-to-end (T2.5) | pc-visual | PASS | `tools/pctest/run-game-visual.ps1`: portrait patches injected into the sandboxed desktop game via its own mod loader, window pinned 1298x2856 -> canvas 1180x2596 (reference bucket), scripted walk main menu -> character select -> tutorial popup -> map -> turn-1 combat, screenshots + tree dumps per step, no OS input, no visible window, Steam disabled via `--force-steam=off` |
| Portrait layouts visually verified on PC (menu, char select, map+HUD, combat) | pc-visual | PASS | shots under `tmp/pctest/shots-20260818-*`; two new findings filed from the evidence (BUG-012 hand clipping, BUG-013 map under HUD) |
| BUG-003 event fix verified on the reported event | pc-visual | PASS | Rig teleports into `BYRDONIS_NEST` via the game's own console commands (`DevConsole` driven in-process); title and body now start below the HUD stack, options readable — the exact screen from the original report. Shot: `shots-20260818-135139/06-event-byrdonis.png` |
| Room sweep: rest site, shop room, treasure room portrait-clean at reference canvas | pc-visual | PASS | Console jumps `room restsite/shop/treasure`; rig hides the map-screen overlay console jumps leave behind (rig-only state). Caveats: 0-relic HUD, merchant inventory panel not yet opened |
| Compact-bar redesign round (design revision after review) | pc-visual | PASS | `shots-20260818-144548/`: slim two-row bar + shared band on event/map/rest/shop/treasure, expanded stack + combat band in combat, mode switch driven by the combat frame lifecycle. Event composition restored (title right under the bar), map points clear the band and scroll under it |
| BUG-012 hand fix verified in combat | pc-visual | PASS | `shots-20260818-144548/10-combat.png`: five-card fan fully visible; hand guard telemetry showed holder pinned at target 2344 across ticks. Root-caused the rig's phantom desktop bottom inset along the way (non-Android now uses simulated insets only) |
| Slim-bar polish round: rewards, deck view, gradient scrim | pc-visual | PASS | `shots-20260818-151045/`: rewards panel centered and reachable; deck view clean (capstone screens force the slim bar even mid-combat, hand hidden while open, room/floor/boss cluster dropped from the slim bar via per-node modulate); black backdrop slab replaced by a deep-teal fade scrim, verified against the beige map art |
| BUG-011 wave 2: settings body, Neow, card choice, multi-relic bar | pc-visual | PASS | `shots-20260818-153056/` and later rounds: Neow event portrait-clean with a five-relic slim bar; the card-choice screen (Choose a Card) portrait-clean; settings body gets the capstone treatment (whole HUD cluster steps aside via modulate, tab row shifted below the safe top). Icon drift filed as BUG-014 |
| BUG-011 wave: pause menu, merchant inventory, death screen | pc-visual | PASS | `shots-20260818-152204/`: pause and death are portrait-clean as authored (no patch needed); merchant inventory's utility band re-anchored at 0.8 scale so relic/potion shelves and prices fit inside the mat. Remaining unvisited: settings body, Neow/ancient events, boss relic select, potion prompts, card select overlays, compendium, LAN, many-relic bar variation |
| Capstone detection repair + hand restore + watermark identity hide + event focus crop | pc-visual | PASS | `shots-20260818-163729/`: the capstone check now reads both real screen locations (pause/settings under the stack's `Submenus`, deck view as a direct container child) instead of the always-visible furniture; that same broken check had been living in a second copy inside the hand-visibility helper and had kept the combat hand invisible for three rounds — deduplicated into `PortraitCapstone.IsOpen`, seven-card fan back on screen (`10-combat.png`). Build watermark labels (`%ReleaseInfo`/`%ModdedWarning`) now hidden by identity through their manager (in-run text `[ver] (date)` matches no safe text rule). Event art crop got a rule-of-thirds focus bias (0.38) with edge clamping: the Byrdonis nest+egg now sits centered in the art band instead of half-cut at the left edge (`06-event-byrdonis.png`). Rig-side: `Shot` no longer hides the map screen when the map IS the current room (a legit-map guard), which had blacked out `04/05` captures |
| Slim-bar slot restore after combat (potion capsule, room icon) | pc-visual | PASS | `shots-20260818-164153/16-neow.png`: combat pins the potion container and room icon as grandchildren of the row; their margin-container slots do not re-sort them on the way back, so the slim bar restores them explicitly. Before the fix the capsule floated mid-art on every post-combat non-combat screen (Neow at 74,481 instead of its 423-row slot) |
| Compendium family: shelf fits the canvas, tabs walked, menu watermark gone | pc-visual | PASS | `shots-20260818-183624/`: the compendium margin box kept its landscape footprint (1920 wide, 200-unit margins, 1520 vbox minimum) and pushed Character Stats past the canvas edge; margins and minimums now yield to the canvas and everything centers in the band. Probe (`STS2_PCTEST_SCENARIO=compendium`) invokes the menu's open handler directly (the button is gated on NumberOfRuns, always zero on the wiped sandbox profile) and walks card library, relic collection and potion lab via Close. Card library is portrait-clean as authored (filter rail + 2-column grid). Menu scenes now run the watermark sweep too (it only rode the top-bar reflow before) |
| Card-select family probe: Smith upgrade select portrait-clean as authored | pc-visual | PASS | `shots-20260818-180210/07b-upgrade-select.png` + follow-up round: the rig now clicks the second campfire card (resolved by type and order, no distinct names) and captures `NDeckUpgradeSelectScreen`; the 3-column grid, bottom prompt and View Upgrades tickbox need no patch at the reference canvas. Exit control is named `Close`. Walk budget raised to 200s for the two extra steps; the transform/enchant screens share this family and are expected to match |
| Touch-scale wave 2: campfire choices, run-end buttons, proceed ribbon | pc-visual | PASS | `shots-20260818-175519/07-rest-site.png` + `17-death.png`: the campfire prompt and its two action cards grow through FillScale and drop into the thumb zone (the seated character stays fully visible above the row; the hover description re-anchors above the prompt), the game-over Continue/Leaderboard buttons lift to the touch minimum and hang from the band bottom stacked, and every NProceedButton grows at _Ready with the right-edge clamp made scale-aware. Full walk 17/17, no timeouts |
| Touch-scale pass: rewards panel, treasure chest, ancient bubble; metrics helpers | pc-visual | PASS | `shots-20260818-174000/09-treasure.png` + `shots-20260818-174351/11-rewards.png`: sizing now goes through pure functions in `PortraitHudMetrics` (ContentBottom/ContentBandHeight with NavClearance, FillScale clamped to never shrink, CenterX, BottomAnchoredY, TouchScale with MinTouchSide 96, EdgeMargin 30) per the touch-layout directive: grow everything, no small targets, respect the gesture strip and the cutout. The loot panel fills the width under whichever bar is active (the rewards band is combat-aware since the screen shows over the combat room), the proceed control hangs off the panel's lower right above the leftover hand fan (ZIndex 460), and the treasure chest grows to the side margins centered in the band. Registration audit added after two silent no-op rounds: any `[HarmonyPatch]` class in the portrait namespace missing from the patch list now logs `UNREGISTERED` at startup (the audit is armored; its first version took the whole patch group down via `GetTypes`) |
| Ancient event composition: bubble with the speaker, options in the thumb zone | pc-visual | PASS | `shots-20260818-172410/n1-neow.png` (probe) + `shots-20260818-172531/16-neow.png` (full walk): the ancient layout's content vbox is stretched over the whole free band with a stretchy spacer pushed between the dialogue and options containers, so the speech bubble sits right under the bar beside the speaker (y 361) and the options anchor at the bottom (2267-2559). The layout's intro tween kept rewriting the authored bottom-anchored position, so the patch re-asserts on a 0.5s chain for the screen's lifetime (hand-guard pattern) plus a postfix on `SetDialogueLineAndAnimate` (which itself rewrites the container height on the last line). Applies to every `NAncientEventLayout` (Neow and act ancients). New probe scenario `STS2_PCTEST_SCENARIO=neow`; tree dumps now go 12 deep and include internal children (the option rows were invisible to the old dump) |
| BUG-015 input shield fix + merchant reachable + full-walk regression round | pc-visual | PASS | Probe rounds first isolated it (fresh walk, no capstone screens: merchant `IsEnabled=True`, active screen `NMerchantRoom`, click still dead), then `shots-20260818-170640/`: with `SetVisible` restoring authored mouse filters, the merchant inventory opens in both the probe and the full walk (`15-shop-inventory.png`, mat re-anchored with priced shelves). Rig honesty fix landed alongside: console-jump map residue is now closed through `NMapScreen.Close(false)` (IsOpen, CombatManager unpause, ActiveScreenContext update) instead of `Visible=false`, so rooms behind it are genuinely interactive; the recurring post-`event NEOW` crash stopped appearing once rooms were no longer swapped under a pseudo-open map. Full walk: 17/17 captures, zero step timeouts, hand/potion/deck-view/map checks all green in the same round |

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
