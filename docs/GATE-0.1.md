# Gate 0.1 — Foundation

A release is **PASS** only when all runtime checks are green on the target Windows machine.

| Check | Required |
|---|---|
| .NET 10 build | PASS |
| Desktop starts | PASS |
| Chrome detected | PASS |
| Native Host registered under HKCU | PASS |
| Extension loads as Manifest V3 | PASS |
| Extension ID equals pinned ID | PASS |
| Extension → Native Host | PASS |
| Native Host → Desktop loopback bridge | PASS |
| Desktop response returns to extension | PASS |
| Test Lab `/health` responds | PASS |

Do not begin Network Capture until this gate is green.
