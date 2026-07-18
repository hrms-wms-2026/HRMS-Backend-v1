# Tenant Status History Completion Report (2026-07-18)

Scope: reconcile OneVo-HR docs for tenant_status_histories, add FK constraints,
and make ConfirmTenantProvisioningCommandHandler write history on activation.

## Docs read

See OneVo-HR/TENANT_STATUS_HISTORY_SCHEMA_RECONCILIATION_REPORT.md for the
full list of docs read and the Phase 1 approval decision.

## Docs changed

- OneVo-HR/database/schemas/shared-platform.md (canonical definition added)
- OneVo-HR/database/schema-catalog.md (index row + counts)
- OneVo-HR/database/phase1-table-inventory.md (full definition + counts)
- OneVo-HR/developer-platform/userflow/tenant-management.md (audit note)
- ONEVO_Backend_Architecture_Document.md (one-sentence Audit Trail addition -
  this file is not tracked in either git repository, so the edit is on disk
  only, not committed)
- OneVo-HR/TENANT_STATUS_HISTORY_SCHEMA_RECONCILIATION_REPORT.md (new)

## Backend files changed

- src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/Tenancy/TenantStatusHistoryConfiguration.cs
  (added tenant_id -> tenants Restrict FK, changed_by_id -> platform_users
  SetNull FK)
- src/ONEVO.Infrastructure/Migrations/20260718012152_AddTenantStatusHistoryForeignKeys.cs
  (+ .Designer.cs, + ApplicationDbContextModelSnapshot.cs) - new correction
  migration, additive only (two AddForeignKey calls plus one CreateIndex for
  the previously-unindexed changed_by_id column; no CreateTable)
- src/ONEVO.Application/Features/DevPlatform/Tenancy/Commands/ConfirmTenantProvisioning/ConfirmTenantProvisioningCommandHandler.cs
  (injects ITenantStatusHistoryRepository, writes one TenantStatusHistory row
  on successful activation before the existing SaveChangesAsync call)
- tests/ONEVO.Tests.Architecture/TenantStatusHistoryArchitectureTests.cs (new)
- tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/ConfirmTenantProvisioningCommandHandlerTests.cs (new)
- tests/ONEVO.Tests.Integration/Tenancy/TenantsAdminApiIntegrationTests.cs
  (extended blocked-confirm test + new tenant-not-found test, both asserting
  zero tenant_status_histories rows)

## Migration name

AddTenantStatusHistoryForeignKeys (20260718012152_AddTenantStatusHistoryForeignKeys)

## Exact FK constraints added

- tenant_status_histories.tenant_id -> tenants.id, ON DELETE RESTRICT
  (fk_tenant_status_histories_tenants_tenant_id)
- tenant_status_histories.changed_by_id -> platform_users.id (nullable),
  ON DELETE SET NULL (fk_tenant_status_histories_platform_users_changed_by_id)
- EF also generated ix_tenant_status_histories_changed_by_id, since
  changed_by_id previously had no index while tenant_id and changed_at did.

## Provisioning confirm history behavior

ConfirmTenantProvisioningCommandHandler now writes one TenantStatusHistory row
(FromStatus = previous status, ToStatus = Trial, Reason =
"provisioning_confirmed", ChangedById = the authenticated platform admin's
user id, ChangedAt = IDateTimeProvider.UtcNow) immediately before the existing
IUnitOfWork.SaveChangesAsync call, so tenant status and history persist in the
same transaction - matching ChangeTenantStatusCommandHandler's pattern. No
history is written when: authentication fails (403), the tenant is not found
(404), the tenant is not in Provisioning status (409), or the provisioning
summary reports CanActivate = false (422).

## Tests added/updated

- Unit: 4 new tests in ConfirmTenantProvisioningCommandHandlerTests.cs
  (success writes history; blocked writes none; not-found writes none;
  wrong-status writes none)
- Architecture: 6 new tests in TenantStatusHistoryArchitectureTests.cs
  (entity is tenant-owned; entity columns match inventory; migration adds no
  tables; migration adds the tenants FK with Restrict; migration adds the
  platform_users FK with SetNull; no migration ever creates
  tenant_resource_limits)
- Integration: extended ProvisionConfirm_Returns422_WithSummary_WhenIncomplete
  to assert zero history rows; added
  ProvisionConfirm_TenantNotFound_Returns404_AndWritesNoHistory

## Build/test results

- dotnet build src/ONEVO.Api/ONEVO.Api.csproj                              PASS (0 errors, 2 pre-existing warnings)
- dotnet test tests/ONEVO.Tests.Unit                                        PASS 506/506
- dotnet test tests/ONEVO.Tests.Architecture                                PASS 68/68
- dotnet test tests/ONEVO.Tests.Integration --filter TenantsAdmin*|E2E*    PASS 15/15
- dotnet test tests/ONEVO.Tests.Integration (full suite, Docker up)        PASS 34/34

## Confirmation no security was weakened

AdminPolicy, RequirePlatformPermission (TenantsManage), CSRF enforcement, and
admin session handling were not touched by this change set. No new endpoint
was added. The FK migration only tightens data integrity (adds constraints);
it does not change authorization or validation behavior. The new
unauthorized-request integration test coverage from the prior step
(PatchTenantStatus_Unauthorized_WritesNoHistory) still passes unmodified.

## Confirmation no unrelated tables were added

The correction migration contains only two AddForeignKey statements and one
CreateIndex statement against the pre-existing tenant_status_histories table;
verified by TenantStatusHistoryArchitectureTests.FkCorrectionMigration_AddsNoTables
and .NoMigration_Creates_TenantResourceLimits. No other table was created,
dropped, or altered.

## Remaining risks / known gaps

- No read API exists for tenant_status_histories (deferred by design; a
  likely future GET /admin/v1/tenants/{id}/status-history endpoint is
  documented but not built).
- The "successful provisioning confirm writes history" path has unit-test
  coverage only, not HTTP-level integration coverage: ITenantSubscriptionStatusReader,
  ITenantModuleStatusReader, and ITenantSettingsStatusReader are wired to
  NotConfiguredYetReaders stubs that always return Complete=false, so
  CanActivate can never be true through the real HTTP pipeline today. This is
  a pre-existing, unrelated Member-2-owned gap (already documented by the
  existing ProvisionConfirm_Returns204_WhenAllSectionsCompleteAndInviteExists
  and TenantProvisioningE2ETests tests, both of which bypass the confirm
  endpoint and set tenant.Status directly). Fixing it is out of scope for
  this change set.
- changed_by_id is nullable in the schema but never actually null in Phase 1
  (every writer is an authenticated admin action); this is intentional
  forward-compatibility, not a gap.
- The Task 11 commit to TenantsAdminApiIntegrationTests.cs necessarily
  includes pre-existing uncommitted edits to that same file from an earlier,
  unrelated session (per project memory: admin-login test fixes and
  CreateTenantRequest contract updates), since that file was already tracked
  and modified before this task touched it and git stages whole-file diffs.
  This was not scope creep by intent - it is a byproduct of committing
  directly on main alongside pre-existing dirty state, which the user
  explicitly approved for this session. All tests in the file, old and new,
  pass (34/34 in the full integration run).
- ONEVO_Backend_Architecture_Document.md lives outside both git repositories
  tracked by this task (C:\onevoNew has no .git of its own); its one-sentence
  edit is applied on disk but is not version-controlled by this change set.
