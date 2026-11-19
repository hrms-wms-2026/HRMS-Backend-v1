# Task 26: Remove blocking calls and tune EF for hot paths

| | |
|---|---|
| Priority | 26 of 30 (lower number = build first) |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | Performance, DB |
| Depends on | 02 |

## What we found

- `DomainEventDispatchInterceptor` and `MeetingIntegrityInterceptor` call `.GetAwaiter().GetResult()` in their synchronous paths. Under load this blocks thread-pool threads and can cause sudden latency spikes.
- Two repositories use `.ContinueWith(t => t.Result)` to change a return type (`EfOnboardingPersistenceRepositories.cs:381`, `EfOffboardingTaskBypassRequestRepository.cs:22`).
- `AddDbContext` is used without pooling, with five interceptors resolved per scope.
- No compiled queries and no split queries are used anywhere.
- The health check opens a raw `SqlConnection` on each call.

## What to do

1. Find every caller that uses synchronous `SaveChanges()` and move it to `SaveChangesAsync`. Then make the synchronous interceptor paths throw, so nobody adds one back.
2. Replace the two `ContinueWith` calls with a normal `await`.
3. Check `CancellationToken` is passed from controller to repository on the 20 slowest endpoints, so a closed browser tab stops the query.
4. Try `AddDbContextPool` on a branch. The scoped interceptors make this hard; write down whether it is possible and what it would save. Do not force it.
5. Use compiled queries for the three hottest lookups (session by key hash, tenant by id, permissions for user).
6. Add `AsSplitQuery` where one query includes two or more collections.

## Done when

- No `.GetResult()` or `.Result` remains on a request path.
- The thread pool shows no starvation in a short load run.
- The pooling decision is written down with numbers.
