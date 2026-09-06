# Upstream boundary and sync policy

Upstream: [SocialHummingbird/StS2-Launcher-Overhaul](https://github.com/SocialHummingbird/StS2-Launcher-Overhaul)

## The one rule

**Function comes from upstream; look and layout are ours.** We take upstream's logic work (install, download, runtime selection, hash verification, branch management, cloud save, launch flow, crash and platform fixes). We never take its presentation (layout, theme, colors, typography, icons, button placement, screen composition, animation, responsive UI). When an upstream commit mixes both, port the functional part and re-express it in our portrait UI. On conflict: function upstream wins, appearance we win. When unsure which side a change falls on, stop and ask the maintainer; a wrong merge costs more than the question.

## Structural facts every sync must respect

- This repo is **not a git fork**. It started independently and imported upstream's 162-file snapshot in one commit. There is no shared git history; `git merge` against upstream is never an option. Syncs are manual ports of chosen upstream commits.
- **Import baseline: upstream `main` commit `1696c00` (2026-06-26).** Record the new baseline here after every sync round.
- Upstream has since restructured heavily (launcher split into hundreds of partial classes, 162 to 1500+ files). Upstream file paths no longer map 1:1 to ours; port by behavior, not by file copy.

## File ownership map

### Upstream-owned (sync freely, keep byte-parity where possible)

- `src/STS2Mobile/Steam/` (all files)
- `src/STS2Mobile/Patches/` (platform/lifecycle/mod/LAN patch groups)
- `src/stubs/`
- `patches/godot/sts2-android-runtime.patch` (byte-identical to upstream today; keep it that way unless a portrait need forces a change, and flag it here if so)
- `tools/SteamKitAndroidPatch/`
- `scripts/` build and validation tooling (except portrait-specific scripts listed below)
- `android/src/main/java/net/dot/android/crypto/`

### Ours, do not touch on sync

- `src/STS2Mobile/Portrait/` (entire directory)
- `src/STS2Mobile/Launcher/Components/` (visual system: theme, styles, backgrounds, dialogs)
- `src/STS2Mobile/Launcher/Sections/ModeSelectionSection.cs`, `OfflineImportSection.cs`
- `src/STS2Mobile/Launcher/LauncherMode.cs`, `OfflineGameImporter.cs`
- Layout/composition code inside `LauncherView.cs`, `LauncherUI.cs`, `LauncherViewLayoutMetrics.cs`, `Sections/LauncherSectionMetrics.cs`
- `android/AndroidManifest.xml` orientation attributes; `android/res/` branding and themes
- `scripts/make-bootstrap-pck.py`, `scripts/verify-bootstrap-pck.py`
- `tools/pctest/`
- `README.md`, `docs/` (except inherited reference docs noted below)

### Mixed files (line-level care; this is where syncs go wrong)

- `src/STS2Mobile/Launcher/LauncherController.cs` — upstream flow logic interleaved with our mode-selection/offline flow and copy
- `src/STS2Mobile/Launcher/LauncherModel.cs` — upstream session/launch model plus our launch-mode state
- `src/STS2Mobile/Launcher/Sections/ActionSection.cs` — upstream button wiring plus our disclosure layout and labels
- `android/src/com/game/sts2launcher/GodotApp.java` — upstream runtime host plus our cutout bridge, top cover, orientation coercion

For mixed files: port upstream behavior change by change, never overwrite the file, and re-run the launcher visual comparison below.

## Sync procedure

1. Pick the upstream commits/behaviors to port (prefer reliability and save-safety work first).
2. One upstream behavior = one commit here. Functional ports and any UI re-expression go in separate commits so either can be reverted alone.
3. Never port presentation. If a function needs UI, express it with our components (`StyledButton`, `StyledPanel`, section metrics).
4. After the sync: launcher before/after screenshot comparison on the same screen states (mode select, ready-to-launch, More options expanded, a confirmation dialog). Appearance must be pixel-equivalent except where a deliberate, separately-committed change says otherwise.
5. Update the baseline record below and CHANGELOG.

## Baseline record

| Date | Synced up to | Notes |
| --- | --- | --- |
| 2026-07-20 | `1696c00` (2026-06-26 snapshot) | initial import baseline |

## Inherited reference docs

`docs/current-android-status.md` was upstream's own evidence ledger and has been removed with the root-level upstream governance files (`MIGRATION_CHECKLIST.md`, `OVERHAUL_ROADMAP.md`, `OVERHAUL_STATUS.md`); they described upstream's project, not this one. `docs/testing-needed.md`, `docs/device-log-checklist.md` and the runbook/release docs are inherited but still useful as reference; rewrite them for the portrait project when they next need substantive edits.

## Sync check log

Every work session starts with a check of upstream `main` against the baseline (clone kept at `D:\Projects	mp-upstream-launcher`, `git log --since=<baseline date>`). Record the verdict here even when nothing is ported.

| Date | Upstream head | Verdict |
| --- | --- | --- |
| 2026-09-05 | `5592513` (2026-08-22), 13 commits past baseline | Nothing to port. Upstream rewrote its cloud-save layer (SaveSyncService, SteamCloudTransport) and split the launcher into hundreds of partial classes; the two small fixes in that stream (cloud RPC timeout, skip unchanged files) are behaviors our `CloudSyncCoordinator` / `SteamKit2CloudSaveStore` already have (per-path timeouts, identical-content skip). The rest is restructuring, mod loading, runtime identity and handoff lifecycle work tied to their new file layout; revisit only if a concrete launcher defect appears here. |
| 2026-09-06 | `c4c9ea9` (2026-09-06), 8 more commits | Nothing to port now. The stream is their Issue 37 work: FMOD PCK preparation ownership and an automatic local PCK repair operation inside their restructured install pipeline (BranchInstallStateStore, LocalPckRepairOperation, DepotDownloader.PckPatch), plus a launcher UX/diagnostics pass that touches their StyledButton/StyledPanel (presentation, never ported). Candidate for a later functional port: the local PCK repair idea (re-validate and re-patch a damaged managed PCK without a full re-download), to be re-expressed against our DepotDownloader/OfflineGameImporter if a matching defect shows up. |
