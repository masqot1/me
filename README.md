# TrueWebsiteCloner

Current development stage: **v0.2 Network Capture Core**.

## Passed foundation

Gate 0.1 passed on GitHub Actions on Windows: build, Native Host registration, static checks, Test Lab startup and GET/POST API checks.

## v0.2 scope

The Chrome extension can attach to an explicitly selected HTTP/HTTPS tab through `chrome.debugger`, enable the Network domain, and stream **metadata only** to the Windows application through Native Messaging.

Captured metadata includes URL, method, status, resource type, MIME type, protocol, timing, cache/service-worker flags, encoded length and loading success/failure metadata.

This stage intentionally does **not** save request bodies, response bodies, cookies, authorization headers, or arbitrary request/response headers.

The Windows capture engine uses a whitelist, so unapproved fields such as `Authorization`, `Cookie`, `Set-Cookie` and `postData` are discarded even if they are accidentally supplied.

## Development install

Requirements:

- Windows 10/11 x64
- Google Chrome
- .NET 10 SDK

Run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\Install-Dev.ps1
```

Then load `chrome-extension` as an unpacked extension from `chrome://extensions`, run `artifacts\desktop\TrueWebsiteCloner.exe`, start the Test Lab, and use **Start + Reload** from the extension popup.

## Automated gates

- `.github/workflows/foundation-gate.yml` — Gate 0.1
- `.github/workflows/network-capture-gate.yml` — Gate 0.2

Gate 0.2 creates a synthetic capture, checks JSONL and summary output, and verifies that sensitive/unapproved fields do not leak into the metadata log.

See `docs/GATE-0.2.md` for the manual Chrome runtime check after CI passes.

## Next stage

Only after Gate 0.2 passes do we implement **v0.3 Response Body Capture**.
