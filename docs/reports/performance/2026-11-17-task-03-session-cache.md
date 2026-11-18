# Task 03: per-request session lookup cache

Date: 2026-11-17. Branch: `feature/improve-task-03-session-cache`. Spec: `docs/Improvements/person-1-security-and-auth/task-03 Cut the per-request session lookups.md`.

## What changed

Before this change, every authenticated request rebuilt its cookie ticket from the database: session, tenant, user, the employee's legal entity, and then permission resolution. That was about 5 round trips, roughly 70 ms. Now:

- `TenantDatabaseTicketStore` and `AdminDatabaseTicketStore` cache the built ticket data (not the ticket object) for 30 s, keyed by the session key hash. A cache hit does **zero** database reads. Each hit builds a new `AuthenticationTicket` from immutable cached data, because the cookie handler changes the ticket it is given.
- Every cache hit checks `ExpiresAt` and the absolute lifetime against the cached values again. An expired session is rejected and dropped from the cache.
- `RenewAsync` (both stores) writes `ExpiresAt`/`LastActivityAt` at most once per 60 s per session. Older sessions are renewed on almost every request because `IssuedUtc` stays at `StartedAt`, so before this change they also paid for a tenant lookup, a session read and an UPDATE on every request. A throttled renew does no database work at all. The throttle slot is recorded only after the write commits, so a failed write is retried on the next request.
- The renewal keeps the cached expiry in sync (an added requirement, approved by the user). On a renewal write, the store reads the cached entry first. The EF interceptor drops the entry on save, and the store then puts it back with the new `ExpiresAt` and its original build time, so an eviction that raced the renewal still wins. On a throttled renewal (no DB write, but the cookie still slides), the cached `ExpiresAt` slides too. Either way, the `session_expires_at` claim and `ExpiresUtc` follow the renewed value, and the new expiry is always capped at the absolute lifetime. A session past its original expiry but inside its renewed one is accepted from the cache. A session past the absolute lifetime is rejected.
- User deactivation evicts the cache (an added requirement, approved by the user). The tenant store rejects `!user.IsActive` and the admin store rejects a non-active `PlatformUser.Status`. Every path that deactivates, suspends or deletes a user is a tracked EF write, so the interceptor evicts that user's sessions (see the table below).
- The cache sits behind small interfaces so task 21 can swap in a shared cache:
  - `ISessionTicketCache` (Application): `EvictSession`, `EvictUser`, `EvictTenant`, `EvictAll`.
  - `ISessionTicketStoreCache` (Infrastructure): the store side (`TryGet`, `Set`, `BeginBuild`, `DropSession`, `ShouldWriteRenewal`).
  - `SessionTicketCache` is the in-process implementation, built on `IMemoryCache`.

### How eviction stays safe under races

An eviction does more than delete the entry. It also leaves a marker (eviction time + 2 s grace) on the session, the user, the tenant, or on everything. An entry is served only if its build **started** after every marker that applies to it. `Set` refuses to store a build that started before a marker. This has two effects:

- Evicting a user or a tenant does not need to know which session keys belong to them.
- A request that read the old rows just before a revoke committed cannot put them back into the cache. This also covers evictions that happen inside a transaction that has not committed yet. The EF interceptor evicts again on `TransactionCommitted`, and the repositories evict both before and after bulk updates.

## Settings

Section `Auth:SessionTicketCache` (`SessionTicketCacheOptions`):

| Setting | Default | Meaning |
|---|---|---|
| `Auth:SessionTicketCache:LifetimeSeconds` | 30 | How long a built ticket is reused. **0 turns the cache off.** |
| `Auth:SessionTicketCache:InvalidationGraceSeconds` | 2 | How long after an eviction rebuilt tickets are not cached. |
| `Auth:SessionTicketCache:RenewWriteIntervalSeconds` | 60 | Minimum time between renewal writes per session. 0 writes every time. |

No appsettings change is needed: the defaults apply when the section is missing.

## Every eviction point

| Event | Code path | Eviction |
|---|---|---|
| Logout (tenant) | `TenantDatabaseTicketStore.RemoveAsync` | `EvictSession`, before and after the revoke |
| Logout (admin) | `AdminDatabaseTicketStore.RemoveAsync` | `EvictSession`, before and after the revoke |
| Admin revokes one tenant session | `RevokeTenantSessionCommandHandler` → `EfSessionRepository.RevokeByIdAsync` (tracked) | Interceptor: `Session` modified → `EvictSession(key_hash)` |
| Revoke by key hash | `EfSessionRepository.RevokeByKeyHashAsync` (tracked) | Interceptor → `EvictSession` |
| User account: revoke one session | `RevokeUserAccountSessionCommandHandler` → `EfUserAccountRepository.RevokeSessionAsync` (bulk) | Repository → `EvictUser`, before and after |
| User account: sign out everywhere / suspend | `SignOutUserAccountCommandHandler`, `SuspendUserAccountCommandHandler` → `EfUserAccountRepository.RevokeAllAccessAsync` (bulk) | Repository → `EvictUser`, before and after; the suspend also changes the `User` row → interceptor `EvictUser` |
| Password change | `ChangePasswordCommandHandler` → `RevokeAllActiveByUserIdExceptAsync` / `RevokeAllActiveByUserIdAsync` (bulk) | Repository → `EvictUser`; the `User.PasswordHash` change → interceptor `EvictUser` |
| Forced password change | `ForcePasswordChangeCommandHandler` | Bulk revoke → `EvictUser`; `PermissionVersionService.IncrementVersionAsync` → `EvictUser` |
| Password reset | `ResetPasswordCommandHandler` | Bulk revoke → `EvictUser`; `IncrementVersionAsync` → `EvictUser` |
| Offboarding complete | `CompleteOffboardingCommandHandler` → `RevokeAllActiveByUserIdAsync` | Repository → `EvictUser` |
| Role permission change | `AssignRolePermissionsCommandHandler`, `AdminAssignTenantRolePermissionsCommandHandler`, `ApplyRoleTemplateCommandHandler` | `IncrementVersionAsync` per affected user → `EvictUser`; `RolePermission`/`Role` writes → interceptor `EvictTenant` |
| Role assignment to a user | any tracked `UserRole` add/change/delete | Interceptor → `EvictUser` |
| Permission override | any tracked `UserPermissionOverride` write | Interceptor → `EvictUser` |
| **User deactivated** (added requirement) | `SuspendUserAccountCommandHandler` (`IsActive = false`), `CompleteOffboardingCommandHandler` (`IsActive = false`, plus bulk revoke), user soft delete (`Remove` → `SoftDeleteInterceptor`), all tracked | Interceptor: `User` modified/deleted → `EvictUser` |
| **Platform admin deactivated** (added requirement) | `RevokePlatformUserInviteCommandHandler` (`Status = inactive`), MFA state changes (`ResetPlatformUserMfa`, `EnableAdminMfa`, `ConfirmAdminMfaSetup`), all tracked | Interceptor: `PlatformUser` modified → `EvictUser` |
| User edited (email, password hash, reactivation) | any tracked `User` change | Interceptor → `EvictUser` |
| Legal entity switch | `SwitchActiveCompanyCommandHandler` sets `Session.ActiveEmployeeId` (tracked) | Interceptor → `EvictSession` |
| Employee moved between legal entities or users | any tracked `Employee` change/delete | Interceptor → `EvictUser` (old and new user) |
| Tenant row or subscription changes (module entitlements) | tracked `Tenant` change, `TenantSubscription` write | Interceptor → `EvictTenant` |
| Permission catalog change | tracked `Permission` change/delete | Interceptor → `EvictAll` |
| Platform admin: revoke sessions, reset MFA, reset admin password | `RevokePlatformUserSession(s)`, `ResetPlatformUserMfa`, `ResetAdminPassword` → `EfPlatformUserSessionRepository` (all tracked) | Interceptor: `PlatformUserSession` modified → `EvictSession(token_hash)` |
| Platform user status, MFA state, role assignment | tracked `PlatformUser`, `PlatformUserRole` writes | Interceptor → `EvictUser` |
| Platform role or role permissions | tracked `PlatformRole`, `PlatformRolePermission` writes | Interceptor → `EvictTenant(SessionTicketCacheScopes.Platform)` |
| **Session renewal** (added requirement: keep expiry in sync) | `RenewAsync` in both stores, written or throttled | Cached entry re-stored with the renewed `ExpiresAt` (capped at the absolute lifetime). The interceptor's `DropSession` on a renewal-only write (only `ExpiresAt`/`LastActivityAt` changed) has no grace window, because nothing security-relevant changed. |

The interceptor is `Persistence/Interceptors/SessionTicketCacheInvalidationInterceptor.cs`, registered with `AddDbContext` next to the existing interceptors. `TenantRlsInterceptor` is unchanged (task 02 owns it). I searched for bulk `ExecuteUpdate`/`ExecuteDelete` and raw SQL on the auth tables. The only ones are the four `Session` revokes listed above, and they now evict.

## Known limits

- The cache is per process. With more than one API instance, an eviction on one instance does not reach the others, so a revoked session could stay valid on another instance for up to 30 s. Task 21 moves the cache to a shared store. Until then, set `LifetimeSeconds` to 0 for multi-instance deployments.
- A role assignment that expires by time (`UserRole.ExpiresAt` passing, with no write) is picked up when the entry ages out, at most 30 s late.
- `EfSessionRepository` and `EfUserAccountRepository` take the cache as an optional constructor argument. This is only so the many standalone test contexts still compile. The API always registers it.

## Tests

The tests were written before the implementation. The red step was a compile failure, because the types did not exist yet. I also ran a mutation check: with the eviction markers disabled, 6 of the security tests fail (revoke, admin revoke, permission removal tenant and admin, role-permission change, grace window).

New test files (`tests/ONEVO.Tests.Unit/Features/Auth/`):

- `TenantDatabaseTicketStoreCacheTests` (22 tests): repeat requests do zero session DB reads (`GetByKeyHashForTenantResolutionAsync`, tenant, user and permission resolution are each called once across three requests); fresh ticket instance per hit; lifetime expiry; **lifetime 0 disables the cache**; **logout rejected on the next request**; **admin revoke rejected on the next request**; **permission removal (via `PermissionVersionService`) takes effect on the next request**; role-permission change for the tenant; legal entity switch; **deactivated user rejected on the next request**; **expired session rejected while cached**; absolute lifetime reached while cached; rebuild inside the grace window is not cached; other users' evictions keep the entry; `EvictAll`; renew writes at most once per interval; renew updates the cached expiry and the `session_expires_at` claim; **renewed session past its original expiry is still accepted from cache**; throttled renew still slides the cached expiry; **renewed session past the absolute lifetime is rejected**; a failed renew write does not use up the throttle slot; renew interval 0.
- `AdminDatabaseTicketStoreCacheTests` (12 tests): the same contract for platform admin sessions, including deactivated admin, renewed past original expiry, throttled slide, and absolute lifetime.
- `SessionTicketCacheInvalidationTests` (14 tests, SQLite): the interceptor evicts on session revoke, legal entity switch, user change, **user soft delete**, **platform user deactivation**, role assignment, role permissions, platform role permissions and platform session revoke. A renewal-only write only drops the entry. An added session evicts nothing. The bulk revokes in both repositories evict the user.

Existing tests were adjusted only to pass the new constructor argument (a disabled cache), in `TenantDatabaseTicketStoreTests`, `AdminDatabaseTicketStoreTests`, `TenantCookieAuthenticationConfigurationTests` and the integration test `TenantSessionRlsIntegrationTests`.

Results:

- `pwsh ../dev-env/scripts/verify.ps1 backend`: build PASS, unit **8000 passed / 0 failed**, architecture **713 passed / 0 failed** (logs `dev-env/reports/HRMS-Backend-v1/verify-20261117-095144`).
- `ONEVO.Tests.Integration` builds. It was not run, because it needs Docker. `TenantSessionRlsIntegrationTests` only got the new constructor argument (with the cache disabled).
- No migrations: this change adds no schema.
