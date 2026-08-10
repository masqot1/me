# Gate 1.2 — XHR / Fetch Request Payload Capture

Gate 1.2 extends the V1.1 capture model with bounded same-origin request payloads for XHR and Fetch traffic while keeping authentication material out of persisted artifacts by default.

## Capture scope

- Resource types: `XHR` and `Fetch` only.
- Methods: `POST`, `PUT`, `PATCH` and `DELETE` only.
- Origin: same-origin with the captured page only.
- Maximum raw payload size: 64 KiB.
- Persisted content types:
  - `application/json`
  - `application/*+json`
  - `application/x-www-form-urlencoded`
- Unsupported, unavailable or oversized payloads fall back to metadata-only events and do not persist body content.

## Storage model

Captured request payloads are written under `_requests/` rather than embedding raw body content in `_network/network.jsonl`.

- `_requests/<sequence>-<requestId>.json` — sanitized JSON payload.
- `_requests/<sequence>-<requestId>.form` — sanitized form payload.
- `_requests/request-payloads.jsonl` — payload manifest containing method, URL, type, content type, sizes, redaction count and file reference.
- `_network/network.jsonl` — metadata and payload file references only; request body content is excluded.

## Sensitive-field redaction

Before a supported payload is persisted, Core parses it and replaces values of sensitive keys with `[REDACTED]`. The matching policy covers password/passcode fields, token families, API keys, client secrets, authorization/auth fields, session identifiers and cookies, including nested JSON objects and arrays.

The Chrome extension does not send raw HTTP headers to Core for Gate 1.2. Core also ignores unknown injected fields, so attempted `Authorization`, `Cookie` or arbitrary header objects are not persisted through this event type.

Malformed JSON is rejected instead of being stored raw.

## Defense in depth

The extension filters same-origin, resource type, method, content type and size before sending a capture event. Core independently re-validates origin, resource type, method, content type, byte length and payload format before writing any artifact.

## CI bridge model

Real-Chrome validation uses `TrueWebsiteCloner.BridgeHarness`, a headless executable that instantiates the production `BridgeServer` without depending on WPF `MainWindow.Loaded`. The native host therefore talks to the same bridge implementation used by Desktop, but CI does not depend on a graphical window becoming ready.

## Validation

`TrueWebsiteCloner.RequestPayloadGateTests` verifies:

- same-origin Fetch JSON capture;
- same-origin XHR form capture;
- nested sensitive-field redaction;
- unsupported/oversized metadata-only fallback;
- cross-origin, method and resource-type rejection;
- malformed JSON, oversize and declared-length rejection;
- exclusion of raw request payloads and injected authentication/header values from `network.jsonl`;
- request manifest and summary accounting.

The GitHub Actions Gate also runs a real Chrome capture against Test Lab and proves the complete path:

`Chrome CDP → extension → native host → BridgeServer → CaptureSessionManager → _requests/`

The runtime fixture submits a JSON POST containing password/token sentinels. The gate requires the request payload to be persisted, the sentinels to be absent from the capture, and `[REDACTED]` values to be present before it passes.

GitHub Actions workflow: `.github/workflows/request-payload-gate.yml`.
