# TrueWebsiteCloner

Current development stage: **v0.9 Visual Comparison**.

## Passed gates

Gates 0.1 through 0.8 have passed on GitHub Actions. The project now has real Chrome capture, response-body storage, deterministic offline building, local GET replay, source-vs-replay verification, controlled missing-resource recovery, and an auditable dependency/completeness graph.

## v0.9 scope

V0.9 renders the project-owned Test Lab and its offline replay in the same Chrome for Testing environment at 1280×900. Both pages reach the same deterministic API-rendered state before screenshots are taken.

Evidence includes `source.png`, `offline.png`, `diff.png`, and `visual-report.json`. The visual gate uses a 0.15% maximum mismatched-pixel threshold and fails if the offline browser makes an HTTP request outside its Local Runtime origin.

See `docs/GATE-0.9.md` for the rendering and comparison rules.

## Next stage

After Gate 0.9 passes, the next stage is a deterministic capture diff/update engine that can compare two project snapshots and explain added, removed, changed, recovered and visually changed resources without overwriting history.
