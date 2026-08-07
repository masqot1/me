# TrueWebsiteCloner

Current development stage: **v0.13 Project Diagnostics Dashboard**.

## Passed gates

Gates 0.1 through 0.12 have passed on GitHub Actions. The Windows app includes a bounded project catalog/workspace in addition to capture, response bodies, offline building, local replay, verification, recovery, dependency scoring, visual comparison, immutable snapshots and portable project integrity.

## v0.13 scope

V0.13 adds one machine-readable health model per project at `_diagnostics/project-health.json`. It normalizes evidence from all previous stages into `PASS`, `WARNING`, `FAIL` and `NOT_RUN` checks, each with a relative evidence path and a recommended next action.

The overall readiness becomes `READY`, `NEEDS_REVIEW` or `NOT_READY`. Reports contain no absolute project paths or timestamps and are deterministic for unchanged evidence.

The Desktop workspace provides **Run Diagnostics** for the selected project and displays its latest Health/Readiness plus the highest-priority next action. Diagnostics only read local project evidence; they do not restart capture or contact any external site.

See `docs/GATE-0.13.md` for the health model.

## Next stage

After Gate 0.13 passes, the next stage is release-readiness and project validation orchestration: run the applicable local project checks in dependency order and produce one final readiness manifest without silently re-running capture or contacting external sites.
