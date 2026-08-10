# Gate 1.1 — WebSocket Capture

Gate 1.1 begins the TrueWebsiteCloner V1.1 capture expansion without changing the published V1.0 release baseline.

## Scope

- Listen to Chrome DevTools Protocol WebSocket lifecycle events while a normal capture session is active.
- Record same-origin WebSocket creation, handshake result, sent/received frames, errors and close events.
- Map page origins safely: `http` → `ws` and `https` → `wss`, with the same host and effective port.
- Capture an individual WebSocket frame payload only when its UTF-8 size is at most 64 KiB.
- For a frame above the limit, keep bounded metadata only and do not persist `payloadData`.

## Sensitive-data defaults

Gate 1.1 intentionally does not persist WebSocket handshake headers, cookies or Authorization headers. Unknown fields are dropped by the Core allow-list even if a caller attempts to inject them.

## Rejection rules

- Cross-origin WebSocket events are rejected by the Core even if they bypass extension-side filtering.
- A claimed captured frame without `payloadData` is rejected.
- A captured payload above 64 KiB is rejected.
- A declared `byteLength` that does not match the UTF-8 payload length is rejected.

## Validation

`TrueWebsiteCloner.WebSocketGateTests` verifies accepted lifecycle events, bounded frame capture, oversized-frame behavior, cross-origin rejection, sensitive-field stripping and summary accounting.

GitHub Actions workflow: `.github/workflows/websocket-capture-gate.yml`.
