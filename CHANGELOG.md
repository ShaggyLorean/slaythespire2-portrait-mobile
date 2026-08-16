# Changelog

Notable changes to the portrait project. Bug ids refer to [docs/BUGS.md](docs/BUGS.md).

## [Unreleased] - 0.4.0 / 0.5.0 line

### Fixed
- BUG-001: bootstrap PCK now requests sensor portrait (`orientation=5`; 4 was sensor landscape in the Godot 4 enum), and `GodotApp` coerces every activity orientation request to the portrait family, removing the landscape flash on launcher cold start and on the Start game handoff. A regression verifier (`scripts/verify-bootstrap-pck.py`) re-parses the generated PCK.
- BUG-002: launcher confirmation dialogs render on a dedicated CanvasLayer above all content; the section-column ZIndex override that drew buttons over dialog text is gone, and buttons under an open dialog no longer receive input.
- BUG-005: the Repair game files confirmation states the real per-mode behavior (Steam: delete download then re-download; Offline: remove import for a clean reimport) instead of upstream's stale "Redownload game files?" copy.
- BUG-003 (event slice): event text starts below the run HUD band. `PortraitHudMetrics` is the single source for HUD-occupied space; the event block keeps its authored Y only when it clears the HUD.
- BUG-004 (geometry): the combat top band and the native top cover take the real display-cutout inset as a lower bound instead of being purely fixed-size; cover sizing is logged for the gutter investigation.

### Removed
- BUG-006/BUG-010: five unregistered patch files (EventLayout, Merchant, MobileLayout, UiScale, CombatBackground), the never-called PortraitTopVignette, and a dead LauncherActivity helper (1,000+ lines of misleading dead code).

### Added
- Project docs: bug inventory ([docs/BUGS.md](docs/BUGS.md)), test log ([docs/TESTS.md](docs/TESTS.md)), upstream boundary and sync policy ([docs/UPSTREAM.md](docs/UPSTREAM.md)), roadmap ([docs/ROADMAP.md](docs/ROADMAP.md)).
- `tools/pctest/`: PC harness that compiles the game-independent layer against official GodotSharp/SteamKit2 packages and unit-checks the HUD metrics math, for machines without game files.

### Removed (docs)
- Inherited upstream governance files (`MIGRATION_CHECKLIST.md`, `OVERHAUL_ROADMAP.md`, `OVERHAUL_STATUS.md`, `docs/current-android-status.md`) that described the upstream project, not this one.

## [0.3.0-portrait-preview] - 2026-07-20

First save point for the portrait-first Android build. See [docs/release-notes/v0.3.0-portrait-preview.md](docs/release-notes/v0.3.0-portrait-preview.md).
