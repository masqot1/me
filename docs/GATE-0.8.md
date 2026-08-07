# Gate 0.8 — Dependency Graph and Completeness Score

V0.8 explains why an offline project is complete or incomplete instead of relying on a visual guess.

## Graph sources

- HTML: `src`, `href`, `poster`
- CSS: `url(...)`, quoted `@import`
- JavaScript: literal `fetch('...')` and dynamic `import('...')`

No JavaScript is executed by the graph builder.

## Nodes

Nodes identify captured, recovered, missing and external resources. Captured nodes also retain MIME type, resource type and local path.

## Edges

Edges record the source resource, target URL, discovery kind, same-origin/external scope, whether the dependency is resolved and a transparent weight.

Weights: document 5, stylesheet/script 4, fetch/XHR 3, font 2, image/other 1.

## Outputs

- `offline/dependency-graph.json`
- `offline/dependency-graph.dot`
- `offline/completeness-report.json`

Gate 0.8 uses a real Chrome Test Lab capture, performs V0.7 recovery, then requires 100% raw and weighted completeness with the expected HTML and JavaScript dependency edges present.
