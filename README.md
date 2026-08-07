# TrueWebsiteCloner — Foundation 0.1

This is a clean restart of the project. No Network capture or cloning exists in this build yet.

## Gate 0.1 goal

Prove this chain on Windows before adding capture features:

`TrueWebsiteCloner.exe ↔ loopback bridge ↔ Native Messaging Host ↔ Chrome Extension ↔ Google Chrome`

The extension has a pinned development ID:

`ggcmdgdiopplpbcfinamhjdkbhiknfbk`

## Requirements

- Windows 10/11 x64
- Google Chrome
- .NET 10 SDK

.NET 10 is used because it is the current LTS baseline for this clean implementation.

## Install and test

Open PowerShell in the project root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\Install-Dev.ps1
```

Then:

1. Open `chrome://extensions`.
2. Enable **Developer mode**.
3. Choose **Load unpacked** and select `chrome-extension`.
4. Run `artifacts\desktop\TrueWebsiteCloner.exe`.
5. Open the extension popup.
6. Click **Test Desktop Connection**.
7. The popup must show `PASS` and the Desktop UI must show the extension as `CONNECTED`.
8. Click **Run foundation check** in the Desktop app.

Static checks can also be run with:

```powershell
.\tests\Test-Foundation.ps1
```

## Test Lab

After `Install-Dev.ps1`, start the local test site from the Desktop UI or:

```powershell
.\scripts\Run-TestLab.ps1
```

Then open:

`http://127.0.0.1:7843`

This test site is owned by the project and will be expanded as each capture feature is added.

## Security decisions already in Foundation

- Desktop bridge listens on `127.0.0.1` only.
- A random 256-bit token is regenerated every Desktop run.
- The Native Host manifest permits only the pinned extension ID.
- Native Messaging messages are length-prefixed JSON, matching Chrome's protocol model.
- No captured credentials, cookies, page data, or browsing content exists in Foundation 0.1.

## Next gate

Only after Gate 0.1 is PASS do we implement **0.2 Network Capture Core**:

- attach/detach to an explicitly selected tab,
- capture request/response metadata,
- save to a chosen project workspace,
- build a deterministic test case in Test Lab,
- require a PASS report before 0.3 response-body capture.
