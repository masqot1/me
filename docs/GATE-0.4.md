# Gate 0.4 — Offline Resource and Path Builder

V0.4 turns the response bodies captured by V0.3 into a deterministic local site tree.

## Scope

- Build from `_bodies/bodies.jsonl` and `_network/session.json`.
- Map captured URLs to stable local paths under `offline/site/`.
- Preserve common file extensions and infer extensions from MIME type when a URL has none.
- Rewrite captured HTML `src`, `href`, and `poster` references to relative local paths.
- Rewrite CSS `url(...)` and quoted `@import` references.
- Leave cross-origin references untouched.
- Report uncaptured same-origin references in `offline/missing-resources.json`.
- Produce deterministic `offline/offline-manifest.json`.
- Do not rewrite arbitrary JavaScript yet; dynamic `fetch`/XHR/API replay is the next stage.

## Output

```text
capture-.../
└── offline/
    ├── offline-manifest.json
    ├── missing-resources.json
    └── site/
        ├── index.html
        ├── styles.css
        ├── app.js
        ├── images/
        └── api/
```

## PASS criteria

Gate 0.4 verifies deterministic URL mapping, HTML/CSS rewriting, missing-resource reporting, preservation of JavaScript for the later API replay stage, and byte-identical manifest output across repeated builds.
