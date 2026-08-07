# TrueWebsiteCloner

Current development stage: **v0.2 Network Capture Core**.

## Passed foundation

Gate 0.1 passed on GitHub Actions on Windows: build, Native Host registration, static checks, Test Lab startup and GET/POST API checks.

Gate 0.2 also passed on GitHub Actions: the metadata capture engine writes JSONL/session/summary files and rejects sensitive/unapproved fields.

## v0.2 scope

The Chrome extension can attach to an explicitly selected HTTP/HTTPS tab through `chrome.debugger`, enable the Network domain, and stream **metadata only** to the Windows application through Native Messaging.

Captured metadata includes URL, method, status, resource type, MIME type, protocol, timing, cache/service-worker flags, encoded length and loading success/failure metadata.

This stage intentionally does **not** save request bodies, response bodies, cookies, authorization headers, or arbitrary request/response headers.

The Windows capture engine uses a whitelist, so unapproved fields such as `Authorization`, `Cookie`, `Set-Cookie` and `postData` are discarded even if they are accidentally supplied.

## Automated gates

- `.github/workflows/foundation-gate.yml` — Gate 0.1
- `.github/workflows/network-capture-gate.yml` — Gate 0.2
- `.github/workflows/runtime-gate-0.2B.yml` — Gate 0.2B real Chrome runtime using Puppeteer + Chrome for Testing

Gate 0.2B exercises the real extension service worker, `chrome.debugger`, Native Messaging Host, Desktop bridge, and Test Lab on a Windows GitHub runner.

## Local Windows runtime gate

You can also run:

```text
03_RUNTIME_GATE_0_2B.bat
```

This produces `runtime-gate-output/0.2B/runtime-gate-0.2B-report.json`.

## Next stage

Only after Gate 0.2B passes do we implement **v0.3 Response Body Capture**.
