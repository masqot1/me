# TrueWebsiteCloner

Current development stage: **v0.6 Offline Verification / Diff**.

## Passed gates

- Gate 0.1 — Windows Foundation: PASS
- Gate 0.2 — Network Metadata Capture Core: PASS
- Gate 0.2B — Real Chrome + Extension + Native Host + Desktop: PASS
- Gate 0.3 — Same-Origin Response Body Capture: PASS
- Gate 0.4 — Offline Resource / Path Builder: PASS
- Gate 0.5 — Loopback Local Runtime / Recorded GET Replay: PASS

## v0.6 scope

V0.6 verifies a real Chrome capture of the project-owned Test Lab end-to-end. It rebuilds the offline site, starts the loopback Local Runtime, compares recorded routes with the live Test Lab, writes `offline/verification-report.json`, and finally opens the offline site in Chrome to prove its JavaScript and recorded API call work without external HTTP requests.

The Gate 0.6 comparison is deliberately restricted to loopback endpoints (`127.0.0.1`/localhost). It does not perform verification traffic against external websites.

## Automated gates

- `.github/workflows/foundation-gate.yml` — Gate 0.1
- `.github/workflows/network-capture-gate.yml` — Gate 0.2
- `.github/workflows/runtime-gate-0.2B.yml` — Gate 0.2B
- `.github/workflows/response-body-gate.yml` — Gate 0.3
- `.github/workflows/offline-builder-gate.yml` — Gate 0.4
- `.github/workflows/local-runtime-gate.yml` — Gate 0.5
- `.github/workflows/verification-gate.yml` — Gate 0.6

See `docs/GATE-0.6.md` for PASS criteria.

## Next stage

After Gate 0.6 passes, the next stage is capture completeness and missing-resource recovery inside the authorized project/test environment.
