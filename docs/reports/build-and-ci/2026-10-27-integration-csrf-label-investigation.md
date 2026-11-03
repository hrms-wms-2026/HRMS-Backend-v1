# Integration tests: the "CSRF issue" investigation (2026-10-27)

## Question

`ci.yml` and several reports said the integration suite had a "pre-existing CSRF-handling issue", and both integration jobs were made non-blocking (`continue-on-error: true`) because of it. We checked whether there is a CSRF bug to fix.

## Answer

**There is no CSRF bug in the integration suite.** The label was attached to a run of red builds in August/September without a root cause. None of the failures in those runs mention CSRF; they were ordinary test regressions that have since been fixed.

## Evidence

**Local run**, branch `chore/uat-hardening` (= `development` at `1ee80bfc` + team-attendance work), Testcontainers on Docker 29.4:

```
pwsh ../dev-env/scripts/verify.ps1 backend -Full
integration  PASS  2072 s  passed 560, failed 0, skipped 1
```

**CI history** of the integration job (`full-integration` / `integration-tests`), sampled across 148 push and nightly runs:

| Run date | Integration result | Failing tests (from `gh run view --log-failed`) |
|---|---|---|
| 2026-08-19 | failure | — (start of the red streak, PR #75) |
| 2026-08-25 | failure | — |
| 2026-09-03 | failure | Leave balance/entitlement/request/calendar tests, `CreateProjectEndpointTests` (objective membership, projects read permission) |
| 2026-09-11 | success | |
| 2026-09-21 | failure | `LeaveCalendarIntegrationTests`, `CreateProjectEndpointTests.EditObjective_*` (creator bypass removed when Objective edits started always requiring approval) |
| 2026-09-24 → 2026-10-06 | success | |
| 2026-10-13 | failure | `MyTeam.LedWorkProgressIntegrationTests`, `MyTeamCapabilitiesIntegrationTests` (fixed on `fix/my-team-integration-tests`) |
| 2026-10-18, 10-23 (nightly) | success | |
| 2026-10-27 (development push) | success | |

No failure message in these runs contains "CSRF". Each integration test class logs in over real HTTP and parses the session and CSRF cookies itself (`admin_session`/`admin_csrf`, `onevo_session`/`onevo_csrf`), and that flow passes in every class today.

## What was changed

- `ci.yml`: the comment now records that the CSRF label was unconfirmed and points here. **The jobs are still non-blocking**; that is a team decision (below).
- Architecture document §6.1 updated to match.
- Unrelated flaky unit test found while checking the same CI history: `WidgetDataOrchestratorTests.Never_more_than_4_concurrent` failed the `build-and-test` gate on the 2026-10-27 `development` push. Its 60 ms providers could be starved past the orchestrator's 3 s default deadline on a busy runner and become timeout errors. The test now passes an explicit 30 s deadline because it tests the concurrency cap, not the timeout.

## Recommendation

Make `full-integration` (push/nightly) blocking again once it has stayed green for about two weeks, then consider `integration-routing` on PRs. Until then, a red integration job is a real regression to read, not a known CSRF gap to ignore.
