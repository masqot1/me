# Gate 0.7 — Capture Completeness and Missing Resource Recovery

V0.7 closes deterministic gaps reported by the V0.4 Offline Builder.

The recovery engine in this gate is deliberately restricted to the project-owned loopback Test Lab. It does not perform recovery requests against external websites.

## Policy

- target capture must be HTTP/HTTPS loopback;
- missing URL must be same-origin with the captured target;
- GET only;
- no cookies;
- no Authorization header;
- no redirects;
- maximum 16 recovery items per pass;
- maximum 512 KiB per recovered response;
- sensitive query parameter names such as token, auth, session, signature, key and password are skipped;
- only web text/JSON/JavaScript/SVG and common image MIME types are accepted.

## Test Lab recovery scenario

The Test Lab document contains a normal link to `/recover/help.html`. The browser does not request that linked document during the initial page capture, so V0.4 reports it as missing. V0.7 reads that report, retrieves the resource from loopback without credentials, appends it to the captured body manifest, rebuilds the offline tree, and verifies the missing count becomes zero.

PASS also requires real Chrome to follow the recovered link entirely inside Local Runtime with no external HTTP traffic.
