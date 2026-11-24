# Task 14: query-in-a-loop fixes, 2026-11-24

Improvements task 14 (`docs/Improvements/person-2-performance-and-db/task-14 Remove query-in-a-loop patterns.md`). Branch `perf/task-14-query-in-a-loop`, stacked on task 06 Part A (`perf/task-06-request-timing`, PR #245), which adds the SQL command counter this report uses.

Decisions: DEC-099 (approver resolver is batched too), DEC-100 (four new read-only batch repository methods), DEC-101 (Calendar edits stay minimal).

## Result

The four read paths now run a fixed number of SQL commands, however many items they return. Each path was measured twice: once with 1 item and once with 6.

| Path | Before (1 item) | Before (6 items) | After (1 item) | After (6 items) |
|---|---|---|---|---|
| `GET /calendar` (`GetCalendarEventsQueryHandler`) | 14 | 59 | 8 | 8 |
| `GET /employees/{id}/upcoming` (`GetEmployeeUpcomingQueryHandler`) | 11 | 31 | 8 | 8 |
| `TaskAccessResolver.ResolveViewableTasksAsync` (meeting action items) | 6 | 26 | 6 | 6 |
| `TaskAccessResolver.FilterViewableTasksAsync` (project, objective, sprint and subtask task lists) | 2 | 7 | 2 | 2 |
| `EmployeeAuthorityResolver.ResolveApproverAsync` (approval routing) | 13 | 48 | 13 | 13 |

What "item" means in each row:

- Calendar and upcoming: one item is one one-off event (organizer plus 2 other participants) and one weekly series (1 other participant). The caller takes part in both, and every item has a different organizer. The window is 14 days.
- Task access: one item is one project, with one public task and one assignees-only task assigned to the caller.
- Approver: one item is one backup coverage owner without the permission, plus one manager in the reporting chain. Only the top manager holds the permission, so the resolver walks every record and every ancestor before it finds an approver.

The approver path's usual case does not get faster. When the first coverage owner or the first manager can approve, the old code already stopped there, and the new code runs the same number of queries (10 or 13). The gain is in deep chains and long backup lists (DEC-099).

## How it was measured

- Test: `tests/ONEVO.Tests.Integration/Performance/QueryInLoopBudgetIntegrationTests.cs`.
- Database: real SQL Server (Testcontainers, the shared migrated template database), with tenant RLS active through `TenantRlsInterceptor`.
- Counter: the task 06 `SqlCommandMetricsInterceptor` with `RequestSqlMetrics`. This is the same counter that writes the `N SQL` part of the request log line.
- The "before" numbers come from running the same test file on the branch before the fix (task 06 base). The "after" numbers come from the same test after the fix. The test asserts that the 6-item count equals the 1-item count, so it now guards against regressions.
- Deviation from the task brief: the counts were taken in the integration harness, not from the local backend against `OnevoDb`. The running local API (port 7229) is the developer's own Release instance from the main clone and does not include task 06. The harness gives repeatable, seeded item counts on the same SQL Server engine.

## What changed

| File | Change |
|---|---|
| `Calendar/Queries/GetCalendarEvents/GetCalendarEventsQueryHandler.cs` | Participants and guests of real rows and series are loaded in one call each. Series children: one `GetChildrenForMastersAsync` call. Participant names: one `ListByIdsAsync` call. Organizer names: one `ListDefaultForUsersAsync` call. Previously each participant, each series and each organizer cost its own queries. |
| `CoreHr/Employee/Queries/GetEmployeeUpcoming/GetEmployeeUpcomingQueryHandler.cs` | Series children: one `GetChildrenForMastersAsync` call. Participant names: one `ListByIdsAsync` call. |
| `WorkManagement/Tasks/Services/TaskAccessResolver.cs` | Projects: one `ListByIdsAsync` call. Memberships: one `GetActiveObjectiveIdsForEmployeeInProjectsAsync` call. Module trees: one `LoadTreesAsync` call. Previously each of these ran once per project. |
| `CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs` | Coverage tier: owner positions, holders, active check, employee rows and permission grants are loaded once per tier, then walked in memory. Reporting-line tier: ancestors, permission grants and primary assignments are loaded once. It reuses the batch methods that `ResolveApprovalInboxScopeAsync` already used. |
| `ICalendarEventRepository` / `EfCalendarEventRepository` | New `GetChildrenForMastersAsync` (no-tracking). |
| `IEmployeeRepository` / CoreHr `EfEmployeeRepository` | New `ListDefaultForUsersAsync`, at most 2 queries. It uses the same rule as `GetDefaultForUserAsync`: a user's only Employee row, or, when there are several, the row with the most recent active primary-employment assignment. |
| `IObjectiveRepository` / `EfObjectiveRepository` | New `GetAllByProjectIdsAsync`. |
| `IProjectMemberRepository` / `EfProjectMemberRepository` | New `GetActiveObjectiveIdsForEmployeeInProjectsAsync`, at most 2 queries. It expands each project's memberships to active descendants with the same `ObjectiveTreeExpander`. |
| `IWorkHierarchyService` / `WorkHierarchyService` | New `LoadTreesAsync` over `GetAllByProjectIdsAsync`. |

Behaviour is unchanged. The new methods have the same filters as the methods they replace: tenant id in every `WHERE`, the same active and status conditions, and no-tracking reads. RLS still applies because the queries go through the same `ApplicationDbContext`. No migration.

### Overlap with CL-01 (Calendar cleanup)

The two calendar read handlers are in Dabi's Calendar cleanup area. Only their loops were changed (DEC-101). The response shape, the conflict flags, the recurrence expansion and the "Unknown" participant fallback are unchanged. One reorder: `GetRecurringMastersForCallerAsync` now runs before the participant lookup, so master and real-row participants can be fetched together. It reads nothing the earlier steps write.

## Tests

- New integration test `QueryInLoopBudgetIntegrationTests`: 4 tests covering the 5 paths, on SQL Server.
- New unit tests:
  - `GetCalendarEventsQueryHandlerTests.Handle_UsesOneBatchedCallPerLookup_HoweverManyEventsAndParticipants`
  - `GetEmployeeUpcomingQueryHandlerTests.Handle_UsesOneBatchedCallForChildrenAndNames_HoweverManySeriesAndParticipants`
  - `TaskAccessResolverTests.ResolveViewableTasksAsync_UsesOneBatchedCallPerLookup_HoweverManyProjects`
  - `TaskAccessResolverTests.FilterViewableTasksAsync_LooksUpEveryPrivateTasksProjectInOneCall`
- Existing unit tests that mocked the old per-item calls now mock the batch calls instead. Two hand-written `IEmployeeRepository` fakes gained the new method.
- Results:
  - Unit: 8481 passed.
  - Architecture: 841 passed.
  - Integration, touched areas only (Meetings, MyTeam, Calendar, employee list, hierarchy closure, the new budget tests): see the "Integration run" line at the end of this report.
  - The full integration suite was not run.

## Write-path and other loops (not changed)

These were in the task's text-search list. Write paths are lower priority, and none was measured as slow, so they were left alone.

| File | What loops | Note |
|---|---|---|
| `Calendar/Services/CalendarNotificationSender.cs` | `employees.GetByIdAsync` per recipient, in each notify method | Runs on create/update/cancel of an event, once per participant. A good next candidate: `ListByIdsAsync` would make it one query. |
| `WorkManagement/Sprints/Services/SprintWriteService.cs` | per-task status lookup and tracked task fetch, per-task activity log add | Sprint close/move. The adds are tracked inserts that go out in one `SaveChanges`. The status lookups could be batched. |
| `WorkManagement/Meetings/Services/MeetingSeriesSplitter.cs` | per-occurrence tracked task fetch, dependents check, materialize | Series split (rare). It writes per occurrence, so batching needs care. |
| `WorkManagement/Sprints/Services/SprintTaskAssignmentService.cs` | tracked task fetch per id | Add/remove tasks to a sprint. Small lists. |
| `WorkManagement/Tasks/Services/TaskAssetLinker.cs` | per-file copy, link lookup and storage record fetch | File copy dominates. This is not a DB problem. |
| `Leave/Policy/EfLeavePolicyRepository.cs` | `foreach` over the result of one query each | Not N+1: four set-based queries whose results are iterated. |
| `Inbox/Services/InboxRouter.cs` | one call per inbox source provider | Fixed number of providers. The inbox cost is covered by other tasks. |
| `Inbox/Chats/InboxChatRequests.cs`, `InboxChatProjector.cs` | per-recipient membership check, per-conversation load, per-case card list | Chat writes and projection. Worth a measured look together with the inbox tasks. |
| `Leave/Inbox/LeaveInboxSourceProvider.cs` | `foreach` over single query results | Mostly not N+1. Its approver loop runs over a permission user list from one query. |

## Integration run

Touched-area integration run (Meetings, MyTeam, Calendar, employee list, hierarchy closure, the new budget tests): running when this report was committed; the result is added in a follow-up commit and in the PR.
