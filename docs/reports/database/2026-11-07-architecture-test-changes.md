# Architecture tests awaiting a decision

**Date:** 2026-11-07
**Status:** Nothing removed. 54 architecture tests fail because they read files that no longer exist (`src/ONEVO.Infrastructure/Migrations` history and `ops/postgres`). A bulk removal was blocked as security test removal, so each group needs the user's approval.

| Group | Tests | Proposal | Covered by |
|---|---|---|---|
| RLS declared in one old migration | AttendanceCorrections, ClockInPolicy, CustomizableDashboard, DepartmentPart2A, FileStorage, LegalAcceptance, TenantSessionExchange, TimeTracking, WorkAreaChangeRequests | Retire | `EveryTable_BlocksCrossTenantAccess` (all 166 tables), `EveryCoveredTable_HasAFilterAndFourBlockPredicates` |
| Exact columns, indexes or foreign keys of an old migration | FileStorage, StorageQuota, LegalAcceptance, TenantStatusHistory, PositionPart2A, DepartmentPart2A, LegalEntityGeneralSettings, SubscriptionBilling, ConfigurationTemplateManager | Retire | `SchemaParityTests` |
| Migration history only (Down steps, prechecks, backfills, file pairs) | LegalAcceptance, NormalizedEmail precheck, LegalDocumentRichContent, Step1LoginBlocking, AttendanceCorrections backfill | Retire | Nothing: no longer meaningful with a baseline |
| Setup scripts and login lookup rights | LocalDatabaseRuntime (5), BaseLogin grants, NormalizedEmail lookup (2) | Replace | `SqlServerOpsArchitectureTests` (added, passing) |
| Tenant isolation | `EveryTenantOwnedEntityTable_HasRlsPolicyCoverage`, `Migrations_NeverDisableRowLevelSecurity_InTheUpDirection`, and the two other migration scans | Rewrite per spec section 9 | Same names, SQL Server rules |
| Payment gateway | CredentialOwnershipCompletion (2) | Update the assertions | Same tests |

Also pending: the `#if LEGACY_MIGRATION_TESTS` blocks in 4 test files, which currently do not compile in. They assert old migration operations for the MFA, workspace selection, platform credential and OAuth tables, and `SchemaParityTests` now covers those tables.
