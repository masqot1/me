# Gate 0.12 — Workspace Project Catalog

V0.12 turns the configured Desktop project folder into a bounded local workspace instead of repeatedly searching the entire filesystem.

## Scan policy

- only the user-configured workspace root is scanned;
- the workspace root itself cannot be a reparse point;
- reparse/symlink directories are skipped;
- candidates are checked to remain inside the configured root;
- scan depth is capped at 8 directory levels;
- at most 10,000 directories are visited per refresh;
- when `_network/session.json` identifies a capture root, recursion stops at that root;
- `_twc_catalog` is reserved for catalog metadata and is not scanned as a project.

## Catalog fields

The in-memory Desktop catalog includes project path, target URL, start time, event/body counts, offline readiness, missing-resource count, completeness and weighted-completeness scores, visual mismatch, snapshot count, import-integrity state, verification state and a concise status.

The persisted `_twc_catalog/catalog.json` intentionally stores relative paths only, not absolute workspace paths.

## Desktop operations

The Windows app exposes Refresh Projects, Open Selected, Export Selected and Import Package in one catalog. Capture-derived operations use the selected project, falling back to the newest indexed project when nothing is selected.

Imported V0.11 package metadata is ignored during re-export by staging only the original project files, so imported projects can be exported again without packaging package-metadata recursively.

## PASS

Gate 0.12 builds the WPF app, validates scan-scope controls, indexes two deterministic in-workspace captures, rejects nested/outside fake captures, validates status/completeness/visual/history/import-integrity fields, proves newest-first ordering, and proves repeated catalog refresh produces byte-identical relative-path metadata.
