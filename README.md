# TrueWebsiteCloner

Current development stage: **v0.8 Dependency Graph / Completeness Score**.

## Passed gates

Gates 0.1 through 0.7 have passed on GitHub Actions, including real Chrome capture, response-body storage, offline building, local API replay, source-vs-replay verification, and deterministic missing-resource recovery inside Test Lab.

## v0.8 scope

V0.8 builds a dependency graph from captured HTML, CSS and simple literal JavaScript dependencies. Each resource becomes a node and each discovered relationship becomes an edge. Nodes identify captured, recovered, missing or external resources.

The project now emits both a raw completeness score and a weighted score. Documents, stylesheets, scripts and API dependencies have greater weight than decorative images, while the exact weights are written into the report so the score is auditable.

Outputs:

- `offline/dependency-graph.json`
- `offline/dependency-graph.dot`
- `offline/completeness-report.json`

Gate 0.8 requires a real Chrome Test Lab capture, V0.7 recovery, the expected HTML and JavaScript edges, zero missing same-origin dependencies, and 100% raw and weighted completeness.

See `docs/GATE-0.8.md` for details.

## Next stage

After Gate 0.8 passes, the next stage is visual comparison: render source and offline pages in controlled Chrome sessions and measure visible layout/render differences with an explicit PASS threshold.
