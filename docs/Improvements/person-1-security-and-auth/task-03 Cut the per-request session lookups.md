# Task 03: Cut the per-request session lookups

| | |
|---|---|
| Priority | 3 of 30 (lower number = build first) |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day |
| Area | Performance, Security |
| Depends on | nothing |

## What we found

`TenantDatabaseTicketStore.RetrieveCoreAsync` runs on every authenticated request. It reads the session, the tenant, the user, the employee legal entity and then resolves all permissions, one after another. The code already carries a `PERF-01` comment that says the same thing. With task 02 not done yet, each of those reads also pays for the tenant context calls.

A dashboard page that fires 15 requests repeats this work 15 times within one second.

## What to do

1. Cache the built ticket data in `IMemoryCache` for a short time (start with 30 seconds), keyed by the session key hash.
2. Remove the cache entry at once on logout, session revoke, password change, role or permission change and legal entity switch. `PermissionVersionService` already knows when permissions change, so use that signal.
3. Always re-check `ExpiresAt` and the absolute lifetime from the cached values, so an expired session is never accepted.
4. Do the same for `AdminDatabaseTicketStore`.
5. Limit how often `RenewAsync` writes `LastActivityAt` (for example once per minute per session).
6. Write tests: revoked session is rejected on the next request, permission removal takes effect on the next request.

## Done when

- Repeat requests inside the cache window do zero session database reads (shown by the Debug timer that already exists in the store).
- The revoke and permission-change tests pass.
- The cache lifetime is a setting, with 0 meaning off.

## Note

This cache is per process. Task 21 moves it to a shared cache when the API runs on more than one instance.
