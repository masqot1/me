# Gate 0.10 — Immutable Snapshot Diff / Update Engine

V0.10 adds version-aware project history without overwriting older captures.

A snapshot records resource URL, MIME/resource type, SHA-256 content hash, byte length, recovery state, local path, completeness metrics and visual mismatch metric. Snapshots are stored under `history/<label>/snapshot.json`; an existing label is rejected rather than overwritten.

The diff engine compares two snapshots by normalized resource URL and reports:

- added resources;
- removed resources;
- changed resources;
- unchanged resources;
- content-hash changes;
- recovery-state changes;
- local-path/MIME changes;
- completeness-score deltas;
- weighted-completeness deltas;
- visual-mismatch deltas.

Gate 0.10 proves both snapshots remain byte-identical before and after comparison.
