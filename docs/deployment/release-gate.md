# Release gate

Task 29 (`docs/Improvements/person-4-deployment-and-architecture/task-29 Load test and release gate.md`).
What a build must show before it goes to UAT users, how it is measured, and where it stands today.

## The gate

Measured with `ops/loadtest/journeys.js` (k6, [README](../../ops/loadtest/README.md)) against the target
environment, signed in with test accounts, one account per virtual user.

| Check | Profile | Rule |
|---|---|---|
| Journeys answer within the team target | `normal` (expected concurrent users) | p95 <= 200 ms for every journey: dashboard, task board, employee list, attendance, leave, inbox |
| No errors at peak | `peak` (3 x normal) | < 1 % failed requests |
| No slow decay over time | `soak` (normal for 60 minutes) | < 1 % failed requests, p95 < 400 ms, memory flat after warm-up |
| Every deploy still works | `smoke`, in the deploy pipeline | every request 2xx |

k6 exits non-zero when a rule fails, so the same script is the check for a person and for the pipeline.
"Expected concurrent users" is 20 until UAT usage shows otherwise; change `NORMAL_VUS`.

## Where it stands (local, 2026-11-20)

Local Release build on the development laptop, local SQL Server, 25 seeded test accounts. Details and the raw
numbers: [2026-11-20 load test](../reports/performance/2026-11-20-load-test-local.md).

| Check | Result |
|---|---|
| Normal (20 users): journeys | **Fails** for dashboard and inbox. Task board, employee list, attendance and leave pass (p95 79 to 130 ms) |
| Peak (60 users): errors | **Passes**, 0 failed requests of 14,150 |
| Soak (20 users, 20 minutes locally) | Errors **pass** (0 of 20,018), memory flat (545 MB average in the first and last quarter). p95 < 400 ms **fails**, again only because of the two inbox endpoints |
| Smoke | **Passes** locally; in the pipeline once the UAT settings below exist |

The two failing journeys fail on the same two endpoints: `GET /inbox/counts` (p95 2.2 s at normal load) and
`GET /inbox/chats` (2.4 s). They are tasks 00a and 00b, which are built on their own branches but not merged.
The dashboard journey fails only because it also calls `/inbox/counts`. **The gate is not met until 00a and 00b
are merged and the normal run is repeated.**

**How many users one instance handles.** On the laptop, one instance served 20 concurrent active users with every
non-inbox journey under 200 ms, and 60 users with no errors, but at 60 users the dashboard bootstrap, leave
balances and project endpoints reached 300 to 430 ms. CPU stayed under 2.5 of 12 cores and memory under 700 MB,
so the first limit is the per-request cost of the slow endpoints, not CPU or memory. UAT numbers will differ (a
smaller App Service plan, a remote database); repeat the runs there.

## Running it against UAT

Only with the server owner's agreement and at an agreed time: the UAT database shares a SQL Server instance with
other systems. Order: smoke, then normal, then peak, then soak, stopping at the first failure. Steps:
[ops/loadtest/README.md](../../ops/loadtest/README.md#run-against-uat). Save each run's summary JSON and a short
report in `docs/reports/performance/`.

## Smoke test in the deploy pipeline

`deploy-appservice.yml` runs the smoke profile after the new build answers `/health/ready`, when the variable
`LOADTEST_SMOKE` is `true` (settings in the README). A failed smoke fails the run; the new build is already live,
so roll back by redeploying the previous commits ([uat-deploy.md](../runbooks/uat-deploy.md)). The step has not
run on GitHub yet (Actions minutes, see task 09), and single-host sign-in through it is untested until then.

## Making the integration tests blocking (proposal)

Today `ci.yml` runs the unit and architecture tests as the required check. The integration tests run on pull
requests in the non-blocking `integration-routing` job (`continue-on-error: true`, only the suites matching the
changed paths) and in full every night.

1. **Measure first.** Keep two weeks of results from the nightly full run and the routed runs. List every test
   that failed without a code reason.
2. **Fix or quarantine the flaky ones.** A flaky test gets a `Trait("Category", "Quarantine")` and an issue; the
   blocking run excludes that trait. Nothing is deleted.
3. **Block on the stable part.** Remove `continue-on-error` from `integration-routing` and add the job to the
   required checks, filtered to `Category!=Quarantine`. Suites with no failures in the two weeks go first:
   `SharedPlatform` (outbox, leases, job runner, seeding lock), `SharedState`, `Auth`, `Tenancy`.
4. **Grow it.** Each further suite joins when it has been green for two weeks. The nightly full run stays as the
   safety net and alerts on failure.
5. **Keep it fast.** The routed job must stay under 15 minutes; if it does not, split it by suite into parallel
   jobs rather than dropping tests.

## Not covered yet

- **Writes.** Clocking in needs a live tray connection (presence enforcement), and submitting a leave request
  changes balances and approvals. Both need test accounts and data reset per run; they are the next step for the
  script. The current journeys are reads.
- **Thread pool and SQL wait statistics** were not captured locally (no `dotnet-counters`); on UAT they come from
  the OpenTelemetry runtime metrics (task 17).
- **Task 06's seeded data set** (realistic volumes, several tenants) does not exist yet; these runs used the dev
  tenant `dapi` only.
