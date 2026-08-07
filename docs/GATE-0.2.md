# Gate 0.2 — Network Capture Core

Scope is intentionally metadata-only. Response bodies, request bodies, cookies and authorization headers are out of scope.

Automated PASS requires:

- Foundation build still passes on Windows.
- Chrome extension Manifest V3 includes `debugger` permission.
- Extension JavaScript passes syntax checks.
- Capture engine creates a session folder and `_network/network.jsonl`.
- Synthetic request, response and loading-finished events produce exactly three records.
- The engine whitelist rejects `Authorization`, `Cookie`, `Set-Cookie`, `postData` and secret values.
- Stop creates `_network/summary.json` with the final event count.

Manual Windows runtime check after CI PASS:

1. Start TrueWebsiteCloner.exe.
2. Start Test Lab and open `http://127.0.0.1:7843` in Chrome.
3. Reload the unpacked extension after updating to v0.2.
4. Click **Start + Reload**.
5. Click **Test local API** on Test Lab.
6. Click **Stop Capture**.
7. Confirm a new project capture folder contains `_network/session.json`, `network.jsonl` and `summary.json`.

Do not implement response-body capture until Gate 0.2 is PASS.
