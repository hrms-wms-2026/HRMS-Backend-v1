# Employee Number Autogeneration — Backend Report

**Repo:** `HRMS-Backend-v1`  
**Date:** 2026-08-20  
**Scope:** Add Employee onboarding employee-number suggestion, availability, and uniqueness enforcement

## Uniqueness scope and evidence

**Scope: tenant + `employee_number` (tenant-wide), not legal-entity-scoped.**

Evidence:

1. `OneVo-HR/database/phase1-table-inventory.md` — `employees.employee_number` notes: **"Unique per tenant"**.
2. `EmployeeConfiguration.cs` — `HasIndex(e => new { e.TenantId, e.EmployeeNumber }).IsUnique()` (`ix_employees_tenant_id_employee_number`).
3. `IEmployeeRepository.EmployeeNumberExistsAsync(tenantId, employeeNumber, …)` — tenant-scoped only (no `legalEntityId`).
4. Seeded values (`DAPI-0001`…, `ACME-0001`…) share the `{COMPANY_CODE}-{NNNN}` shape across legal entities inside a tenant; uniqueness remains tenant-wide so the same number cannot be reused under another company code prefix.

No migration was added: the unique index already existed. Soft-deleted rows are included in existence/sequence queries via `IgnoreQueryFilters()` because the unique index has **no** `IsDeleted` filter.

## Endpoint contracts

Base: `api/v1/onboarding` · `TenantPolicy` · `employees:write` · **no `tenantId` in request**

### Suggestion

`GET /api/v1/onboarding/employee-number-suggestion?legalEntityId={guid}`

```json
{ "employeeNumber": "DAPI-0005", "prefix": "DAPI", "sequence": 5 }
```

Rules:

- Tenant from server context.
- Legal entity must exist for tenant, be active, and have a usable `company_code`.
- Prefix = trimmed `CompanyCode` (no silent uppercasing; matches legal-entity settings).
- Sequence = max numeric suffix for `{prefix}-*` among tenant employees (incl. soft-deleted) + 1; skips collisions.
- **Not a reservation** — save/finalize must re-check.

### Availability

`GET /api/v1/onboarding/employee-number-availability?employeeNumber={value}`

```json
{ "employeeNumber": "DAPI-0005", "available": true }
```

Rules: trim; reject blank/invalid format with 400; same tenant uniqueness scope; does not leak other employee details.

### Draft save / finalize

- Draft save: if `employeeNumber` present → format + uniqueness; trim stored value; blank still allowed on draft.
- Finalize (and approve-access-grant path): require non-blank valid number; uniqueness → **409**; format → **400**; missing → **422**.
- Concurrent insert races map via existing `UniqueConstraintConflictException` → 409.

## Generated format

`{COMPANY_CODE}-{0001}` (4-digit zero-padded sequence), matching seeds (`DAPI-0001`, `ACME-0001`).

Edited values: `^[A-Za-z0-9_-]+$`, max length **20**, no spaces; case preserved (not silently uppercased).

## Files changed

| Area | Path |
|------|------|
| Rules | `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/EmployeeNumberRules.cs` |
| Suggestion query | `.../Onboarding/Queries/GetEmployeeNumberSuggestion/*` |
| Availability query | `.../Onboarding/Queries/CheckEmployeeNumberAvailability/*` |
| Controller | `src/ONEVO.Api/Controllers/Tenant/CoreHr/OnboardingEmployeeNumberController.cs` |
| Contracts | `src/ONEVO.Api/Contracts/CoreHr/Onboarding/EmployeeNumberViewModels.cs` |
| Repository | `IEmployeeRepository` + `EfEmployeeRepository` (`GetNextEmployeeNumberSequenceAsync`, IgnoreQueryFilters on exists) |
| Write paths | `OnboardingDraftWriteService`, `SaveOnboardingDraftCommandValidator`, `ApproveAccessGrantRequestCommandHandler` |
| Tests | `EmployeeNumberRulesTests`, `EmployeeNumberOnboardingQueryHandlerTests`, `OnboardingEmployeeNumberControllerArchitectureTests` |

## Tests run

```text
dotnet build src/ONEVO.Api/ONEVO.Api.csproj                     → succeeded
dotnet test ...Unit --filter "FullyQualifiedName~EmployeeNumber" → 26 passed
dotnet test ...Unit --filter "FullyQualifiedName~EmployeeNumber|FullyQualifiedName~Onboarding" → 239 passed (earlier full Onboarding filter)
dotnet test ...Architecture --filter "...EmployeeNumber|...OnboardingEmployeeNumber" → 9 passed
git diff --check                                               → clean (LF warnings only)
```

Note: a running `ONEVO.Api` process briefly locked DLL copy; process was stopped to complete the build.

## Skipped checks

- Integration/E2E against a live Postgres tenant (unit + architecture only).
- Changing the unique index to filtered soft-delete (would change product semantics).
- Bulk onboarding auto-suggestion (out of scope; still uses `EmployeeNumberExistsAsync`).

## Remaining risks

1. **Suggestion races** — two HR users can receive the same suggestion; finalize/DB unique index remain authoritative.
2. **Very long company codes** — if `company_code` length cannot fit `-NNNN` in 20 chars, suggestion returns 422; HR must enter manually.
3. **Case-sensitive uniqueness** — `dapi-0001` and `DAPI-0001` are distinct in PostgreSQL varchar comparison.
4. **Draft-only duplicates** — two drafts may hold the same number until one finalizes; second finalize gets 409.
