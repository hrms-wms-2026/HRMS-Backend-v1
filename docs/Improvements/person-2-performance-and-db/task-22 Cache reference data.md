# Task 22: Cache reference data

| | |
|---|---|
| Priority | 22 of 30 (lower number = build first) |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | Performance, DB |
| Depends on | 03, 06 |

## What we found

`IMemoryCache` is used in only 13 places in the whole backend and there is no caching folder content (`Infrastructure/Caching` is empty). Data that almost never changes is read from the database on every request that needs it, for example the permission catalogue, role templates, leave types, work modes, clock-in policies, project categories, task statuses, legal entity settings, the tenant row and the module catalogue.

## What to do

1. From the task 06 baseline, list the queries that run most often and return the same rows each time.
2. Add one small cache service (`ITenantCache`) with a key that always includes the tenant id, so one tenant can never read another tenant's cached data.
3. Cache the top 10 lookups with a lifetime of a few minutes.
4. Remove the entry in the command handler that changes the data. Put the cache keys in one static class so reads and invalidation cannot drift apart.
5. Never cache anything that depends on the caller's permissions unless the key includes the user.
6. Add a setting to switch caching off for debugging.

## Done when

- The 10 chosen lookups show zero database queries on a repeat request.
- A change through the API is visible on the very next read.
- A test proves two tenants never share an entry.

## Note

Uses in-process memory first. Task 21 moves it to the shared cache.
