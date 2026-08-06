# Department Foundation — Part 2D Real HTTPS/API Validation Report

**Repo:** `C:\onevoNew\HRMS-Backend-v1`
**Scope:** Validate the Department endpoints (Part 2A/2B/2C, already code-complete) through the real running API surface — not controller/handler unit tests. Performed only after the `roles:read` provisioning fix (see `TENANT_PROVISIONING_ROLES_READ_FIX_REPORT.md`) was verified green.

---

## 1. Approach

**Automated integration tests, not manual Postman/curl walkthroughs**, per the task's stated preference and the precedent already established in this repo for exactly this kind of phase (`LEGAL_ENTITY_GENERAL_SETTINGS_PART2D_POSTMAN_AND_HTTP_VALIDATION_REPORT.md` — Path A there: a Testcontainers-backed `WebApplicationFactory` integration suite was treated as the primary proof, stronger than a manual click-through because it asserts exact status codes and bodies).

**New file:** `tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs` (18 tests).

This goes through the **full real pipeline** for every request:
- **Kestrel `TestServer`** (in-process, real ASP.NET Core host — `WebApplicationFactory<Program>`, same mechanism `TenantProvisioningE2ETests`/`LegalEntitiesIntegrationTests` already use and that this task's own root-cause investigation just exercised for the `roles:read` fix)
- **Routing/controller** — real `DepartmentsController` actions
- **Auth middleware** — real cookie-based session auth (`Authorize(Policy = "TenantPolicy")`)
- **Tenant resolution** — real host-header-based tenant resolution (`dept-a.localhost`/`dept-b.localhost`, mirroring the `*.localhost` convention already used by every other integration test in this repo)
- **Permission attributes** — real `[RequirePermission("org:read"/"org:manage")]` enforcement
- **CSRF middleware** — real `X-CSRF-Token` header checks on every mutating call
- **MediatR** — real command/query handlers, no mocking
- **EF/Postgres/RLS** — a real PostgreSQL instance via Testcontainers, real `tenant_isolation` row-level-security policy on `departments` (added by `20260803085109_AddDepartments.cs`)

**Why not literal `https://localhost:7229`/`https://acme.localhost:7229` against a standalone `dotnet run` process:** those addresses require a long-lived Kestrel process, a trusted mkcert certificate, and a persistent local Postgres database outside this session's control. `WebApplicationFactory`'s `TestServer` exercises the identical ASP.NET Core request pipeline (same middleware order, same DI container, same real Postgres over TCP) without needing a separately running process — this is the same tradeoff already made and documented for Legal Entity Part 2D, and is the stronger, repeatable proof (exact assertions, not eyeballing). See §6 for the one residual gap this leaves.

---

## 2. Fixture

Two tenants provisioned via the real admin API + owner-invite-accept + base-login + session-exchange flow (identical to `TenantProvisioningE2ETests`/`LegalEntitiesIntegrationTests`):
- **Tenant A** (`dept-a`) — Owner (full permissions, incl. `org:read`+`org:manage`), primary legal entity, plus a second legal entity created via the real `POST /api/v1/org/legal-entities` endpoint.
- **Tenant B** (`dept-b`) — Owner only, used for cross-tenant isolation checks.

Two additional Tenant A users, seeded directly in the DB (there is no public "invite an additional employee with a chosen role" endpoint yet — only the single owner-invite issued during tenant creation), then logged in through the **real** base-login → session-exchange HTTP flow, including their own `LegalAcceptanceRecord` rows so login completes cleanly instead of hitting a legal challenge:
- **`org-reader@dept-a.test`** — custom role, permission set = `{org:read}` only.
- **`no-access@dept-a.test`** — custom role, permission set = `{}` (zero permissions).

Only this fixture *setup* touches the DB directly; every assertion in every `[Fact]` still goes through a real HTTP request against the full pipeline described in §1.

**Note on the smoke-seeded `acme`/`dapi` tenants:** `DevSmokeTestTenantSeeder` runs automatically in every Testcontainers-backed integration test (`Test` environment), and its Acme HR Manager/Work Manager users have exactly the `org:read`/`org:manage` split this matrix needed. A throwaway probe confirmed empirically that these users **cannot** complete base login cleanly in this harness — they hit `legal_acceptance_required: true` (no `LegalAcceptanceRecord` rows are seeded for them) and get redirected to `/api/v1/legal/acceptances/complete-login` instead of a session. This is why dedicated fixture users were built instead, with their own acceptance records.

---

## 3. Validation Matrix

| Requirement | Test | Result |
|---|---|---|
| User with `org:read` can list/get departments | `List_WithOrgRead_Returns200` | 200 |
| User with `org:manage` can create/update/delete departments | `Create_WithOrgManage_Returns201`, `Create_Get_Update_Delete_FullLifecycle` | 201 / 200 / 204 |
| User without `org:manage` gets 403 for create/update/delete | `Create_WithOrgReadOnly_NoOrgManage_Returns403`, `Update_WithOrgReadOnly_NoOrgManage_Returns403`, `Delete_WithOrgReadOnly_NoOrgManage_Returns403` | 403 (all three) |
| User without `org:read` gets 403 for list/get | `List_WithoutOrgRead_Returns403`, `Get_WithoutOrgRead_Returns403` | 403 |
| Unauthenticated request | `List_Unauthenticated_Returns401` | 401 |
| Cross-tenant access is blocked | `Get_CrossTenant_Returns404`, `List_CrossTenant_LegalEntityId_Returns404` | 404 — existence-hiding, the correct "blocked" semantic (see §4) |
| Cross-legal-entity access is blocked | `Get_CrossLegalEntity_WithinSameTenant_Returns404` | 404 |
| Duplicate department name blocked in same legal entity | `Create_DuplicateNameInSameLegalEntity_Returns409` | 409 |
| Same name allowed in different legal entity | `Create_SameNameInDifferentLegalEntity_IsAllowed` | 201 |
| Parent department must be same tenant + same legal entity | `Create_ParentInDifferentLegalEntity_Returns404`, `Create_ParentInDifferentTenant_Returns404` | 404 (both) |
| Self-parenting rejected | `Update_SelfParenting_Returns400` | 400 (see §5 — not 409; both validator and handler reject it, validator wins) |
| Delete is soft delete only | `Create_Get_Update_Delete_FullLifecycle` | row still resolves after delete, `isActive: false` |
| Excluded by default / included with `includeInactive=true` | `Create_Get_Update_Delete_FullLifecycle` | confirmed both list states |
| `headPositionId` is response-only, not accepted in request bodies | `HeadPositionId_IsIgnoredOnCreate_NotAcceptedFromRequestBody` | 201, `headPositionId` stays `null` even when sent in the request body (System.Text.Json silently ignores the unmapped property — confirmed at runtime, not just by reading the contract) |

**Result: 18/18 passed.**

---

## 4. Correctness Notes (matching against the task's literal wording)

- **Cross-tenant and cross-legal-entity both return 404, not 403.** This is by design, scoped by `GetByIdForLegalEntityAsync`/`ListDepartmentsQueryHandler`'s `ILegalEntityRepository.GetByIdForTenantAsync` lookup (`src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs`, `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/ListDepartmentsQueryHandler.cs`) — existence itself is hidden from callers outside the tenant/legal entity, exactly the same convention already established and tested for Legal Entity (`LegalEntitiesIntegrationTests.GetGeneralSettings_OutOfTenantId_Returns404`). This is the correct "blocked" semantic, not a weaker check than 403.
- **Self-parenting is Update-only.** `CreateDepartmentCommand` generates its own new `Guid` server-side, so a create request cannot reference its own not-yet-existent id — there is no equivalent Create test case, by construction, not by omission.
- **Self-parenting returns 400, not 409.** `UpdateDepartmentCommandValidator` (FluentValidation, runs in the MediatR pipeline *before* the handler) already has its own rule rejecting `ParentDepartmentId == DepartmentId` (`src/ONEVO.Application/Features/OrgStructure/Department/Commands/UpdateDepartment/UpdateDepartmentCommandValidator.cs:23-25`), which surfaces as 400. `UpdateDepartmentCommandHandler.cs:49-50` has its own separate `Conflict` (409) check for the identical condition, but it is unreachable for this exact input because the validator rejects first. Both layers correctly reject self-parenting — the test was initially written expecting 409 (the handler's status), failed on first run, and was corrected to 400 (the validator's status) once the actual pipeline order was confirmed. This is a defense-in-depth pattern already used elsewhere in the codebase, not a gap.

---

## 5. Debugging Note (systematic-debugging applied)

`Update_SelfParenting_Returns409` initially failed: expected 409, got 400. Root cause traced to `UpdateDepartmentCommandValidator`'s own self-parenting rule running before the handler's. This was a **test assumption bug**, not a product bug — confirmed by reading both the validator and the handler side by side (§4). The test was corrected to assert 400 (renamed `Update_SelfParenting_Returns400`) with a comment explaining why both layers reject the same input with different status codes. No production code was changed for this.

---

## 6. Test Run Evidence

```
dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore \
  --filter "FullyQualifiedName~DepartmentsIntegrationTests" \
  --logger "console;verbosity=normal" --blame-hang --blame-hang-timeout 10m

Test Run Successful.
Total tests: 18
     Passed: 18
 Total time: 4.79 Minutes
```

Full unit/architecture/integration counts after this test class was added (final tree state):

```
dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
Passed! - Failed: 0, Passed: 1175, Skipped: 0, Total: 1175

dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
Passed! - Failed: 0, Passed: 403, Skipped: 0, Total: 403

dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --logger "console;verbosity=normal" --blame-hang --blame-hang-timeout 15m
Total tests: 151, Passed: 150, Failed: 1 (17.00 min)
  -> the 1 failure (BaseForgotPasswordRestrictedRoleHttpIntegrationTests, unrelated auth test)
     is a documented Testcontainers port-contention flake (see companion report §5);
     rerun in isolation: Total tests: 3, Passed: 3.
```

Full integration suite is green: 151 real tests, 0 deterministic failures.

---

## 7. Remaining Risks

1. **Not literally validated against a standalone `dotnet run` process bound to `https://localhost:7229`/`https://acme.localhost:7229` with mkcert TLS.** `WebApplicationFactory`'s in-process `TestServer` exercises the same middleware pipeline, DI container, and a real Postgres over TCP, but does not open an actual TLS socket on port 7229. If the user wants literal TLS-socket-level validation against their persistent local dev environment, that is a separate manual/scripted pass this session did not perform (consistent with how Legal Entity's own Part 2D report treated its "Path B" real-instance run as a secondary, not primary, proof).
2. **The `roles:read` migration fix (see the companion report, §6) does not retroactively repair any already-migrated persistent database.** If the user's own `acme.localhost:7229` dev database already applied the original buggy `20260803085232_AddOrgModuleToStarterPlan` migration, its `starter_51_200.included_modules_json` may still hold the truncated 3-item list, meaning **that specific database's** tenants (not the fresh Testcontainers databases used in this session) could still be missing `roles`/`auth`/`configuration`/etc. permissions on their Owner roles. This does not affect Department's own permissions (`org` was already present pre- and post-fix in every scenario checked), but is worth checking with `SELECT included_modules_json FROM subscription_plans WHERE code = 'starter_51_200';` before relying on that specific database for further manual testing.
3. **Postman:** no Postman collection changes were made. The task said to update Postman only after automated validation, and to ask before creating broad new Postman collection changes if none exist for Department. A `postman/` directory exists in this repo (gitignored, per the Legal Entity Part 2D precedent) but no Department folder was found in it during this phase — creating one was out of scope unless requested.

---

## 8. Confirmation: Excluded Areas Untouched

- Legal Entity `country_id`/`countries` code: not modified.
- Legal Entity logo/file/asset code: not modified.
- `OneVo-HR`, the frontend repo: not touched.
- Postman files: not touched (per §7.3).
- No `tenantId` or `headPositionId` field was added to any Department request contract (`CreateDepartmentRequest`/`UpdateDepartmentRequest` — confirmed unchanged, and confirmed at runtime in §3).
