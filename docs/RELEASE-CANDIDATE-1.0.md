# TrueWebsiteCloner V1.0-RC1

V1.0-RC1 is the master release-candidate verification layer after Gates 0.1–0.17.

The master gate rebuilds all published Windows components, runs the foundation static gate, validates the Manifest V3 Chrome extension and all extension JavaScript, dynamically discovers every `tests/**/*GateTests.csproj`, and executes every discovered gate project in Release configuration.

Output: `release-candidate-output/release-candidate-report.json`.

A V1.0-RC1 PASS means the current commit passes the complete locally executable core gate suite. Browser-specific real-Chrome gates remain separately visible in GitHub Actions and continue to run on every push to `main`.

Local command:

```text
05_VERIFY_RELEASE_CANDIDATE.bat
```
