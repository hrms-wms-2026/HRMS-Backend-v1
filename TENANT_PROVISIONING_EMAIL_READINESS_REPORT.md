# Tenant Provisioning & Email Readiness Report

## Summary

The provisioning confirm flow now works end-to-end without any direct database
activation: admin creates a tenant with an owner invite → owner accepts the
invite with password + confirm_password + legal acceptances → admin calls
`PATCH /admin/v1/tenants/{id}/provision/confirm` → **204 No Content**, and the
tenant transitions out of `provisioning` for real. This was verified by running
the full E2E test against a real PostgreSQL instance (Testcontainers), not by
code inspection alone.

One clarification on "active": the tenant's status becomes **`trial`**, not
literal `TenantStatus.Active`. See "Trial vs. Active" below — this is existing,
deliberate business logic that this task did not change, but it is a real gap
between the literal wording of the task's acceptance criteria and what the
system does.

## Files changed

| File | Change |
|---|---|
| `src/ONEVO.Application/Features/DevPlatform/Tenancy/Queries/GetProvisioningSummary/GetProvisioningSummaryQueryHandler.cs` | Removed stale "...password and phone number" wording from the pending-owner-invite blocker message. |
| `src/ONEVO.Infrastructure/Services/DevPlatform/Tenancy/NotConfiguredYetReaders.cs` | Kept only the shared `Build(...)` helper for the "not configured yet" case; removed the three `NotConfigured*StatusReader` stub classes (subscription/module/settings) that always failed closed. |
| `src/ONEVO.Infrastructure/Services/DevPlatform/Tenancy/TenantSubscriptionStatusReader.cs` (new) | Real `ITenantSubscriptionStatusReader`: complete iff a `tenant_subscriptions` row exists for the tenant (`ITenantSubscriptionRepository.GetByTenantIdAsync`). |
| `src/ONEVO.Infrastructure/Services/DevPlatform/Tenancy/TenantModuleStatusReader.cs` (new) | Real `ITenantModuleStatusReader`: complete iff the tenant's subscription has a non-empty `selected_modules_json`. Reads the subscription row directly rather than through `IModuleEntitlementService`, which additionally filters by active subscription status (`SubscriptionStatusRules.ActiveStatuses` — note `"trialing"` is in that list, so this isn't a correctness fix, just decoupling a "was this ever configured" check from a "is this currently billable" concern it doesn't need). |
| `src/ONEVO.Infrastructure/Services/DevPlatform/Tenancy/TenantSettingsStatusReader.cs` (new) | Real `ITenantSettingsStatusReader`: complete iff `tenant.settings_json` deserializes to a non-empty object (it always contains at least `default_timezone`, set at tenant creation). |
| `src/ONEVO.Infrastructure/DependencyInjection.cs` | Swapped the three `NotConfigured*StatusReader` DI registrations for the real readers above. `ITenantRoleStatusReader` registration unchanged (already real). |
| `src/ONEVO.Application/Features/DevPlatform/Tenancy/Commands/ConfirmTenantProvisioning/ConfirmTenantProvisioningCommandHandler.cs` | Added `ITenantCacheInvalidator` and calls `InvalidateBySlug(tenant.Slug)` after a successful activation commit. **Necessary bug fix**, not scope creep — see "Cache invalidation gap" below. |
| `tests/ONEVO.Tests.Integration/E2E/TenantProvisioningE2ETests.cs` | Removed `ActivateTenantDirectlyAsync` (the direct-DB-write + manual cache-evict workaround). Confirm now asserts `204 No Content`, followed by a read-only `AssertTenantStatusAsync(tenantId, TenantStatus.Trial)`. |
| `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/ConfirmTenantProvisioningCommandHandlerTests.cs` | Added `ITenantCacheInvalidator` mock; asserts it's invoked once on success and never on the 422/404/409 paths. |
| `tests/ONEVO.Tests.Unit/Features/DevPlatform/SystemConfig/EfPlatformServiceKeyRepositoryTests.cs` | Added 6 tests for `ListActiveForProviderFamilyAsync` — the actual EF join between `platform_service_keys` and `platform_providers` that `PlatformServiceKeyResolver` depends on (see "Email provider tests" below). |

No `.env`, no appsettings provider selection, and no OneVo-HR docs were touched.

## Item 1 — Phone wording

The only stale phone reference in the entire invite/provisioning path was the
one identified in the task: `GetProvisioningSummaryQueryHandler.cs`'s pending-
invite blocker message ("...has not yet set their password **and phone
number**"). Fixed to just "...has not yet set their password."

I also verified the accept-invite path itself never required a phone number —
`rg -i phone` across `src/ONEVO.Application/Features/Auth` and `src/ONEVO.Api`
returns zero hits. The only other `phone` hits in `src/` are the unrelated
`Employee.Phone` column and EF migration snapshots (historical schema, not
active code). The invite acceptance flow requires exactly: `password`,
`confirm_password`, `acceptances` (legal documents) — confirmed by reading
`TenantProvisioningE2ETests.cs`'s real request body and by the passing E2E run.

## Item 2 — Mojibake

`rg -n "[^\x00-\x7F]"` against every file this task touched (listed above)
returns **zero matches**. `TenantProvisioningE2ETests.cs` contains real,
correctly-encoded Unicode em-dashes and box-drawing characters (`—`, `─`) used
as comment decoration — these are not mojibake (garbled byte sequences like
`â€”`) and were left alone, per "do not make behavior changes just for
formatting."

One genuine mojibake instance exists in the repo (`â€”` three times in
`src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs`), but that
file is unrelated to tenant provisioning or email and isn't touched by this
task, so it was left as-is and is reported here rather than fixed silently.

## Item 3/4 — Provisioning section readers

All three previously-stubbed sections turned out to have a real, already-
written canonical source — no new tables were invented:

| Section | Source | Written by |
|---|---|---|
| Subscription | `tenant_subscriptions` row | `CreateTenantCommandHandler`, step 7 |
| Modules | `tenant_subscriptions.selected_modules_json` (copied from `subscription_plans.included_modules_json`) | Same row, same step |
| Settings | `tenants.settings_json` (`{"default_timezone": ...}`) | `CreateTenantCommandHandler`, step 4 |
| Roles | System roles seeded per tenant (unchanged, already real) | `DefaultRoleSeeder.SeedOwnerRoleAsync` |

I confirmed the seeded Phase 1 plan (`a1b2c3d4-0001-0001-0001-000000000001`,
used by the E2E test) has a non-empty `IncludedModulesJson` (18 modules), so
the module reader resolves `Complete = true` for real tenants created against
that plan — this isn't just theoretically wired, it's what the passing E2E
proves.

One deliberately-avoided table: `tenant_provisioning_states` (entity
`TenantProvisioningState`) has exactly the shape you'd expect for this
purpose (`SubscriptionCompletedAt`, `ModulesCompletedAt`, `SettingsCompletedAt`,
`ActivationReady`, ...), but **nothing in the codebase ever writes to it** — I
grepped for any assignment to its properties or `AddAsync`/`.Add(...)` calls
against `TenantProvisioningStates` and found none. It's dead schema, not a
canonical source, so I did not read from it.

## Item 4 — Provisioning confirm

**Confirm now returns 204 and works without any manual DB activation.**
Verified by running `tests/ONEVO.Tests.Integration/E2E/TenantProvisioningE2ETests.cs`
against a real PostgreSQL Testcontainers instance — full pass, including the
owner login, CSRF, and host-isolation assertions that all sequentially depend
on tenant status being usable after confirm.

### Trial vs. Active — read this before calling it done

`ConfirmTenantProvisioningCommandHandler` transitions the tenant from
`Provisioning` to **`Trial`**, not `Active`. This is pre-existing, deliberate
logic I did not change: the seeded plan defaults to a 30-day trial, and the
handler hard-codes the confirm target to `Trial` regardless of the
subscription's trial length. `LoginCommandHandler` and every other
tenant-host auth check treat `Active` and `Trial` as equally usable
(`tenant.Status is not (TenantStatus.Active or TenantStatus.Trial)`), so
functionally the owner can log in and use the tenant immediately — but the
literal enum value is `Trial`.

If the product intent is that a *confirmed* tenant should be
`TenantStatus.Active` (e.g., because commercial terms were already agreed at
tenant creation, independent of the plan's trial period), that's a distinct
business-logic change to `ConfirmTenantProvisioningCommandHandler`, not
something I made unilaterally here — changing a hard-coded status transition
felt outside "make the existing flow work" and into "change what the flow
means." Flagging it explicitly rather than silently deciding either way.

### Cache invalidation gap (found and fixed)

`HostTenantResolutionMiddleware` caches `tenant:slug:{slug} -> (id, slug,
status)` for 2 minutes. The E2E test's original workaround
(`ActivateTenantDirectlyAsync`) didn't just bypass the 422 — it also called
`cache.Remove($"tenant:slug:{Slug}")` directly, because earlier steps in the
flow (checking the invite, accepting it) resolve and cache the tenant while
it's still `Provisioning`. Without evicting that cache entry, the post-confirm
owner login would see the stale `Provisioning` status and fail.

`ITenantCacheInvalidator.InvalidateBySlug` already existed for exactly this
purpose but was registered in DI and never called by any command handler —
not `ConfirmTenantProvisioningCommandHandler`, not even
`ChangeTenantStatusCommandHandler` (suspend/activate/cancel). I wired it into
`ConfirmTenantProvisioningCommandHandler` only, since that's what this task
needed to make confirm work without the test-only cache hack.
**`ChangeTenantStatusCommandHandler` has the same latent gap** (a suspended
tenant could still resolve as `Active` from cache for up to 2 minutes) — left
unfixed as out of scope for this task, but worth a follow-up ticket.

## Item 5 — Email provider tests

Most of what this item asks for already existed before this task, in
`tests/ONEVO.Tests.Unit/Features/SharedPlatform/Email/TransactionalEmailPlatformKeyTests.cs`,
`tests/ONEVO.Tests.Unit/Features/DevPlatform/SystemConfig/PlatformServiceKeysTests.cs`,
and `tests/ONEVO.Tests.Architecture/EmailPlatformKeyArchitectureTests.cs`. I
mapped every required bullet to its covering test rather than duplicate:

| Requirement | Covered by |
|---|---|
| Outbox handler uses shared `IEmailService` path | `InviteOutboxHandler_Queued_Email_Is_Sent_Through_Platform_Key_Sender` |
| Sender resolves provider via `platform_providers` + `platform_service_keys` (sender/resolver logic) | `Sender_Sends_Through_Sendgrid_Adapter_When_...`, `PlatformServiceKeysTests.Resolver_*` / `ResolveActiveTransactionalEmailProvider_*` (mocked repo) |
| SendGrid active credential → SendGrid adapter | `Sender_Sends_Through_Sendgrid_Adapter_When_Sendgrid_Is_The_Resolved_Provider`, `SendGridAdapter_Builds_Authorized_Request_And_Captures_MessageId` |
| Resend active credential → Resend adapter | `Sender_Sends_Through_Resend_Adapter_When_Resend_Is_The_Resolved_Provider`, `ResendAdapter_Builds_Authorized_Request_And_Parses_Id` |
| Zero active providers fail safely | `NotConfigured_Resolution_Fails_Safely_And_Does_Not_Default_To_Sendgrid`, `ResolveActiveTransactionalEmailProvider_ReturnsNotConfigured_WhenZeroActive` |
| Two active providers fail safely, no adapter called | `Ambiguous_Resolution_Fails_Safely_And_Does_Not_Call_Any_Adapter`, `ResolveActiveTransactionalEmailProvider_ReturnsAmbiguous_WhenTwoActive` |
| No `Email__Provider`/`Email:Provider` anywhere for selection | `EmailOptions_HasNoProviderProperty`, `EmailPlatformKeyArchitectureTests.PlatformKeyTransactionalEmailSender_HasNoConfigDrivenProviderSelectionOrSendgridFallback` (source-scans the sender file), plus my own `rg` below found zero hits outside migrations/guard tests. |
| No real network calls in tests | All adapter tests use a `CapturingHandler : HttpMessageHandler` fake; `PlatformKeyTransactionalEmailSenderTests` use fakes for resolver/adapter. |

**Genuine gap I found and fixed:** every existing test above proves the
*sender* and the *resolver's business logic* against a **mocked**
`IPlatformServiceKeyRepository`. The actual SQL-translatable EF query —
`EfPlatformServiceKeyRepository.ListActiveForProviderFamilyAsync`, which joins
`platform_service_keys` to `platform_providers` on `ServiceKey ==
ProviderKey` and filters `IsActive` on both sides plus `ProviderFamily` — was
never exercised. I added 6 tests to
`EfPlatformServiceKeyRepositoryTests.cs` (EF Core InMemory provider, the same
pattern already used by that file) proving: zero matches when the provider
card is inactive, exactly one match when key+provider are both active,
**both** rows returned when two providers are simultaneously active (the
scenario that becomes `Ambiguous` upstream), and exclusion when the key is
active but its provider card isn't, when the provider is active but the key
isn't, and when the family doesn't match. This is the DB-backed proof that
was actually missing.

Also ran, per the task's required search:
```
rg -n "phone number|phone.*invite|Email__Provider|Email:Provider|setup_services|tenant_setup_services|TenantSetupOptionKeys|tenant_setup_selections" src tests
```
All `src/` hits are EF migration snapshots (historical) or the legacy
`TenantSetupSelectionConfiguration.cs` (intentionally-kept-inactive per
`SetupOptionModelRetirementArchitectureTests`, which passes). All `tests/`
hits are the guard tests themselves asserting these patterns are *absent*
from active code. No live violations.

## Item 6 — System Config service-key routes

All required routes exist and are covered; no changes were needed:

- `GET /admin/v1/system-config/service-key-providers` — `SystemConfigProviderOptionsController.ListServiceKeyProviders`, route/permission/shape locked by `SystemConfigProviderOptionsArchitectureTests` (5 tests) and behavior by `ProviderOptionQueriesTests.cs`. This is the UI's provider-picker endpoint — confirms operators choose from `sendgrid`/`resend`, not blind-typed strings.
- `GET /admin/v1/system-config/service-keys`, `GET .../{serviceKey}`, `POST /service-keys`, `POST .../{serviceKey}/activate`, `.../deactivate`, `.../rotate-key`, `.../verify` — all on `PlatformServiceKeysController`, covered by `PlatformServiceKeysTests.cs` (create/rotate/activate/deactivate/verify command handlers, list/get queries, authorization).
- Activating/creating a transactional-email key while another is active → 409, never overwrites the existing active row: `Create_ActiveSendgridWhileActiveResendExists_ReturnsConflict`, `SetActivation_ActivatingResendWhileActiveSendgridExists_ReturnsConflict`.
- Responses never carry plaintext/encrypted keys: `ListAndDetail_NeverContainKeyMaterial`, `Security_ResponseDtos_DoNotExposeKeyMaterial`, `Rotate_ReplacesEncryptedValue_AndNeverExposesPlaintext`, `Verify_SuccessStampsLastVerifiedAt_AndNeverReturnsKeyMaterial`.

## Item 7 — Legal Entity / Company: current state (no CRUD built)

**A primary legal entity is created during tenant creation.**
`CreateTenantCommandHandler` step 5 inserts one `LegalEntity` row per tenant
with `IsPrimary = true` in the same transaction as the tenant.

**Fields that exist** (`Domain/Features/OrgStructure/Entities/LegalEntity.cs`):
`Id`, `TenantId`, `Name`, `RegistrationNumber` (nullable), `CountryCode`,
`CurrencyCode`, `AddressJson` (nullable — never populated by anything today),
`IsActive`, `IsPrimary`, `CreatedAt`, `UpdatedAt`.

**API support that exists:** none dedicated. `ILegalEntityRepository` exposes
only `AddAsync` and `GetPrimaryByTenantIdAsync` — no update, no list, no
get-by-id, no delete, no way to add a second legal entity to a tenant. The
only place a legal entity is ever read back is `GetTenantByIdQueryHandler`,
which folds `Name`/`RegistrationNumber`/`CountryCode`/`CurrencyCode` into the
tenant detail response (`GET /admin/v1/tenants/{id}`) as read-only fields.
`AddressJson`, `IsPrimary`, and `IsActive` aren't exposed there either.

**Is it safe to move to Legal Entity/Company CRUD next?** Yes, from a
provisioning-readiness standpoint — tenant creation, invite acceptance, and
provisioning confirm all now work end-to-end without workarounds, and that
was the stated blocker for starting this work. The Legal Entity model itself
is a blank slate: one repository method short of everything, no existing
CRUD surface to reconcile or migrate away from, and no architecture tests
guarding a particular shape yet. Building Create/Update/List/Get for legal
entities is additive, not a rework.

## Tests run

| Suite | Result |
|---|---|
| `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore` | **Build succeeded, 0 warnings, 0 errors** |
| `dotnet test tests/ONEVO.Tests.Unit` | **840/840 passed** (834 pre-existing + 6 new repository tests), 0 failed, 0 skipped |
| `dotnet test tests/ONEVO.Tests.Architecture` | **219/219 passed**, 0 failed, 0 skipped |
| `dotnet test tests/ONEVO.Tests.Integration` | **74/74 passed**, 0 failed, 0 skipped (real PostgreSQL via Testcontainers; Docker Desktop was not running initially and was started for this run) |
| `git diff --check` | clean (only benign LF→CRLF line-ending notices, no whitespace errors) |

The integration run includes `TenantProvisioningE2ETests.Full_tenant_provisioning_flow`
end to end: create tenant → accept invite → **confirm returns 204** → tenant
is `Trial` → owner logs in → roles/CSRF/host-isolation assertions all pass.

## Remaining blockers / follow-ups (not fixed here, by design)

1. **Trial vs. Active** (above) — confirm on this task's seeded plan produces
   `TenantStatus.Trial`. If the product wants literal `Active`, that's a
   follow-up business-logic decision, not a bug in this task's scope.
2. **`ChangeTenantStatusCommandHandler` doesn't invalidate the tenant slug
   cache** either (same class of bug I fixed in the confirm handler). Out of
   scope here; flagging for a follow-up.
3. **`tenant_provisioning_states` is dead schema** — either wire it up as the
   real source of truth for provisioning readiness in a later pass, or drop
   it; leaving it unused and unreferenced is a minor footgun for the next
   person who reads the schema and assumes it's live.
4. `PermissionSeeder.cs` has 3 lines of genuine mojibake (`â€”`), unrelated to
   this task's scope, left untouched.

## Final answer

**Yes — tenant creation → invite acceptance → provisioning confirm → usable
tenant now works without any manual DB activation, verified by a real
integration test run (74/74 passing) rather than static analysis.** The one
caveat, stated plainly rather than glossed over: the resulting status is
`Trial`, not the literal `Active` enum value, by pre-existing design. With
that caveat understood and accepted, **it is safe to move on to Legal
Entity/Company CRUD** — the provisioning and email plumbing this task covers
is solid, and the Legal Entity model has no existing CRUD surface to
reconcile against, so that work can start clean.
