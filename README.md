# TrueWebsiteCloner

Current development stage: **v0.10 Immutable Snapshot Diff / Update Engine**.

## Passed gates

Gates 0.1 through 0.9 have passed on GitHub Actions, including the real-Chrome visual comparison gate.

## v0.10 scope

V0.10 creates immutable project snapshots and compares them without rewriting history. Snapshot resources are identified by normalized URL and SHA-256 content hash, with MIME type, resource type, byte length, recovery state and local path preserved.

Diff reports classify resources as added, removed, changed or unchanged and include completeness, weighted-completeness and visual-mismatch deltas. Reusing an existing snapshot label is rejected.

Outputs are stored under `history/<label>/snapshot.json` and user-selected diff report paths.

See `docs/GATE-0.10.md` for the immutable-history rules.

## Next stage

After Gate 0.10 passes, the next stage is portable project export/import with integrity hashes, so a verified offline project can be moved or archived without losing its history and reports.
