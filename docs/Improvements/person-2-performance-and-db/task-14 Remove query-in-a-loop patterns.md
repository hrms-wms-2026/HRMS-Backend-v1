# Task 14: Remove query-in-a-loop patterns

| | |
|---|---|
| Priority | 14 of 30 (lower number = build first) |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | Performance, DB |
| Depends on | 06 |

## What we found

These files call a repository or the database inside a `foreach`. Each one is a likely N+1 pattern, where a list of N items causes N extra queries:

- `Calendar/Services/CalendarNotificationSender.cs` (7 places)
- `WorkManagement/Sprints/Services/SprintWriteService.cs` (6)
- `Leave/Policy/EfLeavePolicyRepository.cs` (4)
- `WorkManagement/Meetings/Services/MeetingSeriesSplitter.cs` (4)
- `CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs` (4)
- `CoreHr/Employee/Queries/GetEmployeeUpcoming/GetEmployeeUpcomingQueryHandler.cs` (4)
- `Calendar/Queries/GetCalendarEvents/GetCalendarEventsQueryHandler.cs` (4)
- `WorkManagement/Tasks/Services/TaskAssetLinker.cs`, `TaskAccessResolver.cs`, `SprintTaskAssignmentService.cs`
- `Inbox/Chats/InboxChatRequests.cs`, `InboxChatProjector.cs`, `Inbox/Services/InboxRouter.cs`
- `Leave/Inbox/LeaveInboxSourceProvider.cs`

The list came from a text search, so some entries will be harmless (a loop over two items). Read each one before changing it.

## What to do

1. Start with the read paths that users hit often: calendar events, employee upcoming, employee authority resolver, task access resolver.
2. Replace the loop with one query that takes the whole id list (`WHERE id IN (...)`) and a dictionary lookup in memory.
3. Use the SQL command counter from task 06 to prove the drop.
4. Write-path loops (sprint write, meeting splitter) are lower priority. Fix them only if the baseline shows them as slow.

## Done when

- The four read paths above run a fixed number of queries no matter how many items are returned.
- Before and after query counts are in the pull request.
- Existing tests pass.
