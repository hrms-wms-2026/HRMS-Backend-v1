
  # Employee Self-Service Profile — Backend Design

  **Status:** Approved by user 2026-08-15, ready for implementation planning.

  **Companion spec:** `Hrms--Web-application---front-end---v1/docs/superpowers/specs/2026-08-15-employee-self-service-profile-frontend-design.md` (frontend consumer of this API — the "Profile" tab inside the topbar Preferences popup). This document is the backend half; the two share the API contract in §5.

  **Origin:** brainstormed live with the user 2026-08-15 via `superpowers:brainstorming`, triggered by the user asking for a self-service Profile screen reachable from the existing topbar "Appearance" popup. Grounded in `ONEVO_Backend_Architecture_Document.md`, `OneVo-HR/Userflow/Employee-Management/profile-management.md`, `OneVo-HR/Userflow/Employee-Management/dependent-management.md`, and `OneVo-HR/database/phase1-table-inventory.md`, cross-checked against the actual current codebase (not assumed from docs — see §9 for concrete divergences found).

  ---

  ## 1. Goal

  Give every tenant user a self-service "My Profile" capability: view/edit their own Personal Information and Emergency Contacts/Dependents, view their own Job Information (read-only), view/edit their own Payroll & Statutory bank details (edit gated to HR), and manage their own Security Settings (change password, enable/disable MFA) — all through a single composite read plus per-section writes.

  ## 2. Scope

  **In scope:** Personal Information, Emergency Contacts, Dependents, Job Information (read-only reprojection), Payroll & Statutory (bank details), Security Settings (password change, MFA enable/disable — reusing existing MFA infra), avatar upload.

  **Out of scope (per user decision during brainstorming):** Documents/onboarding tasks and Time Off/lifecycle sections from `profile-management.md` — these already have dedicated top-level nav pages (Work/Time Off/Attendance) and are not duplicated here. Admin editing of *other* employees' profiles — unchanged, still the existing `EmployeesController` List/GetById/ResendInvitation + future admin edit endpoints, not part of this spec. Any change to `employee_custom_fields`, `employee_work_history`, `employee_lifecycle_events`, `employee_assignment_history`, or `employee_transfers` — not part of self-service profile.

  ## 3. Current-state facts this design depends on

  Verified directly against the codebase, not assumed from the inventory docs:

  - `Employee` entity (`src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs`) has **no `DisplayTimezone` property** and **no concurrency token**, despite `profile-management.md` documenting `PUT /employees/me/display-preferences` writing `employees.display_timezone`, and backend-arch §4.7 requiring optimistic concurrency on Employee profile writes.
  - `EmploymentTypeId`/`EmploymentStatusId`/`WorkModeId` on `Employee` are `int` lookup-table FKs, not the `varchar` codes `phase1-table-inventory.md` describes — the inventory doc is aspirational/stale here.
  - Two separate `IEmployeeRepository` interfaces exist, each with its own `EfEmployeeRepository` implementation, both registered in `Infrastructure/DependencyInjection.cs`:
    - `Application.Common.RepositoryInterfaces.IEmployeeRepository` → `Infrastructure.Persistence.Repositories.EfEmployeeRepository` — has `GetByUserIdAsync(tenantId, userId)` / `GetByUserIdsAsync`. **This is the one this design uses for `/me` resolution.**
    - `Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository` → `Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository` — has `GetVisibleByIdAsync`/`ListVisibleAsync`/`GetByIdAsync`, used by the existing admin List/GetById endpoints and by `EmployeeListItemResponse`'s label resolution (department/position/legal-entity/manager names, employment-type label).
  - No `Contracts/CoreHr/Employees/` folder exists yet — Employees currently has zero request contracts (List/GetById take route/query params only).
  - No `employee_bank_details`, `employee_addresses`, `employee_emergency_contacts`, or `employee_dependents` tables/entities exist in the codebase today — all four are new.
  - `IEncryptionService` (`Encrypt(string)`/`Decrypt(string)`, plus `EncryptBytes`/`DecryptBytes`) is implemented by `AesEncryptionService` (AES-GCM 256-bit) and already used for other secrets (platform service keys, payment gateway credentials, MFA secrets) — string-in/string-out is the established pattern.
  - MFA setup already exists end-to-end: `AuthMfaController` (`api/v1/auth`) has `POST mfa/enable` (`EnableMfaCommand`, returns TOTP secret + QR URI) and `POST mfa/confirm-setup` (`ConfirmMfaSetupCommand`). **No `mfa/disable` endpoint exists** — new work.
  - `AuthPasswordController` (`api/v1/auth`) has `forgot-password`, `reset-password`, `force-change-password` — none of these is "change password while already logged in, given current password." **New work.**
  - `TenantIsolationArchitectureTests` reflects over every `ITenantOwnedEntity` automatically (query-filter presence, RLS-policy presence, no `BYPASSRLS`) — new tenant-owned entities are covered by this test without editing it, as long as they implement `ITenantOwnedEntity` and get a migration-level RLS policy.
  - `EmployeeLegacyFieldRetirementArchitectureTests` forbids `Employee.ManagerId`/`Employee.JobTitleId` from ever reappearing — this design adds no such properties, so it stays green.
  - File uploads use a server-mediated `IFileStorageService` with a single-call `UploadAsync(stream, ...)` convenience method (used by `LegalEntitiesController.SetLogo`, a `PUT .../logo` multipart endpoint) — **not** a 3-step client-driven reservation flow (no `POST /api/v1/files/reservations` endpoint exists publicly, despite `profile-management.md` describing one). This design follows the real `SetLogo` pattern for avatar upload.

  ## 4. Data model

  All four new entities live under `Domain/Features/CoreHr/Employee/Entities/` (same subfeature as `Employee`, since they're child data of the Employee aggregate — "reuse an existing feature/subfeature" per backend-arch §2.1). Each implements `ITenantOwnedEntity`. EF configs go under `Infrastructure/Persistence/Configurations/CoreHr/Employee/`.

  ### 4.1 `Employee` additions (existing table, new migration)

  - `DisplayTimezone` (`varchar(50)`, nullable) — IANA timezone, UI display only.
  - Concurrency token: `xmin`-backed `uint Version` property, `.IsRowVersion()`, following the `AddOnboardingDraftXminConcurrencyToken` migration precedent. Required by backend-arch §4.7's named coverage list ("Employee profile and employment status"). `PUT /employees/me/personal-information` returns `409 Conflict` on stale writes.

  ### 4.2 `EmployeeAddress` → `employee_addresses`

  | Column | Type | Notes |
  |---|---|---|
  | `id` | uuid | PK |
  | `tenant_id` | uuid | FK tenants |
  | `employee_id` | uuid | FK employees |
  | `address_type` | varchar(20) | `permanent`, `current` (no `emergency` type here — that's `employee_emergency_contacts`) |
  | `address_json` | jsonb | street/city/state/postal/country |
  | `is_primary` | boolean | |

  Self-service scope: employee manages their own `current`/`permanent` addresses via the Personal Information section (address is one of the documented Personal Information fields — "Home address").

  ### 4.3 `EmployeeEmergencyContact` → `employee_emergency_contacts`

  Matches `phase1-table-inventory.md` exactly: `id`, `tenant_id`, `employee_id`, `name` varchar(100), `relationship` varchar(30), `phone` varchar(20), `email` varchar(255), `is_primary` boolean.

  ### 4.4 `EmployeeDependent` → `employee_dependents`

  Matches inventory: `id`, `tenant_id`, `employee_id`, `name` varchar(100), `relationship` varchar(20) (`spouse`/`child`/`parent`/`other`), `date_of_birth` date, `is_emergency_contact` boolean, `phone` varchar(20). Per `dependent-management.md`, marking a dependent `is_emergency_contact = true` is how a dependent surfaces as an emergency contact — it does **not** create a duplicate row in `employee_emergency_contacts`; the two lists are unioned client-side (see companion frontend spec §4).

  ### 4.5 `EmployeeBankDetail` → `employee_bank_details`

  | Column | Type | Notes |
  |---|---|---|
  | `id` | uuid | PK |
  | `tenant_id` | uuid | FK tenants |
  | `employee_id` | uuid | FK employees |
  | `bank_name` | varchar(100) | |
  | `branch_name` | varchar(100) | |
  | `account_holder_name` | varchar(100) | |
  | `account_number_encrypted` | **varchar(500)** | Encrypted via `IEncryptionService.Encrypt(string)` — deviates intentionally from the inventory doc's `bytea`, matching the service's actual string-based contract (same pattern already used for platform/payment secrets) |
  | `account_type` | varchar(30) | |
  | `routing_number` | varchar(20) | |
  | `is_primary` | boolean | |

  ### 4.6 RLS

  Each new table gets a `tenant_isolation` RLS policy added in its own migration (same migration that creates the table), following the shape already used in `AddMissingRlsPolicies`/`AddFileStorageRlsPolicies`. Verified automatically by `TenantIsolationArchitectureTests` — no test file changes needed.

  ## 5. API contract (shared with frontend spec)

  All endpoints on the existing `EmployeesController` (`api/v1/employees`, `[Authorize(Policy = "TenantPolicy")]`) unless noted. All resolve the caller's own `Employee` row via `ICurrentUser.UserId` → `Common.IEmployeeRepository.GetByUserIdAsync` — no `{id}` route parameter, no cross-employee access possible through these routes.

  ### `GET /api/v1/employees/me`

  Composite read — everything the Profile popup needs in one call. `MyProfileResponse`:

  ```json
  {
    "personalInformation": { "firstName": "", "lastName": "", "email": "", "phone": "", "dateOfBirth": "date|null", "gender": "string|null", "maritalStatus": "string|null", "nationalityId": "guid|null", "countryName": "string|null", "displayTimezone": "string|null", "identityDocumentType": "string|null", "identityDocumentNumber": "string|null", "personalEmail": "string|null", "avatarUrl": "string|null", "addresses": [ { "id": "guid", "addressType": "permanent|current", "addressJson": {}, "isPrimary": true } ], "version": "opaque-concurrency-token" },
    "jobInformation": { "employeeNumber": "", "legalEntityName": "", "departmentName": "string|null", "positionName": "string|null", "reportingManagerName": "string|null", "employmentTypeLabel": "", "employmentStatus": "", "hireDate": "date", "probationEndDate": "date|null", "workMode": "" },
    "emergencyContacts": [ { "id": "guid", "name": "", "relationship": "", "phone": "", "email": "string|null", "isPrimary": true } ],
    "dependents": [ { "id": "guid", "name": "", "relationship": "", "dateOfBirth": "date", "isEmergencyContact": false, "phone": "string|null" } ],
    "payroll": { "hasBankDetailsOnFile": true, "bankName": "string|null", "maskedAccountNumber": "****1234|null", "accountType": "string|null", "canEdit": false },
    "security": { "mfaEnabled": true, "lastPasswordChangedAt": "datetime|null" }
  }
  ```

  `jobInformation` reuses the same label-resolution logic `EmployeeListItemResponse` already performs (no new join code invented). `payroll.canEdit` reflects whether the caller holds `employees:write` — frontend disables the edit form when false rather than guessing.

  ### `PUT /api/v1/employees/me/personal-information`

  Body: `UpdatePersonalInformationRequest` (all Personal Information fields from §5's `personalInformation` shape except `avatarUrl`, plus `version` for the concurrency check). `409 Conflict` with a `"refresh and retry"` detail if `version` is stale. `422` field-level validation errors (email format, phone format) per backend-arch §2.3.

  ### `PUT /api/v1/employees/me/avatar`

  Multipart `IFormFile`, single call — mirrors `LegalEntitiesController.SetLogo`. `SetMyAvatarCommand` → `IFileStorageService.UploadAsync` → sets `Employee.AvatarFileId`.

  ### Emergency contacts: `POST` / `PUT /{contactId}` / `DELETE /{contactId}` under `api/v1/employees/me/emergency-contacts`

  Simple CRUD scoped to the caller's own `employee_id`; `404` if `{contactId}` doesn't belong to the caller. No concurrency token needed (list-item CRUD, not a single mutable aggregate).

  ### Dependents: `POST` / `PUT /{dependentId}` / `DELETE /{dependentId}` under `api/v1/employees/me/dependents`

  Same CRUD shape as emergency contacts.

  ### `GET /api/v1/employees/me/payroll`

  Returns the masked `payroll` shape from §5 (also embedded in the composite `GET /me`, exposed standalone for the Payroll tab's own refresh).

  ### `PUT /api/v1/employees/me/payroll`

  **Requires `employees:write`** (see §6). `403` otherwise, even for the caller's own record. Body: `UpdateBankDetailsRequest` (bankName, branchName, accountHolderName, accountNumber (raw, encrypted server-side before storage), accountType, routingNumber). Response never echoes the raw or decrypted account number — only the masked form.

  ### `POST /api/v1/auth/change-password` (on existing `AuthPasswordController`)

  New. `[Authorize(Policy = "TenantPolicy")]`. Body: `{ currentPassword, newPassword }`. Verifies current password via existing password-hash check, then rotates. Revokes other active sessions per backend-arch §4.5 ("password change ... must revoke active sessions"). Writes `audit_logs` (`user.password_changed`) and an `EmployeeSecurityUpdated` outbox event.

  ### `POST /api/v1/auth/mfa/disable` (on existing `AuthMfaController`)

  New. `[Authorize(Policy = "TenantPolicy")]`. Requires re-entering current password or a fresh TOTP code (re-auth-to-disable, standard MFA-disable safety pattern) — body: `{ currentPassword }`. Removes the `user_mfa` row(s), audit-logged as `user.mfa_disabled`.

  `mfa/enable` and `mfa/confirm-setup` are reused as-is from the existing `AuthMfaController` — no changes.

  ## 6. Security & permissions

  | Action | Rule |
  |---|---|
  | View own Personal/Job/Emergency/Dependents | Authenticated self-service — no explicit permission code, trusted session identity (matches profile-management.md: "authenticated self-service") |
  | Edit own Personal Information, Emergency Contacts, Dependents, avatar | Authenticated self-service |
  | View own Payroll & Statutory | Authenticated self-service, masked (last 4 digits only) |
  | **Edit** own Payroll & Statutory | **`employees:write`** required, even for own record — bank-detail edits are HR/Admin-mediated to prevent unauthorized payroll-redirection. This is the one section where "self-service" does not mean "self-edit." |
  | Change own password / enable/disable own MFA | Authenticated self-service, with re-auth (current password) required to disable MFA |

  No caching of any response payload from these endpoints (bank details, addresses, emergency contacts, dependents, personal phone/email are all in backend-arch §4.6's protected-field-groups / §3.7's do-not-cache list). No logging of these payloads (§7.3).

  Every mutating endpoint writes an `audit_logs` row and raises `EmployeeUpdated` (personal info/contacts/dependents/avatar) or `EmployeeSecurityUpdated` (password/MFA) via the outbox pattern, per `profile-management.md`'s "Events Triggered" section.

  ## 7. Errors

  | Status | Cause |
  |---|---|
  | `401` | Not authenticated / expired session |
  | `403` | Payroll edit without `employees:write`; MFA-disable with wrong current password |
  | `404` | Emergency-contact/dependent id not found for caller |
  | `409` | Stale `version` on personal-information update |
  | `422` | Field validation failure (email/phone format, required fields) |

  ## 8. Testing

  - **Unit** (`Tests.Unit/Features/CoreHr/Employee/`, xUnit + Moq, mirroring `GetEmployeeQueryHandlerTests`): one class per new handler (`GetMyProfileQueryHandlerTests`, `UpdatePersonalInformationCommandHandlerTests`, `UpsertEmergencyContactCommandHandlerTests`, `UpsertDependentCommandHandlerTests`, `UpdateBankDetailsCommandHandlerTests`, `SetMyAvatarCommandHandlerTests`, `ChangePasswordCommandHandlerTests`, `DisableMfaCommandHandlerTests`), plus validator tests for each request contract.
  - **Integration** (`Tests.Integration/CoreHr/EmployeeProfile/`, Testcontainers Postgres): full endpoint round-trips; tenant isolation (a second tenant's user gets `404`/empty, never another tenant's data); `403` on payroll edit without `employees:write`; encryption round-trip (raw account number never appears in the HTTP response, decrypts correctly server-side); `409` on concurrent personal-info edit; MFA disable requires correct current password.
  - **Architecture:** no new test files required — `TenantIsolationArchitectureTests` and `EmployeeLegacyFieldRetirementArchitectureTests` cover the new entities/controller automatically by reflection; run both as a baseline check before starting implementation (confirm green pre-change) and again after.

  ## 9. Out of scope

  - Documents/onboarding-tasks and Time Off/lifecycle sections (user decision — already covered elsewhere in the app's nav).
  - Admin-side editing of other employees' profiles (existing `EmployeesController` admin surface, unchanged).
  - `employee_custom_fields`, `employee_work_history`, `employee_lifecycle_events`, `employee_assignment_history`, `employee_transfers`.
  - A public `POST /api/v1/files/reservations` 3-step upload flow — does not exist, not introduced by this spec; avatar upload uses the existing single-call `IFileStorageService.UploadAsync` pattern instead.

  ## 10. Self-review

  - No placeholders — every field traces to `profile-management.md`/`dependent-management.md`/`phase1-table-inventory.md` or an explicit codebase fact found during investigation (§3).
  - Internal consistency: bank-detail encryption type, `/me` repository resolution, avatar-upload pattern, and concurrency-token placement were all flagged as open/ambiguous during design review and are resolved explicitly here rather than left implied, per the second-opinion review that preceded this document.
  - Scope: large but single-aggregate (Employee + 4 child tables) — user explicitly chose "everything in one build" over phasing after being offered a phased alternative; sections requiring genuinely separate subsystems (Documents, Time Off) were carved out by mutual agreement rather than force-fit in.
  - Divergences from the source docs are called out explicitly (§3, §4.5) rather than silently followed or silently overridden.
