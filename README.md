# TrueWebsiteCloner

**Version: 1.0.0 — Final Gates PASS**

TrueWebsiteCloner is a Windows desktop + Chrome capture/offline-replay workspace built through deterministic PASS gates. The V1.0 baseline includes the WPF desktop application, pinned Manifest V3 Native Messaging bridge, bounded response-body capture, offline builder/runtime, verification, recovery, dependency/completeness analysis, visual comparison, immutable history, portable project integrity, release readiness, verified release seals and deterministic `.twcrelease` bundles.

## V1.0 release validation

The repository's V1.0 Final Aggregate Gate waits for every push-triggered `TrueWebsiteCloner` workflow on the exact final commit and requires all of them to succeed. The V1.0 Release Candidate master sweep also executes every `*GateTests` project, including the release-readiness, release-seal, release-bundle and end-to-end release-operations tests.

## Windows distribution

`.github/workflows/distribution-gate.yml` builds `TrueWebsiteCloner-1.0.0-win-x64.zip`, verifies SHA-256 for every shipped file, rebuilds it a second time and requires byte-identical archive hashes. The package contains installer/run/uninstall wrappers, the Chrome extension and the published V1.0 application/tools. See `docs/V1.0-DISTRIBUTION.md`.

## Core release commands

```text
scripts\Install-Dev.ps1
scripts\Release-Project.ps1 -ProjectRoot <verified-project> -OutputBundle <project.twcrelease>
```

Version marker: `VERSION` = `1.0.0`.
