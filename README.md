# TrueWebsiteCloner

Current development stage: **v0.3 Same-Origin Response Body Capture**.

## Passed gates

- Gate 0.1 — Windows Foundation: PASS
- Gate 0.2 — Network Metadata Capture Core: PASS
- Gate 0.2B — Real Chrome + Extension + Native Host + Desktop: PASS

## v0.3 scope

V0.3 captures actual response bodies for the explicitly selected HTTP/HTTPS tab while keeping the scope deliberately constrained. Only same-origin `GET` responses are eligible, each decoded body is capped at 512 KiB, and request bodies, cookies and Authorization headers remain excluded.

Captured body types include HTML, CSS, JavaScript, JSON, text, SVG and common web images. Body bytes are stored under `_bodies/`; `network.jsonl` contains body metadata and file paths but never the body content itself.

## Automated gates

- `.github/workflows/foundation-gate.yml` — Gate 0.1
- `.github/workflows/network-capture-gate.yml` — Gate 0.2
- `.github/workflows/runtime-gate-0.2B.yml` — Gate 0.2B
- `.github/workflows/response-body-gate.yml` — Gate 0.3

Gate 0.3 includes an engine test for text/base64 bodies, size and same-origin policy checks, plus a real Chrome/Puppeteer run that saves Test Lab HTML/CSS/JS/JSON and verifies the contents on disk.

See `docs/GATE-0.3.md` for the exact policy and PASS criteria.

## Next stage

Only after Gate 0.3 passes do we begin the offline resource/path builder stage.
