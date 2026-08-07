# TrueWebsiteCloner

Current development stage: **v0.4 Offline Resource / Path Builder**.

## Passed gates

- Gate 0.1 — Windows Foundation: PASS
- Gate 0.2 — Network Metadata Capture Core: PASS
- Gate 0.2B — Real Chrome + Extension + Native Host + Desktop: PASS
- Gate 0.3 — Same-Origin Response Body Capture: PASS

## v0.4 scope

V0.4 converts captured response bodies into a deterministic local site tree under `offline/site/`. Captured HTML and CSS references are rewritten to relative local files, URL paths are mapped to stable filesystem paths, and uncaptured same-origin resources are reported separately.

JavaScript is intentionally not modified in this gate. Dynamic `fetch`, XHR and API behavior will be handled by the next API replay/runtime stage rather than unsafe blanket string replacement.

## Automated gates

- `.github/workflows/foundation-gate.yml` — Gate 0.1
- `.github/workflows/network-capture-gate.yml` — Gate 0.2
- `.github/workflows/runtime-gate-0.2B.yml` — Gate 0.2B
- `.github/workflows/response-body-gate.yml` — Gate 0.3
- `.github/workflows/offline-builder-gate.yml` — Gate 0.4

See `docs/GATE-0.4.md` for the exact V0.4 PASS criteria.

## Next stage

After Gate 0.4 passes, the next stage is **API replay / local runtime**, so offline pages can reproduce captured same-origin API responses without contacting the original site.
