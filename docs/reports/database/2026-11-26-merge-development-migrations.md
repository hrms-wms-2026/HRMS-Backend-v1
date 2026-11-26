# Merge of origin/development (9910b031a) into feature/testing (3e84d71b8): migrations (2026-11-26)

Branch `claude/merge-development-1126`. The merge base is `b6fabb74f`.

## Conflicts

| File | Resolution |
|---|---|
| `ApplicationDbContextModelSnapshot.cs` | Not hand-merged. EF regenerated it from the merged model in three steps (below). |
| `tests/ONEVO.Tests.Architecture/Schema/missing-foreign-keys.txt` | Our version (39b-39g done), plus development's lines for its new tables: `inbox_items.subject_id` (polymorphic), the three `rate_limit_setting_changes` history columns, `wm_notification_log.actor_employee_id`, and the four inbox employee columns (key still to add, see below). Development's `tenant_id` lines are not needed, because the 39d rule now gives those columns a key. |
| `tests/ONEVO.Tests.Architecture/Schema/missing-check-constraints.txt` | Our version, plus development's `inbox_items.kind` and `inbox_item_assignees.kind` (todo, owned by each inbox source until task 37b). |

## Migration order

Development added `AddRateLimitSettings`, `AddInboxItems`, `AddRowVersionTokens`, `AddBrevoEmailProvider` and `DropNotificationActorForeignKey`. It also removed the `wm_notification_log` actor key from 39a. Both sets are kept, ordered by timestamp:

1. `20261120015440_AddReferenceForeignKeysAndStatusChecks39a` (development's edit)
2. `20261120033318_AddRateLimitSettings` (development)
3. `20261120141304_AddInboxItems` (development)
4. `20261120172929_AddRowVersionTokens` (development)
5. `20261123045150_AddCoreForeignKeys39b`
6. `20261123120447_AddAuditColumnForeignKeys39c`
7. `20261123122014_AddBrevoEmailProvider` (development)
8. `20261123124332_DropNotificationActorForeignKey` (development; `IF EXISTS`, so it works whether or not 39a's old key is there)
9. `20261124131426_AddTenantForeignKeys39d`
10. `20261125053716_AddOtherModuleForeignKeys39e`
11. `20261125053747_AddStatusCheckConstraints39f`
12. `20261125053817_ReviewCascadeDeletes39g`
13. `20261125114212_ReferenceKeysClientNoAction39e`
14. `20261126034434_AddInboxTenantForeignKeysAfterMerge` (new)

A database that already has 39a-39d, like the local one, gets development's five migrations as pending ones, then 39e onwards.

## What the merged model adds to development's tables

Our task 39 rules also cover tables added later. A throwaway migration against our snapshot showed every operation of development's migrations, plus these combined effects:

- `inbox_items.tenant_id` and `inbox_item_assignees.tenant_id` get a key to `tenants` (39d rule). This is migration 14. Its read-only check stops with 50039 if a `tenant_id` is not a tenant.
- `rate_limit_setting_changes.tenant_id` is left out of the 39d rule. Development made it append-only history that outlives the tenant (allow-list line kept).
- `fk_inbox_item_assignees_inbox_entries_inbox_entry_id` stays a cascade (added to `CascadeDeleteRules.Kept` and `ForeignKeyRulesTests`): `InboxEntryConsistencyJob` hard-deletes orphan entries, and their assignee rows go with them.

The snapshot steps were:

1. Take our snapshot.
2. Keep the two inbox tables out of the tenant rule for a moment, and generate a sync migration. It repeated only development's operations, so its files were deleted and the snapshot EF wrote was kept.
3. Put the rule back and generate migration 14.

`dotnet ef migrations has-pending-model-changes` reports none. The snapshot has 23,992 lines: ours had 23,550, and development's 20,464.

## Follow-up

`inbox_items.requested_by_employee_id`, `decided_by_employee_id`, `subject_employee_id` and `inbox_item_assignees.employee_id` still have no key, as on development. Adding one needs a data check and a look at the inbox fixtures first.

## Gate

- Fast gate `verify-20261126-091714`: build PASS, unit 8577 passed, architecture 1022 passed. The integration project builds.
- SqlServerMigration (all migrations on a fresh SQL Server, in this order): not run yet, because Docker Desktop is down.
