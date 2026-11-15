# Task 29: Load test and release gate

| | |
|---|---|
| Priority | 29 of 30 (lower number = build first) |
| Owner folder | person-4-deployment-and-architecture |
| Size | 1 person, 1 day |
| Area | Performance, Deployment |
| Depends on | 06, 17 |

## What we found

There is no load test in the repository. The 200 ms target and the question "how many users can one instance serve" have no measured answer. CI runs integration tests as non-blocking (`continue-on-error: true`), so a broken integration test does not stop a merge.

## What to do

1. Write a k6 (or similar) script for the main user journeys: sign in, load dashboard, open task board, open an employee list, clock in, submit a leave request, open inbox.
2. Use the seeded data set from task 06 and several tenants at once.
3. Run three profiles against UAT: normal (expected users), peak (three times normal) and soak (normal for one hour).
4. Record p95 per endpoint, error rate, CPU, memory, SQL connections and thread pool. Find the first limit that is hit.
5. Set the release gate: p95 at or under 200 ms for the journey endpoints at normal load and under 1 percent errors at peak. Save it in `docs/deployment/release-gate.md`.
6. Add a short smoke version of the script to the deploy pipeline so it runs after every UAT deploy.
7. Propose a plan to make the integration job blocking, starting with the stable part of the suite.

## Done when

- The three runs are saved in `docs/reports/performance/` with graphs or tables.
- The team knows how many users one instance handles.
- The smoke run is part of the UAT deploy.
