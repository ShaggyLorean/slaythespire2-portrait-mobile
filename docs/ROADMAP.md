# Roadmap: 0.3.0 to 1.0

Goal (from the project plan): maximum portrait compatibility. 1.0 does not mean "everything was done"; it means the known-bug list is empty, the tests pass, and behavior is predictable across devices.

Working rules for every milestone: one topic per commit, PC pre-screening with real device metrics, device validation before anything is called fixed, results recorded in [TESTS.md](TESTS.md), bugs tracked in [BUGS.md](BUGS.md).

| Version | Scope | Exit criteria |
| --- | --- | --- |
| **0.4.0** | BUG-001 (startup landscape flash, both legs), BUG-002 (dialog stacking), BUG-005 (repair copy), BUG-006/010 (dead code), docs infrastructure (BUGS/TESTS/UPSTREAM/ROADMAP), PC harness | No landscape frame on cold start or game handoff on device; all dialogs above content; test log filled for the round |
| **0.5.0** | BUG-003 event slice (HUD metrics single source + event Y reservation), BUG-004 (cutout-driven combat band and cover, gutter diagnostics) | Event text clear of the HUD on device; band covers cutout on the reference device; gutter log values recorded |
| **0.6.0** | BUG-011 wave 1: card reward, pause menu, deck viewer, run end/death screens; BUG-009 decision (UI Scale in portrait) | A standard run is portrait-clean start to finish on device |
| **0.7.0** | BUG-011 wave 2: overlays and tooltips (card/grid/hand select, boss relic, potion prompts, purchase confirm, relic tooltips); BUG-007/008 (watermark heuristic, perpetual timers) | Screen-sweep checklist has no unhandled entries; no perpetual full-tree scans |
| **0.8.0** | First upstream sync round (functional only, save-safety/cloud line first) per UPSTREAM.md; launcher modernization design proposal | Sync lands with pixel-equivalent launcher; new baseline recorded; design proposal reviewed |
| **0.9.0** | Device-matrix hardening (19.5:9 / 20:9 / 21:9, cutout and cutout-less, font scale), launcher redesign implementation if approved | Every matrix cell has a recorded pass |
| **1.0** | Stabilization only | BUGS.md open count is zero; TESTS.md matrix green; two consecutive release candidates without a new bug |

The screen-sweep checklist that feeds 0.6.0/0.7.0 lives in [portrait-design-audit.md](portrait-design-audit.md) (device validation matrix) plus the BUG-011 list.
