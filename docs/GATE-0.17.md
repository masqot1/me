# Gate 0.17 — End-to-End Verified Release Smoke

V0.17 adds a one-command Windows release workflow using the tools that already passed Gates 0.14–0.16.

```text
04_RELEASE_PROJECT.bat <project-folder> <output.twcrelease>
```

The workflow executes:

1. V0.14 release-readiness validation;
2. create or verify the immutable V0.15 release seal;
3. verify the seal;
4. create the deterministic V0.16 `.twcrelease` bundle;
5. verify the complete bundle chain.

Gate 0.17 independently creates a deterministic READY fixture and proves the complete local release lifecycle: READY → seal → bundle → verify → import → final seal verification → workspace catalog → portable re-export.

No capture or external-site access is required by this final release smoke gate.
