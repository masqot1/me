# TrueWebsiteCloner

Current development stage: **v0.5 Local Runtime / Recorded GET Replay**.

## Passed gates

- Gate 0.1 — Windows Foundation: PASS
- Gate 0.2 — Network Metadata Capture Core: PASS
- Gate 0.2B — Real Chrome + Extension + Native Host + Desktop: PASS
- Gate 0.3 — Same-Origin Response Body Capture: PASS
- Gate 0.4 — Offline Resource / Path Builder: PASS

## v0.5 scope

V0.5 serves a completed offline capture on loopback only. The local page can request recorded same-origin GET resources such as `/api/sample`, and the Local Runtime returns the captured file from disk. A replay miss is never proxied to the original site.

Security policy: `127.0.0.1` only, GET/HEAD only, no cookies, no Authorization replay, no request bodies and no outbound HTTP proxy/client.

## Automated gates

- `.github/workflows/foundation-gate.yml` — Gate 0.1
- `.github/workflows/network-capture-gate.yml` — Gate 0.2
- `.github/workflows/runtime-gate-0.2B.yml` — Gate 0.2B
- `.github/workflows/response-body-gate.yml` — Gate 0.3
- `.github/workflows/offline-builder-gate.yml` — Gate 0.4
- `.github/workflows/local-runtime-gate.yml` — Gate 0.5

See `docs/GATE-0.5.md` for the exact replay policy and PASS criteria.

## Next stage

After Gate 0.5 passes, the next stage is offline verification: compare the local runtime against the deterministic Test Lab and report missing or behaviorally divergent resources.
