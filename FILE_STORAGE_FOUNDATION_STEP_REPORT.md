# File Storage Foundation — Step Report

## Docs read

- `C:\onevoNew\OneVo-HR\database\phase1-table-inventory.md` — exact `file_records`, `file_upload_reservations`, `tenant_storage_stats` column definitions (Infrastructure section).
- `C:\onevoNew\OneVo-HR\database\schemas\shared-platform.md` — cross-checked for any conflicting/duplicate table definitions (none found; `file_records`/`file_upload_reservations` live in the Infrastructure group, not Shared Platform).
- `C:\onevoNew\ONEVO_Backend_Architecture_Document.md` — Storage Quota and File Storage Handling section (upload/quota/delete flow, encryption-at-rest rules, folder conventions). Note: this document names `tenant_resource_limits` as part of the quota model; per explicit task instruction and the Phase 1 inventory, that table was **not** created — the storage limit is resolved from the tenant's subscription/module entitlements instead (existing `IStorageQuotaService.GetTenantStorageLimitAsync`, unchanged in this step).

## Tables created/verified

- `file_records` — **created** (did not exist before this step). Exact inventory columns only.
- `file_upload_reservations` — **created** (did not exist before this step). Exact inventory columns only.
- `tenant_storage_stats` — **verified unchanged** as a table; only its repository/service gained new atomic methods (no migration touches this table).
- `tenant_resource_limits` — confirmed **not created**. No migration in this repository creates it (verified by `FileStorageArchitectureTests` and the pre-existing `StorageQuotaArchitectureTests.NoMigration_Creates_TenantResourceLimits`).

## Exact migration names

- `20260719081138_AddFileStorageTables` — creates `file_records` and `file_upload_reservations` with exact inventory columns, FKs (`tenant_id → tenants.id`, `uploaded_by_user_id`/`reserved_by_user_id → users.id`, `completed_file_record_id → file_records.id` nullable), and indexes (`ix_file_records_tenant_id_status`, `ix_file_upload_reservations_tenant_id_status_expires_at`, plus FK-supporting indexes).
- `20260719120142_AddFileStorageRlsPolicies` — adds PostgreSQL RLS (`ENABLE`/`FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy) for both new tables, mirroring the existing `AddRlsPolicies` migration's pattern exactly (see "Tenant isolation" below for why this was added).

## Exact APIs added

**None.** No controller, no public/tenant/admin HTTP endpoint was added in this step, per the task's stated preference ("skip API entirely and verify through integration tests"). The foundation is verified entirely through unit, architecture, and integration tests. A future upload feature (e.g., employee avatar) is expected to add its own thin controller/command that calls `IFileStorageService.UploadAsync(...)`.

## Credential handling proof

- `platform_service_keys` schema **unchanged** — no migration, no new column. The Cloudflare R2 credential JSON bundle (`accountId`, `bucketName`, `accessKeyId`, `secretAccessKey`, `endpoint`, `region`) is serialized and stored through the **existing** single `api_key_encrypted` string column, under `serviceKey = cloudflare_r2` (already registered in `PlatformServiceKeyCatalog` before this step). No changes were made to `PlatformServiceKeysController` or its Create/Update commands — an operator sets the R2 bundle through the existing admin endpoints exactly as they would any other service key.
- `CloudflareR2ObjectStorageAdapter` resolves the bundle via the existing `IPlatformServiceKeyResolver.ResolveActiveKeyAsync("cloudflare_r2", ct)`, deserializes it in-memory only, and constructs an `AmazonS3Client` per adapter instance (cached for the request's lifetime, never persisted or logged).
- All adapter exceptions (`ObjectStorageException`) carry only generic, safe-to-log text (e.g., `"Failed to upload object to Cloudflare R2."`) — no credential field, no raw AWS SDK exception message, is ever included.
- Verified by:
  - `FileStorageArchitectureTests.NoCloudflareR2CredentialFieldNames_AppearInAppsettingsFiles` — no `secretAccessKey`, `accessKeyId`, or the `cloudflarestorage.com` domain appear in any `appsettings*.json`.
  - `FileStorageServiceTests.Errors_NeverContainSecretLookingValues` — `Result.Error` strings never contain secret-looking substrings.
  - `EmailPlatformKeyArchitectureTests.OnlyInfrastructureEmailSender_ConsumesPlatformServiceKeyResolver` (extended, see below) — confirms `IPlatformServiceKeyResolver` is consumed **only** by the email sender and `CloudflareR2ObjectStorageAdapter`; no Application type, no controller, and no other Infrastructure type may resolve it.

## Quota/reservation behavior

- `IStorageQuotaService` gained three new atomic methods (`ReserveStorageAsync`, `ReleaseReservedStorageAsync`, `CommitReservedStorageAsync`), backed by three new atomic methods on `ITenantStorageStatsRepository` (`TryReserveBytesAsync`, `ReleaseReservedBytesAsync`, `CommitReservedToUsedAsync`), each a **single conditional SQL statement** (`INSERT ... ON CONFLICT ... DO UPDATE ... WHERE`, or `UPDATE ... WHERE`) run via `ExecuteSqlInterpolatedAsync`. No new application-level transaction API was introduced — none existed in the codebase before this step, and a single atomic statement was judged the minimal, correctly-scoped fix rather than adding a new `IUnitOfWork.BeginTransactionAsync` surface.
- `FileUploadReservation` status transitions (`Active → Completed`, `Active → Cancelled`) use the same pattern: `IFileUploadReservationRepository.TryTransitionStatusAsync` is a single conditional `UPDATE ... WHERE status = @from`, which is what makes double-complete and double-cancel safe under concurrency — only one caller's statement can match.
- `ConcurrentReservations_CannotOversubscribeReservedBytes` (integration test) fires 6 concurrent `TryReserveBytesAsync` calls of 3,000 bytes each against a 10,000-byte limit through separate `DbContext`/connections on real PostgreSQL: exactly 3 succeed (9,000 ≤ 10,000; a 4th would be 12,000), and `tenant_storage_stats.reserved_r2_bytes` ends at exactly `3 × 3,000` — no lost or double-counted reservations. **Passed.**
- Full lifecycle failure handling implemented in `FileStorageService`:
  - Purpose/size/content-type validation fails before any quota or DB write.
  - Quota reservation failure (403/409) fails before any DB row is created.
  - DB save failure after a successful quota reservation releases the reserved bytes.
  - R2 upload failure cancels the reservation (releases bytes) and never creates a `file_records` row.
  - DB save failure for `file_records` *after* a successful R2 upload deletes the orphaned R2 object (best-effort, logged if that also fails), releases the reserved bytes, and moves the reservation to `Cancelled`.

## Tenant isolation — a real gap found and closed (with a documented pre-existing limitation)

While writing the integration test for row-level tenant isolation, two pre-existing issues (not introduced by this step) were uncovered:

1. **`file_records`/`file_upload_reservations` had no RLS policy**, same as `tenant_storage_stats` and `mfa_challenges` before them — neither was ever retrofitted into the `AddRlsPolicies` migration's table list after that migration ran. Closed for these two tables by the new `AddFileStorageRlsPolicies` migration, exactly mirroring the proven `AddRlsPolicies` pattern (`ENABLE`/`FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy on `tenant_id`).
2. **The EF Core `HasQueryFilter` tenant filter in `ApplicationDbContext.OnModelCreating` captures `ITenantContext` in a closure that EF's default model caching only builds once per process.** Since `TenantContextAccessor` is registered `AddScoped` (a new instance per request/DbContext), every `ApplicationDbContext` instance after the very first one in a process uses a stale, frozen tenant-context reference for its query filter — the filter does not reflect the current request's resolved tenant. This is a genuine, cross-cutting bug affecting every tenant-owned entity, not specific to file storage. **Not fixed in this step** (out of scope — a shared-kernel fix affecting the whole application's `ApplicationDbContext`/model-caching setup, flagged here for a dedicated follow-up).

Because of (2), and because PostgreSQL superusers unconditionally bypass RLS regardless of `FORCE ROW LEVEL SECURITY` (the Testcontainers default role **and** the real `appsettings` connection role are both superuser-equivalent in this environment), the only mechanism that can currently, verifiably enforce tenant isolation for `file_records`/`file_upload_reservations` is PostgreSQL RLS driven through a properly restricted (non-superuser, non-bypassrls) connection role — which is not how the application currently connects to the database in any environment inspected. The integration test (`FileRecords_AreIsolatedByTenant`) proves the RLS policy itself is correct by creating a dedicated non-superuser test role inline and routing the tenant-scoped assertions through it; this is test-only and touches no production configuration.

**Net effect for this step:** `file_records`/`file_upload_reservations` now have exactly the same isolation posture as the rest of the application (RLS policy present and proven correct in principle; both RLS-bypass-by-superuser and the EF-filter model-caching bug are pre-existing, cross-cutting issues that also affect the original 20+ RLS-protected tables and `tenant_storage_stats`/`mfa_challenges` respectively). Provisioning a restricted application database role (closing the superuser-bypass gap) and fixing the EF model-caching closure (closing the stale-filter gap) are both flagged as follow-up work — see "Remaining gaps."

## Tests run and results

| Project | Command | Result |
|---|---|---|
| Unit | `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj` | **522 passed**, 0 failed |
| Architecture | `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj` | **77 passed**, 0 failed |
| Integration | `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj` | **37 passed**, 0 failed (1m35s) |

New tests added by this step (all included in the totals above):
- `UploadPurposePolicyTests` — 8 tests (unsupported purpose, zero size, oversized, disallowed content type/extension, valid upload accepted, storage key never contains client path traversal, filename sanitization).
- `FileStorageServiceTests` — 8 tests (quota-exceeded prevents reservation, successful reservation increments reserved bytes, completion moves reserved→used, cancel releases reserved bytes, double-complete rejected with 409, double-cancel idempotently safe, R2 upload failure releases reservation, no secret-shaped values in errors).
- `FileStorageArchitectureTests` — 8 tests (tenant-owned entities, service implementation location, `IObjectStorageAdapter` has no AWS types in its signature, no Application code bypasses `IFileStorageService` via the raw repositories, migration creates only the two approved tables, exact column sets for both, no R2 credential field names/domain in appsettings).
- `EmailPlatformKeyArchitectureTests` — 1 existing test's allowlist extended (not a new test) to include `CloudflareR2ObjectStorageAdapter` as a second legitimate consumer of `IPlatformServiceKeyResolver`.
- `FileStorageIntegrationTests` — 3 tests (tenant isolation via real RLS through a restricted role, `completed_file_record_id` FK enforcement with PostgreSQL error code `23503`, concurrent reservation oversubscription prevention).
- `StorageQuotaServiceTests.FakeTenantStorageStatsRepository` — extended (not new) with in-memory equivalents of the three new atomic repository methods, since the interface it implements grew.

## Docker/PostgreSQL integration tests

**Ran successfully**, including a manual ad hoc verification pass beyond the automated suite:
- The `AddFileStorageTables` and `AddFileStorageRlsPolicies` migrations were applied against a real, throwaway `postgres:16-alpine` container (outside Testcontainers, via `docker run` + `dotnet ef database update`) and the resulting schema was inspected directly with `psql` (`\d+ file_records`, `pg_policies`, `pg_class.relrowsecurity`) to confirm exact column types, FKs, indexes, and RLS policy definitions before trusting the automated test suite's assertions.
- The full `ONEVO.Tests.Integration` project (37 tests across the whole existing suite, not just this feature) was run and passed, confirming no regression from the `IStorageQuotaService`/`ITenantStorageStatsRepository` interface extensions.

## Remaining gaps

- **No scan pipeline.** Every `file_records` row created by `CompleteUploadAsync` is created with `status = pending_scan` and stays there — there is no malware/virus scanner integration in this Phase 1 foundation. A future feature must add a scan pipeline that flips status to `available` or `quarantined`. This is an intentional, explicitly out-of-scope gap per the task's own instructions.
- **No download/read path.** `IObjectStorageAdapter.GetObjectAsync`/`ObjectExistsAsync` are implemented but unused by any handler — no signed-URL or authorized-streaming download endpoint exists yet. A future feature must add one per the architecture doc's "Private file access must use signed URLs or authorized API streaming" rule.
- **No background reconciliation job.** Expired (`ExpiresAt` passed) reservations are never automatically transitioned to `Expired`, and there is no job comparing `file_records`/R2 object existence for drift correction, as described (but not required in this step) by the architecture document.
- **Two pre-existing, cross-cutting isolation issues surfaced and only partially addressed** (see "Tenant isolation" above): (a) the RLS-bypass-by-superuser connection role, and (b) the EF `HasQueryFilter` model-caching closure staleness. Both affect the wider application, not just file storage, and are flagged for dedicated follow-up rather than fixed here.
- **No real Cloudflare R2 network smoke test was automated**, per task instructions (a fake `IObjectStorageAdapter` is used in all automated tests). Manual follow-up: create a real `cloudflare_r2` platform service key via the existing admin endpoint pointing at a real bucket, then exercise `IFileStorageService.UploadAsync` through a throwaway harness against real R2 before this foundation is relied upon by a production feature.
- **`AWSSDK.S3` is a new dependency** (version `4.0.101.3`, resolved to latest stable at the time this step ran) added to `ONEVO.Infrastructure.csproj` — no prior S3-compatible client existed in this codebase.

## Confirmation

- **No unapproved tables were created.** Only `file_records` and `file_upload_reservations` (both are exact matches to the Phase 1 inventory). `tenant_resource_limits` was explicitly not created, verified by architecture test.
- **No credentials were stored in appsettings or source.** `platform_service_keys.api_key_encrypted` (existing, unchanged column) is the only credential storage location; no R2 field names, no R2 domain, no other secret-shaped values appear in any appsettings file or source file added by this step, verified by architecture and unit tests.
