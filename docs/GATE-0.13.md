# Gate 0.13 — Project Diagnostics Dashboard

V0.13 normalizes the evidence produced by earlier gates into one deterministic health model per project.

## Status model

Every check is one of `PASS`, `WARNING`, `FAIL`, or `NOT_RUN`. Each check contains a stable code, a human-readable message, a **relative** evidence path and a recommended next action.

The overall project status is `FAIL` if any check fails, otherwise `WARNING` if any warning exists, otherwise `PASS`. Readiness is `NOT_READY`, `NEEDS_REVIEW`, or `READY` respectively.

## Evidence normalized

- capture session and summary;
- response-body count;
- offline build;
- missing-resource report;
- recovery report;
- completeness/dependency graph;
- source-vs-replay verification;
- visual comparison threshold;
- immutable snapshot history;
- portable import integrity when applicable.

Output: `_diagnostics/project-health.json`.

The report deliberately excludes absolute project/workspace paths and timestamps so repeated diagnostics over unchanged evidence are byte-identical.
