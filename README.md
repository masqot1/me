# TrueWebsiteCloner

Current development stage: **v0.12 Project Catalog / Workspace Index**.

## Passed gates

Gates 0.1 through 0.11 have passed on GitHub Actions. The project includes real Chrome capture, response bodies, deterministic offline building, loopback replay, verification, controlled recovery, dependency/completeness analysis, visual diffing, immutable snapshots, and tamper-resistant portable export/import.

## v0.12 scope

The Windows Desktop now has a project catalog for the configured workspace. It shows each capture's status, target, completeness, visual mismatch, history count, captured bodies and missing resources, and provides Refresh, Open, Export and Import operations from the same view.

Catalog discovery never scans outside the selected workspace. Reparse points are skipped, depth and directory counts are bounded, and scanning stops at each recognized capture root. Persisted catalog metadata uses relative paths only.

Portable packages imported through V0.11 are integrity-verified before becoming visible in the workspace. Workspace export also supports re-exporting an imported project without recursively embedding `_twc_package` metadata.

See `docs/GATE-0.12.md` for scan and catalog rules.

## Next stage

After Gate 0.12 passes, the next stage is a project diagnostics dashboard: normalize all gate/report results into one machine-readable health model and surface actionable failures, warnings, evidence paths and recommended next actions per project.
