# Foundation architecture

```text
Google Chrome
  └─ TrueWebsiteCloner Bridge (Manifest V3)
       └─ chrome.runtime.sendNativeMessage()
            └─ TrueWebsiteCloner.NativeHost.exe
                 └─ TCP 127.0.0.1 + ephemeral port + session token
                      └─ TrueWebsiteCloner.exe
```

The Native Host is intentionally tiny. Chrome owns its lifetime. It reads one Chrome Native Messaging frame from stdin, forwards the JSON payload to the Desktop bridge, receives one reply, and writes one framed JSON reply to stdout.

The Desktop application owns project state and future capture/build engines. Browser-specific code stays in the extension.

## Why this split

- Chrome extension: browser permissions and DevTools/CDP integration.
- Native Host: secure Chrome-to-Windows bridge.
- Desktop: filesystem, projects, UI, local services and test gates.
- Test Lab: deterministic site under our control for every feature test.
