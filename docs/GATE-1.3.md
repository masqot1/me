# Gate 1.3 — Safe HTTP Header Capture

Gate 1.3 adds useful HTTP request/response header metadata without persisting raw header blocks or authentication material.

## Capture policy

Headers are captured only for same-origin HTTP(S) requests and responses belonging to the active capture tab.

Request allowlist:

- `accept`
- `accept-language`
- `cache-control`
- `content-type`
- `if-modified-since`
- `if-none-match`
- `pragma`

Response allowlist:

- `accept-ranges`
- `cache-control`
- `content-encoding`
- `content-language`
- `content-length`
- `content-type`
- `etag`
- `last-modified`
- `vary`

Everything else is dropped rather than copied and redacted later. In particular, `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, API-key headers, auth-token headers and CSRF-token headers are never persisted by this gate.

## Bounds

- Maximum accepted headers per event: 16.
- Maximum UTF-8 bytes per value: 2 KiB.
- Maximum aggregate accepted name/value bytes per event: 8 KiB.
- Values containing CR/LF are dropped.
- Duplicate normalized header names are dropped.

The Chrome extension applies these bounds before forwarding an event. The Core `SafeHeaderCaptureManager` independently repeats the allowlist, same-origin and size checks before writing `network.jsonl`.

## Storage

Safe header events are stored as `capture.request.headers` and `capture.response.headers` entries in `_network/network.jsonl`.

Each event contains only normalized allow-listed headers plus bounded metadata such as request id, URL, resource type, method/status, accepted/dropped counts and byte count. Rejected header names and values are not recorded.

`_network/header-policy.json` records the deterministic policy used for the capture.

## Real Chrome validation

The Test Lab runtime POST intentionally sends fake `Authorization` and `X-API-Key` values. Its response intentionally includes fake `Set-Cookie` and `X-API-Key` values while also returning safe headers such as `ETag`, `Content-Language` and `Cache-Control`.

The Gate 1.3 workflow uses the production `BridgeServer` through the headless bridge harness and requires:

- safe request headers to appear;
- safe response headers to appear;
- all sensitive header sentinel values to be absent from every capture artifact;
- sensitive header names to be absent from `network.jsonl`;
- `header-policy.json` to be created.

GitHub Actions workflow: `.github/workflows/safe-http-header-gate.yml`.
