# Portrait design and integration audit

## Reference reading

The supplied current screen is a narrow landscape-layout canvas expanded vertically. The supplied target is a portrait-native composition with a strong top-to-bottom reading order.

The target's defining structure is:

1. safe-area frame and compact run HUD at the top;
2. enemy in the upper combat half;
3. player in the lower combat half;
4. selected card centered between combatants;
5. energy and End Turn on one reachable control line;
6. five large, readable cards occupying the bottom quarter;
7. draw/discard piles pinned to the lower corners.

The target is treated as a layout and hierarchy reference. Its card values, character art, enemy art, and exact icons are not treated as shippable game assets or literal gameplay data.

## Inconsistencies found in the portrait repository

### Display and safe area

- Portrait was applied after startup and could fight the base launcher's landscape window callbacks.
- Canvas width mixed a 1080 nominal width, a 1066 floor, a 1280 ceiling, and a 1.35 magnification without one documented target density.
- Activity orientation remained the responsibility of APK rewriting rather than the source Android manifest.
- The top bar measured only the top cutout. Bottom gesture/navigation inset was not part of combat control placement.

### Combat hierarchy

- Player and enemy rows stayed on nearly the same horizontal band, producing the large unused middle/top field visible in the current screenshot.
- End Turn and energy remained near the extreme bottom, competing with the hand and system gesture area.
- The hand was always scaled to one fixed value. A five-card hand was too small, while a ten-card hand could still exceed the short edge.
- Fan compression was fixed and did not pair with visible hand count.
- Targeted-card docking improved thumb tracking but remained too low and too small for the target's central-card emphasis.
- The background used a fixed `1.7` scale rather than a documented cover rule.

### HUD

- The original one-row desktop HUD was mostly scaled as a unit. Scaling reduced legibility without creating the stacked information groups shown in the target.
- Timer, map, deck, and pause could overlap when optional HUD content appeared.
- Relics used a rough count-to-width estimate and a polling loop, but the right-side start position was not guaranteed after late layout changes.
- A text-search timer repeatedly hid the mod status strip; this was brittle, localization-sensitive, and could hide an unrelated UI container.

### Other screens

- Map centering measured only the immediate point controls and used a near-black fill that made seams more visible.
- Shop used three columns, leaving cards too small for the target's portrait readability. It also continuously rewrote positions every 400 ms.
- Event centering relied on the title width as a proxy for the whole block.
- Neow's banner was hidden rather than given a portrait treatment.
- Rest/shop background scale and character-select offsets were fixed pixels with no safe-area relationship.

### Launcher and build integration

- The portrait launcher replaced another launcher's private UI through reflection, so every launcher rename or signature change could break it.
- Steam support was an experimental reflection scaffold and did not expose the overhaul's full auth, cloud, recovery, or diagnostics model.
- The build injected one DLL into a prebuilt APK and disabled a patch by type name. That made source-level conflict resolution and CI difficult.
- Offline install depended on behavior from an older launcher base; the selected overhaul had no explicit local-file importer.
- Development bridge code polled commands from disk and exposed dev-console actions. It was useful for local research but inappropriate in a production runtime.

## Inconsistencies found in the launcher-overhaul base

- The Android manifest forced landscape for all three activities.
- Launcher scale used the long edge divided by 960, making controls enormous on a tall portrait viewport.
- The view used a horizontal 1:4 primary/console split and capped its panel at 800 virtual pixels high.
- “Offline” was only a timeout fallback after a previous Steam ownership verification, not a user choice.
- Cloud Push appeared before Pull even though project guidance describes Pull as the safer first action.
- The launcher entry and bootstrap trace contained development-package absolute paths.
- Project docs, package defaults, update URLs, activity labels, and scripts still identified the overhaul fork rather than the portrait app.
- The checked-out base did not compile against the current local game API: nested helper access modifiers were invalid, `LauncherModel.SessionState` collided with its nested enum name, and `ICloudSaveStore` had gained `HasUserEnabledCloudSync()`.

## Integrated decisions

- `StS2-Launcher-Overhaul` is the repository foundation and Git history.
- Portrait patches are compiled into `STS2Mobile.dll` and applied by the normal startup orchestrator.
- Conflicting landscape scale, mobile layout, event, merchant, and combat-background patch groups are replaced by one portrait group.
- The Android manifest and Godot runtime both request sensor portrait.
- Virtual width is derived from the physical short edge and clamped to a phone-readable range.
- Top and bottom safe-area insets are exposed centrally.
- The hand scales by visible-card count; fan spread is compressed; energy and End Turn sit above the hand; player and enemy rows use separate vertical bands.
- Shop moves to two columns.
- Launcher layout is single-column and scrollable, with touch targets between 54 and 72 virtual pixels.
- Offline and Steam Online are explicit first-screen modes.
- Offline import uses `offline-import-staging`, validates the PCK and `sts2.dll`, swaps only after success, and retains the prior installation as `game-backup`.
- Offline launch writes a one-shot marker so startup cannot re-enable Cloud from a saved preference.
- Steam Online retains ownership verification, download/update, Steam Guard, encrypted credentials, diagnostics, and manual Cloud actions.

## Device validation matrix

Before calling the redesign complete, capture and inspect each case on ARM64 hardware:

| Area | Required states |
| --- | --- |
| Aspect | 19.5:9, 20:9, 21:9 portrait; punch-hole and no-cutout devices |
| Launcher | mode choice, keyboard open, Steam Guard, long errors, download progress, confirmation dialog |
| Offline | missing permission, missing PCK, missing `sts2.dll`, interrupted copy, successful import, reimport with live install |
| Combat hand | 0, 5, 7, and 10 cards; targeted and untargeted cards |
| Combatants | one enemy, multiple enemies, summon/add, boss, player block/status effects |
| HUD | 0/3/5 potion slots, many relics, timer visible, long localized numbers |
| Screens | main menu, character select, Neow, map top/middle/bottom, event, rest, shop, rewards, settings |
| Lifecycle | rotate attempt, background/resume, process kill, failed startup recovery, safe launch |
| Saves | Offline local-only, Online Pull, Online Push confirmation, network timeout fallback |

## Remaining visual limitations

- Exact target spacing still needs runtime screenshots from the integrated APK; compile-time geometry cannot reveal every Godot container's late layout pass.
- The base game's top bar is still one scene hierarchy. The integration repositions and scales its major groups defensively but does not replace game-owned controls or art.
- The rounded frame is an overlay border; it does not shader-clip the entire game viewport into physically rounded corners.
- Dynamic localization and accessibility font scaling need device testing.
