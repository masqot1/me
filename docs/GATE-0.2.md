# Gate 0.2 — Network Capture Core

Scope is intentionally metadata-only. Response bodies, request bodies, cookies and authorization headers are out of scope.

## Automated CI PASS

- Foundation build passes on Windows.
- Chrome extension Manifest V3 includes `debugger` permission.
- Extension JavaScript passes syntax checks.
- Capture engine creates a session folder and `_network/network.jsonl`.
- Synthetic request, response and loading-finished events produce the expected records.
- The engine whitelist rejects `Authorization`, `Cookie`, `Set-Cookie`, `postData` and secret values.
- Stop creates `_network/summary.json`.

## Gate 0.2B — real Chrome runtime

On Windows run:

```text
03_RUNTIME_GATE_0_2B.bat
```

The harness builds and registers the Native Host, starts the Desktop app and Test Lab, launches an isolated Chrome profile with the unpacked extension, opens the Test Lab plus the extension runtime-gate page, performs a real `chrome.debugger` Network capture, then validates the output.

PASS requires:

- Chrome extension attaches to the Test Lab HTTP tab.
- Test Lab reload traffic is captured.
- `/api/sample` is captured.
- At least six metadata events are written.
- `_network/session.json`, `network.jsonl`, and `summary.json` exist.
- sensitive/unapproved fields are absent.

The report is written to:

`runtime-gate-output/0.2B/runtime-gate-0.2B-report.json`

Do not implement response-body capture until Gate 0.2B is PASS on the target Windows machine.
