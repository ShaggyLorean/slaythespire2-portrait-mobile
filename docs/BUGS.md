# Bug inventory

Tracked bugs for the portrait project. Every entry keeps the same fields so fixes stay auditable:

- **Where**: launcher / game screen / build tooling
- **Repro**: how to trigger it
- **Expected vs actual**
- **Root cause**: filled once diagnosed, with file references
- **Status**: `open` / `fixed-pending-device` / `verified` / `wontfix-by-design`
- **Fixed in**: version that carries the fix

Status meaning: `fixed-pending-device` = code fix landed and passed the PC-side checks available for it, but the on-device pass in [TESTS.md](TESTS.md) has not confirmed it yet. A bug is only `verified` after the matching device test passes.

---

## BUG-001: Launcher and game briefly force landscape at startup

- **Where**: launcher cold start and game handoff, both.
- **Repro**: cold-start the app, or press Start game. The window appears in landscape for a moment, then rotates back to portrait.
- **Expected**: portrait from the very first frame; no transient rotation.
- **Actual**: one visible landscape flash per engine boot (twice per full launcher-to-game session).
- **Root cause**: two independent legs, one shared mechanism. The Godot engine applies the mounted PCK's `display/window/handheld/orientation` during setup, overriding the manifest's `sensorPortrait` via `setRequestedOrientation` before any C# patch can run (the first `PortraitDisplay.Apply()` call happens before the SceneTree exists and silently no-ops, `src/STS2Mobile/Portrait/PortraitDisplay.cs:32`).
  - Launcher leg: `scripts/make-bootstrap-pck.py` wrote `orientation=4`, which is `SCREEN_SENSOR_LANDSCAPE` in the Godot 4 enum (`SCREEN_SENSOR_PORTRAIT` is `5`). The bootstrap PCK was actively requesting landscape.
  - Game leg: verified against the retail PCK (2026-08-16): `SlayTheSpire2.pck`'s `project.binary` (151 settings) does not contain `display/window/handheld/orientation` at all, so the engine falls back to its default, `SCREEN_LANDSCAPE` (0), on every game boot. Because the key is absent, an in-place PCK patch could not fix this (adding a key would resize the file), which is why the fix lives at the activity boundary instead.
- **Fix**: bootstrap PCK now writes `orientation=5`, and `GodotApp` overrides `Activity.setRequestedOrientation` to coerce every non-portrait request to sensor portrait at the OS boundary. That covers both PCKs and any future engine-side request with one choke point.
- **Status**: fixed-pending-device
- **Fixed in**: 0.4.0

## BUG-002: Launcher confirmation dialogs render behind the buttons

- **Where**: launcher, any confirmation dialog; reported from the Repair game files confirmation over the expanded More options group.
- **Repro**: expand More options, tap Repair game files (or Upload/Download saves). Dialog text and Cancel/OK interleave with the section buttons and are unreadable; underlying buttons stay clickable.
- **Expected**: modal dialog above all launcher content, content blocked while it is open.
- **Actual**: dialog draws above the panel background but below every button; buttons also keep receiving input.
- **Root cause**: ours, not upstream's. `StyledPanel` set `Content.ZIndex = 10` (`src/STS2Mobile/Launcher/Components/StyledPanel.cs`), lifting all section content above the dialog, which was added as a plain `ColorRect` at ZIndex 0 (`LauncherView.ShowConfirmation`). Upstream's baseline has no such ZIndex override.
- **Fix**: dialogs now live on a dedicated `CanvasLayer` above all layer-0 content, and the `Content.ZIndex` override is removed (tree order already keeps content above the backdrop).
- **Status**: fixed-pending-device
- **Fixed in**: 0.4.0

## BUG-003: Run HUD overlaps content on non-combat screens

- **Where**: in-game, every screen outside combat; worst on events (screenshot: Byrdonis Nest, first lines of event text unreadable under the HP/gold/potion stack).
- **Repro**: enter any event with the run HUD visible. Event body text starts under the HUD column.
- **Expected**: content on every screen starts below the space the HUD occupies.
- **Actual**: HUD (`ZIndex 400`) draws over event text; other non-combat screens have per-screen, inconsistent protection (shop computes its own top offset, rest site has none).
- **Root cause**: the portrait HUD is the game's own `NTopBar` re-anchored with no screen gating (`PortraitTopBar.Apply`, `src/STS2Mobile/Portrait/PortraitLayoutPatches.cs`), and the live event patch (`EventRoomPatch`) recenters the text block on X only; the authored landscape Y lands inside the HUD band. No shared "HUD occupies this much vertical space" source existed.
- **Fix (0.5.0, revised)**: first attempt reserved space under the full stacked HUD, which fixed the overlap but produced dead bands and inconsistent compositions (rejected on review). The shipped design keeps the expanded HUD for combat only; outside combat the HUD is a slim two-row bar over one shared ink backdrop band, `ContentTop` derives from that bar, and `EventRoomPatch` keeps the authored Y whenever it already clears the bar. Every non-combat screen now wears the same top.
- **Status**: fixed-pending-device (event screens); PC-visual PASS on the originally reported event (`BYRDONIS_NEST`, `shots-20260818-144548/06-event-byrdonis.png`); remaining unpatched screens tracked under BUG-011
- **Fixed in**: 0.5.0 (event slice, compact-bar design)

## BUG-004: Top strip reserved for the punch-hole is fixed-size and combat-only

- **Where**: in-game combat (three mechanisms), plus design question for every other screen.
- **Repro**: play combat on a device without a cutout, or with a different cutout height than the reference device: the reserved strip is wrong (wasted space or misaligned).
- **Expected**: reserved space tracks the actual display cutout instead of being tuned to one device.
- **Actual**: three independent combat-only mechanisms, all fixed-size: renderer clear color band, `PortraitCombatFrame` bands (top solid 118 canvas px hardcoded), and a native Android cover view sized `width / 12` (`GodotApp.java`). The cutout inset API is already bridged (`getDisplayCutoutTopInsetPixels`) but only feeds content placement (`PortraitDisplay.SafeTop()`), never the strip geometry. Outside combat no strip is drawn at all; content placement is already inset-driven there.
- **Fix (0.5.0, first step)**: the combat band and the native cover now take the real cutout inset as a lower bound, so deeper cutouts than the reference device are fully covered. The historical floors stay for now: the band still needs to cover the authored sky strip on cutout-less devices, and the native cover hides a "~90px renderer gutter" whose root cause is unconfirmed, so shrinking below today's sizes is gated on the gutter investigation (cover sizing is logged at combat entry; see TESTS.md).
- **Status**: fixed-pending-device (geometry); gutter root-cause investigation open
- **Fixed in**: 0.5.0

## BUG-005: "Repair game files" label does not match the dialog copy or the behavior

- **Where**: launcher, More options group.
- **Repro**: tap Repair game files. The confirmation still says "Redownload game files?" and the action deletes the downloaded installation outright.
- **Expected**: label, confirmation copy, and behavior tell the same story.
- **Actual**: our rename to "Repair game files" (upstream baseline: "REDOWNLOAD GAME FILES") kept upstream's dialog copy and delete-then-redownload behavior, so "repair" reads like a verify pass but performs a deletion.
- **Root cause**: rename without copy/behavior alignment; upstream message string survived in `LauncherController`.
- **Fix**: per-mode dialog copy now states exactly what happens (Steam: deletes the download, then re-downloads from Steam; Offline: removes the import, re-import needed; saves and login untouched in both).
- **Status**: fixed-pending-device
- **Fixed in**: 0.4.0

## BUG-006: Five patch files are dead code

- **Where**: `src/STS2Mobile/Patches/`.
- **Repro**: `EventLayoutPatches`, `MerchantLayoutPatches`, `MobileLayoutPatches`, `UiScalePatches`, `CombatBackgroundPatches` have `Apply()` entry points no live code path calls; only `StartupPatchOrchestrator` + `PortraitPatches` register patches.
- **Expected**: files in the tree take part in the build's behavior or do not exist.
- **Actual**: 1,000+ lines that compile, read as live, and mislead diagnosis (this inventory's own investigation initially chased them).
- **Root cause**: the portrait patch group replaced these wholesale (documented in `docs/portrait-design-audit.md`), but the replaced files were never deleted.
- **Fix**: files removed; the one behavior they still implied is tracked honestly as BUG-009.
- **Status**: fixed-pending-device (compile-level change; device smoke run still required)
- **Fixed in**: 0.4.0

## BUG-007: HUD reflow hides arbitrary upper-right labels and rescans the whole tree forever

- **Where**: in-game, all screens with the run HUD.
- **Repro**: any label whose global position transiently lands right of 62% width and above the safe top is permanently hidden by `HideBuildWatermark`; the reflow timer re-runs a full scene-tree recursion every 1.2 s for the entire run.
- **Expected**: watermark hiding targets the watermark; reflow work is event-driven or cheap.
- **Actual**: overbroad text matching plus a perpetual full-tree walk (battery/thermal cost, and a correctness hazard for legitimate UI).
- **Root cause**: `PortraitLayoutPatches.HideBuildWatermark` heuristic + self-rescheduling reflow timer.
- **Fix**: identity-only watermark rules (position rule removed), and the tree sweep now runs only on reflow signature transitions instead of every tick; the steady reflow itself stays but does bounded per-node work.
- **Status**: fixed-pending-device
- **Fixed in**: 0.5.0

## BUG-008: Merchant reflow timer keeps running after the shop closes

- **Where**: in-game, after visiting a shop.
- **Repro**: open and close a shop; the 0.5 s reflow timer continues for the node's lifetime, only skipping work via a meta flag.
- **Expected**: timers stop when their screen closes.
- **Actual**: idle timer churn for the rest of the run.
- **Root cause**: `MerchantOpenPatch.Reflow` re-schedules unconditionally; `Close` only sets a flag.
- **Fix**: the chain now stops on the closed flag and clears its start marker so the next Open restarts it.
- **Status**: fixed-pending-device
- **Fixed in**: 0.5.0

## BUG-009: In-game UI Scale setting has no effect in portrait

- **Where**: in-game settings.
- **Repro**: change UI Scale in the game's settings; the portrait canvas ignores it.
- **Expected**: either the setting works in portrait or it is hidden with an explanation.
- **Actual**: the desktop scale selector's adaptation lived in `UiScalePatches`, which was never registered (see BUG-006); independently, `PortraitViewportGuard` re-applies the portrait canvas every 0.2 s, overriding any scale the game applies.
- **Root cause**: portrait canvas ownership and the game's scale setting were never reconciled.
- **Status**: open (0.6.0; needs a design decision: support scale within the portrait clamp range, or hide the setting)
- **Fixed in**: -

## BUG-010: Dead helper in LauncherActivity

- **Where**: `android/src/com/game/sts2launcher/LauncherActivity.java`.
- **Repro**: `hasDownloadedGamePck()` has no callers.
- **Status**: fixed-pending-device (removed with BUG-006's cleanup)
- **Fixed in**: 0.4.0

## BUG-012: Combat hand clips below the bottom edge

- **Where**: in-game combat, PC rig round `tmp/pctest/shots-20260818-133912/06-first-room.png` (1180x2596 canvas, 4-card hand).
- **Repro**: enter turn-1 combat; the card fan's lower quarter renders past the canvas bottom.
- **Expected**: full cards visible above the bottom edge (plus gesture inset on device).
- **Actual**: card bottoms cut off.
- **Root cause**: three stacked problems. `PlaceHand` set a raw local Position (only correct when the holder's parent sits at the canvas origin; teleport-entered combats put the fan mid-screen), the holder can be created after every placement retry has passed (no enforcement), and the baseline ignored `SafeBottom()`. A fourth, rig-side: the desktop safe area reported a ~1300-unit phantom bottom inset against the oversized phone window, which poisoned the first version of this fix.
- **Fix**: global-space anchoring + a lifetime hand guard (SceneTreeTimer chain) + `min(0.925 * canvas, canvas - SafeBottom - clearance)` baseline; non-Android ignores the desktop safe area entirely and uses only the simulated insets.
- **Status**: fixed-pending-device; PC-visual PASS (`shots-20260818-144548/10-combat.png`, five-card fan fully visible)
- **Fixed in**: 0.5.0

## BUG-013: Map nodes render under the HUD stack

- **Where**: in-game map screen, PC rig round `tmp/pctest/shots-20260818-133123/05-run-settled.png`.
- **Repro**: open the act map; upper-left map points sit behind the HP/gold text (HUD ZIndex 400 draws over them).
- **Expected**: map content clears the HUD, or passes under it legibly.
- **Actual**: top map rows overlapped the tall HUD column.
- **Root cause**: BUG-003 family; the stacked non-combat HUD occupied ~520 canvas px with no shared band.
- **Fix**: solved by the compact-bar redesign: outside combat the HUD is a slim two-row bar over one ink backdrop band, map points start below the band and scroll under it legibly.
- **Status**: fixed-pending-device; PC-visual PASS (`shots-20260818-144548/05-run-settled.png`)
- **Fixed in**: 0.5.0

## BUG-014: Top-bar icons fight the game's own layout writers

- **Where**: slim bar, top-right map/deck/pause icons and the room/floor/boss cluster.
- **Repro**: on some frames the icons render at the game's landscape-strip positions (y around 40 canvas) or fly toward the origin when a capstone screen opens, instead of holding the slim-bar row; rig clicks occasionally land on the drifted positions.
- **Expected**: icons hold the positions the portrait layout assigns.
- **Actual**: a tug of war between our 0.5s reflow and the game's writers.
- **Root cause (diagnosed so far)**: two writers. `LeftAlignedStuff`/`RightAlignedStuff` are containers whose sort pass re-lays children every frame, and the buttons' own anim-state machinery (`NTopBar.ToggleAnimState` fires on capstone child enter/exit; `NTopBarDeckButton`/`NTopBarPauseButton`) repositions them on screen-open transitions. Reparenting the controls out of the containers made it worse (buttons then landed at the origin) and was reverted.
- **Fix**: stopped fighting the writers and adopted them. Outside combat the slim bar places the two HBox CONTAINERS (`PlaceRow`) and lets their native sort arrange the children — hidden children are skipped by the sort, which is how the room/floor/boss cluster leaves row 1. Two grandchildren the sort cannot fix (the potion capsule inside `PotionMarginifier`, the room icon inside its resizer) are handed back to their slots explicitly on every compact reflow, because the combat branch pins them directly and margin containers do not re-sort them on the way back. Capstone screens are detected via `PortraitCapstone.IsOpen` (pause/settings live under the submenu stack's `Submenus` node, deck view is a direct container child; the container's other children are permanent furniture) and drop the whole HUD cluster.
- **Status**: fixed-pending-device; PC-visual PASS (`shots-20260818-163729/` nav clicks stable across rounds: Deck 930,203 / Pause 1062,203; potion slot restore verified in the follow-up round)
- **Fixed in**: 0.6.0-dev

## BUG-011: Non-combat screens without portrait handling

- **Where**: in-game. Card reward layout, deck/master-deck viewer, pause menu body, relic/keyword tooltips, run end / victory / death / score, boss relic select, potion use/discard prompts, card/grid/hand select overlays, campfire action menu, merchant purchase confirm, compendium/stats/unlocks/credits, mod menu, LAN screens, settings body.
- **Repro**: visit any of the listed screens in portrait; layout is the game's landscape-authored one.
- **Expected**: every reachable screen is portrait-usable.
- **Actual**: no patch exists for these; severity varies and needs on-device screenshots per screen.
- **Root cause**: v0.3.0 scope covered the core run loop only.
- **Status**: open (0.6.0 wave 1: card reward, pause, deck viewer, run end/death; 0.7.0 wave 2: overlays and tooltips)
- **Fixed in**: -

## BUG-015: Slim-bar re-show turned the relic strip into a fullscreen click shield

- **Where**: every non-combat screen since the capstone-hide change; found via the PC rig's merchant probe (`STS2_PCTEST_SCENARIO=merchant`).
- **Repro**: outside combat, click room content between roughly y 243 and y 2280 canvas (the merchant mat, campfire options, anything mid-screen). Hover and press never arrive; the merchant button stays enabled but unreachable.
- **Expected**: only the relic icons capture input; the strip's container stays input-transparent as authored.
- **Actual**: dead input across most of the canvas. Top-bar buttons (above y 243) and the bottom map point (below y 2280) kept working, which hid the regression for several rounds.
- **Root cause**: the capstone hide/show helper (`SetVisible`) stamped `MouseFilter=Stop` onto every control it re-showed. `RelicInventory`'s control rect is 1084x2036 (relic rows grow downward), so re-showing it every reflow tick installed a near-fullscreen invisible click shield. Diagnosis chain that got here: merchant button `IsEnabled=True` and `ActiveScreenContext` current = `NMerchantRoom`, yet synthesized hover+press never fired `NClickableControl.HandleMousePress` (which requires focus from a real hover hit).
- **Fix**: `SetVisible` parks the authored filter in node meta while hidden (`Ignore`) and restores it exactly on show; controls that were never hidden are never touched.
- **Status**: fixed-pending-device; PC-visual PASS (merchant probe opens the inventory, full round clean)
- **Fixed in**: 0.6.0-dev

## BUG-016: Every Harmony patch failed on device with EINVAL

- **Where**: game leg on device, startup patch orchestration.
- **Repro**: launch the game on Android. `core/Platform compatibility` reports `criticalFailed=True` and the standalone launcher fallback appears instead of the game.
- **Expected**: patches apply, the game boots.
- **Actual**: every patch failed. First with `NotImplementedException` from `PlatformTriple.CreateCurrentSystem()`, then `EntryPointNotFoundException: __errno_location`, then `Win32Exception (22): Invalid argument` right after MonoMod logged `Detouring arm64 from ... to ...`.
- **Root cause**: three separate Android gaps in MonoMod, which the game bundles inside `0Harmony.dll`.
  1. MonoMod detects OS `Android` and implements only Windows, Linux and macOS, so the detour platform could not be built at all.
  2. MonoMod imports the glibc-only `__errno_location`; bionic exports `__errno()`.
  3. MonoMod reads the page size with `sysconf(_SC_PAGESIZE)` using the glibc constant `30`. Bionic numbers that limit `39`, so it received a nonsense page size, rounded the detour address to an unaligned boundary and `mprotect` rejected it with EINVAL. This was the last blocker and the reason the trace showed a detour being created and then instantly failing.
- **Fix**: `src/STS2Mobile/ModEntry.cs` forces the detour platform to Linux, resolves libc imports through `native/monomodshim/monomodshim.c`, and corrects the page size on the live `LinuxSystem` and its allocator before the first patch (`FixDetourPageSize`).
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-017: Mono aborts the process while patching nullable signatures

- **Where**: game leg on device, `optional/LAN multiplayer` patch step.
- **Repro**: with patches applying, boot the game. The process dies with SIGABRT; logcat shows `mono_class_from_mono_type_internal: implement me 0x00` and `Assertion: should not be reached at .../mono/metadata/class.c:2324`, followed by a FORTIFY mutex message from the dying process.
- **Expected**: an unpatchable method is skipped, not fatal.
- **Actual**: the whole game process aborted, and because the abort is native no try/catch could contain it.
- **Root cause**: Mono on Android cannot build Harmony's copy of a method whose signature carries a nullable game struct (`NetHostGameService.StartENetHost` returns `NetErrorInfo?`), and it also aborts while reading that type's method table.
- **Fix**: `PatchHelper.IsUnpatchableOnDevice` skips nullable signatures on Android for every patch, and `LanMultiplayerPatcher` leaves the host beacon patches alone there. Joining a LAN game still works; advertising one from the phone does not.
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-018: No character art, no logo and no VFX on device

- **Where**: game leg on device, every screen.
- **Repro**: reach the main menu on device. The logo is missing, character select shows an empty frame instead of the splash art, and the main menu text stays visible over the submenu.
- **Expected**: the same art the PC rig renders.
- **Actual**: everything Spine-backed draws nothing. Logcat: `Cannot get class 'SpineSkeletonDataResource'`, then parse errors for every `.skel` and `.tres` that references it.
- **Root cause**: the game draws characters, the logo and VFX through the Spine GDExtension. `res://addons/spine/spine_godot_extension.gdextension` declares `android.release.arm64`, but the Steam desktop build ships only the Windows, Linux and macOS binaries, so the Android library does not exist and the extension never registers its classes. The same applies to the FMOD extension, which is why the game has no audio on device.
- **Fix**: `scripts/build-spine-android.ps1` builds `libspine_godot.android.template_release.arm64.so` from Esoteric Software's sources (spine-runtimes 4.2, matching the game's 4.2.43 skeleton data) and stages it into the APK's `arm64-v8a` libraries, where Godot resolves GDExtension libraries by file name. The binary is not committed: the Spine Runtimes License requires each user to hold their own Spine license.
- **Status**: fixed-pending-device
- **Fixed in**: 0.4.0

## BUG-019: Game has no audio on device

- **Where**: game leg on device.
- **Repro**: play on device with the volume up.
- **Expected**: music and sound effects.
- **Actual**: silence.
- **Root cause**: same shape as BUG-018. `res://addons/fmod/fmod.gdextension` declares `android.release.arm64`, but the desktop build carries no Android FMOD libraries, so the extension does not load. Unlike Spine, the FMOD runtime cannot be rebuilt from public sources: it needs the FMOD Engine SDK for Android under Firelight's licence.
- **Status**: open

## BUG-020: Combat never started, the arena stayed black

- **Where**: game leg on device, entering any combat room.
- **Repro**: start a run and walk into the first fight. The HUD appears but the arena is black: no background, no player, no enemies, no cards, energy stuck at 0/3.
- **Expected**: the fight loads.
- **Actual**: `NCombatRoom.SetUpBackground` threw NullReferenceException because the room's own `BgContainer` was never assigned.
- **Root cause**: a Harmony-patched method runs as a generated copy, and on device that copy cannot reach the protected members the original called. `NProceedButton._Ready` was patched for portrait sizing, so its call to `NClickableControl.ConnectSignals` failed with `MethodAccessException`, `_Ready` died halfway, `NCombatRoom._Ready` then threw on the half-built button and stopped before assigning `BgContainer`. Pinning MonoMod's DMD generator to `dynamicmethod` (which does skip visibility) is not an option: on this runtime it fails to produce working patches at all and the process dies in a detour finalizer.
- **Fix**: the proceed button is sized from its `ShowPos` getter instead, and `_Ready` is left unpatched.
- **Lesson**: never patch a `_Ready` (or any method) that calls protected or internal members of its own class hierarchy. Prefer a public method, a property getter, or a layout pass driven from the parent screen.
- **Status**: verified
- **Fixed in**: 0.4.0
