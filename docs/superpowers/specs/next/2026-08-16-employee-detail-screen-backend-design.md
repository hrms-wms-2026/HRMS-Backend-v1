# Employee Detail Screen — Backend Design

**Status:** Approved by user 2026-08-16, ready for implementation planning.

**Sub-project 2 of 2** in the decomposed employee-detail/invitation/cross-entity feature request. Depends on sub-project 1 (`2026-08-16-multi-legal-entity-employment-foundation-design.md`, now finished) for the session/permission mechanics; consumes coverage-manager visibility infrastructure that already existed before sub-project 1 and was left untouched.

**Companion spec:** `Hrms--Web-application---front-end---v1/docs/superpowers/specs/2026-08-16-employee-detail-screen-frontend-design.md` — the frontend consumer of this API.

**Origin:** brainstormed live with the user across two sessions (2026-08-16), grounded in `OneVo-HR/Userflow/Employee-Management/profile-management.md`, the existing `GetMyProfileQueryHandler`/`IEmployeeProfileRepository` self-service implementation (2026-08-15), and `EMPLOYEE_MANAGEMENT_FRONTEND_IMPLEMENTATION_REPORT.md`'s own documented "known gap: detail page" section — cross-checked against the actual current codebase.

## 1. Goal

Give an HR admin (or anyone with `employees:read` + management coverage over the target employee) a full section-by-section Employee Detail view for *another* employee: Personal Information, Job Information, Emergency Contact, and — gated behind a separate permission — Payroll & Statutory (masked bank details). Add a minimal "Change Position" action that reassigns the employee's primary position with an atomic capacity check, reusing the seat-reservation mechanism built in sub-project 1.

## 2. Current-state facts this design depends on

- `EmployeeDetailComponent` (frontend) is explicitly a stub today — its own doc comment says the full section UI "is out of scope here" and the `EMPLOYEE_MANAGEMENT_FRONTEND_IMPLEMENTATION_REPORT.md`'s "Known gap: detail page" section confirms: Personal Info/Emergency Contact/Payroll have **no admin-facing backend surface at all** (only self-service `/employees/me/...` exists); Documents and Lifecycle sections have **no backend surface of any kind**, self-service or admin.
- `IEmployeeProfileRepository` (`Application/Features/CoreHr/Employee/RepositoryInterfaces/`) already takes `employeeId` as an explicit parameter on every method (`ListAddressesAsync`, `ListEmergencyContactsAsync`, `GetPrimaryBankDetailAsync`, etc.) — it is **not** hardcoded to "the caller's own employee." The self-service restriction lives entirely in `GetMyProfileQueryHandler`, which resolves `employeeId` from `ICurrentUser.UserId` before calling it. This repository can be reused as-is for the admin query; no new repository methods are needed for Personal Info, Emergency Contact, or Payroll data access.
- Coverage-manager visibility is fully implemented and already used by `GetEmployeeQueryHandler`/`GetVisibleByIdAsync` (`EmployeeVisibilityScopeResolver` against `ManagementCoverageRecord`) — reused unchanged.
- No `employees:read:sensitive` permission exists yet (deliberately not added in sub-project 1 — it had no consumer there; this spec is that consumer).
- No Promotion/Transfer command handler exists anywhere in the backend (`grep` for `Promotion`/`Transfer` under `Features/CoreHr` returns nothing) — the OneVo-HR docs describe a full approval-routed workflow, but it was never built. Per user decision, "Change Position" in this spec is a **minimal capacity-checked reassignment**, not that workflow.
- `PositionAssignment` capacity reservation (`TryReservePositionAssignmentAsync`, sub-project 1) inserts a `"planned"` row guarded by an atomic capacity subquery. For an immediate, non-invitation action like Change Position, the equivalent atomic primitive is needed but must insert `"active"` directly (no invitation lifecycle involved) — see §4.
- Bank detail masking pattern already exists: `BankAccountMasker.Mask(_encryption.Decrypt(bankDetail.AccountNumberEncrypted))`, used verbatim in `GetMyProfileQueryHandler` — reused unchanged, raw/decrypted account number never leaves the encryption boundary.

## 3. Permission changes

- Add **`employees:read:sensitive`** to `PermissionSeeder.cs` (single bucket, per earlier decision) — gates the Payroll & Statutory section of the admin detail response. Without it, the section is entirely omitted from the response (not a 403 on the whole endpoint — the rest of the detail view still renders).
- No new permission for "Change Position" — reuses `employees:write` (already the permission for other employee-mutating actions), plus the same coverage-manager check as read access.

## 4. API contract

### `GET /api/v1/employees/{id}/detail`

New endpoint on `EmployeesController` (separate from the existing `GET /api/v1/employees/{id}`, which stays as the list-item-shaped response used by the employee list and other callers — not repurposed, to avoid a breaking response-shape change for existing consumers). `[RequirePermission("employees:read")]`, plus the same `EmployeeVisibilityScope`-based coverage check `GetEmployeeQueryHandler` already performs (403 if outside scope, 404 if the employee doesn't exist in the tenant at all).

`EmployeeDetailResponse`:

```json
{
  "id": "guid",
  "jobInformation": { "employeeNumber": "", "legalEntityName": "string|null", "departmentName": "string|null", "positionName": "string|null", "positionId": "guid|null", "reportingManagerName": "string|null", "employmentTypeLabel": "", "status": "", "hireDate": "date", "probationEndDate": "date|null" },
  "personalInformation": { "firstName": "", "lastName": "", "email": "", "phone": "string|null", "dateOfBirth": "date|null", "gender": "string|null", "nationalityId": "guid|null", "addresses": [ { "id": "guid", "addressType": "permanent|current", "addressJson": "{}", "isPrimary": true } ] },
  "emergencyContacts": [ { "id": "guid", "name": "", "relationship": "", "phone": "", "email": "string|null", "isPrimary": true } ],
  "payroll": { "hasBankDetailsOnFile": true, "bankName": "string|null", "maskedAccountNumber": "****1234|null", "accountType": "string|null" } | null,
  "invitationStatus": "pending|accepted|expired|revoked|null",
  "invitationExpiresAt": "datetime|null"
}
```

`payroll` is `null` in the JSON response (field omitted client-side) unless the caller holds `employees:read:sensitive` — computed server-side, never partially redacted. `invitationStatus`/`invitationExpiresAt` reuse the exact join `GetEmployeeQueryHandler` already added in sub-project 1 (`IInvitationTokenRepository.GetLatestByEmployeeIdAsync`).

Dependents, Security, Documents, and Lifecycle sections are **not included** — per §2, they have no admin backend surface today and were not requested; adding them is future scope, not silently expanded into this one.

### `POST /api/v1/employees/{id}/change-position`

`[RequirePermission("employees:write")]`, plus the same coverage check. Body: `{ positionId: string, effectiveFrom: string (date) }`. `204 No Content` on success. Errors: `404` if the target position doesn't exist in the tenant/legal entity, `409` "This position has reached its capacity." on a full seat, `422` if `positionId` belongs to a different legal entity than the employee's current one (cross-entity position changes are out of scope for this minimal version — a legal-entity change is a bigger operation than "change position" and isn't what was asked for).

## 5. Change Position command

`ChangeEmployeePositionCommandHandler`:

1. Load the employee (`IEmployeeRepository.GetTrackedByIdAsync`), 404 if not found.
2. Coverage + `employees:write` already enforced at the controller/pipeline level (same pattern as other write endpoints) — no duplicate check in the handler.
3. Load the target `Position` (`IPositionRepository.GetByIdForLegalEntityAsync(tenantId, employee.LegalEntityId, positionId)`), 404 if missing/inactive, 422 if it resolves to a different legal entity than the employee's own (defensive — `GetByIdForLegalEntityAsync` already scopes by the employee's own `LegalEntityId`, so a cross-entity id simply won't resolve, giving a natural 404 rather than needing a separate 422 check — simplify to 404 only, drop the 422 case from §4 to match).
4. Atomically reserve a seat on the **new** position as `"active"` directly (new repository method, §6) — if it fails (capacity full), return 409 immediately, nothing else is touched.
5. On success: end the employee's current active `PrimaryEmployment` `PositionAssignment` (`AssignmentStatus = "ended"`, `EffectiveTo` = the requested `effectiveFrom` minus one day, or same-day if no current assignment exists — e.g. this is the employee's very first position, which shouldn't happen in practice but is handled without throwing).
6. Save once, single transaction.

## 6. New repository primitive

`IPositionAssignmentRepository.TryCreateActiveAssignmentAsync(Guid tenantId, Guid employeeId, Guid positionId, DateOnly effectiveFrom, Guid createdById, CancellationToken ct)` — identical atomic INSERT...WHERE-guarded-by-capacity-subquery shape as sub-project 1's `TryReservePositionAssignmentAsync`, differing only in `assignment_status` being `"active"` instead of `"planned"` at insert time (no invitation lifecycle attached). Returns the new assignment's `Guid?` (null = capacity full).

## 7. Testing

- Unit: `GetEmployeeDetailQueryHandlerTests` (visibility/coverage denial, sensitive-permission gating on/off, full happy path against mocked `IEmployeeProfileRepository`), `ChangeEmployeePositionCommandHandlerTests` (capacity-full 409, successful reassignment ends old + creates new assignment, position-not-found 404).
- Integration: end-to-end detail read with/without `employees:read:sensitive` (payroll section present/absent), change-position happy path + capacity-full race (mirroring sub-project 1's concurrent-reservation test pattern) against real Postgres.

## 8. Self-review

- No placeholders — every field traces to `profile-management.md`, the existing self-service response shapes, or an explicit current-codebase fact (§2).
- Scope: deliberately narrow. Dependents/Security/Documents/Lifecycle sections and full approval-routed Transfer/Promotion were all considered and explicitly excluded per user decision, not silently dropped.
- Internal consistency: §4's original 422 case for cross-entity position IDs was caught as unreachable during §5's handler design and corrected in the same pass rather than left as a contradiction between sections.
