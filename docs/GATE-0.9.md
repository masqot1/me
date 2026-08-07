# Gate 0.9 — Visual Comparison

V0.9 compares the project-owned Test Lab with its offline replay using the same Chrome runtime, viewport and rendered API state.

## Deterministic render

- Chrome for Testing via Puppeteer
- viewport: 1280 × 900
- deviceScaleFactor: 1
- reduced-motion media preference
- wait for network idle, API status PASS and document fonts
- source and offline pages both use the same deterministic `/api/sample` response

## Evidence

- `source.png`
- `offline.png`
- `diff.png`
- `visual-report.json`

`pixelmatch` compares screenshots with a per-pixel threshold of 0.1. The Gate PASS threshold is a maximum of **0.15% different pixels**.

The browser additionally records HTTP requests and Gate 0.9 fails if the offline page requests any origin other than its loopback Local Runtime.
