# Gate 0.6 — Offline Verification / Diff

V0.6 verifies the generated offline site against the project-owned deterministic Test Lab.

The gate is intentionally local-only: both source and replay endpoints are bound to `127.0.0.1`. It does not connect to external websites.

## Verification layers

1. Capture Test Lab using the real Chrome extension path already proven by Gate 0.3.
2. Build the offline tree from that real capture.
3. Start Local Runtime on loopback.
4. Compare captured routes between Test Lab and Local Runtime.
5. Run the offline site in Chrome and verify its JavaScript and recorded API response work without external HTTP requests.

## PASS

- recorded routes return the expected status and content type;
- JSON API data matches;
- JavaScript and unchanged resources match;
- HTML/CSS path rewrites are classified as expected changes;
- Local Runtime responses carry the offline replay marker;
- the browser observes no HTTP request outside the local replay origin;
- `verification-report.json` reports zero unexpected divergences.
