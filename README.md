# TrueWebsiteCloner

Current development stage: **v0.11 Portable Project Export / Import Integrity**.

## Passed gates

Gates 0.1 through 0.10 have passed on GitHub Actions. TrueWebsiteCloner now covers real Chrome capture, response bodies, deterministic offline building, local GET replay, verification, controlled recovery, dependency/completeness analysis, visual comparison and immutable snapshot diffs.

## v0.11 scope

V0.11 introduces portable `.twcproj` packages for moving or archiving a verified project without losing its history and reports. Every source file has a SHA-256 and byte length in the package manifest, the full project has a deterministic content-root SHA-256, and the package bytes receive a separate SHA-256 sidecar.

Import verifies the whole archive before extraction and re-verifies every file while materializing it. Path traversal, ZIP symlinks, source reparse points, undeclared files, duplicate paths, integrity mismatches and existing destinations are rejected. Import uses a staging directory and becomes visible at the requested destination only after successful verification.

CLI:

```text
TrueWebsiteCloner.PortableTool export --project <capture-folder> --output <project.twcproj>
TrueWebsiteCloner.PortableTool verify --package <project.twcproj>
TrueWebsiteCloner.PortableTool import --package <project.twcproj> --destination <new-folder>
```

See `docs/GATE-0.11.md` for integrity and import-safety rules.

## Next stage

After Gate 0.11 passes, the next stage is project catalog/indexing inside the desktop app: discover local projects, show gate/completeness/visual/history status, and open/export/import projects from one workspace without scanning outside configured project roots.
