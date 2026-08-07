# Gate 0.3 — Same-Origin Response Body Capture

V0.3 adds response-body capture without expanding into unrestricted browsing-data collection.

## Policy

- The user explicitly starts capture on one HTTP/HTTPS tab.
- Only `GET` response bodies are eligible.
- Only responses from the selected tab's **same origin** are saved in V0.3.
- Maximum decoded body size is **512 KiB per response**.
- Request bodies are not saved.
- Cookies and Authorization headers are not saved.
- Body content is written to `_bodies/` and is never embedded into `_network/network.jsonl`.
- Enabled types include HTML, CSS, JavaScript, JSON, text, SVG, PNG, JPEG, WebP and GIF.

## Output

```text
capture-.../
├── _network/
│   ├── session.json
│   ├── network.jsonl
│   └── summary.json
└── _bodies/
    ├── bodies.jsonl
    ├── 0001-....html
    ├── 0002-....css
    └── ...
```

## Automated PASS

Gate 0.3 requires both a core test and a real Chrome runtime test on Windows GitHub Actions. The core test verifies text and base64 bodies, rejects cross-origin and oversized bodies, and proves body content is not copied to the network metadata log. The real Chrome test captures Test Lab HTML/CSS/JS/JSON and verifies their contents on disk.

Do not begin offline path rewriting/building until Gate 0.3 is green.
