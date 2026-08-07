# Gate 0.5 — Local Runtime / Recorded GET Replay

V0.5 runs the offline build on a loopback-only local server so captured pages can execute same-origin GET/fetch requests against recorded responses without contacting the original site.

## Security and scope

- Bind only to `127.0.0.1`.
- Serve only files already present in the V0.4 offline build.
- Replay recorded `GET` and `HEAD` paths only.
- Never proxy a replay miss to the original site or any other network destination.
- Unknown paths return `404 replay_miss`.
- POST/PUT/PATCH/DELETE and other methods return `405 method_not_replayed`.
- Cookies are not replayed.
- Authorization headers are not replayed.
- Request bodies are not replayed.

## Runtime

```text
TrueWebsiteCloner.LocalRuntime.exe --capture <capture-folder> --port 7850
```

Health endpoint:

`http://127.0.0.1:7850/__twc/health`

## PASS criteria

Gate 0.5 builds a deterministic V0.4 fixture, launches the real Windows Local Runtime, serves HTML/CSS/JS, replays the recorded `/api/sample` JSON response, verifies replay marker headers, verifies an unknown route returns 404, and verifies POST is rejected with 405.
