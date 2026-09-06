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

## BUG-021: Buttons ignored the first tap; holds and hover stuck after lifting

- **Where**: every clickable in the game on device.
- **Repro**: fresh boot, tap any button once. Nothing happens; the second tap on the same spot works. After any tap, the control keeps its hovered or pressed visuals; long-press bars could stick mid-fill.
- **Expected**: Android touch semantics: first tap presses, lifting the finger clears all hover state.
- **Root cause**: the game's clickable controls accept a press only while `IsFocused` (their hover flag) is true, and hover is updated exclusively by mouse motion. Godot's touch emulation delivers a button press with no preceding motion, so the first tap only relocated the invisible emulated cursor; after lifting, the cursor stayed parked on the control and nothing ever sent the exit event.
- **Fix**: `PortraitTouchInput` bridges the pointer model at the window: on touch press it pushes a mouse motion to the touch point first (hover exists before the press arrives), on release it parks the pointer off-canvas one frame later (hover clears once the release lands). Drags and long presses ride the engine's own emulation between those two moments.
- **Verified on device**: first tap opens menu rows, card drag plays cards, End Turn long-press fills and fires, nothing sticks after lifting.
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-022: Pause menu unresponsive with every button visible

- **Where**: pause menu in a run, on device only.
- **Repro**: pause during combat. Six small rows appear (both Disconnect and Save and Quit); none respond to taps.
- **Root cause**: an old portrait patch targeted `NPauseMenu._Ready` to raise button sizes. On device a patched method runs as a generated copy that cannot call the protected `ConnectSignals`, so `_Ready` died on its first line: no signal connections (dead buttons), no visibility rules (both quit variants shown), and `Initialize` then threw on null fields, which also kept the new layout postfix from running. Same mechanism as BUG-020.
- **Fix**: the `_Ready` patch is deleted; the portrait pause layout runs from a postfix on the public `Initialize` and re-runs the game's own label autosizing after growing the rows.
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-023: Hand fan pushed outer cards off both screen edges

- **Where**: combat hand with 5+ cards.
- **Root cause**: two authored-for-landscape assumptions. The fan's holder origin sits left of the portrait canvas centre, and the game's per-hand-size card offsets (up to x610) are holder-local, multiplied by the holder's live scale, so wide hands overshot both edges unevenly.
- **Fix**: `PlaceHand` centres the holder origin on the canvas and records the holder's effective global scale; the `HandPosHelper.GetPosition` postfix scales card X by exactly the ratio this hand size needs, converted through that live scale. The hand guard now corrects X drift as well, because the combat intro tween slides the holder after `_Ready`.
- **Verified**: rig at 7 cards (both edges inside, centred), device at 4-5 cards; a device check with 8+ cards is still pending.
- **Status**: fixed-pending-device
- **Fixed in**: 0.4.0

## BUG-024: Settings screen unusable on device

- **Where**: in-run settings, device.
- **Actual**: tabs flush against the cutout band, rows at landscape pitch above a dead lower half, the back tab half off the canvas, and the combat piles, energy and End Turn drawn over the open screen.
- **Fix**: content block scaled through the plain-Control Clipper (row internals stay intact; growing per-row minimums split rows from their own backgrounds), tabs dropped below the safe inset, back tab placed inside the canvas, and fullscreen capstones now hide every absolute-Z combat control with the hand.
- **Status**: verified

## BUG-025: Loot banner buried in darkness, then flickering

- **Where**: rewards screen, device.
- **Root cause**: the compact bar's gradient scrim (Sts2PortraitHudBackdrop, ~800 units deep, absolute z 390) covered the banner while the loot rows started below its fade. The first fix had the hand guard hide the scrim while the top bar reflow re-showed it, which turned the burial into a flicker.
- **Fix**: the scrim has one owner: the reflow itself skips it while a rewards screen is visible. The rewards pass is also re-driven from the hand guard, because the screen's own assert loop died silently on device more than once.
- **Status**: verified (three consecutive captures stable)

## BUG-026: Two Skips at once on the card selection overlay

- **Root cause**: the rewards proceed arrow is Z-pinned at 460 and the selection overlays live in the same canvas layer, so the arrow punched through them. Type-name detection failed twice; the game's own overlay stack (NOverlayStack.Peek) is the source of truth.
- **Fix**: the rewards pass unpins and zeroes the arrow whenever its screen is not the top overlay, and the map's assert loop clears it as well for the window after combat dies.
- **Status**: verified (single Skip on the overlay, none leaking onto the map)

## BUG-027: Skip arrow shrank under the finger and dropped the press

- **Root cause**: the portrait pass scaled the proceed control while the game's press animation writes that same Scale, and the assert loop repositioned it mid-press, which the drag threshold read as a cancel.
- **Fix**: no scale on the proceed arrow, and repositioning only on real drift (>3 units).
- **Status**: verified (Skip and Proceed both fire first tap)

## BUG-028: .NET file gates dead on device

- **Root cause**: SELinux hides /data/local/tmp from the managed side on this device even though the Java boot path reads it, so every managed file gate there (bisect switches, dump trigger) silently never fired.
- **Fix**: managed triggers moved into the app's own user data directory (OS.GetUserDataDir()). The Java-side override directory stays where it was, since Java demonstrably reads it.
- **Lesson**: any future managed-side device switch goes under user://, never /data/local/tmp.
- **Status**: verified (region dump consumed and logged from user://)

## BUG-029: Phone runs hot; textures decompressed to RGBA8 on device

- **Where**: whole game leg on device.
- **Repro**: play for a few minutes; the device heats up fast. Trace shows ~500MB VRAM; logcat spams "Image format DXT5/DXT1 not supported by hardware, converting to RGBA8".
- **Root cause**: three stacked costs. (1) The game ships uncapped and the panel runs 90-120Hz. (2) Rendering happened at the native 1440x3168 through canvas_items scaling. (3) The PCK carries only desktop texture formats (683 bptc, 539 s3tc, zero ETC2/ASTC), so every compressed texture is decompressed to RGBA8 at load: 4x the VRAM and the memory bandwidth bill that turns into heat. A fourth, self-inflicted cost: the portrait layout guards walked all ~3700 nodes through the C# interop several times a second (130-145ms frame spikes).
- **Fix**: 60fps budget re-asserted at runtime (user://sts2_fps_cap overrides); viewport scaling renders at the 1180x2596 canvas (user://sts2_render_mode "native" reverts); scene queries answered by NOverlayStack.Peek plus a WeakReference node cache (idle combat fps 47 -> 56, spikes halved); and tools/pck/transcode_textures.py rewrites every bptc/s3tc ctex payload to ASTC 4x4 (natively supported by the device GPU) and repacks the PCK.
- **Note**: measurements taken while the phone was fast-charging, which adds its own heat; validate unplugged.
- **Status**: partially fixed; texture work parked by decision
- **Update 2026-08-20**: the ASTC pack passed PSNR gates (56-59dB) and removed
  every load-time conversion, but on device one class of sprites (spine
  atlases, several UI icons) rendered invisible with no loader error. Rolled
  back to the original PCK per the standing rule that the original experience
  loses nothing. Kept: the 60fps budget (the game itself settles at 60), the
  scene-query caches (pure CPU win, idle combat 47->56fps), and the viewport
  render mode as an opt-IN experiment (user://sts2_render_mode "viewport");
  the default is the panel's native resolution. The transcoder and findings
  live in tools/pck/transcode_textures.py for a future pass that first solves
  the silent-invisible class on a single texture.

## BUG-030: Resuming a run into the loot screen kept the native layout

- **Where**: any overlay-stack screen; hit hardest as the rewards screen after Continue restored a run that was saved mid-loot.
- **Repro**: quit at a loot screen, relaunch, Continue. The rewards panel renders as the small landscape plate, the Skip arrow sits inside it, and the trace shows no portrait activity after "Hand guard installed".
- **Root cause**: overlay-stack screens are detached from the tree (not freed) while covered and during run restore. `AssertLoop` and the hand guard both re-armed through the node's own tree handle behind an in-tree gate, so a single tick landing in a detach window ended the chain silently and permanently. The `EnsureLoop` meta stays set, so nothing ever re-arms it. The guard's rewards re-drive was also nested inside the card-holder null check, so it could not rescue the pass on a boot that restores straight into loot.
- **Fix**: both loops re-arm through the main loop's `SceneTree` and idle while the node is out of tree; the chain now only ends when the node is freed. The rewards re-drive moved outside the holder check, and the rewards pass logs a one-shot breadcrumb per branch so a silent success can never hide which path ran.
- **Status**: verified on device (resume into loot renders portrait layout, trace shows "rewards layout active")
- **Fixed in**: 0.4.0

## BUG-031: Patched copies are denied protected base-class FIELDS too

- **Where**: device only; `NCombatCardPile.AnimIn` source hook.
- **Repro**: register a Harmony patch on `AnimIn`. Orchestration logs "Patch skipped: Field `NCombatCardPile:_hidePosition` is inaccessible from method DMD<>...".
- **Root cause**: sharper form of BUG-020/BUG-022. The generated copy contains the ORIGINAL method body; if that body touches a protected field declared on a BASE class, Mono denies the access even when the prefix itself is reflection-only. Same-class private fields remain fine, which is why the other source hooks survive.
- **Fix**: the hook is deleted; `PortraitCombat.PlacePile` rewrites the pile's `_showPosition` through reflection from normal code before the entry tween runs, which gives the same zero-flash arrival without patching the restricted method.
- **Rule**: before patching a method on device, check its body for protected/internal member access of ANY kind, methods and fields alike, across the whole class hierarchy.
- **Status**: verified (fresh combat entry: both piles sat at their portrait corners from the first visible frame)
- **Fixed in**: 0.4.0

## BUG-032: Silent no-boot deploys measured the OLD build

- **Where**: device iteration tooling (quick-push/boot-game).
- **Repro**: run quick-push while the game leg is mid-session and the launch intent fails or lands on a foregrounded activity. The script reports success, the previous screen stays up, and every screenshot or measurement after it reads the previous build. A 1.8x potion scale change measured as exactly 1.5x this way.
- **Root cause**: boot-game force-stops and relaunches, but never verified the relaunch produced a fresh patch orchestration; the deploy pipeline treats "adb start returned" as "the new DLL is running".
- **Fix**: boot-game counts "Applied N/M layout patch classes" lines in the bootstrap trace before and after the launch and prints a loud WARNING when no new orchestration appeared within the wait window.
- **Rule**: a device measurement only counts if the trace shows a patch orchestration NEWER than the deploy.
- **Status**: verified (guard caught two real silent no-boots, then flagged a false alarm that led to the timestamp comparison; stable since)
- **Fixed in**: 0.4.0

## BUG-033: Save and Quit froze the menu into a landscape strip with dead input

- **Where**: device only; return-to-menu after Save and Quit (any run screen).
- **Repro**: Continue into a run, pause, Save and Quit. The menu rendered as a ~2580x1080 letterboxed strip mid-screen, every tap dead, and backgrounding or forcing rotation did not recover it. Fresh boots were fine.
- **Root cause**: on the return path the NATIVE window content scale drifts to the game's landscape fit while every C# window property still reads back the portrait values, so all same-value rewrites (ApplyDisplaySettings postfix, OnWindowChange prefixes, the aspect heartbeat) were engine-side no-ops: the setter skips the native update when the value matches its cache. Godot rendered the correct portrait frame the whole time; a viewport dump (`user://sts2_vpdump`) captured a full portrait frame in the same second a screencap showed the strip.
- **Fix**: `PortraitDisplay.ForceRefresh()` writes a deliberately different ContentScaleSize first and then the target, forcing the native path; MainMenu `_Ready` calls it on every menu (re)build. `NMainMenu.OnWindowChange` (which writes KeepWidth 2580x1080 / KeepHeight 1680x1260 itself) is blocked, and on Android the game's window-fit bodies never run even if `Apply()` misses a frame.
- **Trap recorded in code**: an only-on-change write optimization in `Apply()` letterboxed even fresh boots; the blind per-call rewrite is load-bearing because the C# cache does not reflect native state. Do not "optimize" it again.
- **Diagnostics kept**: drop `user://sts2_vpdump` to save what Godot renders (`sts2_vpdump.png`) and compare against a screencap; the aspect guard heartbeat logs window scale state.
- **Status**: verified (S&Q returns to a full-screen portrait menu, taps work, loot reachable via Continue)
- **Fixed in**: 0.4.0

## BUG-034: Open items from the design tour (2026-08-26)

Rule (user): cosmetic overlap bugs are FIRST-CLASS, never "minor";
visual collisions and unplayable states are the biggest bug class.

- **Proceed arrow overlaps the resting hero**: FIXED and verified. The
  ShowPos postfix now floors the arrow's Y to the bottom content band on
  every screen, under any room's character art.
- **"Ping" coop button leaks into singleplayer**: FIXED and verified. It
  is `NPingButton` under CombatUi; the combat pass hides it (multiplayer
  does not exist on this build). Loot screen shows zero ping pixels now.
- **View Upgrades tickbox small on select grids**: FIXED, see BUG-038.
- **Status**: open (tracked; none block 0.4)

## BUG-035: Map top third darkened; no touch zoom on the map

- **Where**: NMapScreen on device.
- **Root cause (darkness)**: the shared scrim's default depth (ContentTop + 470)
  dated from before the event prose got its own measured depth; every
  non-combat screen, the map included, carried a needless dark band.
  Default is now the bar band plus a 140-unit fade (event keeps its
  prose-measured depth). Map band brightness 85.9 -> 134.3 (0-255 mean).
- **Zoom**: the game has no touch zoom. Two fingers down over a visible map
  open a pinch session (PortraitTouchInput tracks touch index 0/1); TheMap,
  the container holding MapBg/Paths/Points/Drawings/MapMarker, scales about
  the fingers' midpoint (1.0-2.2). Scaling only Points/Paths left the ink
  strokes and marker behind. Two-finger drift pans horizontally through
  PivotOffset (dp*(1-s)) because the game rewrites TheMap.Position every
  frame for its vertical scroll. A single finger stays the game's gesture:
  the quill draws with it.
- **Probes**: user://sts2_zoom (factor) and user://sts2_pan (dx) drive the
  same code from adb, since adb cannot fake multi-touch. Real pinch needs a
  hand on the device.
- **Status**: zoom/pan verified via probes (1.8x + pan showed the left column
  with ink and marker aligned; 1.0 restores the original layout); the
  finger gesture itself awaits the user's test.
- **Fixed in**: 0.4.0

## BUG-036: Merchant shelves and treasure relic were fingernail-sized

- **Merchant**: the relic/potion shelves sat in a 190-unit band at 0.8x
  (icons ~15dp), first relic under the back tab, last card's price on the
  sunflower. Side by side at thumb size they collided and the last potion
  fell off the rug, so the shelves stack (relics above, potions below,
  centered on measured widths) in a 520-unit band the card grid yields via
  its cell clamp. Verified on device.
- **Treasure**: the relic collection is a landscape-sized box; fitting it
  barely scaled it and the single relic stayed ~22dp. The
  SingleplayerRelicHolder scales about its center to 2.6x (bright box 90 ->
  330 px). Verified on device, relic picked up cleanly.
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-037: Map opened over a finished combat kept the hand and End Turn

- **Repro**: skip the loot after a fight; the map opens while the combat
  scene is still alive underneath and the fan plus End Turn drew over it.
- **Fix**: a visible NMapScreen counts as eclipsing the combat HUD (same
  rule as the rewards screen and capstones); the hand guard hides the fan,
  piles, energy and End Turn and restores them when the map closes.
- **Status**: verified (parchment where End Turn sat, no card glow)
- **Fixed in**: 0.4.0

## BUG-038: Grid select overlays started under the top bar

- **Where**: NCardGridSelectionScreen family (upgrade at the smith, transform,
  enchant, deck picks).
- **Root cause**: NCardGrid's authored YOffset assumes a landscape header;
  the first card row sat under the compact bar. The View Upgrades tickbox
  was also ~12dp because these screens do not inherit NCardsViewScreen.
- **Fix**: AfterOverlayOpened (empty body, safe) postfix: once per screen the
  grid's YOffset is pushed down to the content top and the game's own
  ReflowColumns re-lays the rows (the grid scrolls, nothing is lost); the
  tickbox grows 2.2x from its bottom-left corner.
- **Status**: verified at the smith (rows clear of the bar, box thumb-sized)
- **Fixed in**: 0.4.0

## BUG-039: Deck button vanished from the compact bar after a smith upgrade (transient)

- **Repro (once)**: second rest-site smith of the run, upgrade confirmed; on the
  following map screens the bar showed only the map scroll and the gear.
  A fresh boot restored it. Probe (reflow signature carries the deck
  state) read Visible/InTree/alpha 1/scale 1 while it was gone, so the
  node was fine and a child (icon) was hidden or off-slot, most likely
  the game's OnScreenClosed -> AnimUnhover path on a touch device that
  never delivers a hover to undo it.
- **Root cause (2026-09-06, reproduced on the act 2 map after a fight)**:
  the tree probe showed the Deck button back inside its DeckContainer
  margin slot but with the combat-branch transform still on it (local
  position 574,167, scale 1.5), so it drew far off the bar. Combat places
  the button directly; the compact branch handed only the room icon back
  to its slot, and a MarginContainer does not re-sort on the way back
  (same trap as BUG-014). Not a hover path after all.
- **Fix**: the compact branch hands every directly placed child back to
  its slot (deck, map, pause, hp, gold) and queues a sort on both rows;
  restoring the deck alone left the scroll and the gear at their combat
  coordinates on the next loot. Verified on device: all three right icons
  on the loot and on the map after Skip.
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-040: Map legend and drawing tools drew over settings and pause

- **Where**: open settings or pause while the map screen is up.
- **Root cause**: the map's own furniture (MapLegend, DrawingTools plate)
  lives above the capstone layer, so it stayed bright over the settings
  rows (the tools plate sat on "Reset to Default").
- **Fix**: the map pass hides both while `PortraitCapstone.IsOpen` and hands
  them back on close (meta marks what the pass hid).
- **Status**: verified (pause over the act 2 map: legend and tools zones dark, restored on close)
- **Fixed in**: 0.4.0

## BUG-041: Settings back tab sat on the Credits row

- **Root cause**: the spread settings list fills the screen on the long
  General tab and the shared back tab parked bottom-left landed on the last
  rows' labels. A Position write on that tab is undone within the frame:
  NBackButton tweens global_position to its private _showPos on every show.
- **Fix**: the back tab moves to the top band (free while settings/pause are
  open, the top bar hides) through a _showPos rewrite (PlaceBackTab, same
  source-hook idea as the piles), for both the pause menu and the settings
  screen; the tab strip and list start 150 units lower, content scale 1.0,
  and the row separation derives per tab (6-32) so the list ends with a
  bottom margin. Verified on device (General tab: no overlaps, Reset row
  at 86% height).
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-042: main menu profile button never opened the profile screen

- **Symptom**: tapping "Profile 1 / Click to edit" on the main menu did
  nothing; the patch-notes button beside it (same corner placement) worked.
- **Cause**: NOpenProfileScreenButton writes its own Scale on focus (1.02)
  and tweens it back to 1 on unfocus. The corner placement wrote Scale 1.9
  once; the hover that precedes a touch press shrank the button to 1.02 at
  pivot zero, so the press landed outside it, and the layout loop grew it
  back before the next frame. Visually nothing moved.
- **Fix**: the button's own Scale is left alone. Its children are scaled and
  spread about the origin once and the button's Size grows with them, so
  the game's 1.02 hover rides on top harmlessly. A first attempt with
  postfixes on OnFocus/OnUnfocus was withdrawn: those bodies call protected
  base members and the patched copies threw MethodAccessException on every
  focus change (BUG-020 rule). Verified on device: same rendered size, the
  profile screen opens on the first tap, no exceptions in the log.
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-043: profile card clipped the "Updated" timestamp

- **Symptom**: the save-profile card showed "Updated / September 2," with the
  year and time cut off.
- **Cause**: the card's Info block is a clipped 340x360 rich label with
  auto-size off; "September 2, 2026 12:48 PM" wraps to a sixth and seventh
  line that the rect cuts (the label's fitter measures with the theme face
  and reports one date line, so font caps changed nothing).
- **Fix**: the rect takes the card's full width and the free plate above and
  below the block (y 100, 460 tall): the date wraps once and six lines fit.
  The profile back tab also moves to the top band like every other submenu.
  Verified on device.
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-044: patch notes back tab sat on the article text

- **Symptom**: the patch-notes back tab was bottom-left over the paragraph
  text, while pause and settings had theirs at the top.
- **Cause**: authored bottom-left tab; the article's scroll box starts at the
  top of the screen. Moving the tab up alone put it over the date line, and
  neither OffsetTop on the scroll box nor a margin override on the inner
  MarginContainer moved the text: NScrollableContainer positions its Content
  from its private _paddingTop.
- **Fix**: a prefix on NPatchNotesScreen.Open rewrites the tab's _showPos
  before Open enables the tab (OnEnable captures the target into a tween);
  a postfix raises the container's padding through its public UpdatePadding
  by 280 and places the content there. Verified on device: tab top-left,
  date line below it.
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-045: card pick grid swapped cards under the finger

- **Symptom**: tapping a card on the reward/boss card pick shuffled the three
  cards to new positions instead of selecting one; a second tap on the moved
  card selected it.
- **Cause**: the game moves the hovered card holder to the front of CardRow's
  child list for z-order. The portrait 2+1 grid assigned slots by child order
  on every pass, so the hover that precedes a touch press re-dealt the grid
  and the press released over a different card.
- **Fix**: slots are pinned to each holder on first sight (meta) and the grid
  sorts by that slot. Verified on device: one tap selects (deck 16 to 17).
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-046: map parked with the bottom 45 percent of the screen empty

- **Symptom**: on entering an act (and whenever the current row is low), the
  map's bottom row sat mid-screen with bare parchment below it down to the
  drawing tools; scrolling could not bring it lower.
- **Cause**: NMapScreen nudges the container's Y back into the landscape
  constants [-600, 1800] every frame and parks the current row at -600 +
  row * distY. On a 1080-tall view that puts the bottom row near the bottom
  edge; on the 2596-tall portrait canvas the same -600 leaves the band.
- **Fix**: a transpiler on UpdateScrollPosition replaces the two -600
  constants with -600 + 780 (portrait allowance). Parks below the new bound
  glide there through the game's own lerp, so the act start still animates.
  Verified on device: the bottom row sits just above the legend, rows fill
  the screen up to the HUD.
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-047: ancient event option text cut mid-sentence

- **Symptom**: on act ancients (Tezcatara etc.) each relic option showed one
  description line cut at the plate's right edge ("Exhaust the top", "Every
  3 combats, your").
- **Cause**: the option's Text is a fixed 830x74 rich label with wrapping
  off, authored for a wide landscape row; the 1000-wide portrait row has no
  room for the sentence on one line.
- **Fix**: word wrap on, label 150 tall, option plate min height 180 (width
  kept), block bottom margin 40, only for rows that carry a description: a
  title-only row ("Proceed") wrapped into the tall rect came out at half
  size from the label's own fitter, so it keeps its authored layout.
  Verified on device: three full sentences, no plate overlap, rows above
  the safe bottom, Proceed at full size.
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-048: ancient events rendered at 60 percent brightness

- **Symptom**: every act ancient (Neow, Tezcatara) showed a dark scene with
  grey dialogue and dim option text; the speaker art was barely visible.
- **Cause**: the top-bar backdrop's event branch measures the event prose
  block by finding a "Title" node in the event room. Ancient rooms are
  event rooms too but have no prose block; the "Title" found there sat in
  the whole layout, so the measured depth covered the full screen (2656)
  and the scrim dimmed everything under the bar.
- **Fix**: the prose branch skips rooms with a visible NAncientEventLayout;
  they keep the bar-band depth. Verified on device: white text (255,246,226)
  on both the bubble and the options, the scene art lit.
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-049: ancient event scene filled the top third, black below

- **Symptom**: on act ancients the painted scene (a landscape Spine
  composition drawn at 1.12 in a full-rect container) covered only the top
  third of the phone; the middle of the screen down to the options was black
  and the speaker's face sat under the top bar.
- **Fix**: the portrait pass scales AncientBgContainer 2.2x about a
  top-center anchor (590,120) and slides it 280 left, remembering the
  authored transform in meta so repeat passes do not compound; the dialogue
  block hangs 430 under the bar so the face shows between the bar and the
  bubble. Verified on device with Tezcatara (act 2): face centered above the
  bubble, scene to the options, text white. Open item: check Neow (act 1)
  and the act 3 ancient with the same anchor; the shift may need a per
  ancient value.
- **Status**: verified (Tezcatara)
- **Fixed in**: 0.4.0

## BUG-050: combat top bar stayed expanded on the loot screen

- **Symptom**: right after a fight the Loot overlay showed the expanded
  combat bar (HP and gold stacked, potions in the combat station, relic
  rows) instead of the compact bar with the potion capsule at its station.
- **Cause**: the bar's combat branch only yielded to capstone screens; the
  finished combat scene stays alive under the loot overlay, so
  CombatHudActive alone still said combat.
- **Fix**: the bar also yields to the shared eclipse rule (rewards overlay,
  map over a finished fight). Verified on device on the loot after the act
  2 elite: compact bar, capsule at the bar.
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-051: loot and card pick sat 100 units lower after a fight than after a resume

- **Symptom**: the Loot panel and the card pick grid hung from the expanded
  combat bar's bottom when the fight scene was still alive underneath,
  and from the compact bar otherwise; the same screens landed at two
  heights depending on how they were reached.
- **Fix**: both overlays always start from the compact content top, since
  the bar is compact under them (BUG-050). Verified on device: loot after
  the fight at the resume position.
- **Status**: verified
- **Fixed in**: 0.4.0

## BUG-052: Crystal Sphere divination overlay laid out for landscape

- **Where**: the "Crystal Sphere" event's Uncover Future / Payment Plan
  options open NCrystalSphereScreen (an overlay with a sphere of tiles,
  Big/Small Divination buttons, instructions, a divinations-left line and
  Proceed).
- **Symptom**: sphere on the left, the button column and instructions cut
  at the right edge, "divinations remain" cut at the bottom-left.
- **Fix**: PortraitCrystalSphere (AssertLoop from AfterOverlayOpened): the
  Bg art slides so the sphere centers right under the bar; RightUi (a
  VBox whose children fill its width) takes the panel width at 1.0 with
  its hand-placed inner instruction VBox widened to match, placed under
  the sphere; the footer line sits bottom-left, Proceed bottom-right at
  1.3x, the dialogue bubble in the sphere's top-right corner. Verified on
  device: sphere, both buttons, full instructions, tile reveals, footer
  count, session end into the loot.
- **Status**: verified
- **Fixed in**: 0.4.0
