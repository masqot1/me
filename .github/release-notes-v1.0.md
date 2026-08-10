# TrueWebsiteCloner 1.0.0

First complete Windows release of TrueWebsiteCloner.

## Downloads

Two Windows x64 packages are attached:

- **TrueWebsiteCloner-1.0.0-win-x64.zip** — smaller framework-dependent package; requires .NET 10 Desktop and ASP.NET Core runtimes.
- **TrueWebsiteCloner-1.0.0-win-x64-standalone.zip** — self-contained package; no separately installed .NET runtime is required.

Each ZIP has a matching `.sha256` asset. Both package families are built deterministically and verified by GitHub Actions. The standalone package additionally passes a clean-install smoke test that launches the CLI and WPF Desktop with global `dotnet` hidden, verifies NativeHost → Desktop framing, and validates uninstall cleanup.

## V1.0 validation

The V1.0 Final Aggregate Gate requires all push-triggered TrueWebsiteCloner gates on the exact release commit to succeed. The release baseline covers Chrome/Native Messaging foundation, network metadata and same-origin response-body capture, offline building, local replay, verification, missing-resource recovery, dependency/completeness scoring, visual comparison, immutable snapshots, portable package integrity, diagnostics, release readiness, release seals, deterministic release bundles, end-to-end release operations, Windows distribution integrity, and standalone distribution integrity.

## Install

For the standalone package:

1. Extract the ZIP to a normal local folder.
2. Run `01_INSTALL.bat`.
3. Open `chrome://extensions`, enable Developer mode, and **Load unpacked** the `chrome-extension` folder.
4. Confirm extension ID `ggcmdgdiopplpbcfinamhjdkbhiknfbk`.
5. Run `02_RUN_TRUEWEBSITECLONER.bat`.

Use TrueWebsiteCloner only with websites, applications, and test environments you own or are authorized to capture.
