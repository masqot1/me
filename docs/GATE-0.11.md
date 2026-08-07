# Gate 0.11 — Portable Project Export / Import Integrity

V0.11 packages a completed TrueWebsiteCloner project for local transfer or archival without losing capture data, offline outputs, verification reports, visual evidence, dependency reports, or immutable snapshot history.

## Package

Recommended extension: `.twcproj` (ZIP container).

Each package contains `_twc_package/project-export-manifest.json` plus every project file. The manifest records a SHA-256 and byte length for each relative file path and a deterministic content-root SHA-256 for the complete project. Export also writes a sidecar `<package>.sha256` containing the SHA-256 of the package bytes.

## Determinism

Entries are sorted, use a fixed ZIP timestamp, fixed attributes, and no compression. Re-exporting unchanged input must produce byte-identical package bytes and identical package/content-root hashes.

## Import safety

- verify the complete manifest before extraction;
- verify every extracted file again while writing;
- reject absolute paths, `.` / `..` path segments and drive-qualified paths;
- reject case-insensitive duplicate entries on Windows;
- reject ZIP symlink entries;
- reject source filesystem reparse points during export;
- maximum 50,000 files, 1 GiB per file and 8 GiB total uncompressed data;
- destination must not already exist;
- extraction happens in a staging folder and is moved into place only after every file verifies;
- corrupted/tampered packages never materialize the requested destination.

Imported projects receive `_twc_package/import-verification.json` and a copy of the verified export manifest. Original project files and history remain byte-identical.

## PASS

Gate 0.11 requires deterministic repeated export, successful verify/import, byte equality for all original files, preserved snapshot history, destination-overwrite refusal, tamper rejection, path-traversal rejection and symlink-entry rejection.
