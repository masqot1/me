# TrueWebsiteCloner

Current development stage: **v0.7 Capture Completeness / Missing Resource Recovery**.

## Passed gates

- Gate 0.1 — Windows Foundation: PASS
- Gate 0.2 — Network Metadata Capture Core: PASS
- Gate 0.2B — Real Chrome + Extension + Native Host + Desktop: PASS
- Gate 0.3 — Same-Origin Response Body Capture: PASS
- Gate 0.4 — Offline Resource / Path Builder: PASS
- Gate 0.5 — Loopback Local Runtime / Recorded GET Replay: PASS
- Gate 0.6 — Offline Verification / Diff: PASS

## v0.7 scope

V0.7 reads the missing-resource report created by the offline builder, performs a tightly controlled recovery pass against the project-owned loopback Test Lab, appends successfully recovered response bodies to the capture, and rebuilds the offline tree.

The current recovery implementation is intentionally limited to loopback, same-origin GET requests. It sends no cookies or Authorization headers, follows no redirects, skips sensitive query parameter names, limits each pass to 16 items, and caps each recovered resource at 512 KiB.

The deterministic test case uses `/recover/help.html`: it is linked from the Test Lab document but is not requested during the initial page load. Gate 0.7 proves it is first reported missing, then recovered, rebuilt, replayed locally, and opened in real Chrome without any external HTTP traffic.

## Automated gates

The repository now includes automated Gates 0.1 through 0.7 under `.github/workflows/`.

See `docs/GATE-0.7.md` for the recovery policy and PASS criteria.

## Next stage

After Gate 0.7 passes, the next stage is a dependency graph and capture-completeness scoring layer so each offline project can explain exactly which captured resources depend on which others and why a project is or is not complete.
