# Changelog

Notable changes to the portrait project. Bug ids refer to [docs/BUGS.md](docs/BUGS.md).

## [Unreleased] - 0.4.0 / 0.5.0 line

### Fixed
- BUG-001: bootstrap PCK now requests sensor portrait (`orientation=5`; 4 was sensor landscape in the Godot 4 enum), and `GodotApp` coerces every activity orientation request to the portrait family, removing the landscape flash on launcher cold start and on the Start game handoff. A regression verifier (`scripts/verify-bootstrap-pck.py`) re-parses the generated PCK.
- BUG-002: launcher confirmation dialogs render on a dedicated CanvasLayer above all content; the section-column ZIndex override that drew buttons over dialog text is gone, and buttons under an open dialog no longer receive input.
- BUG-005: the Repair game files confirmation states the real per-mode behavior (Steam: delete download then re-download; Offline: remove import for a clean reimport) instead of upstream's stale "Redownload game files?" copy.
- BUG-003 (event slice): event text starts below the run HUD band. `PortraitHudMetrics` is the single source for HUD-occupied space; the event block keeps its authored Y only when it clears the HUD.
- BUG-004 (geometry): the combat top band and the native top cover take the real display-cutout inset as a lower bound instead of being purely fixed-size; cover sizing is logged for the gutter investigation.

- BUG-030/031: layout loops survive overlay detach windows (resume-into-loot stayed native); the patched-copy access rule sharpened to protected base-class FIELDS, and the pile arrival rewrite moved out of the unpatchable AnimIn. Device applies every registered patch class (50/50 at the time).
- BUG-032/033: deploy guard verifies a fresh patch orchestration; Save and Quit no longer freezes the menu into a landscape strip (native content scale drift; ForceRefresh writes a different size first).
- BUG-035: map scrim shortened to the bar band; pinch-to-zoom (TheMap scales about the fingers, two-finger drift pans through PivotOffset).
- BUG-036/037/038: merchant shelves and the treasure relic at thumb size; the map eclipses a finished combat's hand and End Turn; grid select overlays start below the bar with a grown View Upgrades box.
- BUG-042/043/044: the main menu profile button takes a tap (corner buttons grow through their children, not their own Scale); the profile card shows the whole "Updated" timestamp and its back tab sits at the top; the patch notes back tab moves to the top and the article starts below it.
- BUG-045: the card pick grid pins slots per holder, so the hover that precedes a touch no longer re-deals the cards under the finger.
- BUG-046: the map's lower scroll bound moves down 780 in portrait; the bottom row sits above the legend instead of mid-screen.
- BUG-047/048/049: act ancients: option descriptions wrap on taller plates, the top-bar scrim no longer covers the whole ancient screen (text and art at full brightness), and the scene fills the phone with the speaker's face above the bubble.
- BUG-039 (closed) and BUG-050/051: the deck, map and pause buttons return to their bar slots after a fight (the icon "vanishing" was a stale combat transform in a margin slot); the loot overlay gets the compact bar with the potion capsule at its station, and loot/card pick hang from the same content top whether reached after a fight or a resume.
- Pause menu: the portrait pass runs on every open (a pause opened mid-combat came up at authored size), rows take 82 percent of the canvas width with taller plates and a larger title.
- Dynamic fit: main menu rows scale to the band under the logo and the VBox minimum is cleared (the last row left the screen on 16:9); the map's lower scroll bound and the card pick grid derive from the canvas height; a dev trigger `sts2_aspect` ("16:9", "off") letterboxes the canvas to emulate shorter phones on this device.
- Events: option descriptions wrap on regular event plates too, the option block measures its visible rows (a lone Proceed no longer floats mid-screen), and the top-bar scrim keeps the bar-band depth while an overlay covers the event (the removal grid's top rows read dim).
- Card pick: the Skip plate's open tween is dropped so it no longer bounces against the portrait placement.
- BUG-040/041: map furniture yields to pause/settings; the back tab moves to the top on pause and settings (NBackButton _showPos rewrite) and the settings list fits above it.
- Card pick overlays rebuilt as a 2+1 grid of 1.5x cards; pause rows centered under their labels; potion capsule keeps its vanilla station at thumb size; coop Ping button hidden; Proceed arrow floors below character art.

### Added (dev tooling)
- File-triggered probes under user://: `sts2_weaken` (all enemies to 1 hp for test runs), `sts2_zoom` / `sts2_pan`, `sts2_types`, `sts2_vpdump`.
- Device scripts run from Linux or Git Bash on Windows (repo derived from script location, adb/serial from env, MSYS path conversion off, Windows-form local paths).

### Removed
- BUG-006/BUG-010: five unregistered patch files (EventLayout, Merchant, MobileLayout, UiScale, CombatBackground), the never-called PortraitTopVignette, and a dead LauncherActivity helper (1,000+ lines of misleading dead code).

### Added
- Project docs: bug inventory ([docs/BUGS.md](docs/BUGS.md)), test log ([docs/TESTS.md](docs/TESTS.md)), upstream boundary and sync policy ([docs/UPSTREAM.md](docs/UPSTREAM.md)), roadmap ([docs/ROADMAP.md](docs/ROADMAP.md)).
- `tools/pctest/`: PC harness that compiles the game-independent layer against official GodotSharp/SteamKit2 packages and unit-checks the HUD metrics math, for machines without game files.

### Removed (docs)
- Inherited upstream governance files (`MIGRATION_CHECKLIST.md`, `OVERHAUL_ROADMAP.md`, `OVERHAUL_STATUS.md`, `docs/current-android-status.md`) that described the upstream project, not this one.

## [0.3.0-portrait-preview] - 2026-07-20

First save point for the portrait-first Android build. See [docs/release-notes/v0.3.0-portrait-preview.md](docs/release-notes/v0.3.0-portrait-preview.md).
