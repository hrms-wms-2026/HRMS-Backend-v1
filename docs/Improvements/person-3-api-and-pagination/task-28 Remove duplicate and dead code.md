# Task 28: Remove duplicate and dead code

| | |
|---|---|
| Priority | 28 of 30 (lower number = build first) |
| Owner folder | person-3-api-and-pagination |
| Size | 1 person, 1 day |
| Area | Code quality |
| Depends on | nothing |

## What we found

The dev-env cleanup scan of 2026-11-13 reports:

- 649 duplicate blocks, 7,346 lines. The largest:
  - `TeamsAttendanceSyncJob` and `ZoomAttendanceSyncJob` share three blocks of 54, 40 and 39 lines.
  - `InboxEscalationSweepJob`, `ProjectMonitorJob` and `AttendanceAlertMaterializerJob` share the same 33-line loop.
  - `GetProjectBannerQueryHandler` and `GetProjectLogoQueryHandler` share 29 lines.
  - Three monitoring ingest handlers share 30 lines.
  - `ClockOutCommandHandler` and `EndBreakCommandHandler` share 25 lines.
- 239 unused-code warnings: 224 unused `using` lines, 5 private fields never read, 2 unused private members, 6 unused parameters.

## What to do

1. Create a `RecurringJobBase` hosted service (delay loop, scope creation, lock, error handling, logging) and move the three sweep jobs and both attendance sync jobs to it. This also helps task 13.
2. Merge the Teams and Zoom attendance sync into one job with a provider interface.
3. Merge the project logo and banner handlers into one shared asset reader.
4. Extract the shared monitoring ingest steps into one service.
5. Remove the confirmed unused members. Before removing a field in auth, token, tenant or permission code, read why it exists. The unused `MaximumFailedAttempts` in `EfLoginWorkspaceSelectionChallengeRepository` may mean a missing lockout check, not dead code. Report it.
6. Remove unused `using` lines with `dotnet format`, one project per commit.
7. Only merge copies that must change together. Similar code in unrelated features stays.

## Done when

- The scan shows at least 30 percent fewer duplicate lines.
- No behaviour changed and all three test suites pass.
- The `MaximumFailedAttempts` question has an answer.
